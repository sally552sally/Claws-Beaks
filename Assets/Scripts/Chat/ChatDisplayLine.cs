using System;

/// <summary>
/// Готовая к отображению строка чат-лога — собрана ChatPresenter из ChatMessageView.
/// Presenter не ссылается на UnityEngine (в т.ч. Color, по правилам слоёв проекта) —
/// цвет/тег канала резолвит View через ChatConfig.ColorFor(ChannelType).
/// </summary>
public sealed class ChatDisplayLine
{
    public long MessageId { get; set; }

    /// <summary>lowercase — см. ChatChannelTypes.VIEW_*.</summary>
    public string ChannelType { get; set; }

    public DateTime SentAtUtc { get; set; }

    /// <summary>Кто отправил — нужен View для тапа "ответить" (личка этому игроку).</summary>
    public long SenderCharacterId { get; set; }
    public string SenderNickname { get; set; }

    /// <summary>Готовый текст строки: "Ник: текст", либо, для собственных исходящих
    /// личных сообщений — "Вы → Ник: текст" (см. ChatPresenter.BuildDisplayLine).</summary>
    public string BodyText { get; set; }
}
