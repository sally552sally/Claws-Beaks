using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Presenter экрана инвентаря (Panel_Inventory в Game-сцене).
///
/// Отвечает за:
///   — открытие/закрытие панели (IsOpen);
///   — активную вкладку (ActiveTab) из расширяемого списка категорий (см. InventoryTab);
///   — данные: надетое, рюкзак, сундук, расходка (стаки);
///   — команды: надеть/снять/починить/выбросить/положить в сундук/достать;
///   — авто-закрытие, если стартовал бой (CombatPresenter.IsInCombat).
///
/// АРХИТЕКТУРА (UnityStyle):
///   — чистый C#, без using UnityEngine логики (Debug допустим как и в CombatPresenter);
///   — View подписывается на реактивное состояние и зовёт публичные команды.
///
/// БЕЗОПАСНОСТЬ:
///   — instanceId всегда из серверного ответа (InventoryItemDto), не из клиентского кода;
///   — все проверки (в бою / уровень / доступность сундука) дублируются сервером;
///   — клиентские проверки — только для UX.
///
/// РАСШИРЯЕМОСТЬ (§6.8 — «будут добавляться другие категории»):
///   — вкладки описаны enum InventoryTab + Tabs (список) → добавить категорию = добавить
///     значение в enum и в Tabs, View строит кнопки по списку, без правки разметки.
/// </summary>
public sealed class InventoryPresenter : DisposableObject, IInitializable
{
    // ─── Реактивное состояние ─────────────────────────────────────────────────

    private readonly Reactive<bool> mIsOpen = new(false);
    private readonly Reactive<bool> mIsLoading = new(false);
    private readonly Reactive<string> mErrorMessage = new(string.Empty);
    private readonly Reactive<string> mInfoMessage = new(string.Empty);

    private readonly Reactive<InventoryTab> mActiveTab = new(InventoryTab.Equipment);

    private readonly Reactive<List<InventoryItemDto>> mEquipped = new(new List<InventoryItemDto>());
    private readonly Reactive<List<InventoryItemDto>> mBackpack = new(new List<InventoryItemDto>());
    private readonly Reactive<List<InventoryItemDto>> mChest = new(new List<InventoryItemDto>());
    private readonly Reactive<List<ConsumableStackDto>> mStacks = new(new List<ConsumableStackDto>());

    private readonly Reactive<int> mBackpackCapacity = new(0);
    private readonly Reactive<int> mBackpackUsed = new(0);
    private readonly Reactive<bool> mChestAvailableHere = new(false);

    /// <summary>Предмет, открытый в Popup_ItemDetail (null — попап закрыт).</summary>
    private readonly Reactive<InventoryItemDto> mSelectedItem = new(null);

    public ReadonlyReactive<bool> IsOpen => mIsOpen.Readonly;
    public ReadonlyReactive<bool> IsLoading => mIsLoading.Readonly;
    public ReadonlyReactive<string> ErrorMessage => mErrorMessage.Readonly;
    public ReadonlyReactive<string> InfoMessage => mInfoMessage.Readonly;
    public ReadonlyReactive<InventoryTab> ActiveTab => mActiveTab.Readonly;
    public ReadonlyReactive<List<InventoryItemDto>> Equipped => mEquipped.Readonly;
    public ReadonlyReactive<List<InventoryItemDto>> Backpack => mBackpack.Readonly;
    public ReadonlyReactive<List<InventoryItemDto>> Chest => mChest.Readonly;
    public ReadonlyReactive<List<ConsumableStackDto>> Stacks => mStacks.Readonly;
    public ReadonlyReactive<int> BackpackCapacity => mBackpackCapacity.Readonly;
    public ReadonlyReactive<int> BackpackUsed => mBackpackUsed.Readonly;
    public ReadonlyReactive<bool> ChestAvailableHere => mChestAvailableHere.Readonly;
    public ReadonlyReactive<InventoryItemDto> SelectedItem => mSelectedItem.Readonly;

    /// <summary>Список вкладок в порядке показа. Расширяется добавлением значения сюда (и в enum).</summary>
    public IReadOnlyList<InventoryTab> Tabs { get; } = new[]
    {
        InventoryTab.Equipment,
        InventoryTab.Chest,
        InventoryTab.Effects,
        InventoryTab.Resources,
        InventoryTab.Quests
    };

    // ─── Внутреннее состояние ─────────────────────────────────────────────────

    private readonly CancellationTokenSource mLifetimeCts = new();

    private readonly IInventoryService mService;
    private readonly CombatPresenter mCombatPresenter;

    [Inject]
    public InventoryPresenter(IInventoryService service, CombatPresenter combatPresenter)
    {
        mService = service;
        mCombatPresenter = combatPresenter;

        AutoDispose(
            mIsOpen, mIsLoading, mErrorMessage, mInfoMessage, mActiveTab,
            mEquipped, mBackpack, mChest, mStacks,
            mBackpackCapacity, mBackpackUsed, mChestAvailableHere, mSelectedItem);
    }

    // ─── IInitializable ───────────────────────────────────────────────────────

