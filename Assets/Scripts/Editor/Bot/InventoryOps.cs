using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

/// <summary>
/// Операции с инвентарём/сундуком.
///
/// РАЗДЕЛЕНИЕ:
///   — ЧТЕНИЯ (что лежит в рюкзаке/сундуке) — через IInventoryService напрямую:
///     авторитетно, await-абельно, без угадывания «загрузилось ли».
///   — МУТАЦИИ (надеть/снять/сундук/выброс) — через InventoryPresenter: так его
///     реактивное состояние остаётся когерентным и действия видны в UI.
///
/// ПРОВЕРКА ИСХОДА МУТАЦИИ (важно, изменено при верификации после Фазы 5):
///   Раньше отказ сервера читался из InventoryPresenter.ErrorMessage. Начиная с
///   Фазы 5 (миграция на тосты, TD-C10) этого реактивного свойства больше нет —
///   ошибки уходят прямо тостом через INotificationService и не оседают в
///   состоянии презентора (см. RunMutation в InventoryPresenter: при исключении
///   он вызывает ShowError и НЕ обновляет свои списки).
///   Поэтому здесь используется другой авторитетный источник: после мутации
///   заново запрашиваем IInventoryService (и сундук, если нужно) и сверяем поле
///   Container конкретного предмета (backpack/equipped/chest) с тем, что должно
///   было получиться. Если сервер отклонил операцию — Container не поменяется,
///   и это видно напрямую, без парсинга текста ошибки.
///
/// Пишет в канал Inventory; после каждой мутации — пауза (если включена).
/// </summary>
public static class InventoryOps
{
    private const string CONTAINER_EQUIPPED = "equipped";
    private const string CONTAINER_BACKPACK = "backpack";
    private const string CONTAINER_CHEST = "chest";

    // ─── Экипировка по сету / коду ────────────────────────────────────────────

    /// <summary>Надеть из рюкзака все вещи указанного сета (по SetId).</summary>
    public static async UniTask EquipSetAsync(BotContext ctx, long setId)
    {
        await EnsurePanelOpenAsync(ctx);
        var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);

        var items = inv.Backpack
            .Where(i => i.SetId == setId && !string.IsNullOrEmpty(i.SlotCategory)) // SlotCategory null = расходка
            .ToList();

        if (items.Count == 0)
        {
            ctx.Log.Warn(BotChannel.Inventory, $"В рюкзаке нет вещей сета #{setId} — надевать нечего.");
            return;
        }

