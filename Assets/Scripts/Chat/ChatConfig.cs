using UnityEngine;

/// <summary>
/// Конфиг чата. Создать: ПКМ в Project → Create → MMORPG → ChatConfig.
/// Хранить в Assets/Configs/ChatConfig.asset, назначить в ProjectInstaller.
/// </summary>
[CreateAssetMenu(fileName = "ChatConfig", menuName = "MMORPG/ChatConfig")]
public class ChatConfig : ScriptableObject
{
    [Header("Отправка")]
    [SerializeField, Tooltip("Должно совпадать с Game:Chat:MaxMessageLength в appsettings сервера — " +
        "синхронизируется РУКАМИ в обе стороны (см. PROGRESS_CLIENT.md). Сервер всё равно " +
        "проверяет независимо — здесь только для UX (счётчик символов, ранний отказ без похода в сеть).")]
    private int mMaxMessageLength = 250;

    [Header("Буфер (клиент, НЕ история с сервера — каналы live-only по дизайну)")]
    [SerializeField, Tooltip("Сколько минут держим сообщение локации и торгового канала до авточистки")]
    private float mBufferWindowMinutes = 10f;

    [SerializeField, Tooltip("Жёсткий потолок сообщений в буфере — защита от разрастания, " +
        "если чат зальют спамом за окно. Не альтернатива времени, а страховка сверху. " +
        "Единственное, что ограничивает личку: по времени она не истекает вообще.")]
    private int mBufferMaxMessages = 300;

    [SerializeField, Tooltip("Сколько минут живёт системное сообщение. Дольше болтовни: " +
        "результат боя и анонс техработ нужны и через полчаса после прихода.")]
    private float mSystemLifetimeMinutes = 60f;

    [SerializeField, Tooltip("Сколько системок держим одновременно. Отдельный потолок поверх " +
        "общего: за час их накапливается по одной на бой, и без своего лимита они вытеснили бы " +
        "живую переписку из общего буфера.")]
    private int mSystemMaxMessages = 20;

    [SerializeField, Tooltip("Как часто чистим протухшие по времени сообщения, если новых не приходит")]
    private float mCleanupIntervalSeconds = 30f;

    [SerializeField, Tooltip("Заглушка на будущее: не стирать буфер чата локации при смене локации. " +
        "Сейчас всегда стирается (см. обсуждение захода 2). Флаг есть, поведение пока не ветвится по нему.")]
    private bool mPreserveLocationBufferOnLocationChange = false;

    [Header("Цвета тега канала (резолвит View — Presenter не знает про UnityEngine.Color)")]
    [SerializeField, Tooltip("Подобраны под БЕЛЫЙ фон лога сообщений (см. View_Chat/ChatSetup) — " +
        "не пастельные, иначе не читаются на белом.")]
    private Color mLocationColor = new(0.15f, 0.45f, 0.15f);
    [SerializeField] private Color mTradeColor = new(0.55f, 0.4f, 0.05f);
    [SerializeField] private Color mPrivateColor = new(0.35f, 0.2f, 0.55f);
    [SerializeField] private Color mSystemColor = new(0.55f, 0.1f, 0.1f);

    public int MaxMessageLength => mMaxMessageLength;
    public float BufferWindowMinutes => mBufferWindowMinutes;
    public int BufferMaxMessages => mBufferMaxMessages;
    public float SystemLifetimeMinutes => mSystemLifetimeMinutes;
    public int SystemMaxMessages => mSystemMaxMessages;
    public float CleanupIntervalSeconds => mCleanupIntervalSeconds;
    public bool PreserveLocationBufferOnLocationChange => mPreserveLocationBufferOnLocationChange;

    /// <summary>Акцентный цвет тега канала. Ждёт lowercase (ChatMessageView.ChannelType) —
    /// см. ChatChannelTypes.VIEW_*.</summary>
    public Color ColorFor(string viewChannelType) => viewChannelType switch
    {
        ChatChannelTypes.VIEW_LOCATION => mLocationColor,
        ChatChannelTypes.VIEW_TRADE => mTradeColor,
        ChatChannelTypes.VIEW_PRIVATE => mPrivateColor,
        ChatChannelTypes.VIEW_SYSTEM => mSystemColor,
        _ => mSystemColor
    };

    /// <summary>
    /// Сколько минут сообщение канала живёт в буфере. null — НЕ истекает по времени вовсе.
    ///
    /// Раньше срок был один на все каналы, и личка молча исчезала через десять минут: отошёл
    /// на четверть часа — переписки нет, хотя собеседник её видит у себя в окне. Болтовня в
    /// локации и торге устаревает сама и режется по времени; личка ограничена только общим
    /// потолком буфера; системки живут дольше остальных (см. MaxMessagesFor — у них ещё и
    /// собственный лимит по количеству).
    ///
    /// Ждёт lowercase — см. ChatChannelTypes.VIEW_*, по образцу ColorFor.
    /// </summary>
    public float? LifetimeFor(string viewChannelType) => viewChannelType switch
    {
        ChatChannelTypes.VIEW_PRIVATE => null,
        ChatChannelTypes.VIEW_SYSTEM => mSystemLifetimeMinutes,
        _ => mBufferWindowMinutes
    };

    /// <summary>
    /// Собственный потолок количества для канала. null — отдельного лимита нет, работает
    /// только общий BufferMaxMessages.
    /// </summary>
    public int? MaxMessagesFor(string viewChannelType) => viewChannelType switch
    {
        ChatChannelTypes.VIEW_SYSTEM => mSystemMaxMessages,
        _ => null
    };
}
