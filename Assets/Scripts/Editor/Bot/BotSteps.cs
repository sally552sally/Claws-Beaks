using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Один шаг сценария. Реализация читает всё нужное из BotContext (в т.ч. токен отмены ctx.Ct).
/// Добавить новую операцию боту = добавить класс-шаг сюда + метод в BotScenarioBuilder.
/// Если шаг может проверить себя до запуска — дополнительно реализуй IBotStepValidator.
/// </summary>
public interface IBotStep
{
    /// <summary>Человекочитаемое описание для лога/окна.</summary>
    string Describe { get; }

    UniTask ExecuteAsync(BotContext ctx);
}

/// <summary>Общий помощник для проверок (ассертов): статистика, лог, скриншот при провале.</summary>
public static class BotAssert
{
    public static void Report(BotContext ctx, bool passed, string what, string failDetail)
    {
        if (passed)
        {
            ctx.Stats.AssertsPassed++;
            ctx.Log.Info(BotChannel.Assert, $"✓ {what}");
        }
        else
        {
            ctx.Stats.AssertsFailed++;
            ctx.Log.Error(BotChannel.Assert, $"✗ {what} — {failDetail}");
            if (ctx.Options.ScreenshotOnError)
                ctx.CaptureScreenshot("assert");
        }
    }
}

// ─── Навигация ──────────────────────────────────────────────────────────────────

/// <summary>Дойти до локации по её коду (путь строится сам по карте).</summary>
public sealed class GoToStep : IBotStep, IBotStepValidator
{
    private readonly string mCode;
    public GoToStep(string code) => mCode = code;
    public string Describe => $"Идти в «{mCode}»";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        // GoToAsync сам логирует, дошли/не дошли; bool здесь не нужен.
        await NavigationOps.GoToAsync(ctx, mCode);
    }

    public UniTask<string> ValidateAsync(BotDryRunState state)
    {
        if (!state.LocationExists(mCode))
            return UniTask.FromResult($"локации «{mCode}» нет на карте (опечатка в коде?)");

        if (!state.PathExists(state.SimulatedLocationCode, mCode))
            return UniTask.FromResult($"нет пути из «{state.SimulatedLocationCode}» до «{mCode}»");

        state.SimulatedLocationCode = mCode; // двигаем симуляцию для следующих шагов
        return UniTask.FromResult<string>(null);
    }
}

// ─── Бой ────────────────────────────────────────────────────────────────────────

/// <summary>
/// Убить N мобов в текущей локации. Сам ждёт респавн, если живых мобов нет.
/// При смерти (воскрешение в городе) фарм в этой локации прерывается с логом;
/// если включено «стоп при смерти» — запрашивается остановка всего прогона.
/// </summary>
public sealed class KillMobsStep : IBotStep
{
    private const int MAX_REJECTED_ATTEMPTS = 5;

    private readonly int mCount;
    private readonly ICombatPolicy mPolicy;

    public KillMobsStep(int count, ICombatPolicy policy)
    {
        mCount = count;
        mPolicy = policy ?? new SimpleCombatPolicy();
    }

    public string Describe => $"Убить {mCount} моб(ов) [{mPolicy.Name}]";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        // Для наглядности откроем экран охоты.
        ctx.Location.OpenHunting();

        int killed = 0;
        int rejectedInARow = 0;
        ctx.Progress.Detail = $"0/{mCount}";

        while (killed < mCount)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            if (ctx.StopRequested) return;

            var mob = await WaitForAliveMobAsync(ctx);
            if (mob == null)
            {
                ctx.Log.Warn(BotChannel.Combat, $"Живых мобов нет и респавна не будет — прерываю (убито {killed}/{mCount}).");
                break;
            }

            var outcome = await CombatOps.FightMobAsync(ctx, mob.SpawnId, mPolicy);

