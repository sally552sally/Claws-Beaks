/// <summary>
/// Состояние одного SignalR-соединения (см. RealtimeConnection).
/// Пока не отображается в UI — заготовка под будущий индикатор связи.
/// </summary>
public enum HubConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}