    public void Initialize()
    {
        // Если бой стартовал, пока инвентарь открыт (например PvP-нападение) — закрываем его.
        // Combat-панель и так перекрывает по Sort Order, но открытый инвентарь должен уйти,
        // т.к. в бою менять шмот нельзя.
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

    /// <summary>Открыть инвентарь и загрузить данные. Игнорируется, если идёт бой.</summary>
    public void Open()
    {
        if (mCombatPresenter.IsInCombat.Value) return;
        if (mIsOpen.Value) return;

        mIsOpen.Value = true;
        mActiveTab.Value = InventoryTab.Equipment;
        RefreshAsync(mLifetimeCts.Token).Forget();
    }

    /// <summary>Закрыть инвентарь и попап деталей.</summary>
    public void Close()
    {
        mSelectedItem.Value = null;
        mIsOpen.Value = false;
        mErrorMessage.Value = string.Empty;
        mInfoMessage.Value = string.Empty;
    }

    /// <summary>Переключить вкладку. При открытии вкладки сундука/эффектов догружаем их данные.</summary>
    public void SelectTab(InventoryTab tab)
    {
        mActiveTab.Value = tab;
        mInfoMessage.Value = string.Empty;
        mErrorMessage.Value = string.Empty;

        switch (tab)
        {
            case InventoryTab.Chest:
                LoadChestAsync(mLifetimeCts.Token).Forget();
                break;
            case InventoryTab.Effects:
                LoadStacksAsync(mLifetimeCts.Token).Forget();
                break;
        }
    }

    // ─── Детали предмета ──────────────────────────────────────────────────────

    public void OpenItemDetail(InventoryItemDto item) => mSelectedItem.Value = item;
    public void CloseItemDetail() => mSelectedItem.Value = null;

    // ─── Команды ──────────────────────────────────────────────────────────────

    public void Equip(long instanceId) =>
        RunMutation(ct => mService.EquipAsync(instanceId, ct)).Forget();

    public void Unequip(long instanceId) =>
        RunMutation(ct => mService.UnequipAsync(instanceId, ct)).Forget();

    public void Repair(long instanceId) =>
        RunMutation(async ct =>
        {
            var result = await mService.RepairAsync(instanceId, ct);
            mInfoMessage.Value = $"Починено. Списано золота: {result.GoldSpent}.";
        }).Forget();

    public void Deposit(long instanceId) =>
        RunMutation(ct => mService.DepositToChestAsync(instanceId, ct).AsUniTask()).Forget();

    public void Withdraw(long instanceId) =>
        RunMutation(ct => mService.WithdrawFromChestAsync(instanceId, ct).AsUniTask()).Forget();

    public void Discard(long instanceId) =>
        RunMutation(ct => mService.DiscardAsync(instanceId, ct)).Forget();

    // ─── Внутренняя загрузка ──────────────────────────────────────────────────

    /// <summary>Полное обновление: инвентарь (+ сундук/эффекты, если активна их вкладка).</summary>
    private async UniTask RefreshAsync(CancellationToken ct)
    {
        mIsLoading.Value = true;
        mErrorMessage.Value = string.Empty;
        try
        {
            var inv = await mService.GetInventoryAsync(ct);
            ApplyInventory(inv);

            // Догрузить активную вкладку, если это сундук/эффекты.
            if (mActiveTab.Value == InventoryTab.Chest && mChestAvailableHere.Value)
                await LoadChestAsync(ct);
            else if (mActiveTab.Value == InventoryTab.Effects)
                await LoadStacksAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ApiMessage(ex);
            Debug.LogError($"[InventoryPresenter] Refresh: {ex}");
        }
        finally { mIsLoading.Value = false; }
    }

    private async UniTask LoadChestAsync(CancellationToken ct)
    {
        try
        {
            var chest = await mService.GetChestAsync(ct);
            mChestAvailableHere.Value = chest.Available;
            mChest.Value = chest.Items ?? new List<InventoryItemDto>();
            mBackpackCapacity.Value = chest.BackpackCapacity;
            mBackpackUsed.Value = chest.BackpackUsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ApiMessage(ex);
            Debug.LogWarning($"[InventoryPresenter] LoadChest: {ex.Message}");
        }
    }

    private async UniTask LoadStacksAsync(CancellationToken ct)
    {
        try
        {
            var resp = await mService.GetStacksAsync(ct);
            mStacks.Value = resp?.Stacks ?? new List<ConsumableStackDto>();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ApiMessage(ex);
            Debug.LogWarning($"[InventoryPresenter] LoadStacks: {ex.Message}");
        }
    }

    private void ApplyInventory(InventoryResponseDto inv)
    {
        mEquipped.Value = inv.Equipped ?? new List<InventoryItemDto>();
        mBackpack.Value = inv.Backpack ?? new List<InventoryItemDto>();
        mBackpackCapacity.Value = inv.BackpackCapacity;
        mBackpackUsed.Value = inv.BackpackUsed;
        mChestAvailableHere.Value = inv.ChestAvailableHere;
    }

    /// <summary>
    /// Обёртка мутации: блокирует на время запроса, закрывает попап деталей, перечитывает данные.
    /// Любая ошибка сервера (в бою / нет места / уровень и т.д.) показывается в ErrorMessage.
    /// </summary>
    private async UniTask RunMutation(Func<CancellationToken, UniTask> mutation)
    {
        if (mIsLoading.Value) return;

        mIsLoading.Value = true;
        mErrorMessage.Value = string.Empty;
        mInfoMessage.Value = string.Empty;
        try
        {
            await mutation(mLifetimeCts.Token);
            mSelectedItem.Value = null;       // закрыть Popup_ItemDetail после успешного действия
            await RefreshAsync(mLifetimeCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ApiMessage(ex);
            Debug.LogWarning($"[InventoryPresenter] Mutation: {ex.Message}");
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
