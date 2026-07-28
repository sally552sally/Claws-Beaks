using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Попап результата боя — Popup_CombatResult.
/// Показывается поверх Panel_Combat когда бой завершён.
///
/// Содержит:
///   — «Победа!» / «Поражение...» / «Бой прерван»
///   — Плейсхолдер дропа (заполнится в Фазе 4 — Инвентарь)
///   — Кнопка OK → сбрасывает состояние боя (CombatPresenter.ExitCombatAsync).
///     Дальше решает LocationPresenter (TD-C32, подписка на CombatEnded):
///     при победе и прерывании остаёмся в охоте, при поражении — закрывает охоту
///     и обновляет локацию (там же, если нужно, всплывёт диалог воскрешения).
///
/// Скрытием попапа управляет View_Combat по IsFinished — на собственную кнопку OK
/// полагаться нельзя: бой может быть сброшен и мимо неё.
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
    public void Show(CombatOutcome outcome)
    {
        if (mLabelResult != null)
            mLabelResult.text = outcome switch
            {
                CombatOutcome.Win => "Победа!",
                CombatOutcome.Interrupted => "Бой прерван",
                _ => "Поражение..."
            };

        // У прерванного боя нет ни победителя, ни лута — обещать дроп нечестно.
        if (mLabelDrop != null)
            mLabelDrop.text = outcome == CombatOutcome.Interrupted
                ? "Бой длился слишком долго и был прерван. HP сохранены."
                // TD Фаза 4: подтянуть реальный дроп из инвентаря после боя
                : "Дроп: см. инвентарь";

        gameObject.SetActive(true);
    }

    /// <summary>Скрыть попап (бой сброшен — в том числе мимо кнопки OK).</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void OnOkClicked()
    {
        gameObject.SetActive(false);
        mPresenter.ExitCombatAsync().Forget();
    }
}
