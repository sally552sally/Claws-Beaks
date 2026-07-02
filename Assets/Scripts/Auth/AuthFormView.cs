using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Единственный View для Auth-сцены. Один экран — два режима.
/// Тексты кнопок меняются реактивно. Никакого SetActive между экранами.
/// GameObject: View_Auth
/// </summary>
public class AuthFormView : DisposableBehaviour
{
    [SerializeField] private TMP_Text       mTitleLabel;
    [SerializeField] private TMP_InputField mEmailInput;
    [SerializeField] private TMP_InputField mPasswordInput;
    [SerializeField] private Button         mSubmitButton;
    [SerializeField] private TMP_Text       mSubmitButtonLabel;
    [SerializeField] private Button         mSwitchButton;
    [SerializeField] private TMP_Text       mSwitchButtonLabel;
    [SerializeField] private GameObject     mSpinner;

    private AuthPresenter mPresenter;
    private BanPopup      mBanPopup;

    [Inject]
    public void Construct(AuthPresenter presenter, BanPopup banPopup)
    {
        mPresenter = presenter;
        mBanPopup  = banPopup;
    }

    protected override void SafeAwake()
    {
        // Тексты — меняются при смене режима автоматически
        mTitleLabel.SetTextSource(mPresenter.TitleText)
            .DisposeWhenLifeEnded(this);

        mSubmitButtonLabel.SetTextSource(mPresenter.SubmitButtonText)
            .DisposeWhenLifeEnded(this);

        mSwitchButtonLabel.SetTextSource(mPresenter.SwitchButtonText)
            .DisposeWhenLifeEnded(this);

        // Состояние
        mPresenter.IsLoading
            .SubscribeOnValueChanged(SetLoadingState)
            .DisposeWhenLifeEnded(this);

        // Ошибки формы/сервера теперь идут тостами через INotificationService
        // (панель Canvas_Notifications) — см. Фаза 5. BanMessage остаётся отдельным
        // модальным BanPopup, это не обычная ошибка.

        mPresenter.BanMessage
            .SubscribeOnValueChanged(OnBanMessageChanged)
            .DisposeWhenLifeEnded(this);

        // Кнопки
        mSubmitButton.SubscribeOnClick(OnSubmitClicked).DisposeWhenLifeEnded(this);
        mSwitchButton.SubscribeOnClick(OnSwitchClicked).DisposeWhenLifeEnded(this);
    }

    // ─── Обработчики ─────────────────────────────────────────────────────────

    private void OnSubmitClicked()
    {
        mPresenter.SubmitAsync(
            mEmailInput.text,
            mPasswordInput.text,
            destroyCancellationToken).Forget();
    }

    private void OnSwitchClicked()
    {
        // Очищаем поля при переключении режима
        mEmailInput.text    = string.Empty;
        mPasswordInput.text = string.Empty;
        mPresenter.SwitchMode();
    }

    // ─── Обновление UI ───────────────────────────────────────────────────────

    private void SetLoadingState(bool isLoading)
    {
        mSubmitButton.interactable = !isLoading;
        mSwitchButton.interactable = !isLoading;
        mSpinner.SetActive(isLoading);
    }

    private void OnBanMessageChanged(string message)
    {
        if (!string.IsNullOrEmpty(message))
            mBanPopup.Show(message);
    }
}
