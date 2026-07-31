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

    /// <summary>
    /// Кто отправил — нужен View для тапа «ответить» (личка этому игроку).
    /// null у системных строк: отвечать некому, и это не «кнопка выключена», а отсутствие цели.
    /// </summary>
    public long? SenderCharacterId { get; set; }
    public string SenderNickname { get; set; }

    /// <summary>Готовый текст строки: «Ник: текст», либо, для собственных исходящих
    /// личных сообщений — «Вы → Ник: текст» (см. ChatPresenter.BuildDisplayLine).
    /// У системок — текст сервера как есть, без префикса ника.</summary>
    public string BodyText { get; set; }

    // ─── Действие на кликабельном фрагменте ──────────────────────────────────

    /// <summary>Вид действия — см. ChatActionKinds. null/пусто — действия нет.</summary>
    public string ActionKind { get; set; }

    /// <summary>Подстрока BodyText, которую View оборачивает в TMP-линк.</summary>
    public string ActionLinkText { get; set; }

    /// <summary>Id боевой сессии для ActionKind = BATTLE_RESULT.</summary>
    public long? ActionSessionId { get; set; }

    /// <summary>Исход боя — см. ChatBattleOutcomes.</summary>
    public string ActionOutcome { get; set; }

    /// <summary>
    /// Действие на этой строке РАЗРЕШЕНО. Отдельно от наличия самого действия, потому что
    /// кликабельна только ПОСЛЕДНЯЯ строка боя: снимок награды на сервере один на персонажа
    /// и перетирается следующим боем, поэтому из десяти строк в чате девять указывали бы на
    /// данные, которых уже нет (см. решение в PROGRESS.md).
    /// </summary>
    public bool IsActionEnabled { get; set; }

    /// <summary>Системная строка — отправителя нет.</summary>
    public bool IsSystem => ChannelType == ChatChannelTypes.VIEW_SYSTEM;

    /// <summary>На строку можно ответить личкой.</summary>
    public bool CanReply => SenderCharacterId.HasValue;

    /// <summary>Во View есть что делать кликабельным.</summary>
    public bool HasClickableAction =>
        IsActionEnabled
        && !string.IsNullOrEmpty(ActionKind)
        && !string.IsNullOrEmpty(ActionLinkText);
}
