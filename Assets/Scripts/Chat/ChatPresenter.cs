using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Presenter окна чата (Panel_Chat в Game-сцене).
///
/// Отвечает за:
///   — открытие/закрытие панели (IsOpen) — панель ЧИСТО UI, приём сообщений идёт
///     всегда, независимо от IsOpen (см. подписки в конструкторе);
///   — фильтры ОТОБРАЖЕНИЯ (ShowLocation/ShowTrade) — Личка и Система показываются
///     всегда, чекбокса не имеют;
///   — канал ОТПРАВКИ (SendChannel) — отдельно от фильтров: можно смотреть только
///     Локацию, но писать в Торговый — независимые состояния;
///   — адресата лички (PrivateTargetCharacterId/Nickname) — выставляется через
///     ContextMenuPopup.MessageClicked ("Написать" в профиле игрока) или тапом по
///     уже полученному сообщению (SetPrivateTargetFromLine — "ответить");
///   — слияние двух источников живых сообщений (LocationHub для Location,
///     ChatHub для Trade/Private/System) в единый буфер через IChatHistoryService, и
///     построение отфильтрованного DisplayedMessages для View;
///   — диспетчеризацию действий на кликабельных фрагментах системных строк
///     (InvokeLineAction) — сейчас единственное действие открывает окно результата боя.
///
/// АРХИТЕКТУРА (UnityStyle): чистый C#, без using UnityEngine логики (Debug допустим,
/// как и в остальных Presenter'ах проекта). Цвет тега канала НЕ здесь — см. ChatDisplayLine.
///
/// Presenter→Presenter: инжектит LocationPresenter напрямую (как InventoryPresenter
/// инжектит CombatPresenter) — читает CurrentLocationId, чтобы чистить буфер локации
/// при смене локации; и BattleReportPresenter — чтобы открывать окно прошедшего боя.
/// </summary>
public sealed class ChatPresenter : DisposableObject
{
    // ─── Реактивное состояние ─────────────────────────────────────────────────

    private readonly Reactive<bool> mIsOpen = new(false);
    private readonly Reactive<bool> mShowLocation = new(true);
    private readonly Reactive<bool> mShowTrade = new(true);
    private readonly Reactive<ChatSendChannel> mSendChannel = new(ChatSendChannel.Location);
    private readonly Reactive<long?> mPrivateTargetCharacterId = new(null);
    private readonly Reactive<string> mPrivateTargetNickname = new(null);
    private readonly Reactive<bool> mIsSending = new(false);

    private readonly ReadonlyReactive<List<ChatDisplayLine>> mDisplayedMessages;

    public ReadonlyReactive<bool> IsOpen => mIsOpen.Readonly;
    public ReadonlyReactive<bool> ShowLocation => mShowLocation.Readonly;
    public ReadonlyReactive<bool> ShowTrade => mShowTrade.Readonly;
    public ReadonlyReactive<ChatSendChannel> SendChannel => mSendChannel.Readonly;
    public ReadonlyReactive<long?> PrivateTargetCharacterId => mPrivateTargetCharacterId.Readonly;
    public ReadonlyReactive<string> PrivateTargetNickname => mPrivateTargetNickname.Readonly;
    public ReadonlyReactive<bool> IsSending => mIsSending.Readonly;
    public ReadonlyReactive<List<ChatDisplayLine>> DisplayedMessages => mDisplayedMessages;

    // ─── Внутреннее состояние ─────────────────────────────────────────────────

    /// <summary>Ники отправителей, увиденные хоть раз (из любого канала) — нужно для
    /// "Вы → Ник" у собственных исходящих личных сообщений (ChatMessageView несёт только
    /// ChannelScopeId=id получателя, без его ника). Пополняется на каждом сообщении,
    /// прошедшем через BuildDisplayLine, плюс явно при выборе адресата (SetPrivateTarget).</summary>
    private readonly Dictionary<long, string> mKnownNicknames = new();

    // ─── Зависимости ──────────────────────────────────────────────────────────

    private readonly IChatService mChatService;
    private readonly IChatHistoryService mHistory;
    private readonly ILocationRealtimeService mLocationRealtime;
    private readonly IChatRealtimeService mChatRealtime;
    private readonly LocationPresenter mLocationPresenter;
    private readonly BattleReportPresenter mBattleReport;
    private readonly ICharacterContext mCharacterContext;
    private readonly INotificationService mNotifications;
    private readonly ChatConfig mConfig;