            switch (outcome)
            {
                case FightOutcome.Win:
                    killed++;
                    rejectedInARow = 0;
                    ctx.Progress.Detail = $"{killed}/{mCount}";
                    ctx.Log.Info(BotChannel.Combat, $"Прогресс: {killed}/{mCount}.");
                    break;

                case FightOutcome.Lost:
                    ctx.Log.Warn(BotChannel.Combat, "Погиб — воскрешён в городе, фарм в этой локации прерван.");
                    if (ctx.Options.StopOnDeath)
                        ctx.RequestStop("смерть персонажа (включено «стоп при смерти»)");
                    return;

                case FightOutcome.Rejected:
                    if (++rejectedInARow >= MAX_REJECTED_ATTEMPTS)
                    {
                        ctx.Log.Warn(BotChannel.Combat, "Слишком много отказов подряд — прерываю шаг.");
                        return;
                    }
                    break;

                case FightOutcome.Timeout:
                    ctx.Log.Warn(BotChannel.Combat, "Бой прерван по таймауту — прерываю шаг.");
                    return;
            }
        }

        ctx.Log.Info(BotChannel.Combat, $"Готово: убито {killed}/{mCount}.");
    }

    /// <summary>
    /// Вернуть живого моба. Если живых нет — ждём ближайший респавн, периодически рефрешим.
    /// null — мобов в локации нет вовсе (или респавн неизвестен).
    /// </summary>
    private static async UniTask<MobSpawnDto> WaitForAliveMobAsync(BotContext ctx)
    {
        while (true)
        {
            ctx.Ct.ThrowIfCancellationRequested();

            await ctx.Location.RefreshAsync(ctx.Ct);
            await BotWait.Until(() => !ctx.Location.IsLoading.Value, BotConfig.LOAD_TIMEOUT, ctx.Ct);

            var mobs = ctx.Location.Mobs.Value ?? new List<MobSpawnDto>();
            var alive = mobs.FirstOrDefault(m => m.State == "alive");
            if (alive != null) return alive;

            // Живых нет. Есть ли у кого-то время респавна?
            var nextRespawn = mobs
                .Where(m => m.RespawnAt.HasValue)
                .Select(m => m.RespawnAt.Value)
                .DefaultIfEmpty(DateTime.MinValue)
                .Min();

            if (nextRespawn == DateTime.MinValue)
                return null; // мобов/респавна нет

            var wait = nextRespawn - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                ctx.Log.Info(BotChannel.Combat, $"Мобы кончились — жду респавн (~{(int)wait.TotalSeconds}с)…");
                await BotWait.UntilForever(
                    () => DateTime.UtcNow >= nextRespawn, ctx.Ct,
                    heartbeat: () =>
                    {
                        var left = (int)(nextRespawn - DateTime.UtcNow).TotalSeconds;
                        if (left > 0) ctx.Log.Info(BotChannel.Combat, $"…респавн через {left}с");
                    });
            }
            // Небольшая пауза и повторный рефреш (сервер должен поднять моба).
            await UniTask.Delay(500, cancellationToken: ctx.Ct);
        }
    }
}

/// <summary>
/// Атаковать игрока по нику (PvP) и провести один бой. Ник ищется среди игроков,
/// видимых прямо сейчас в текущей локации (ctx.Location.Players) — так же, как это
/// делает игрок глазами в списке Охоты. Если игрока сейчас нет в локации — шаг НЕ
/// ждёт и не ищет по карте (в отличие от KillMobsStep с респавном мобов): PvP-цель
/// либо онлайн рядом прямо сейчас, либо атаки не будет — лог + шаг завершается.
/// </summary>
public sealed class AttackPlayerStep : IBotStep
{
    private readonly string mNickname;
    private readonly ICombatPolicy mPolicy;

    public AttackPlayerStep(string nickname, ICombatPolicy policy)
    {
        mNickname = nickname;
        mPolicy = policy ?? new SimpleCombatPolicy();
    }

    public string Describe => $"Атаковать игрока «{mNickname}» [{mPolicy.Name}]";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        await ctx.Location.RefreshAsync(ctx.Ct);
        await BotWait.Until(() => !ctx.Location.IsLoading.Value, BotConfig.LOAD_TIMEOUT, ctx.Ct);

