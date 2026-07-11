using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// View панели уведомлений (Canvas_Notifications): тост снизу экрана + модальный диалог по центру.
/// Никакой логики очередей — только подписка на INotificationService и отрисовка + проброс кликов.
///
/// Живёт в каждой сцене, где нужны уведомления (Auth и Game), но резолвит ОДИН сервис с
/// ProjectContext — как BanPopup резолвит сервисы уровня проекта, оставаясь объектом сцены.
///
/// GameObject: Canvas_Notifications → SafeArea → Panel_Toast / Panel_Dialog (собирается
/// Editor-скриптом NotificationsSetup).
/// </summary>
public sealed class View_Notifications : DisposableBehaviour
{
    [SerializeField] private NotificationConfig mConfig;

    [Header("Тост")]
    [SerializeField] private GameObject mToastRoot;
    [SerializeField] private TMP_Text mToastLabel;
    [SerializeField] private Image mToastAccent;
    [SerializeField] private Button mToastTapArea;   // невидимая кнопка на весь тост — тап дисмиссит
    [SerializeField] private Button mToastActionButton;
    [SerializeField] private TMP_Text mToastActionLabel;

    [Header("Диалог")]
    [SerializeField] private GameObject mDialogRoot;
    [SerializeField] private Image mDialogBlocker;
    [SerializeField] private TMP_Text mDialogTitleLabel;
    [SerializeField] private TMP_Text mDialogMessageLabel;
    [SerializeField] private Button mDialogPrimaryButton;
    [SerializeField] private TMP_Text mDialogPrimaryLabel;
    [SerializeField] private Button mDialogSecondaryButton;
    [SerializeField] private TMP_Text mDialogSecondaryLabel;

    private INotificationService mService;

    [Inject]
    public void Construct(INotificationService service)
    {
        mService = service;
    }

    protected override void SafeAwake()
    {
        mService.CurrentToast
            .SubscribeOnValueChanged(SetToast)
            .DisposeWhenLifeEnded(this);

        mService.CurrentDialog
            .SubscribeOnValueChanged(SetDialog)
            .DisposeWhenLifeEnded(this);

        if (mToastTapArea != null)
            mToastTapArea.SubscribeOnClick(OnToastTapped).DisposeWhenLifeEnded(this);

        if (mDialogPrimaryButton != null)
            mDialogPrimaryButton.SubscribeOnClick(() => mService.RespondToDialog(true)).DisposeWhenLifeEnded(this);

        if (mDialogSecondaryButton != null)
            mDialogSecondaryButton.SubscribeOnClick(() => mService.RespondToDialog(false)).DisposeWhenLifeEnded(this);
    }

    // ─── Тост ────────────────────────────────────────────────────────────────

    private void OnToastTapped()
    {
        // Тап по телу тоста = дисмисс. Если у тоста есть кнопка-действие, у неё свой обработчик
        // (mToastActionButton) — она перехватывает клик и до tap-area не доходит.
        mService.DismissCurrentToast();
    }

    private void SetToast(ToastRequest toast)
    {
        bool visible = toast != null;
        if (mToastRoot != null) mToastRoot.SetActive(visible);
        if (!visible) return;

        if (mToastLabel != null) mToastLabel.text = toast.Message;
        if (mToastAccent != null && mConfig != null) mToastAccent.color = mConfig.ColorFor(toast.Type);

        // Опциональная кнопка-действие.
        if (mToastActionButton != null)
        {
            mToastActionButton.onClick.RemoveAllListeners();
            mToastActionButton.gameObject.SetActive(toast.HasAction);

            if (toast.HasAction)
            {
                if (mToastActionLabel != null) mToastActionLabel.text = toast.ActionLabel;
                var action = toast.OnAction;
                mToastActionButton.onClick.AddListener(() =>
                {
                    // Действие + дисмисс текущего тоста.
                    action?.Invoke();
                    mService.DismissCurrentToast();
                });
            }
        }
    }

    // ─── Диалог ──────────────────────────────────────────────────────────────

    private void SetDialog(DialogRequest dialog)
    {
        bool visible = dialog != null;
        if (mDialogRoot != null) mDialogRoot.SetActive(visible);
        if (!visible) return;

        bool hasTitle = !string.IsNullOrEmpty(dialog.Title);
        if (mDialogTitleLabel != null)
        {
            mDialogTitleLabel.gameObject.SetActive(hasTitle);
            if (hasTitle)
            {
                mDialogTitleLabel.text = dialog.Title;
                if (mConfig != null) mDialogTitleLabel.color = mConfig.ColorFor(dialog.Type);
            }
        }

        if (mDialogMessageLabel != null) mDialogMessageLabel.text = dialog.Message;

        if (mDialogBlocker != null && mConfig != null)
            mDialogBlocker.color = new Color(0f, 0f, 0f, mConfig.DialogDimAlpha);

        if (mDialogPrimaryLabel != null) mDialogPrimaryLabel.text = dialog.PrimaryLabel;

        // Вторичная кнопка показывается только если задана подпись (иначе диалог-уведомление с одной кнопкой).
        if (mDialogSecondaryButton != null)
            mDialogSecondaryButton.gameObject.SetActive(dialog.HasSecondary);
        if (dialog.HasSecondary && mDialogSecondaryLabel != null)
            mDialogSecondaryLabel.text = dialog.SecondaryLabel;
    }
}