    [Inject]
    public ChatPresenter(
        IChatService chatService,
        IChatHistoryService history,
        ILocationRealtimeService locationRealtime,
        IChatRealtimeService chatRealtime,
        LocationPresenter locationPresenter,
        BattleReportPresenter battleReport,
        ICharacterContext characterContext,
        INotificationService notifications,
        ChatConfig config)
    {
        mChatService = chatService;
        mHistory = history;
        mLocationRealtime = locationRealtime;
        mChatRealtime = chatRealtime;
        mLocationPresenter = locationPresenter;
        mBattleReport = battleReport;
        mCharacterContext = characterContext;
        mNotifications = notifications;
        mConfig = config;

        AutoDispose(
            mIsOpen, mShowLocation, mShowTrade, mSendChannel,
            mPrivateTargetCharacterId, mPrivateTargetNickname, mIsSending);

        mDisplayedMessages = ReactiveExtensions.Combine(
            mHistory.AllMessages, mShowLocation.Readonly, mShowTrade.Readonly,
            (all, showLocation, showTrade) => BuildDisplayList(all, showLocation, showTrade),
            this);

        // Приём сообщений — ВСЕГДА, независимо от IsOpen (панель — чистое UI поверх буфера).
        mLocationRealtime.ChatMessageReceived += OnChatMessageReceived;
        mChatRealtime.ChatMessageReceived += OnChatMessageReceived;

        // Смена локации → буфер локационного чата стухает (см. IChatHistoryService.ClearLocationBuffer).
        // callOnSubscribe:false — на старте сцены чистить ещё нечего, это не "смена", а первый заход.
        mLocationPresenter.CurrentLocationId
            .SubscribeOnValueChanged(OnCurrentLocationIdChanged, callOnSubscribe: false)
            .DisposeWhenLifeEnded(this);
    }

    // ─── Публичные команды ──────────────────────────────────────────────────────

    public void Open() => mIsOpen.Value = true;
    public void Close() => mIsOpen.Value = false;

    public void SetShowLocation(bool value) => mShowLocation.Value = value;
    public void SetShowTrade(bool value) => mShowTrade.Value = value;

    public void SetSendChannel(ChatSendChannel channel) => mSendChannel.Value = channel;

    /// <summary>Выбрать адресата лички — из профиля игрока ("Написать") или тапом по
    /// сообщению (см. SetPrivateTargetFromLine). Сразу переключает канал отправки на Private —
    /// выбор адресата означает "хочу написать ему сейчас".</summary>
    public void SetPrivateTarget(long characterId, string nickname)
    {
        if (mCharacterContext.CharacterId.Value == characterId) return; // самому себе — сервер и так откажет, не даём даже попытаться

        mKnownNicknames[characterId] = nickname;
        mPrivateTargetCharacterId.Value = characterId;
        mPrivateTargetNickname.Value = nickname;
        mSendChannel.Value = ChatSendChannel.Private;
    }

    /// <summary>
    /// "Ответить" тапом по строке чат-лога — то же самое, что SetPrivateTarget,
    /// но данные берутся из уже отображённого сообщения.
    ///
    /// У системной строки отправителя нет, отвечать некому — молча выходим. Это не «кнопка
    /// заблокирована»: View до сюда с системной строкой вообще не доходит (см. Item_ChatMessage),
    /// проверка здесь — страховка на случай второго вызывающего.
    /// </summary>
    public void SetPrivateTargetFromLine(ChatDisplayLine line)
    {
        if (line?.SenderCharacterId is not { } characterId) return;
        SetPrivateTarget(characterId, line.SenderNickname);
    }

    /// <summary>
    /// Тап по кликабельному фрагменту строки. Диспетчер: разбирает вид действия и зовёт
    /// нужный обработчик. Видов пока один, но развилка заведена сразу — сервер шлёт
    /// обобщённое поле Kind, и левелап/поломка вещи/истёкший таймер лягут сюда же.
    /// </summary>
    public void InvokeLineAction(ChatDisplayLine line)
    {
        if (line == null || !line.HasClickableAction) return;

        switch (line.ActionKind)
        {
            case ChatActionKinds.BATTLE_RESULT:
                OpenBattleResult(line);
                break;

            default:
                // Сервер новее клиента — прислал вид действия, которого мы не знаем. Тихо
                // игнорируем: показать «неизвестное действие» игроку нечестно, он ни при чём.
                Debug.LogWarning($"[ChatPresenter] Неизвестный вид действия на строке: «{line.ActionKind}».");
                break;
        }
    }