        var target = (ctx.Location.Players.Value ?? new List<PlayerInLocationDto>())
            .FirstOrDefault(p => string.Equals(p.Nickname, mNickname, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            ctx.Log.Warn(BotChannel.Combat, $"Игрока «{mNickname}» нет в текущей локации — атака пропущена.");
            return;
        }

        var outcome = await CombatOps.FightPlayerAsync(ctx, target.CharacterId, mPolicy);

        switch (outcome)
        {
            case FightOutcome.Win:
                ctx.Log.Info(BotChannel.Combat, $"PvP-бой против «{mNickname}» выигран.");
                break;

            case FightOutcome.Lost:
                ctx.Log.Warn(BotChannel.Combat, $"PvP-бой против «{mNickname}» проигран.");
                if (ctx.Options.StopOnDeath)
                    ctx.RequestStop("смерть персонажа в PvP (включено «стоп при смерти»)");
                break;

            case FightOutcome.Rejected:
                ctx.Log.Warn(BotChannel.Combat, $"Сервер отклонил PvP-атаку на «{mNickname}» " +
                                                 "(цель вышла из локации/уже в бою/недоступна).");
                break;

            case FightOutcome.Timeout:
                ctx.Log.Warn(BotChannel.Combat, $"PvP-бой против «{mNickname}» прерван по таймауту.");
                break;
        }
    }
}

// ─── Инвентарь ────────────────────────────────────────────────────────────────

/// <summary>Надеть сет по SetId.</summary>
public sealed class EquipSetStep : IBotStep, IBotStepValidator
{
    private readonly long mSetId;
    public EquipSetStep(long setId) => mSetId = setId;
    public string Describe => $"Одеть сет #{mSetId}";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.EquipSetAsync(ctx, mSetId);

    public UniTask<string> ValidateAsync(BotDryRunState state)
    {
        bool inBackpack = state.Inventory.Backpack.Any(i => i.SetId == mSetId);
        if (inBackpack) return UniTask.FromResult<string>(null);

        bool equipped = state.Inventory.Equipped.Any(i => i.SetId == mSetId);
        if (equipped) return UniTask.FromResult<string>(null); // уже надет — не ошибка

        bool inChest = state.Chest?.Items != null && state.Chest.Items.Any(i => i.SetId == mSetId);
        if (inChest)
            return UniTask.FromResult($"вещи сета #{mSetId} лежат в сундуке, не в рюкзаке — " +
                                      "не забудь WithdrawSetFromChest перед этим шагом");

        return UniTask.FromResult($"вещей сета #{mSetId} нет ни в рюкзаке, ни в сундуке");
    }
}

/// <summary>Надеть одну вещь по коду.</summary>
public sealed class EquipItemStep : IBotStep, IBotStepValidator
{
    private readonly string mCode;
    public EquipItemStep(string code) => mCode = code;
    public string Describe => $"Одеть предмет «{mCode}»";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.EquipItemAsync(ctx, mCode);

    public UniTask<string> ValidateAsync(BotDryRunState state)
    {
        bool found = state.Inventory.Backpack.Any(i => i.Code == mCode)
                     || state.Inventory.Equipped.Any(i => i.Code == mCode);
        return UniTask.FromResult(found ? null : $"предмета «{mCode}» нет в рюкзаке");
    }
}

/// <summary>Снять всё надетое в рюкзак.</summary>
public sealed class UnequipAllStep : IBotStep
{
    public string Describe => "Снять всё";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.UnequipAllAsync(ctx);
}

/// <summary>Сложить сет в сундук (нужна локация с сундуком).</summary>
public sealed class DepositSetStep : IBotStep, IBotStepValidator
{
    private readonly long mSetId;
    public DepositSetStep(long setId) => mSetId = setId;
    public string Describe => $"Сет #{mSetId} → в сундук";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.DepositSetToChestAsync(ctx, mSetId);

    public UniTask<string> ValidateAsync(BotDryRunState state)
    {
        bool inBackpack = state.Inventory.Backpack.Any(i => i.SetId == mSetId);
        if (inBackpack) return UniTask.FromResult<string>(null);

        bool equipped = state.Inventory.Equipped.Any(i => i.SetId == mSetId);
        if (equipped)
            return UniTask.FromResult($"вещи сета #{mSetId} сейчас надеты — " +
                                      "перед складыванием нужен UnequipAll");

        bool inChest = state.Chest?.Items != null && state.Chest.Items.Any(i => i.SetId == mSetId);
        if (inChest) return UniTask.FromResult<string>(null); // уже в сундуке — не ошибка

        return UniTask.FromResult($"вещей сета #{mSetId} нет ни в рюкзаке, ни надетыми");
    }
}

