using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Лог боя — Popup_Log.
/// Показывает историю ударов, комбо и расходки текущего боя.
/// Скроллируется. Обновляется автоматически через CombatPresenter.CombatLogText.
///
/// Цвета:
///   Белый  — наш удар
///   Красный — удар по нам
///   Жёлтый — комбо-финишер
///   Голубой — расходка
///
/// Структура (создаётся вручную или через доработку CombatSetup):
///   Popup_Log (Image тёмный, на весь Panel_Combat)
///   └── ScrollRect
///       └── Viewport
///           └── Content (TMP_Text — сюда SetTextSource)
/// </summary>
public sealed class CombatLogView : DisposableBehaviour
{
    [SerializeField] private TMP_Text   mLogText;
    [SerializeField] private ScrollRect mScrollRect;
    [SerializeField] private Button     mButtonClose;

    private CombatPresenter mPresenter;

    [Inject]
    public void Construct(CombatPresenter presenter)
    {
        mPresenter = presenter;
    }

    private void Awake()
    {
        if (mButtonClose != null)
            mButtonClose.onClick.AddListener(() => gameObject.SetActive(false));

        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // При открытии — обновить текст и прокрутить вниз
        UpdateText(mPresenter?.CombatLogText.Value ?? string.Empty);

        mPresenter?.CombatLogText
            .SubscribeOnValueChanged(OnLogUpdated)
            .DisposeWhenLifeEnded(this);
    }

    private void OnLogUpdated(string text)
    {
        UpdateText(text);
        // Прокрутить до последней записи
        Canvas.ForceUpdateCanvases();
        if (mScrollRect != null)
            mScrollRect.verticalNormalizedPosition = 0f;
    }

    private void UpdateText(string text)
    {
        if (mLogText != null)
            mLogText.text = text;
    }
}
