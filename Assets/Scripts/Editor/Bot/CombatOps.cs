using System.Diagnostics;
using Cysharp.Threading.Tasks;

/// <summary>Чем закончился бой.</summary>
public enum FightOutcome
{
    Win,        // победа
    Lost,       // поражение (персонаж погиб → воскрешён в городе)
    Rejected,   // сервер не дал начать бой (моб занят/бой запрещён/…)
    Timeout     // бот не дождался хода/конца — прервано по таймауту
}

/// <summary>
/// Провести ОДИН бой с мобом целиком. Вся возня с fire-and-forget командами и гвардами —
/// здесь. Наружу — простой await с понятным результатом.
///
/// Схема одного хода = язык боя из диздока: опц. расходка → стойка → удар (или пропуск),
/// затем ждём, пока сервер обработает ход (IsLoading вернётся в false).
///
/// Пишет в канал Combat; считает ходы и время боя (BotStats); после каждого действия —
/// пауза ctx.PauseAfterActionAsync() (если включена в настройках).
/// </summary>
public static class CombatOps
{
    public static async UniTask<FightOutcome> FightMobAsync(BotContext ctx, long spawnId, ICombatPolicy policy)
    {
        var combat = ctx.Combat;
        var ct = ctx.Ct;

        if (combat.IsInCombat.Value)
        {
            ctx.Log.Warn(BotChannel.Combat, "Уже в бою — новый бой начать нельзя, пропускаю.");
            return FightOutcome.Rejected;
        }

        ctx.Stats.Fights++;
        var stopwatch = Stopwatch.StartNew();

        // ── Старт боя ────────────────────────────────────────────────────────
        combat.EngageMobAsync(spawnId, ct).Forget();

        await BotWait.Until(
            () => combat.IsInCombat.Value || !string.IsNullOrEmpty(combat.ErrorMessage.Value),
            BotConfig.ENGAGE_TIMEOUT, ct);

        if (!combat.IsInCombat.Value)
        {
            var err = combat.ErrorMessage.Value;
            ctx.Log.Warn(BotChannel.Combat, $"Бой не начался: {(string.IsNullOrEmpty(err) ? "таймаут" : err)}");
            ctx.Stats.Rejections++;
            return FightOutcome.Rejected;
        }

        ctx.Log.Info(BotChannel.Combat, $"Бой начат против «{combat.EnemyName.Value}».");
        await ctx.PauseAfterActionAsync();

        // ── Цикл ходов ───────────────────────────────────────────────────────
        while (combat.IsInCombat.Value && !combat.IsFinished.Value)
        {
            ct.ThrowIfCancellationRequested();

            // Ждём своего хода (или конца боя).
            bool ready = await BotWait.Until(
                () => (combat.IsMyTurn.Value && !combat.IsLoading.Value)
                      || combat.IsFinished.Value || !combat.IsInCombat.Value,
                BotConfig.TURN_TIMEOUT, ct);

            if (combat.IsFinished.Value || !combat.IsInCombat.Value) break;

            if (!ready)
            {
                ctx.Log.Warn(BotChannel.Combat, "Не дождался своего хода (таймаут). Прерываю бой.");
                await ForceExitAsync(ctx);
                stopwatch.Stop();
                ctx.Stats.FightSeconds += stopwatch.Elapsed.TotalSeconds;
                return FightOutcome.Timeout;
            }

            var move = policy.Decide(combat);

            // Опциональная расходка (не является ходом).
            if (move.ConsumeTemplateId.HasValue)
            {
                combat.ConsumeAsync(move.ConsumeTemplateId.Value, ct).Forget();
                await BotWait.Until(() => !combat.IsLoading.Value, BotConfig.TURN_TIMEOUT, ct);
                if (combat.IsFinished.Value || !combat.IsInCombat.Value) break;
                await ctx.PauseAfterActionAsync();
            }

            // Стойка + удар (или пропуск).
            combat.SetStance(move.Stance);
            if (move.Skip) combat.SkipAsync(ct).Forget();
            else           combat.ActionAsync(move.Direction, ct).Forget();

            ctx.Stats.TotalTurns++;

            // Ждём, пока ход обработается (IsLoading вернётся в false), либо конец боя.
            await BotWait.Until(
                () => combat.IsFinished.Value || !combat.IsInCombat.Value || !combat.IsLoading.Value,
                BotConfig.TURN_TIMEOUT, ct);

            await ctx.PauseAfterActionAsync();
        }

        // ── Итог ─────────────────────────────────────────────────────────────
        bool finished = combat.IsFinished.Value;
        bool won = combat.DidWin.Value;
        int myHp = combat.MyCurrentHp.Value;

        await ForceExitAsync(ctx); // выйти из боя (внутри воскресит, если HP<=0)
        await ctx.PauseAfterActionAsync();

        stopwatch.Stop();
        ctx.Stats.FightSeconds += stopwatch.Elapsed.TotalSeconds;

        if (!finished)
            return FightOutcome.Timeout;

        if (won)
        {
            ctx.Stats.Wins++;
            ctx.Stats.MobKills++;
            ctx.Log.Info(BotChannel.Combat, "Победа.");
            return FightOutcome.Win;
        }

        // Поражение. Если HP был <=0 — это смерть с воскрешением в городе.
        ctx.Stats.Losses++;
        if (myHp <= 0)
        {
            ctx.Stats.Deaths++;
            ctx.Log.Warn(BotChannel.Combat, "Поражение: персонаж погиб и воскрешён в городе.");
            return FightOutcome.Lost;
        }

        ctx.Log.Warn(BotChannel.Combat, "Поражение.");
        return FightOutcome.Lost;
    }

    /// <summary>Выйти из боя и дождаться сброса состояния (IsInCombat=false).</summary>
    private static async UniTask ForceExitAsync(BotContext ctx)
    {
        var combat = ctx.Combat;
        if (!combat.IsInCombat.Value) return;

        combat.ExitCombatAsync(ctx.Ct).Forget();
        await BotWait.Until(() => !combat.IsInCombat.Value, BotConfig.EXIT_TIMEOUT, ctx.Ct);
    }
}
