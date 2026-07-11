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
///   — фильтры ОТОБРАЖЕНИЯ (ShowLocation/ShowTrade) — Личка и (в беклоге) Система
///     показываются всегда, чекбокса не имеют;
///   — канал ОТПРАВКИ (SendChannel) — отдельно от фильтров: можно смотреть только
///     Локацию, но писать в Торговый — независимые состояния;
///   — адресата лички (PrivateTargetCharacterId/Nickname) — выставляется через
///     ContextMenuPopup.MessageClicked ("Написать" в профиле игрока) или тапом по
///     уже полученному сообщению (SetPrivateTargetFromLine — "ответить");
///   — слияние двух источников живых сообщений (LocationHub для Location,
///     ChatHub для Trade/Private) в единый буфер через IChatHistoryService, и
///     построение отфильтрованного DisplayedMessages для View.
///
/// АРХИТЕКТУРА (UnityStyle): чистый C#, без using UnityEngine логики (Debug допустим,
/// как и в остальных Presenter'ах проекта). Цвет тега канала НЕ здесь — см. ChatDisplayLine.
///
/// Presenter→Presenter: инжектит LocationPresenter напрямую (как InventoryPresenter
/// инжектит CombatPresenter) — читает CurrentLocationId, чтобы чистить буфер локации
/// при смене локации.
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
        ICharacterContext characterContext,
        INotificationService notifications,
        ChatConfig config)
    {
        mChatService = chatService;
        mHistory = history;
        mLocationRealtime = locationRealtime;
        mChatRealtime = chatRealtime;
        mLocationPresenter = locationPresenter;
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

    /// <summary>"Ответить" тапом по строке чат-лога — то же самое, что SetPrivateTarget,
    /// но данные берутся из уже отображённого сообщения.</summary>
    public void SetPrivateTargetFromLine(ChatDisplayLine line)
    {
        if (line == null) return;
        SetPrivateTarget(line.SenderCharacterId, line.SenderNickname);
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

    // ─── Внутреннее ─────────────────────────────────────────────────────────────

    private List<ChatDisplayLine> BuildDisplayList(List<ChatMessageView> all, bool showLocation, bool showTrade)
    {
        var result = new List<ChatDisplayLine>();
        if (all == null) return result;

        foreach (var msg in all)
        {
            if (msg.ChannelType == ChatChannelTypes.VIEW_LOCATION && !showLocation) continue;
            if (msg.ChannelType == ChatChannelTypes.VIEW_TRADE && !showTrade) continue;
            // Private — фильтру не подчиняется (всегда виден). Group в буфер вообще
            // не попадает (соответствующий SignalR-канал не подключаем — см. беклог).

            result.Add(BuildDisplayLine(msg));
        }

        return result;
    }

    private ChatDisplayLine BuildDisplayLine(ChatMessageView msg)
    {
        // Кэш ников пополняем на КАЖДОМ сообщении, не только личных — дёшево и держит
        // кэш тёплым на случай, если игрок потом решит написать этому же человеку в личку.
        if (!string.IsNullOrEmpty(msg.SenderNickname))
            mKnownNicknames[msg.SenderCharacterId] = msg.SenderNickname;

        var isMine = mCharacterContext.CharacterId.Value == msg.SenderCharacterId;
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
