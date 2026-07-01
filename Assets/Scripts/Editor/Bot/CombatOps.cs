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
/// </summary>
public static class CombatOps
{
    public static async UniTask<FightOutcome> FightMobAsync(BotContext ctx, long spawnId, ICombatPolicy policy)
    {
        var combat = ctx.Combat;
        var ct = ctx.Ct;

        if (combat.IsInCombat.Value)
        {
            ctx.Log.Warn("Уже в бою — новый бой начать нельзя, пропускаю.");
            return FightOutcome.Rejected;
        }

        ctx.Stats.Fights++;

        // ── Старт боя ────────────────────────────────────────────────────────
        combat.EngageMobAsync(spawnId, ct).Forget();

        await BotWait.Until(
            () => combat.IsInCombat.Value || !string.IsNullOrEmpty(combat.ErrorMessage.Value),
            BotConfig.ENGAGE_TIMEOUT, ct);

        if (!combat.IsInCombat.Value)
        {
            var err = combat.ErrorMessage.Value;
            ctx.Log.Warn($"Бой не начался: {(string.IsNullOrEmpty(err) ? "таймаут" : err)}");
            ctx.Stats.Rejections++;
            return FightOutcome.Rejected;
        }

        ctx.Log.Info($"Бой начат против «{combat.EnemyName.Value}».");

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
                ctx.Log.Warn("Не дождался своего хода (таймаут). Прерываю бой.");
                await ForceExitAsync(ctx);
                return FightOutcome.Timeout;
            }

            var move = policy.Decide(combat);

            // Опциональная расходка (не является ходом).
            if (move.ConsumeTemplateId.HasValue)
            {
                combat.ConsumeAsync(move.ConsumeTemplateId.Value, ct).Forget();
                await BotWait.Until(() => !combat.IsLoading.Value, BotConfig.TURN_TIMEOUT, ct);
                if (combat.IsFinished.Value || !combat.IsInCombat.Value) break;
            }

            // Стойка + удар (или пропуск).
            combat.SetStance(move.Stance);
            if (move.Skip) combat.SkipAsync(ct).Forget();
            else           combat.ActionAsync(move.Direction, ct).Forget();

            // Ждём, пока ход обработается (IsLoading вернётся в false), либо конец боя.
            await BotWait.Until(
                () => combat.IsFinished.Value || !combat.IsInCombat.Value || !combat.IsLoading.Value,
                BotConfig.TURN_TIMEOUT, ct);
        }

        // ── Итог ─────────────────────────────────────────────────────────────
        bool finished = combat.IsFinished.Value;
        bool won = combat.DidWin.Value;
        int myHp = combat.MyCurrentHp.Value;

        await ForceExitAsync(ctx); // выйти из боя (внутри воскресит, если HP<=0)

        if (!finished)
            return FightOutcome.Timeout;

        if (won)
        {
            ctx.Stats.Wins++;
            ctx.Stats.MobKills++;
            ctx.Log.Info("Победа.");
            return FightOutcome.Win;
        }

        // Поражение. Если HP был <=0 — это смерть с воскрешением в городе.
        ctx.Stats.Losses++;
        if (myHp <= 0)
        {
            ctx.Stats.Deaths++;
            ctx.Log.Warn("Поражение: персонаж погиб и воскрешён в городе.");
            return FightOutcome.Lost;
        }

        ctx.Log.Warn("Поражение.");
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
