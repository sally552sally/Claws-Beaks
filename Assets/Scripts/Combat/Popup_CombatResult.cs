using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Попап результата боя — Popup_CombatResult.
/// Показывается поверх Panel_Combat когда бой завершён.
///
/// Содержит:
///   — «Победа!» / «Поражение...»
///   — Плейсхолдер дропа (заполнится в Фазе 4 — Инвентарь)
///   — Кнопка OK → сбрасывает состояние боя и возвращает на охоту
///
/// GameObject: Popup_CombatResult (вложен в Panel_Combat или выше в иерархии)
/// </summary>
public sealed class Popup_CombatResult : MonoBehaviour
{
    [SerializeField] private TMP_Text mLabelResult;
    [SerializeField] private TMP_Text mLabelDrop;
    [SerializeField] private Button   mButtonOk;

    private CombatPresenter mPresenter;

    [Inject]
    public void Construct(CombatPresenter presenter)
    {
        mPresenter = presenter;
    }

    private void Awake()
    {
        mButtonOk.onClick.AddListener(OnOkClicked);
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        mButtonOk.onClick.RemoveListener(OnOkClicked);
    }

    /// <summary>Показать попап с результатом боя.</summary>
    public void Show(bool won)
    {
        if (mLabelResult != null)
            mLabelResult.text = won ? "Победа!" : "Поражение...";

        // TD Фаза 4: подтянуть реальный дроп из инвентаря после боя
        if (mLabelDrop != null)
            mLabelDrop.text = "Дроп: см. инвентарь";

        gameObject.SetActive(true);
    }

    private void OnOkClicked()
    {
        gameObject.SetActive(false);
        mPresenter.ExitCombatAsync().Forget();
    }
}
