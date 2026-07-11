using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Живые сообщения торгового чата и лички через SignalR ChatHub (/hubs/chat).
/// Локационный чат — НЕ здесь, он приходит через уже установленное соединение
/// ILocationRealtimeService (сервер переиспользует группу LocationHub, см. комментарий
/// в серверном ChatHub.cs).
///
/// Держит одно соединение на всю Game-сцену; сам вступает в группу торгового канала
/// и личный канал персонажа (как только известен CharacterId — см. ICharacterContext)
/// и восстанавливает членство после реконнекта. Подписка идёт всегда при старте
/// Game-сцены, не только когда открыта панель чата.
/// </summary>
public interface IChatRealtimeService
{
    /// <summary>Пришло сообщение торгового канала или лички.</summary>
    event Action<ChatMessageView> ChatMessageReceived;

    /// <summary>Установить соединение. Вызывается один раз при старте Game-сцены.</summary>
    UniTask StartAsync(CancellationToken ct);
}
