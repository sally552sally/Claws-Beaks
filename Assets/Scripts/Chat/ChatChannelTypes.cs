/// <summary>
/// Строковые значения ChannelType — сервер использует ДВА РАЗНЫХ РЕГИСТРА в разных местах,
/// не взаимозаменяемо:
///
///  — ChatMessageView.ChannelType (входящие/отображаемые сообщения, REST-эхо И SignalR-пуш) —
///    lowercase, из Postgres native enum через PgEnum.ToDb: "location"/"trade"/"group"/"private"/"system".
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

    /// <summary>
    /// Системный канал: результаты боёв, общие анонсы. У сообщения нет отправителя
    /// (ChatMessageView.SenderCharacterId == null), поэтому «ответить» и «пожаловаться»
    /// для него не существуют — не «отключены», а именно отсутствуют.
    /// </summary>
    public const string VIEW_SYSTEM = "system";

    // ── SendMessageRequest.ChannelType (исходящие, PascalCase) ──
    public const string SEND_LOCATION = "Location";
    public const string SEND_TRADE = "Trade";
    public const string SEND_PRIVATE = "Private";
    // SEND_GROUP намеренно не заведена — групповой чат в беклоге, нужен нестандартный
    // подход (см. PROGRESS_CLIENT.md), не добавлять по аналогии не подумав.
    //
    // SEND_SYSTEM не заведена и не будет: писать в системный канал клиент не может,
    // сервер отказывает явной проверкой в ChatService.SendAsync ДО мута и рейт-лимита.
}

/// <summary>
/// Виды действия на кликабельном фрагменте системной строки — значения
/// ChatMessageActionView.Kind.
///
/// Приходят СТРОКАМИ: сервер сериализует enum через JsonStringEnumConverter без naming
/// policy, причём одинаково по обоим путям (AddControllers и AddJsonProtocol у SignalR —
/// см. Program.cs). Значит регистр ровно такой, как имя enum-члена в серверном коде.
/// </summary>
public static class ChatActionKinds
{
    /// <summary>Действия нет — строка чисто информационная (вся общая рассылка такая).</summary>
    public const string NONE = "None";

    /// <summary>Открыть окно результата боя по ActionSessionId.</summary>
    public const string BATTLE_RESULT = "BattleResult";
}

/// <summary>
/// Исход боя в системной строке — значения ChatMessageActionView.Outcome.
/// Зеркало серверного SystemBattleOutcome и клиентского CombatOutcome; отдельные константы
/// нужны, потому что по проводу это строки, а не enum.
/// </summary>
public static class ChatBattleOutcomes
{
    public const string WIN = "Win";
    public const string LOSS = "Loss";
    public const string INTERRUPTED = "Interrupted";
}

/// <summary>
/// Канал, в который СЕЙЧАС пишет игрок (выбор в UI поверх поля ввода) — НЕ то же самое,
/// что фильтры отображения (ChatPresenter.ShowLocation/ShowTrade). Group сюда не входит —
/// групповой чат в беклоге. System не входит принципиально — туда пишет только сервер.
/// </summary>
public enum ChatSendChannel
{
    Location,
    Trade,
    Private
}