/// <summary>Достать сет из сундука (нужна локация с сундуком).</summary>
public sealed class WithdrawSetStep : IBotStep, IBotStepValidator
{
    private readonly long mSetId;
    public WithdrawSetStep(long setId) => mSetId = setId;
    public string Describe => $"Сет #{mSetId} ← из сундука";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.WithdrawSetFromChestAsync(ctx, mSetId);

    public UniTask<string> ValidateAsync(BotDryRunState state)
    {
        if (state.Chest == null)
            return UniTask.FromResult("не могу проверить содержимое сундука из текущей локации " +
                                      "(сундук здесь недоступен) — проверь SetId вручную");

        bool inChest = state.Chest.Items != null && state.Chest.Items.Any(i => i.SetId == mSetId);
        if (inChest) return UniTask.FromResult<string>(null);

        bool inBackpack = state.Inventory.Backpack.Any(i => i.SetId == mSetId)
                          || state.Inventory.Equipped.Any(i => i.SetId == mSetId);
        if (inBackpack) return UniTask.FromResult<string>(null); // положим этим же сценарием — ок

        return UniTask.FromResult($"вещей сета #{mSetId} нет ни в сундуке, ни у персонажа");
    }
}

// ─── Проверки (ассерты) ─────────────────────────────────────────────────────────

/// <summary>Проверить: текущая локация = ожидаемая.</summary>
public sealed class AssertLocationStep : IBotStep, IBotStepValidator
{
    private readonly string mCode;
    public AssertLocationStep(string code) => mCode = code;
    public string Describe => $"Проверка: локация = «{mCode}»";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        var current = await ctx.LocationService.GetCurrentAsync(ctx.Ct);
        BotAssert.Report(ctx, current.Code == mCode, Describe, $"факт: «{current.Code}» ({current.Name})");
    }

    public UniTask<string> ValidateAsync(BotDryRunState state)
        => UniTask.FromResult(state.LocationExists(mCode) ? null : $"локации «{mCode}» нет на карте");
}

/// <summary>
/// Проверить: надет сет (минимум minItems вещей сета и НИ ОДНОЙ надетой вещи чужого сета;
/// вещи без сета допускаются).
/// </summary>
public sealed class AssertEquippedSetStep : IBotStep
{
    private readonly long mSetId;
    private readonly int mMinItems;

    public AssertEquippedSetStep(long setId, int minItems)
    {
        mSetId = setId;
        mMinItems = minItems;
    }

    public string Describe => $"Проверка: надет сет #{mSetId} (≥{mMinItems} вещ.)";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);
        int ours = inv.Equipped.Count(i => i.SetId == mSetId);
        int foreign = inv.Equipped.Count(i => i.SetId.HasValue && i.SetId != mSetId);

        bool passed = ours >= mMinItems && foreign == 0;
        BotAssert.Report(ctx, passed, Describe,
            $"надето вещей сета: {ours}, чужих сетовых вещей: {foreign}");
    }
}

/// <summary>Проверить: в рюкзаке есть предмет с кодом.</summary>
public sealed class AssertBackpackContainsStep : IBotStep
{
    private readonly string mCode;
    public AssertBackpackContainsStep(string code) => mCode = code;
    public string Describe => $"Проверка: в рюкзаке есть «{mCode}»";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);
        bool found = inv.Backpack.Any(i => i.Code == mCode);
        BotAssert.Report(ctx, found, Describe, "предмет не найден");
    }
}

/// <summary>Проверить: в сундуке есть вещи сета (требует локацию с сундуком).</summary>
public sealed class AssertChestContainsStep : IBotStep
{
    private readonly long mSetId;
    public AssertChestContainsStep(long setId) => mSetId = setId;
    public string Describe => $"Проверка: в сундуке есть сет #{mSetId}";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        ChestResponseDto chest;
        try { chest = await ctx.InventoryService.GetChestAsync(ctx.Ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            BotAssert.Report(ctx, false, Describe, $"сундук не прочитался: {ex.Message}");
            return;
        }

        if (!chest.Available)
        {
            BotAssert.Report(ctx, false, Describe, "сундук недоступен в этой локации");
            return;
        }

        int count = chest.Items?.Count(i => i.SetId == mSetId) ?? 0;
        BotAssert.Report(ctx, count > 0, Describe, "вещей сета в сундуке нет");
    }
}

