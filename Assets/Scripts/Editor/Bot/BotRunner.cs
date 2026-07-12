using System;
using System.Diagnostics;
using System.Linq;
using Cysharp.Threading.Tasks;

/// <summary>
/// Прогоняет сценарий: шаг за шагом, с уважением к паузе и отмене (Stop).
///
/// Что делает вокруг каждого шага:
///   — пишет прогресс (индекс шага/круг) в ctx.Progress — для окна и оверлея;
///   — замеряет длительность (BotStats.AddStepTiming);
///   — ошибка одного шага не роняет прогон: лог + Errors++ (+скриншот, если включено);
///   — после шага — пауза ctx.PauseAfterActionAsync() (если включена);
///   — между шагами проверяет стоп-условия (RequestStop, лимит ошибок).
/// Отмена (Stop / выход из Play Mode) пробрасывается наверх и корректно всё останавливает.
/// </summary>
public sealed class BotRunner
{
    /// <summary>
    /// Запустить сценарий на исполнение.
    /// isPaused — функция «сейчас пауза?» (окно дёргает тумблер Pause).
    /// </summary>
    public async UniTask RunAsync(BotScenario scenario, BotContext ctx, Func<bool> isPaused)
    {
        var ct = ctx.Ct;
        var progress = ctx.Progress;
        progress.ResetFor(scenario);

        ctx.Log.Info($"▶ Старт сценария «{scenario.Name}» ({scenario.Steps.Count} шаг., " +
                     $"{(scenario.Loop ? "зациклен" : "один проход")}).");

        do
        {
            progress.Pass++;
            if (scenario.Loop) ctx.Log.Info($"=== Круг #{progress.Pass} ===");

            for (int i = 0; i < scenario.Steps.Count; i++)
            {
                var step = scenario.Steps[i];

                ct.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(ctx, isPaused);
                ct.ThrowIfCancellationRequested();

                if (ShouldStop(ctx, out var reason))
                {
                    ctx.Log.Warn($"⏹ Остановка по условию: {reason}");
                    LogFinish(ctx, scenario);
                    return;
                }

                progress.CurrentIndex = i;
                progress.Detail = "";
                ctx.Log.Step(step.Describe);

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await step.ExecuteAsync(ctx);
                }
                catch (OperationCanceledException)
                {
                    throw; // отмена — не «ошибка шага», пробрасываем наверх
                }
                catch (Exception ex)
                {
                    ctx.Stats.Errors++;
                    ctx.Log.Error($"Шаг «{step.Describe}» упал: {ex.Message}");
                    if (ctx.Options.ScreenshotOnError)
                        ctx.CaptureScreenshot("error");
                    // Продолжаем со следующего шага — один сбой не должен ронять весь прогон.
                }
                finally
                {
                    stopwatch.Stop();
                    ctx.Stats.AddStepTiming(step.Describe, stopwatch.Elapsed.TotalSeconds);
                }

                await ctx.PauseAfterActionAsync();
            }
        }
        while (scenario.Loop && !ct.IsCancellationRequested && !ctx.StopRequested);

        if (ctx.StopRequested)
            ctx.Log.Warn($"⏹ Остановка по условию: {ctx.StopReason}");

        LogFinish(ctx, scenario);
    }

    /// <summary>Сработало ли какое-то стоп-условие.</summary>
    private static bool ShouldStop(BotContext ctx, out string reason)
    {
        if (ctx.StopRequested)
        {
            reason = ctx.StopReason;
            return true;
        }

        int problems = ctx.Stats.Errors + ctx.Stats.AssertsFailed;
        if (ctx.Options.StopAfterErrors > 0 && problems >= ctx.Options.StopAfterErrors)
        {
            reason = $"достигнут лимит проблем ({problems}/{ctx.Options.StopAfterErrors})";
            return true;
        }

        reason = null;
        return false;
    }

    private static void LogFinish(BotContext ctx, BotScenario scenario)
        => ctx.Log.Info($"✅ Сценарий «{scenario.Name}» завершён. {ctx.Stats.Summary()}");

    /// <summary>Если нажата пауза — ждём, пока снимут (или пока не отменят).</summary>
    private static async UniTask WaitWhilePausedAsync(BotContext ctx, Func<bool> isPaused)
    {
        if (isPaused == null || !isPaused()) return;

        ctx.Log.Info("⏸ Пауза…");
        await BotWait.UntilForever(() => !isPaused(), ctx.Ct);
        ctx.Log.Info("▶ Продолжаю.");
    }
}
