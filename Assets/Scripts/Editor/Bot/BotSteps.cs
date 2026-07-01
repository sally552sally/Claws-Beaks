using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Один шаг сценария. Реализация читает всё нужное из BotContext (в т.ч. токен отмены ctx.Ct).
/// Добавить новую операцию боту = добавить класс-шаг сюда + метод в BotScenarioBuilder.
/// </summary>
public interface IBotStep
{
    /// <summary>Человекочитаемое описание для лога/окна.</summary>
    string Describe { get; }

    UniTask ExecuteAsync(BotContext ctx);
}

// ─── Навигация ──────────────────────────────────────────────────────────────────

/// <summary>Дойти до локации по её коду (путь строится сам по карте).</summary>
public sealed class GoToStep : IBotStep
{
    private readonly string mCode;
    public GoToStep(string code) => mCode = code;
    public string Describe => $"Идти в «{mCode}»";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        // GoToAsync сам логирует, дошли/не дошли; bool здесь не нужен.
        await NavigationOps.GoToAsync(ctx, mCode);
    }
}

// ─── Бой ────────────────────────────────────────────────────────────────────────

/// <summary>
/// Убить N мобов в текущей локации. Сам ждёт респавн, если живых мобов нет.
/// При смерти (воскрешение в городе) фарм в этой локации прерывается с логом.
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

        while (killed < mCount)
        {
            ctx.Ct.ThrowIfCancellationRequested();

            var mob = await WaitForAliveMobAsync(ctx);
            if (mob == null)
            {
                ctx.Log.Warn($"Живых мобов нет и респавна не будет — прерываю (убито {killed}/{mCount}).");
                break;
            }

            var outcome = await CombatOps.FightMobAsync(ctx, mob.SpawnId, mPolicy);

            switch (outcome)
            {
                case FightOutcome.Win:
                    killed++;
                    rejectedInARow = 0;
                    ctx.Log.Info($"Прогресс: {killed}/{mCount}.");
                    break;

                case FightOutcome.Lost:
                    ctx.Log.Warn("Погиб — воскрешён в городе, фарм в этой локации прерван.");
                    return;

                case FightOutcome.Rejected:
                    if (++rejectedInARow >= MAX_REJECTED_ATTEMPTS)
                    {
                        ctx.Log.Warn("Слишком много отказов подряд — прерываю шаг.");
                        return;
                    }
                    break;

                case FightOutcome.Timeout:
                    ctx.Log.Warn("Бой прерван по таймауту — прерываю шаг.");
                    return;
            }
        }

        ctx.Log.Info($"Готово: убито {killed}/{mCount}.");
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
                ctx.Log.Info($"Мобы кончились — жду респавн (~{(int)wait.TotalSeconds}с)…");
                await BotWait.UntilForever(
                    () => DateTime.UtcNow >= nextRespawn, ctx.Ct,
                    heartbeat: () =>
                    {
                        var left = (int)(nextRespawn - DateTime.UtcNow).TotalSeconds;
                        if (left > 0) ctx.Log.Info($"…респавн через {left}с");
                    });
            }
            // Небольшая пауза и повторный рефреш (сервер должен поднять моба).
            await UniTask.Delay(500, cancellationToken: ctx.Ct);
        }
    }
}

// ─── Инвентарь ────────────────────────────────────────────────────────────────

/// <summary>Надеть сет по SetId.</summary>
public sealed class EquipSetStep : IBotStep
{
    private readonly long mSetId;
    public EquipSetStep(long setId) => mSetId = setId;
    public string Describe => $"Одеть сет #{mSetId}";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.EquipSetAsync(ctx, mSetId);
}

/// <summary>Надеть одну вещь по коду.</summary>
public sealed class EquipItemStep : IBotStep
{
    private readonly string mCode;
    public EquipItemStep(string code) => mCode = code;
    public string Describe => $"Одеть предмет «{mCode}»";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.EquipItemAsync(ctx, mCode);
}

/// <summary>Снять всё надетое в рюкзак.</summary>
public sealed class UnequipAllStep : IBotStep
{
    public string Describe => "Снять всё";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.UnequipAllAsync(ctx);
}

/// <summary>Сложить сет в сундук (нужна локация с сундуком).</summary>
public sealed class DepositSetStep : IBotStep
{
    private readonly long mSetId;
    public DepositSetStep(long setId) => mSetId = setId;
    public string Describe => $"Сет #{mSetId} → в сундук";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.DepositSetToChestAsync(ctx, mSetId);
}

/// <summary>Достать сет из сундука (нужна локация с сундуком).</summary>
public sealed class WithdrawSetStep : IBotStep
{
    private readonly long mSetId;
    public WithdrawSetStep(long setId) => mSetId = setId;
    public string Describe => $"Сет #{mSetId} ← из сундука";
    public UniTask ExecuteAsync(BotContext ctx) => InventoryOps.WithdrawSetFromChestAsync(ctx, mSetId);
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

    public string Describe => $"Повторить x{mTimes} ({mSteps.Count} шаг.)";

    public async UniTask ExecuteAsync(BotContext ctx)
    {
        for (int i = 1; i <= mTimes; i++)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            ctx.Log.Info($"— повтор {i}/{mTimes} —");
            foreach (var step in mSteps)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                ctx.Log.Step(step.Describe);
                await step.ExecuteAsync(ctx);
            }
        }
    }
}

// ─── UI-смоук ────────────────────────────────────────────────────────────────

/// <summary>
/// Лёгкая проверка проводки UI: найти GameObject по имени и проверить активность.
/// Не кликает кнопки (это хрупко) — проверяет, что View отреагировал на команду презентора.
/// Пример: после открытия инвентаря Panel_Inventory должна стать активной.
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
            ctx.Log.Warn($"UI-смоук: объект «{mObjectName}» не найден в сцене.");
            return UniTask.CompletedTask;
        }

        bool active = go.activeInHierarchy;
        if (active == mShouldBeActive)
            ctx.Log.Info($"UI-смоук OK: «{mObjectName}» {(active ? "видна" : "скрыта")}.");
        else
            ctx.Log.Warn($"UI-смоук FAIL: «{mObjectName}» ожидалась {(mShouldBeActive ? "видимой" : "скрытой")}, " +
                         $"а она {(active ? "видна" : "скрыта")}.");

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