// ─── Сервисные ────────────────────────────────────────────────────────────────

/// <summary>Снимок состояния в лог: где мы, рюкзак, счётчики.</summary>
public sealed class SnapshotStep : IBotStep
{
    private readonly string mLabel;
    public SnapshotStep(string label) => mLabel = label;
    public string Describe => $"Снимок: {mLabel}";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        string where = ctx.Location.LocationName.Value;
        int level = ctx.Location.LocationLevel.Value;

        int used = -1, capacity = -1;
        try
        {
            var inv = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);
            used = inv.BackpackUsed;
            capacity = inv.BackpackCapacity;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* снимок не критичен */ }

        var backpack = used >= 0 ? $"Рюкзак: {used}/{capacity}" : "Рюкзак: н/д";
        ctx.Log.Snapshot($"[{mLabel}] Локация: «{where}» ур.{level} | {backpack} | {ctx.Stats.Summary()}");
    }
}

/// <summary>Просто подождать N секунд (иногда полезно вставить паузу).</summary>
public sealed class WaitStep : IBotStep
{
    private readonly float mSeconds;
    public WaitStep(float seconds) => mSeconds = seconds;
    public string Describe => $"Пауза {mSeconds:0.#}с";

    public async UniTask ExecuteAsync(BotContext ctx)
        => await UniTask.Delay(TimeSpan.FromSeconds(mSeconds), cancellationToken: ctx.Ct);
}

/// <summary>Повторить блок шагов N раз.</summary>
public sealed class RepeatStep : IBotStep
{
    private readonly int mTimes;
    private readonly IReadOnlyList<IBotStep> mSteps;

    public RepeatStep(int times, IReadOnlyList<IBotStep> steps)
    {
        mTimes = times;
        mSteps = steps;
    }

    /// <summary>Вложенные шаги (нужно сухому прогону для рекурсивного обхода).</summary>
    public IReadOnlyList<IBotStep> InnerSteps => mSteps;

    public string Describe => $"Повторить x{mTimes} ({mSteps.Count} шаг.)";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        for (int i = 1; i <= mTimes; i++)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            if (ctx.StopRequested) return;

            ctx.Log.Info(BotChannel.System, $"— повтор {i}/{mTimes} —");
            foreach (var step in mSteps)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                if (ctx.StopRequested) return;

                ctx.Log.Step(step.Describe);
                await step.ExecuteAsync(ctx);
                await ctx.PauseAfterActionAsync();
            }
        }
    }
}

// ─── UI-смоук ────────────────────────────────────────────────────────────────

/// <summary>
/// Лёгкая проверка проводки UI: найти GameObject по имени и проверить активность.
/// Не кликает кнопки (это хрупко) — проверяет, что View отреагировал на команду презентора.
/// Пример: после открытия инвентаря Panel_Inventory должна стать активной.
/// Провал считается проваленной проверкой (идёт в статистику ассертов).
/// </summary>
public sealed class VerifyPanelStep : IBotStep
{
    private readonly string mObjectName;
    private readonly bool mShouldBeActive;

    public VerifyPanelStep(string objectName, bool shouldBeActive)
    {
        mObjectName = objectName;
        mShouldBeActive = shouldBeActive;
    }

    public string Describe => $"Проверить панель «{mObjectName}» = {(mShouldBeActive ? "видна" : "скрыта")}";

    public UniTask ExecuteAsync(BotContext ctx)
    {
        var go = FindByName(mObjectName);
        if (go == null)
        {
            BotAssert.Report(ctx, false, Describe, "объект не найден в сцене");
            return UniTask.CompletedTask;
        }

        bool active = go.activeInHierarchy;
        BotAssert.Report(ctx, active == mShouldBeActive, Describe,
            $"факт: {(active ? "видна" : "скрыта")}");

        return UniTask.CompletedTask;
    }

    /// <summary>Ищет объект по имени, включая неактивные (обычный Find их не находит).</summary>
    private static GameObject FindByName(string name)
    {
        var all = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
            if (t.name == name) return t.gameObject;
        return null;
    }
}
