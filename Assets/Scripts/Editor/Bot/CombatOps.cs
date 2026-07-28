using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>Чем закончился бой.</summary>
public enum FightOutcome
{
    Win,         // победа
    Lost,        // поражение (персонаж погиб → воскрешён)
    Interrupted, // бой прерван сервером без победителя (истёк лимит длительности сессии)
    Rejected,    // сервер не дал начать бой (моб/игрок занят, недоступен, бой запрещён/…)
    Timeout      // бот не дождался хода/конца — прервано по таймауту
}

/// <summary>
/// Провести ОДИН бой целиком — против моба (PvE) или против игрока (PvP). Вся возня
/// с fire-and-forget командами и гвардами — здесь. Наружу — простой await с понятным
/// результатом. FightMobAsync/FightPlayerAsync отличаются только тем, КАК стартует
/// бой (EngageMobAsync vs EngagePlayerAsync) — весь цикл ходов дальше общий: и стойки,
/// и комбо, и расходка, и поражение/победа устроены одинаково для PvE и PvP.
///
/// Схема одного хода = язык боя из диздока: опц. расходка → стойка → удар (или пропуск),
/// затем ждём, пока сервер обработает ход (IsLoading вернётся в false).
///
/// PvP-нюанс: FightPlayerAsync минует клиентский диалог подтверждения
/// (CombatPresenter.RequestAttackPlayer) — бот бьёт напрямую через EngagePlayerAsync,
/// так же как остальные боевые/инвентарные операции бота обходят UI-confirm'ы.
///
/// Пишет в канал Combat; считает ходы и время боя (BotStats); после каждого действия —
/// пауза ctx.PauseAfterActionAsync() (если включена в настройках).
/// </summary>
public static class CombatOps
{
    /// <summary>Провести бой с мобом по SpawnId.</summary>
    public static UniTask<FightOutcome> FightMobAsync(BotContext ctx, long spawnId, ICombatPolicy policy)
        => FightAsync(ctx, engageCt => ctx.Combat.EngageMobAsync(spawnId, engageCt).Forget(), policy, isPvp: false);

    /// <summary>Провести PvP-бой с игроком по CharacterId.</summary>
    public static UniTask<FightOutcome> FightPlayerAsync(BotContext ctx, long characterId, ICombatPolicy policy)
        => FightAsync(ctx, engageCt => ctx.Combat.EngagePlayerAsync(characterId, engageCt).Forget(), policy, isPvp: true);

    /// <summary>Общее ядро боя. engage — как именно стартовать (моб или игрок).</summary>
    private static async UniTask<FightOutcome> FightAsync(
        BotContext ctx, Action<CancellationToken> engage, ICombatPolicy policy, bool isPvp)
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
        engage(ct);

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
            else combat.ActionAsync(move.Direction, ct).Forget();

            ctx.Stats.TotalTurns++;

            // Ждём, пока ход обработается (IsLoading вернётся в false), либо конец боя.
            await BotWait.Until(
                () => combat.IsFinished.Value || !combat.IsInCombat.Value || !combat.IsLoading.Value,
                BotConfig.TURN_TIMEOUT, ct);

            await ctx.PauseAfterActionAsync();
        }

        // ── Итог ─────────────────────────────────────────────────────────────
        bool finished = combat.IsFinished.Value;
        var outcome = combat.Outcome.Value;
        int myHp = combat.MyCurrentHp.Value;

        await ForceExitAsync(ctx); // выйти из боя (внутри воскресит, если HP<=0)
        await ctx.PauseAfterActionAsync();

        stopwatch.Stop();
        ctx.Stats.FightSeconds += stopwatch.Elapsed.TotalSeconds;

        if (!finished)
            return FightOutcome.Timeout;

        if (outcome == CombatOutcome.Win)
        {
            ctx.Stats.Wins++;
            if (isPvp) ctx.Stats.PvpWins++;
            else ctx.Stats.MobKills++;
            ctx.Log.Info(BotChannel.Combat, isPvp ? "PvP-победа." : "Победа.");
            return FightOutcome.Win;
        }

        // Бой прерван сервером (истёк CombatConfig.MaxSessionMinutes) — это НЕ поражение:
        // персонаж жив, HP сохранены, воскрешение не нужно. В статистику поражений не пишем,
        // иначе долгие прогоны бота копили бы фантомные «проигрыши».
        if (outcome == CombatOutcome.Interrupted)
        {
            ctx.Log.Warn(BotChannel.Combat, "Бой прерван сервером (истёк лимит длительности).");
            return FightOutcome.Interrupted;
        }

        // Поражение. Если HP был <=0 — это смерть с воскрешением
        // (в городе для PvE, по правилам сервера — для PvP; клиент точку не знает).
        ctx.Stats.Losses++;
        if (myHp <= 0)
        {
            ctx.Stats.Deaths++;
            ctx.Log.Warn(BotChannel.Combat, "Поражение: персонаж погиб и воскрешён.");
            return FightOutcome.Lost;
        }

        ctx.Log.Warn(BotChannel.Combat, "Поражение.");
        return FightOutcome.Lost;
    }

    /// <summary>
    /// Выйти из боя и дождаться сброса состояния (IsInCombat=false). Если персонаж погиб —
    /// дополнительно эмулирует клик «Воскреснуть» на диалоге локации.
    /// TD-C32: CombatPresenter.ExitCombatAsync больше не воскрешает сам (единственный путь
    /// воскрешения теперь диалог «Вы мертвы»). Диалог закрывается ТОЛЬКО через
    /// INotificationService.RespondToDialog — прямой вызов LocationPresenter.ResurrectAsync
    /// воскресит персонажа на сервере, но оставит панель диалога висеть на экране,
    /// потому что ничего не скажет NotificationService её закрыть.
    /// </summary>
    private static async UniTask ForceExitAsync(BotContext ctx)
    {
        var combat = ctx.Combat;
        if (!combat.IsInCombat.Value) return;

        // Снимаем ДО выхода из боя — ExitCombatAsync сбросит MyCurrentHp в 0 в любом случае.
        bool needsResurrect = combat.MyCurrentHp.Value <= 0;

        combat.ExitCombatAsync(ctx.Ct).Forget();
        await BotWait.Until(() => !combat.IsInCombat.Value, BotConfig.EXIT_TIMEOUT, ctx.Ct);

        if (!needsResurrect) return;

        // Диалог всплывает асинхронно (после рефреша локации по событию CombatEnded),
        // не мгновенно по сбросу боя — ждём, пока сервер реально подтвердит смерть.
        bool awaitingResurrection = await BotWait.Until(
            () => ctx.Location.IsAwaitingResurrection.Value, BotConfig.EXIT_TIMEOUT, ctx.Ct);

        if (!awaitingResurrection)
        {
            ctx.Log.Warn(BotChannel.Combat,
                "Ожидался диалог воскрешения (HP<=0), но IsAwaitingResurrection не пришёл вовремя.");
            return;
        }

        ctx.Notifications.RespondToDialog(primary: true);

        // RespondToDialog лишь запускает ResurrectAsync (fire-and-forget внутри диалога) —
        // раньше вызывающий код получал гарантию, что к возврату из ForceExitAsync
        // воскрешение уже завершилось на сервере. Сохраняем эту гарантию явным ожиданием,
        // иначе следующий EngageMobAsync в сценарии рискует стартовать по мёртвому персонажу.
        await BotWait.Until(
            () => !ctx.Location.IsAwaitingResurrection.Value, BotConfig.EXIT_TIMEOUT, ctx.Ct);
    }
}
