using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Заглушка <see cref="IChatRealtimeService"/> для WebGL-таргета.
/// Причина и план — см. <see cref="NullLocationRealtimeService"/> и TD-C35 в PROGRESS_CLIENT.md.
///
/// В WebGL-билде торговый чат и личка живут без live-доставки, пока не появится
/// jslib-мост на JS-клиент SignalR. ChatHistoryService (REST-история) не затронут.
/// </summary>
public sealed class NullChatRealtimeService : IChatRealtimeService
{
    public event Action<ChatMessageView> ChatMessageReceived { add { } remove { } }

    /// <summary>Ничего не подключает — WebGL-заглушка.</summary>
    public UniTask StartAsync(CancellationToken ct) => UniTask.CompletedTask;
}
