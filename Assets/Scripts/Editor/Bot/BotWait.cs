using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Тайминги бота. Все ожидания щедрые — игра по своей природе медленная
/// (у ActionAsync внутри пауза 1с «моб думает», PvP-polling 2с, таймеры локаций).
/// Правим только здесь, в коде не хардкодим.
/// </summary>
public static class BotConfig
{
    /// <summary>Как часто опрашиваем реактивное состояние (мс). 100мс — быстро и дёшево.</summary>
    public const int POLL_MS = 100;

    /// <summary>Раз в сколько секунд писать «сердцебиение» при долгом ожидании (таймер/респавн).</summary>
    public const int HEARTBEAT_SEC = 5;

    /// <summary>Таймаут старта боя после EngageMob.</summary>
    public static readonly TimeSpan ENGAGE_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>Таймаут одного действия/хода в бою (с запасом на паузу «моб думает» и polling).</summary>
    public static readonly TimeSpan TURN_TIMEOUT = TimeSpan.FromSeconds(90);

    /// <summary>Таймаут выхода из боя (воскрешение + сброс состояния).</summary>
    public static readonly TimeSpan EXIT_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>Таймаут одного перехода между локациями (после того как CanMove стал true).</summary>
    public static readonly TimeSpan MOVE_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>Таймаут одной инвентарной мутации (надеть/снять/сундук/выброс).</summary>
    public static readonly TimeSpan INVENTORY_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>Таймаут разовой загрузки данных (рефреш локации/инвентаря).</summary>
    public static readonly TimeSpan LOAD_TIMEOUT = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Ядро всей асинхронности бота: ожидание условия по реактивному состоянию.
///
/// ЗАЧЕМ ОНО ГЛАВНОЕ:
///   Команды презенторов (EngageMobAsync/ActionAsync/Equip/...) — fire-and-forget
///   (UniTaskVoid / .Forget()), их НЕЛЬЗЯ await-ить. Плюс у каждой есть гвард
///   (например ActionAsync молча выходит, если не мой ход). Поэтому бот работает так:
///   «дождаться предусловия → выстрелить команду → дождаться пост-условия».
///   Всё ожидание — через опрос .Value реактивных свойств (poll), это надёжнее,
///   чем подписки через границу редактор/рантайм.
/// </summary>
public static class BotWait
{
    /// <summary>
    /// Ждать, пока условие не станет true, но не дольше timeout.
    /// </summary>
    /// <returns>true — условие выполнилось; false — вышел таймаут.</returns>
    public static async UniTask<bool> Until(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (condition()) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await UniTask.Delay(BotConfig.POLL_MS, cancellationToken: ct);
        }
    }

    /// <summary>
    /// Ждать условие СКОЛЬКО УГОДНО (без таймаута), но с уважением к отмене (Stop).
    /// Для законно долгих ожиданий: таймер локации, респавн мобов.
    /// heartbeat — вызывается раз в HEARTBEAT_SEC, чтобы писать в лог «сколько ещё».
    /// </summary>
    public static async UniTask UntilForever(Func<bool> condition, CancellationToken ct, Action heartbeat = null)
    {
        var nextBeat = DateTime.UtcNow;
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();

            if (heartbeat != null && DateTime.UtcNow >= nextBeat)
            {
                heartbeat();
                nextBeat = DateTime.UtcNow.AddSeconds(BotConfig.HEARTBEAT_SEC);
            }

            await UniTask.Delay(BotConfig.POLL_MS, cancellationToken: ct);
        }
    }
}
