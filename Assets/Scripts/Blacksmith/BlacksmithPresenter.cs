using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Presenter экрана кузнеца (Panel_Blacksmith в Game-сцене).
///
/// Отвечает за:
///   — открытие/закрытие панели (IsOpen);
///   — данные предварительного расчёта: что и почём починится, хватает ли золота;
///   — команды: починить всё, починить одну вещь;
///   — авто-закрытие, если стартовал бой (в бою ремонт запрещён сервером).
///
/// АРХИТЕКТУРА (UnityStyle): чистый C#, View подписывается на реактивное состояние и зовёт
/// публичные команды.
///
/// БЕЗОПАСНОСТЬ И ЦЕНЫ:
///   — клиент НЕ считает цену ремонта. Формула (3 × уровень вещи × недостающая прочность) и
///     правило «что подлежит ремонту» живут только на сервере; сюда приходит готовый список.
///     Второй экземпляр этих правил в Unity разъехался бы при первой же подкрутке баланса;
///   — instanceId всегда из серверного ответа;
///   — наличие кузнеца в локации проверяет сервер. Клиент лишь прячет кнопку — это UX,
///     а не защита.
///
/// РАСШИРЯЕМОСТЬ: сейчас панель показывает только ремонт. Купля-продажа станет вкладками этого
/// же экрана — тогда здесь появится enum вкладок по образцу InventoryTab.
/// </summary>
public sealed class BlacksmithPresenter : DisposableObject, IInitializable
{
    // ─── Состояние ────────────────────────────────────────────────────────────

    private readonly Reactive<bool> mIsOpen = new(false);
    private readonly Reactive<bool> mIsLoading = new(false);

    /// <summary>Вещи, которые починятся, с ценой каждой. Пусто — чинить нечего.</summary>
    private readonly Reactive<List<RepairQuoteItemDto>> mItems = new(new List<RepairQuoteItemDto>());

    private readonly Reactive<int> mTotalCost = new(0);
    private readonly Reactive<long> mGold = new(0);

    /// <summary>Хватает ли золота на весь список. Ремонт атомарен — частично не выйдет.</summary>
    private readonly Reactive<bool> mCanAffordAll = new(false);

    /// <summary>Сколько надетых вещей пропущено из-за предельного износа (максимум 1).</summary>
    private readonly Reactive<int> mSkippedWornOut = new(0);

    public ReadonlyReactive<bool> IsOpen => mIsOpen.Readonly;
    public ReadonlyReactive<bool> IsLoading => mIsLoading.Readonly;
    public ReadonlyReactive<List<RepairQuoteItemDto>> Items => mItems.Readonly;
    public ReadonlyReactive<int> TotalCost => mTotalCost.Readonly;
    public ReadonlyReactive<long> Gold => mGold.Readonly;
    public ReadonlyReactive<bool> CanAffordAll => mCanAffordAll.Readonly;
    public ReadonlyReactive<int> SkippedWornOut => mSkippedWornOut.Readonly;

    // ─── Зависимости ──────────────────────────────────────────────────────────

    private readonly CancellationTokenSource mLifetimeCts = new();

    private readonly IBlacksmithService mService;
    private readonly CombatPresenter mCombatPresenter;
    private readonly INotificationService mNotifications;

    [Inject]
    public BlacksmithPresenter(IBlacksmithService service, CombatPresenter combatPresenter,
        INotificationService notifications)
    {
        mService = service;
        mCombatPresenter = combatPresenter;
        mNotifications = notifications;

        AutoDispose(mIsOpen, mIsLoading, mItems, mTotalCost, mGold, mCanAffordAll, mSkippedWornOut);
    }

    // ─── IInitializable ───────────────────────────────────────────────────────

    public void Initialize()
    {
        // Бой мог стартовать, пока экран открыт (PvP-нападение). Ремонт в бою запрещён сервером,
        // держать открытым экран, все кнопки которого вернут ошибку, незачем.
        mCombatPresenter.IsInCombat
            .SubscribeOnValueChanged(OnCombatStateChanged, callOnSubscribe: false)
            .DisposeWhenLifeEnded(this);
    }

    protected override void OnDispose()
    {
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
    }

    // ─── Открытие / закрытие ──────────────────────────────────────────────────

    /// <summary>Открыть экран кузнеца и загрузить расчёт. Игнорируется, если идёт бой.</summary>
    public void Open()
    {
        if (mCombatPresenter.IsInCombat.Value) return;
        if (mIsOpen.Value) return;

        mIsOpen.Value = true;
        RefreshAsync(mLifetimeCts.Token).Forget();
    }

    public void Close() => mIsOpen.Value = false;

