using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Живые события текущей локации (мобы/игроки/PvP) через SignalR LocationHub.
/// Держит ровно одно соединение на всю Game-сцену; группу локации меняет сам при
/// SetCurrentLocationAsync и восстанавливает членство в ней после реконнекта.
///
/// LocationPresenter — единственный потребитель: подписывается на события и обновляет
/// свои реактивные списки; на Resynced — перезапрашивает REST-снимок (GetCurrentAsync),
/// т.к. пропущенные во время разрыва соединения дельты сервер не буферизует.
/// </summary>
public interface ILocationRealtimeService
{
    /// <summary>Состояние спавна моба изменилось.</summary>
    event Action<MobStateChangedEvent> MobStateChanged;

    /// <summary>Игрок вошёл в текущую локацию.</summary>
    event Action<PlayerEnteredEvent> PlayerEntered;

    /// <summary>Игрок покинул текущую локацию.</summary>
    event Action<PlayerLeftEvent> PlayerLeft;

    /// <summary>Начался PvP-бой (шлётся всем в локации — фильтровать по DefenderCharacterId).</summary>
    event Action<CombatStartedEvent> CombatStarted;

    /// <summary>Пришло сообщение чата локации. Сервер переиспользует эту же группу
    /// ("location:{id}") для чата — отдельного соединения под чат локации не нужно,
    /// см. комментарий в серверном ChatHub.cs.</summary>
    event Action<ChatMessageView> ChatMessageReceived;

    /// <summary>
    /// Соединение установлено впервые ИЛИ восстановлено после разрыва — подписчику нужен
    /// полный REST-ресинк, пропущенные за паузу дельты сервер не хранит.
    /// </summary>
    event Action Resynced;

    /// <summary>Установить соединение. Вызывается один раз при старте Game-сцены.</summary>
    UniTask StartAsync(CancellationToken ct);

    /// <summary>
    /// Сообщить, в какой локации мы сейчас (по данным REST). Сервис сам покинет
    /// предыдущую SignalR-группу и вступит в новую; переживает разрывы соединения —
    /// при реконнекте автоматически вступает в последнюю запрошенную группу заново.
    /// </summary>
    UniTask SetCurrentLocationAsync(long locationId, CancellationToken ct);
}