        foreach (var item in items)
            await EquipOneAsync(ctx, item.InstanceId, item.Name);
    }

    /// <summary>Надеть из рюкзака одну вещь по коду.</summary>
    public static async UniTask EquipItemAsync(BotContext ctx, string code)
    {
        await EnsurePanelOpenAsync(ctx);
        var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);

        var item = inv.Backpack.FirstOrDefault(i => i.Code == code);
        if (item == null)
        {
            ctx.Log.Warn(BotChannel.Inventory, $"В рюкзаке нет предмета с кодом «{code}».");
            return;
        }

        await EquipOneAsync(ctx, item.InstanceId, item.Name);
    }

    /// <summary>Снять всё надетое в рюкзак.</summary>
    public static async UniTask UnequipAllAsync(BotContext ctx)
    {
        await EnsurePanelOpenAsync(ctx);
        var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);

        // Копируем id заранее — список презентора будет меняться при каждой мутации.
        var ids = inv.Equipped.Select(i => (i.InstanceId, i.Name)).ToList();
        if (ids.Count == 0)
        {
            ctx.Log.Info(BotChannel.Inventory, "Снимать нечего — экипировка пуста.");
            return;
        }

        foreach (var (id, name) in ids)
        {
            var err = await MutateAsync(ctx, () => ctx.Inventory.Unequip(id), id, CONTAINER_BACKPACK);
            LogMutation(ctx, err, $"снял «{name}»", $"не снял «{name}»");
        }
    }

    // ─── Сундук ────────────────────────────────────────────────────────────────

    /// <summary>Сложить в сундук все вещи сета из рюкзака. Требует локацию с сундуком.</summary>
    public static async UniTask DepositSetToChestAsync(BotContext ctx, long setId)
    {
        if (!await EnsureChestHereAsync(ctx)) return;

        var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);
        var items = inv.Backpack.Where(i => i.SetId == setId).ToList();
        if (items.Count == 0)
        {
            ctx.Log.Warn(BotChannel.Inventory, $"В рюкзаке нет вещей сета #{setId} — складывать нечего.");
            return;
        }

        foreach (var item in items)
        {
            var err = await MutateAsync(ctx, () => ctx.Inventory.Deposit(item.InstanceId), item.InstanceId, CONTAINER_CHEST);
            LogMutation(ctx, err, $"в сундук: «{item.Name}»", $"не положил «{item.Name}»");
        }
    }

    /// <summary>Достать из сундука все вещи сета в рюкзак. Требует локацию с сундуком.</summary>
    public static async UniTask WithdrawSetFromChestAsync(BotContext ctx, long setId)
    {
        if (!await EnsureChestHereAsync(ctx)) return;

        var chest = await ctx.InventoryService.GetChestAsync(ctx.Ct);
        var items = (chest.Items ?? new List<InventoryItemDto>())
            .Where(i => i.SetId == setId).ToList();
        if (items.Count == 0)
        {
            ctx.Log.Warn(BotChannel.Inventory, $"В сундуке нет вещей сета #{setId} — доставать нечего.");
            return;
        }

        foreach (var item in items)
        {
            var err = await MutateAsync(ctx, () => ctx.Inventory.Withdraw(item.InstanceId), item.InstanceId, CONTAINER_BACKPACK);
            LogMutation(ctx, err, $"из сундука: «{item.Name}»", $"не достал «{item.Name}»");
        }
    }

    // ─── Внутреннее ────────────────────────────────────────────────────────────

    private static async UniTask EquipOneAsync(BotContext ctx, long instanceId, string name)
    {
        var err = await MutateAsync(ctx, () => ctx.Inventory.Equip(instanceId), instanceId, CONTAINER_EQUIPPED);
        LogMutation(ctx, err, $"надел «{name}»", $"не надел «{name}»");
    }

    /// <summary>
    /// Выполнить одну мутацию: дождаться простоя → выстрелить → дождаться завершения →
    /// авторитетно проверить итог по Container предмета → вернуть текст ошибки (или null
    /// при успехе). После — пауза (если включена).
    /// </summary>
    private static async UniTask<string> MutateAsync(
        BotContext ctx, Action fire, long instanceId, string expectedContainer)
    {
        var inv = ctx.Inventory;

        await BotWait.Until(() => !inv.IsLoading.Value, BotConfig.INVENTORY_TIMEOUT, ctx.Ct);

        fire(); // презентор синхронно ставит IsLoading=true

        bool done = await BotWait.Until(() => !inv.IsLoading.Value, BotConfig.INVENTORY_TIMEOUT, ctx.Ct);

        await ctx.PauseAfterActionAsync();

        if (!done) return "таймаут операции";

        var actualContainer = await GetItemContainerAsync(ctx, instanceId);
        if (actualContainer == expectedContainer) return null;

        return actualContainer == null
            ? "предмет не найден после операции (отклонён сервером?)"
            : $"сервер отклонил операцию — предмет остался в «{actualContainer}»";
    }

    /// <summary>
    /// Авторитетно узнать, где сейчас лежит предмет (backpack/equipped/chest).
    /// null — предмет не нашёлся нигде (неожиданный случай, тоже трактуется как отказ).
    /// </summary>
    private static async UniTask<string> GetItemContainerAsync(BotContext ctx, long instanceId)
    {
        var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);
        var item = inv.Equipped.FirstOrDefault(i => i.InstanceId == instanceId)
                   ?? inv.Backpack.FirstOrDefault(i => i.InstanceId == instanceId);
        if (item != null) return item.Container;

        // Не нашли в рюкзаке/экипировке — проверяем сундук (актуально для Deposit/Withdraw).
        var chest = await ctx.InventoryService.GetChestAsync(ctx.Ct);
        var chestItem = (chest.Items ?? new List<InventoryItemDto>())
            .FirstOrDefault(i => i.InstanceId == instanceId);
        return chestItem?.Container;
    }

    private static void LogMutation(BotContext ctx, string error, string okMsg, string failMsg)
    {
        if (error == null)
        {
            ctx.Log.Info(BotChannel.Inventory, okMsg);
        }
        else
        {
            ctx.Log.Warn(BotChannel.Inventory, $"{failMsg}: {error}");
            ctx.Stats.Rejections++;
        }
    }

    /// <summary>Открыть панель инвентаря (для наглядности) и дождаться загрузки.</summary>
    private static async UniTask EnsurePanelOpenAsync(BotContext ctx)
    {
        if (!ctx.Inventory.IsOpen.Value)
            ctx.Inventory.Open(); // no-op, если идёт бой — но инвентарные шаги вне боя

        await BotWait.Until(() => !ctx.Inventory.IsLoading.Value, BotConfig.LOAD_TIMEOUT, ctx.Ct);
    }

    /// <summary>Убедиться, что в текущей локации есть сундук. Иначе — предупредить и вернуть false.</summary>
    private static async UniTask<bool> EnsureChestHereAsync(BotContext ctx)
    {
        await EnsurePanelOpenAsync(ctx);

        // Авторитетно спрашиваем сервер о доступности/содержимом сундука здесь.
        var chest = await ctx.InventoryService.GetChestAsync(ctx.Ct);
        if (!chest.Available)
        {
            ctx.Log.Warn(BotChannel.Inventory, "В этой локации сундук недоступен — операция с сундуком пропущена.");
            return false;
        }

        // Переключим вкладку на сундук, чтобы игрок видел его в UI.
        ctx.Inventory.SelectTab(InventoryTab.Chest);
        return true;
    }
}
