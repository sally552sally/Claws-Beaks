using System;
using Cysharp.Threading.Tasks;

/// <summary>
/// Прогоняет сценарий: шаг за шагом, с уважением к паузе и отмене (Stop).
/// Ошибка одного шага не роняет весь прогон — логируется, счётчик Errors++, идём дальше
/// (кроме отмены — она пробрасывается наверх и корректно останавливает бота).
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
        ctx.Log.Info($"▶ Старт сценария «{scenario.Name}» ({scenario.Steps.Count} шаг., " +
                     $"{(scenario.Loop ? "зациклен" : "один проход")}).");

        int pass = 0;
        do
        {
            pass++;
            if (scenario.Loop) ctx.Log.Info($"=== Проход #{pass} ===");

            foreach (var step in scenario.Steps)
            {
                ct.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(ctx, isPaused);
                ct.ThrowIfCancellationRequested();

                ctx.Log.Step(step.Describe);
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
                    // Продолжаем со следующего шага — один сбой не должен ронять весь прогон.
                }
            }
        }
        while (scenario.Loop && !ct.IsCancellationRequested);

        ctx.Log.Info($"✅ Сценарий «{scenario.Name}» завершён. {ctx.Stats.Summary()}");
    }

    /// <summary>Если нажата пауза — ждём, пока снимут (или пока не отменят).</summary>
    private static async UniTask WaitWhilePausedAsync(BotContext ctx, Func<bool> isPaused)
    {
        if (isPaused == null || !isPaused()) return;

        ctx.Log.Info("⏸ Пауза…");
        await BotWait.UntilForever(() => !isPaused(), ctx.Ct);
        ctx.Log.Info("▶ Продолжаю.");
    }
}