    // ─── Команды ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Починить всё — с подтверждением. Показываем и сумму, и последствие для максимальной
    /// прочности: ремонт снижает её на 1 у КАЖДОЙ вещи, и кнопка делает это массовым. Игрок,
    /// жмущий её после каждого поражения не глядя, угробит сет заметно быстрее — он должен
    /// понимать, за что платит.
    /// </summary>
    public void RequestRepairAll()
    {
        int count = mItems.Value?.Count ?? 0;
        if (count == 0)
        {
            mNotifications.ShowInfo("Чинить нечего — всё целое.");
            return;
        }

        if (!mCanAffordAll.Value)
        {
            mNotifications.ShowError(
                $"Недостаточно золота: нужно {mTotalCost.Value}, есть {mGold.Value}. " +
                "Можно починить вещи по одной.");
            return;
        }

        mNotifications.ShowConfirm(
            message: $"Починить вещей: {count}. Стоимость: {mTotalCost.Value} золота.\n" +
                     "У каждой вещи максимальная прочность уменьшится на 1.",
            onConfirm: RepairAll,
            onCancel: null,
            title: "Починить всё",
            confirmLabel: "Починить",
            cancelLabel: "Отмена",
            type: NotificationType.Warning);
    }

    /// <summary>Починить одну вещь — с подтверждением и ценой из расчёта сервера.</summary>
    public void RequestRepairOne(long instanceId)
    {
        var item = FindItem(instanceId);
        if (item == null) return;

        mNotifications.ShowConfirm(
            message: $"Починить «{item.Name}» за {item.Cost} золота?\n" +
                     $"Максимальная прочность: {item.DurabilityMax} → {item.DurabilityMaxAfter}.",
            onConfirm: () => RepairOne(instanceId),
            onCancel: null,
            title: "Починить вещь",
            confirmLabel: "Починить",
            cancelLabel: "Отмена",
            type: NotificationType.Warning);
    }

    private void RepairAll() =>
        RunMutation(async ct =>
        {
            var result = await mService.RepairAllAsync(ct);
            string skipped = result.SkippedWornOut > 0
                ? $" Пропущено изношенных: {result.SkippedWornOut}."
                : string.Empty;
            mNotifications.ShowInfo(
                $"Починено вещей: {result.Items?.Count ?? 0}. Списано: {result.GoldSpent} золота.{skipped}");
        }).Forget();

    private void RepairOne(long instanceId) =>
        RunMutation(async ct =>
        {
            var result = await mService.RepairAsync(instanceId, ct);
            mNotifications.ShowInfo($"Починено. Списано золота: {result.GoldSpent}.");
        }).Forget();

    // ─── Внутреннее ───────────────────────────────────────────────────────────

    private async UniTask RefreshAsync(CancellationToken ct)
    {
        mIsLoading.Value = true;
        try
        {
            var quote = await mService.GetRepairQuoteAsync(ct);
            Apply(quote);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mNotifications.ShowError(ApiMessage(ex));
            Debug.LogWarning($"[BlacksmithPresenter] Refresh: {ex.Message}");
        }
        finally { mIsLoading.Value = false; }
    }

    private void Apply(RepairQuoteResponseDto quote)
    {
        mItems.Value = quote.Items ?? new List<RepairQuoteItemDto>();
        mTotalCost.Value = quote.TotalCost;
        mGold.Value = quote.GoldAvailable;
        mCanAffordAll.Value = quote.CanAffordAll;
        mSkippedWornOut.Value = quote.SkippedWornOut;
    }

    private RepairQuoteItemDto FindItem(long instanceId)
    {
        var list = mItems.Value;
        if (list == null) return null;

        for (int i = 0; i < list.Count; i++)
            if (list[i].InstanceId == instanceId)
                return list[i];

        return null;
    }

    private async UniTask RunMutation(Func<CancellationToken, UniTask> mutation)
    {
        if (mIsLoading.Value) return;

        mIsLoading.Value = true;
        try
        {
            await mutation(mLifetimeCts.Token);
            // Перечитываем расчёт: после ремонта изменились и прочность, и золото, и сам состав
            // списка. Пересчитывать его на клиенте нельзя — разъедется с сервером.
            await RefreshAsync(mLifetimeCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mNotifications.ShowError(ApiMessage(ex));
            Debug.LogWarning($"[BlacksmithPresenter] Mutation: {ex.Message}");
        }
        finally { mIsLoading.Value = false; }
    }

    private void OnCombatStateChanged(bool inCombat)
    {
        if (inCombat && mIsOpen.Value)
            Close();
    }

    /// <summary>Достаёт человекочитаемое сообщение из ApiException (текст ошибки сервера).</summary>
    private static string ApiMessage(Exception ex)
        => ex is ApiException api && !string.IsNullOrEmpty(api.ServerError)
            ? api.ServerError
            : "Не удалось выполнить действие.";
}
