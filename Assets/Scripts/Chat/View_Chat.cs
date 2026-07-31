using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Экран чата — Panel_Chat (Game-сцена, на весь экран поверх Location/Hunting, открывается
/// кнопкой рядом с «Инвентарь» в обоих). Единый слитный лог (не вкладки) — фильтры
/// Локация/Торговый чекбоксами, Личка и Система видны всегда без чекбокса. Отдельно, под логом —
/// переключатель КУДА пишем (Локация/Торговый/Личка), не путать с фильтрами приёма.
///
/// Приём сообщений НЕ зависит от этого экрана — ChatPresenter копит буфер всегда,
/// этот View только рисует то, что там уже есть (см. ChatPresenter.DisplayedMessages).
///
/// Список сообщений — через ViewPool (не Instantiate/Destroy на каждое сообщение).
///
/// Иерархию собирает Editor/ChatSetup.cs.
///
/// GameObject: Panel_Chat
/// </summary>
public sealed class View_Chat : DisposableBehaviour
{
    private static readonly Color ActiveColor = new(0.30f, 0.50f, 0.32f);
    private static readonly Color InactiveColor = new(0.20f, 0.20f, 0.24f);

    [Header("Шапка")]
    [SerializeField] private Button mButtonClose;

    [Header("Фильтры приёма (чекбоксы)")]
    [SerializeField] private Button mFilterLocationButton;
    [SerializeField] private Image mFilterLocationBg;
    [SerializeField] private Button mFilterTradeButton;
    [SerializeField] private Image mFilterTradeBg;

    [Header("Лог сообщений")]
    [SerializeField] private ScrollRect mScrollRect;
    [SerializeField] private Transform mMessagesContent;
    [SerializeField] private Item_ChatMessage mMessageItemPrefab;

    [Header("Канал отправки (выбрана ровно одна)")]
    [SerializeField] private Button mSendLocationButton;
    [SerializeField] private Image mSendLocationBg;
    [SerializeField] private Button mSendTradeButton;
    [SerializeField] private Image mSendTradeBg;
    [SerializeField] private Button mSendPrivateButton;
    [SerializeField] private Image mSendPrivateBg;
    [SerializeField] private TMP_Text mSendPrivateLabel; // "Личка" / "Личка → Ник"

    [Header("Ввод")]
    [SerializeField] private TMP_InputField mInputField;
    [SerializeField] private TMP_Text mCounterLabel;
    [SerializeField] private Button mSendButton;

    // ─── Внутренний стейт ─────────────────────────────────────────────────────

    private ChatPresenter mPresenter;
    private ChatConfig mConfig;
    private IViewPool<Item_ChatMessage> mMessagePool;

    // ─── Zenject Inject ───────────────────────────────────────────────────────

    [Inject]
    public void Construct(ChatPresenter presenter, ChatConfig config)
    {
        mPresenter = presenter;
        mConfig = config;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        mMessagePool = new ViewPool<Item_ChatMessage>(mMessageItemPrefab, mMessagesContent);

        BindButtons();
        BindInput();
        BindReactive();

        gameObject.SetActive(mPresenter.IsOpen.Value);
    }

    protected override void OnDispose()
    {
        if (mInputField != null)
            mInputField.onValueChanged.RemoveListener(OnInputChanged);

        base.OnDispose();
    }

    // ─── Привязки ─────────────────────────────────────────────────────────────

    private void BindButtons()
    {
        if (mButtonClose != null)
            mButtonClose.SubscribeOnClick(() => mPresenter.Close()).DisposeWhenLifeEnded(this);

        if (mFilterLocationButton != null)
            mFilterLocationButton.SubscribeOnClick(OnFilterLocationClicked).DisposeWhenLifeEnded(this);
        if (mFilterTradeButton != null)
            mFilterTradeButton.SubscribeOnClick(OnFilterTradeClicked).DisposeWhenLifeEnded(this);

        if (mSendLocationButton != null)
            mSendLocationButton.SubscribeOnClick(() => mPresenter.SetSendChannel(ChatSendChannel.Location))
                .DisposeWhenLifeEnded(this);
        if (mSendTradeButton != null)
            mSendTradeButton.SubscribeOnClick(() => mPresenter.SetSendChannel(ChatSendChannel.Trade))
                .DisposeWhenLifeEnded(this);
        if (mSendPrivateButton != null)
            mSendPrivateButton.SubscribeOnClick(() => mPresenter.SetSendChannel(ChatSendChannel.Private))
                .DisposeWhenLifeEnded(this);

        if (mSendButton != null)
            mSendButton.SubscribeOnClick(OnSendClicked).DisposeWhenLifeEnded(this);
    }

    private void BindInput()
    {
        if (mInputField == null) return;

        mInputField.characterLimit = mConfig.MaxMessageLength;
        mInputField.onValueChanged.AddListener(OnInputChanged);
        UpdateCounter(mInputField.text ?? string.Empty);
    }

    private void BindReactive()
    {
        mPresenter.IsOpen
            .SubscribeOnValueChanged(gameObject.SetActive)
            .DisposeWhenLifeEnded(this);

        mPresenter.ShowLocation
            .SubscribeOnValueChanged(v => SetToggleVisual(mFilterLocationBg, v))
            .DisposeWhenLifeEnded(this);

        mPresenter.ShowTrade
            .SubscribeOnValueChanged(v => SetToggleVisual(mFilterTradeBg, v))
            .DisposeWhenLifeEnded(this);

        mPresenter.SendChannel
            .SubscribeOnValueChanged(OnSendChannelChanged)
            .DisposeWhenLifeEnded(this);

        mPresenter.PrivateTargetNickname
            .SubscribeOnValueChanged(OnPrivateTargetChanged)
            .DisposeWhenLifeEnded(this);

        mPresenter.IsSending
            .SubscribeOnValueChanged(sending =>
            {
                if (mSendButton != null) mSendButton.interactable = !sending;
            })
            .DisposeWhenLifeEnded(this);

        mPresenter.DisplayedMessages
            .SubscribeOnValueChanged(RebuildMessages)
            .DisposeWhenLifeEnded(this);
    }

    // ─── Лог сообщений ────────────────────────────────────────────────────────

    private void RebuildMessages(List<ChatDisplayLine> lines)
    {
        mMessagePool.ReturnAll();
        if (lines == null) return;

        foreach (var line in lines)
        {
            var item = mMessagePool.Get();
            item.Setup(
                line,
                TagFor(line.ChannelType),
                mConfig.ColorFor(line.ChannelType),
                OnMessageClicked,
                OnMessageActionClicked);
        }

        ScrollToBottom();
    }

    private static string TagFor(string channelType) => channelType switch
    {
        ChatChannelTypes.VIEW_TRADE => "Торг",
        ChatChannelTypes.VIEW_PRIVATE => "Личка",
        ChatChannelTypes.VIEW_SYSTEM => "Система",
        _ => null // Локация — без тега, это канал "по умолчанию"
    };

    private void ScrollToBottom()
    {
        if (mScrollRect == null) return;
        // Content верхне-заякорен (см. Editor/ChatSetup.MakeScrollList) → низ списка = 0.
        Canvas.ForceUpdateCanvases();
        mScrollRect.verticalNormalizedPosition = 0f;
    }

    // ─── Обработчики ──────────────────────────────────────────────────────────

    private void OnFilterLocationClicked() => mPresenter.SetShowLocation(!mPresenter.ShowLocation.Value);
    private void OnFilterTradeClicked() => mPresenter.SetShowTrade(!mPresenter.ShowTrade.Value);

    /// <summary>Тап по обычной строке — «ответить» (личка отправителю).</summary>
    private void OnMessageClicked(ChatDisplayLine line) => mPresenter.SetPrivateTargetFromLine(line);

    /// <summary>
    /// Тап по кликабельному фрагменту строки. Что именно делать — решает Presenter
    /// (см. ChatPresenter.InvokeLineAction): View не знает ни про бой, ни про будущие
    /// виды действий, его дело — сообщить о попадании.
    /// </summary>
    private void OnMessageActionClicked(ChatDisplayLine line) => mPresenter.InvokeLineAction(line);

    private void OnSendChannelChanged(ChatSendChannel channel)
    {
        SetToggleVisual(mSendLocationBg, channel == ChatSendChannel.Location);
        SetToggleVisual(mSendTradeBg, channel == ChatSendChannel.Trade);
        SetToggleVisual(mSendPrivateBg, channel == ChatSendChannel.Private);
    }

    private void OnPrivateTargetChanged(string nickname)
    {
        if (mSendPrivateLabel != null)
            mSendPrivateLabel.text = string.IsNullOrEmpty(nickname) ? "Личка" : $"Личка → {nickname}";
    }

    private void OnInputChanged(string text) => UpdateCounter(text);

    private void OnSendClicked()
    {
        if (mInputField == null) return;

        var text = mInputField.text;
        mPresenter.SendAsync(text, destroyCancellationToken).Forget();

        // Очищаем сразу — не ждём ответа сети. Если сервер откажет (рейт-лимит/мут/лимит
        // длины), Presenter покажет тост; перепечатать несложно.
        mInputField.text = string.Empty;
        UpdateCounter(string.Empty);
    }

    private void UpdateCounter(string text)
    {
        if (mCounterLabel != null)
            mCounterLabel.text = $"{text?.Length ?? 0}/{mConfig.MaxMessageLength}";
    }

    private static void SetToggleVisual(Image background, bool active)
    {
        if (background == null) return;
        background.color = active ? ActiveColor : InactiveColor;
    }
}