    /// <summary>
    /// Отправляет текст в текущий канал отправки (SendChannel). Валидация длины/пустоты —
    /// только для UX (быстрый отказ без похода в сеть); сервер всё равно проверяет
    /// независимо и может отказать по другим причинам (рейт-лимит/мут/блок) — в этом
    /// случае показываем его же текст ошибки тостом, он уже человекочитаем.
    /// </summary>
    public async UniTask SendAsync(string rawText, CancellationToken ct)
    {
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0) return;

        if (text.Length > mConfig.MaxMessageLength)
        {
            mNotifications.ShowError($"Сообщение длиннее {mConfig.MaxMessageLength} символов.");
            return;
        }

        var channel = mSendChannel.Value;
        if (channel == ChatSendChannel.Private && mPrivateTargetCharacterId.Value == null)
        {
            mNotifications.ShowError("Выбери получателя через профиль игрока.");
            return;
        }

        var request = new SendMessageRequest
        {
            ChannelType = ToRequestChannelType(channel),
            RecipientCharacterId = channel == ChatSendChannel.Private ? mPrivateTargetCharacterId.Value : null,
            Text = text
        };

        mIsSending.Value = true;
        try
        {
            var sent = await mChatService.SendAsync(request, ct);
            // Кладём сразу из REST-эха, не ждём SignalR-пуш — быстрее для собственного
            // сообщения; тот же Id прилетит следом через SignalR, дедуп в IChatHistoryService.
            mHistory.AddMessage(sent);
        }
        catch (ApiException ex)
        {
            mNotifications.ShowError(ex.ServerError);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            mNotifications.ShowError("Не удалось отправить сообщение.");
            Debug.LogWarning($"[ChatPresenter] SendAsync: {ex}");
        }
        finally
        {
            if (!IsDisposed) mIsSending.Value = false;
        }
    }

    // ─── Handle-методы ──────────────────────────────────────────────────────────

    private void OnChatMessageReceived(ChatMessageView message)
    {
        mHistory.AddMessage(message);
    }

    private void OnCurrentLocationIdChanged(long? locationId)
    {
        mHistory.ClearLocationBuffer();
    }

    // ─── Действия на строках ────────────────────────────────────────────────────

    private void OpenBattleResult(ChatDisplayLine line)
    {
        if (line.ActionSessionId is not { } sessionId)
        {
            // Действие BattleResult без номера сессии — рассинхрон с сервером, показывать нечего.
            Debug.LogWarning("[ChatPresenter] BattleResult без sessionId — действие пропущено.");
            return;
        }

        // Панель чата НЕ закрываем: окно результата лежит выше по иерархии канваса и
        // рисуется поверх, а закрыв его, игрок возвращается ровно туда, откуда тыкал.
        mBattleReport.ShowHistorical(sessionId, ToOutcome(line.ActionOutcome));
    }

    /// <summary>
    /// Исход боя из системной строки в клиентский enum. Значения приходят строками
    /// (см. ChatBattleOutcomes) — неизвестное считаем поражением: это самый безобидный
    /// вариант из трёх, он не обещает игроку награду, которой нет.
    /// </summary>
    private static CombatOutcome ToOutcome(string outcome) => outcome switch
    {
        ChatBattleOutcomes.WIN => CombatOutcome.Win,
        ChatBattleOutcomes.INTERRUPTED => CombatOutcome.Interrupted,
        _ => CombatOutcome.Loss
    };

    // ─── Внутреннее ─────────────────────────────────────────────────────────────

    private List<ChatDisplayLine> BuildDisplayList(List<ChatMessageView> all, bool showLocation, bool showTrade)
    {
        var result = new List<ChatDisplayLine>();
        if (all == null) return result;

        // Кликабельна только ПОСЛЕДНЯЯ строка боя из тех, что сейчас в буфере. Снимок награды
        // на сервере один на персонажа и перетирается следующим боем, поэтому у более старых
        // строк ссылка вела бы на данные, которых уже нет. Определяем по МАКСИМАЛЬНОМУ
        // sessionId, а не по позиции в списке: сессии нумеруются монотонно, и это надёжнее
        // сортировки по времени отправки сообщения.
        var lastBattleSessionId = FindLastBattleSessionId(all);

        foreach (var msg in all)
        {
            if (msg.ChannelType == ChatChannelTypes.VIEW_LOCATION && !showLocation) continue;
            if (msg.ChannelType == ChatChannelTypes.VIEW_TRADE && !showTrade) continue;
            // Private и System фильтрам не подчиняются (видны всегда, чекбокса не имеют).
            // Group в буфер вообще не попадает (соответствующий SignalR-канал не подключаем —
            // см. беклог).

            result.Add(BuildDisplayLine(msg, lastBattleSessionId));
        }

        return result;
    }

    /// <summary>Максимальный sessionId среди системок с действием "результат боя". null — таких нет.</summary>
    private static long? FindLastBattleSessionId(List<ChatMessageView> all)
    {
        long? max = null;

        foreach (var msg in all)
        {
            if (msg.Action == null) continue;
            if (msg.Action.Kind != ChatActionKinds.BATTLE_RESULT) continue;
            if (msg.Action.SessionId is not { } sessionId) continue;

            if (max == null || sessionId > max.Value)
                max = sessionId;
        }

        return max;
    }

    private ChatDisplayLine BuildDisplayLine(ChatMessageView msg, long? lastBattleSessionId)
    {
        if (msg.ChannelType == ChatChannelTypes.VIEW_SYSTEM)
            return BuildSystemLine(msg, lastBattleSessionId);

        // Кэш ников пополняем на КАЖДОМ сообщении, не только личных — дёшево и держит
        // кэш тёплым на случай, если игрок потом решит написать этому же человеку в личку.
        // Системки сюда не доходят: у них отправителя нет.
        if (msg.SenderCharacterId is { } senderId && !string.IsNullOrEmpty(msg.SenderNickname))
            mKnownNicknames[senderId] = msg.SenderNickname;

        var isMine = msg.SenderCharacterId.HasValue
                     && mCharacterContext.CharacterId.Value == msg.SenderCharacterId.Value;

        var body = isMine && msg.ChannelType == ChatChannelTypes.VIEW_PRIVATE
            ? $"Вы → {ResolveNickname(msg.ChannelScopeId)}: {msg.Text}"
            : $"{msg.SenderNickname}: {msg.Text}";

        return new ChatDisplayLine
        {
            MessageId = msg.Id,
            ChannelType = msg.ChannelType,
            SentAtUtc = msg.SentAt,
            SenderCharacterId = msg.SenderCharacterId,
            SenderNickname = msg.SenderNickname,
            BodyText = body
        };
    }

    /// <summary>
    /// Системная строка: текст сервера как есть, без «Ник: » впереди. Префикс
    /// «Системное сообщение: » у информационных строк тоже проставляет сервер — клиент
    /// текст не досочиняет, иначе одно и то же сообщение выглядело бы в чате и в журнале
    /// модерации по-разному.
    /// </summary>
    private ChatDisplayLine BuildSystemLine(ChatMessageView msg, long? lastBattleSessionId)
    {
        var action = msg.Action;

        var isEnabled = action != null
                        && action.Kind == ChatActionKinds.BATTLE_RESULT
                        && action.SessionId is { } sessionId
                        && lastBattleSessionId == sessionId;

        return new ChatDisplayLine
        {
            MessageId = msg.Id,
            ChannelType = msg.ChannelType,
            SentAtUtc = msg.SentAt,
            SenderCharacterId = null,
            SenderNickname = null,
            BodyText = msg.Text,
            ActionKind = action?.Kind,
            ActionLinkText = action?.LinkText,
            ActionSessionId = action?.SessionId,
            ActionOutcome = action?.Outcome,
            IsActionEnabled = isEnabled
        };
    }

    private string ResolveNickname(long? characterId)
    {
        if (characterId.HasValue && mKnownNicknames.TryGetValue(characterId.Value, out var nickname))
            return nickname;
        return "?"; // не должно случаться на практике — см. комментарий у mKnownNicknames
    }

    private static string ToRequestChannelType(ChatSendChannel channel) => channel switch
    {
        ChatSendChannel.Location => ChatChannelTypes.SEND_LOCATION,
        ChatSendChannel.Trade => ChatChannelTypes.SEND_TRADE,
        ChatSendChannel.Private => ChatChannelTypes.SEND_PRIVATE,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    protected override void OnDispose()
    {
        mLocationRealtime.ChatMessageReceived -= OnChatMessageReceived;
        mChatRealtime.ChatMessageReceived -= OnChatMessageReceived;
        base.OnDispose();
    }
}
