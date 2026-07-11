using System.Collections.Generic;

/// <summary>
/// Буфер сообщений чата — ЖИВЁТ В ПАМЯТИ КЛИЕНТА, это НЕ история с сервера (каналы
/// live-only по дизайну — сервер намеренно не отдаёт прошлые сообщения). Копится по мере
/// прихода push-событий/эха отправки, стареет по времени (ChatConfig.BufferWindowMinutes)
/// и по количеству (ChatConfig.BufferMaxMessages — страховка сверху).
///
/// Приём идёт ВСЕГДА, независимо от того, открыта ли панель чата (ChatPresenter.IsOpen) —
/// это чистое UI-состояние, буфер не в курсе, смотрит ли на него сейчас игрок.
/// </summary>
public interface IChatHistoryService
{
    /// <summary>Все сообщения из всех каналов, объединённые и отсортированные по времени
    /// отправки (старые → новые). ChatPresenter применяет фильтры отображения поверх
    /// этого единого потока.</summary>
    ReadonlyReactive<List<ChatMessageView>> AllMessages { get; }

    /// <summary>Добавляет сообщение в буфер. Дедуп по Id — безопасно звать и из REST-эха
    /// отправки, и из SignalR-пуша одного и того же сообщения, дубля не будет.</summary>
    void AddMessage(ChatMessageView message);

    /// <summary>Очищает буфер ТОЛЬКО канала локации (остальные каналы не трогает).
    /// Вызывается при смене локации — см. ChatPresenter, подписка на
    /// LocationPresenter.CurrentLocationId. Уважает
    /// ChatConfig.PreserveLocationBufferOnLocationChange (заглушка на будущее).</summary>
    void ClearLocationBuffer();
}
