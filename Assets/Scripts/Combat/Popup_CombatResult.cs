using System.Text;
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
///   — Награду за бой: золото, опыт, выпавшие вещи, следы «Запаса сил», баннер левелапа
///   — Кнопка OK → сбрасывает состояние боя (CombatPresenter.ExitCombatAsync).
///     Дальше решает LocationPresenter (TD-C32, подписка на CombatEnded):
///     при победе и прерывании остаёмся в охоте, при поражении — закрывает охоту
///     и обновляет локацию (там же, если нужно, всплывёт диалог воскрешения).
///
/// Скрытием попапа управляет View_Combat по IsFinished — на собственную кнопку OK
/// полагаться нельзя: бой может быть сброшен и мимо неё.
///
/// TD-C33: текстовый вывод награды — временный. Нормальная панель (иконки предметов, цвета
/// редкости, анимация опыта) ждёт макета от штаба; здесь пока просто читаемый список, чтобы
/// данные были видны глазами, а не только в логе.
///
/// GameObject: Popup_CombatResult (вложен в Panel_Combat или выше в иерархии)
/// </summary>
public sealed class Popup_CombatResult : DisposableBehaviour
{
    [SerializeField] private TMP_Text mLabelResult;
    [SerializeField] private TMP_Text mLabelDrop;
    [SerializeField] private Button   mButtonOk;

    private CombatPresenter mPresenter;

    /// <summary>
    /// Исход текущего показа. Нужен, потому что награда может доехать ПОЗЖЕ открытия попапа:
    /// если ответ на добивающий ход потерялся, презентер дочитывает её отдельным запросом,
    /// и текст надо перерисовать уже на открытом окне.
    /// </summary>
    private CombatOutcome mCurrentOutcome = CombatOutcome.None;

    [Inject]
    public void Construct(CombatPresenter presenter)
    {
        mPresenter = presenter;

        // callOnSubscribe: false — на старте награды нет, а Show() и так рисует текст сам.
        mPresenter.LastReward
            .SubscribeOnValueChanged(_ => RenderReward(), callOnSubscribe: false)
            .DisposeWhenLifeEnded(this);

        mPresenter.LastLevelUp
            .SubscribeOnValueChanged(_ => RenderReward(), callOnSubscribe: false)
            .DisposeWhenLifeEnded(this);
    }

    protected override void SafeAwake()
    {
        base.SafeAwake();

        mButtonOk.onClick.AddListener(OnOkClicked);
        gameObject.SetActive(false);
    }

    protected override void OnDestroy()
    {
        mButtonOk.onClick.RemoveListener(OnOkClicked);

        base.OnDestroy();
    }

    /// <summary>Показать попап с результатом боя.</summary>
    public void Show(CombatOutcome outcome)
    {
        mCurrentOutcome = outcome;

        if (mLabelResult != null)
            mLabelResult.text = outcome switch
            {
                CombatOutcome.Win => "Победа!",
                CombatOutcome.Interrupted => "Бой прерван",
                _ => "Поражение..."
            };

        RenderReward();
        gameObject.SetActive(true);
    }

    /// <summary>Скрыть попап (бой сброшен — в том числе мимо кнопки OK).</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Перерисовать блок награды по текущему состоянию презентера.
    /// Зовётся и из Show(), и по приходу награды (она может доехать после открытия окна).
    /// </summary>
    private void RenderReward()
    {
        if (mLabelDrop == null)
            return;

        // У прерванного боя нет ни победителя, ни лута — обещать дроп нечестно.
        if (mCurrentOutcome == CombatOutcome.Interrupted)
        {
            mLabelDrop.text = "Бой длился слишком долго и был прерван. HP сохранены.";
            return;
        }

        var reward = mPresenter.LastReward.Value;
        if (reward == null)
        {
            // Награды нет вовсе: поражение, либо победа, где моба добил союзник (в N×M награда
            // идёт тому, кто бил его лично). Обещать «см. инвентарь» в этом случае — врать.
            mLabelDrop.text = mCurrentOutcome == CombatOutcome.Win
                ? "Награды нет."
                : string.Empty;
            return;
        }

        var text = new StringBuilder();
        text.Append($"Опыт: {reward.Experience}   Золото: {reward.Gold}");

        if (reward.RestedBonusApplied)
            text.Append($"\n<color=#7FD8FF>Запас сил применён (осталось зарядов: {reward.RestedChargesLeft})</color>");

        var levelUp = mPresenter.LastLevelUp.Value;
        if (levelUp != null)
            text.Append($"\n<color=#FFD700>Новый уровень: {levelUp.OldLevel} → {levelUp.NewLevel}</color>");

        if (reward.Items != null && reward.Items.Count > 0)
        {
            text.Append("\n\nДобыча:");
            foreach (var item in reward.Items)
                text.Append($"\n  {item.Name} ×{item.Quantity}");
        }

        mLabelDrop.text = text.ToString();
    }

    private void OnOkClicked()
    {
        gameObject.SetActive(false);
        mPresenter.ExitCombatAsync().Forget();
    }
}
