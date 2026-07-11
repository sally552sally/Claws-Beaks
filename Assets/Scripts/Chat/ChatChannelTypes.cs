/// <summary>
/// Строковые значения ChannelType — сервер использует ДВА РАЗНЫХ РЕГИСТРА в разных местах,
/// не взаимозаменяемо:
///
///  — ChatMessageView.ChannelType (входящие/отображаемые сообщения, REST-эхо И SignalR-пуш) —
///    lowercase, из Postgres native enum через PgEnum.ToDb: "location"/"trade"/"group"/"private".
///
///  — SendMessageRequest.ChannelType (исходящая отправка) — PascalCase, десериализуется
///    сервером в C# enum ChatChannelType через JsonStringEnumConverter (регистр важен,
///    матчится по имени enum-члена КАК ОБЪЯВЛЕНО В КОДЕ: Location/Trade/Group/Private) —
///    это происходит РАНЬШЕ, чем PgEnum.ToDb вообще применяется (тот только на исходящей стороне).
///
/// Используй эти константы везде в чат-коде, не пиши строки руками — легко перепутать регистр.
/// </summary>
public static class ChatChannelTypes
{
    // ── Сравнение ChatMessageView.ChannelType (входящие, lowercase) ──
    public const string VIEW_LOCATION = "location";
    public const string VIEW_TRADE = "trade";
    public const string VIEW_GROUP = "group";
    public const string VIEW_PRIVATE = "private";

    // ── SendMessageRequest.ChannelType (исходящие, PascalCase) ──
    public const string SEND_LOCATION = "Location";
    public const string SEND_TRADE = "Trade";
    public const string SEND_PRIVATE = "Private";
    // SEND_GROUP намеренно не заведена — групповой чат в беклоге, нужен нестандартный
    // подход (см. PROGRESS_CLIENT.md), не добавлять по аналогии не подумав.
}

/// <summary>
/// Канал, в который СЕЙЧАС пишет игрок (выбор в UI поверх поля ввода) — НЕ то же самое,
/// что фильтры отображения (ChatPresenter.ShowLocation/ShowTrade). Group сюда не входит —
/// групповой чат в беклоге.
/// </summary>
public enum ChatSendChannel
{
    Location,
    Trade,
    Private
}
