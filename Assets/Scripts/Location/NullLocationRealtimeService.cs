using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Заглушка <see cref="ILocationRealtimeService"/> для WebGL-таргета.
///
/// Официальный C#-клиент SignalR (Microsoft.AspNetCore.SignalR.Client) несовместим
/// с Unity WebGL: под капотом использует HttpClient/ClientWebSocket и фоновые потоки,
/// которых в браузерном wasm-рантайме нет — попытка их использовать роняет приложение
/// с "RuntimeError: function signature mismatch" (см. TD-C35 в PROGRESS_CLIENT.md).
///
/// Пока не готов jslib-мост на JS-клиент SignalR, WebGL-билд живёт без live-обновлений
/// локации: мобы/игроки входят-выходят без мгновенного пуша, только по ручному/периодическому
/// REST-Refresh. Как только появится jslib-реализация — она встанет вместо этой заглушки
/// в биндинге (см. GameInstaller.InstallRealtime), сама заглушка останется fallback'ом.
/// </summary>
public sealed class NullLocationRealtimeService : ILocationRealtimeService
{
    // События никогда не стреляют — подписчики (LocationPresenter) просто не получают
    // живых пушей. Компилятор ругается на "unused" без add/remove, поэтому событие
    // объявлено обычным способом с пустым телом через no-op паттерн.
    public event Action<MobStateChangedEvent> MobStateChanged { add { } remove { } }
    public event Action<PlayerEnteredEvent> PlayerEntered { add { } remove { } }
    public event Action<PlayerLeftEvent> PlayerLeft { add { } remove { } }
    public event Action<CombatStartedEvent> CombatStarted { add { } remove { } }
    public event Action<ChatMessageView> ChatMessageReceived { add { } remove { } }
    public event Action Resynced { add { } remove { } }

    /// <summary>Ничего не подключает — WebGL-заглушка.</summary>
    public UniTask StartAsync(CancellationToken ct) => UniTask.CompletedTask;

    /// <summary>Ничего не делает — членство в SignalR-группах не актуально без соединения.</summary>
    public UniTask SetCurrentLocationAsync(long locationId, CancellationToken ct) => UniTask.CompletedTask;
}
