using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Попап результата боя — Popup_CombatResult.
///
/// Содержит:
///   — «Победа!» / «Поражение...» / «Бой прерван»
///   — Награду за бой: золото, опыт, выпавшие вещи, следы «Запаса сил», баннер левелапа
///   — Сворачиваемую таблицу участников: кто был в замесе, сколько нанёс урона, кто выжил
///   — Кнопку OK.
///
/// Открывается ДВУМЯ путями: концом живого боя (View_Combat) и тапом по «[Результат боя]»
/// в системной строке чата. Раньше это было невозможно: попап читал состояние прямо из
/// CombatPresenter, а OK звал ExitCombatAsync — то есть окно умело показывать только
/// текущий бой и обязано было его завершать. Теперь всё это знает BattleReportPresenter,
/// а попап стал обычным View поверх его реактивных полей и сам ничего не решает.
///
/// ИЕРАРХИЯ: объект живёт на уровне SafeArea, НЕ внутри Panel_Combat. Внутри он открыться
/// из чата физически не может — Panel_Combat выключена, когда боя нет, а SetActive(true)
/// на ребёнке выключенного родителя ничего не делает. Перенос делает Editor/SystemChatSetup.
///
/// TD-C33: текстовый вывод награды — временный. Нормальная панель (иконки предметов, цвета
/// редкости, анимация опыта) ждёт макета от штаба.
///
/// GameObject: Popup_CombatResult (прямой потомок SafeArea, последний в порядке — рисуется поверх)
/// </summary>
public sealed class Popup_CombatResult : DisposableBehaviour
{
    [Header("Результат и награда")]
    [SerializeField] private TMP_Text mLabelResult;
    [SerializeField] private TMP_Text mLabelDrop;
    [SerializeField] private Button mButtonOk;

    [Header("Участники боя")]
    [SerializeField] private Button mButtonParticipants;      // заголовок-переключатель
    [SerializeField] private TMP_Text mLabelParticipantsHeader; // «Участники (4)» / «Загрузка…»
    [SerializeField] private GameObject mParticipantsBody;      // контейнер, который сворачивается
    [SerializeField] private Transform mParticipantsContent;    // родитель строк
    [SerializeField] private Item_BattleParticipant mParticipantItemPrefab;

    private BattleReportPresenter mPresenter;
    private IViewPool<Item_BattleParticipant> mParticipantPool;

    [Inject]
    public void Construct(BattleReportPresenter presenter)
    {
        mPresenter = presenter;
    }

    protected override void SafeAwake()
    {
        base.SafeAwake();

        if (mParticipantItemPrefab != null && mParticipantsContent != null)
            mParticipantPool = new ViewPool<Item_BattleParticipant>(mParticipantItemPrefab, mParticipantsContent);

        BindButtons();
        BindReactive();

        gameObject.SetActive(mPresenter.IsOpen.Value);
    }

    // ─── Привязки ─────────────────────────────────────────────────────────────

    private void BindButtons()
    {
        if (mButtonOk != null)
            mButtonOk.SubscribeOnClick(() => mPresenter.Close()).DisposeWhenLifeEnded(this);

        if (mButtonParticipants != null)
            mButtonParticipants.SubscribeOnClick(() => mPresenter.ToggleParticipants())
                .DisposeWhenLifeEnded(this);
    }

    private void BindReactive()
    {
        mPresenter.IsOpen
            .SubscribeOnValueChanged(gameObject.SetActive)
            .DisposeWhenLifeEnded(this);

        mPresenter.Outcome
            .SubscribeOnValueChanged(RenderOutcome)
            .DisposeWhenLifeEnded(this);

        // Награда может доехать ПОЗЖЕ открытия окна (живой бой — дочитывание снимка,
        // исторический — запрос last-reward), поэтому именно подписка, а не разовая отрисовка.
        mPresenter.Reward
            .SubscribeOnValueChanged(_ => RenderReward())
            .DisposeWhenLifeEnded(this);

        mPresenter.LevelUp
            .SubscribeOnValueChanged(_ => RenderReward())
            .DisposeWhenLifeEnded(this);

        mPresenter.Participants
            .SubscribeOnValueChanged(RebuildParticipants)
            .DisposeWhenLifeEnded(this);

        mPresenter.IsParticipantsLoading
            .SubscribeOnValueChanged(_ => RenderParticipantsHeader())
            .DisposeWhenLifeEnded(this);

        mPresenter.ParticipantsFailed
            .SubscribeOnValueChanged(_ => RenderParticipantsHeader())
            .DisposeWhenLifeEnded(this);

        mPresenter.IsParticipantsExpanded
            .SubscribeOnValueChanged(OnParticipantsExpandedChanged)
            .DisposeWhenLifeEnded(this);
    }

    // ─── Отрисовка ────────────────────────────────────────────────────────────

    private void RenderOutcome(CombatOutcome outcome)
    {
        if (mLabelResult != null)
            mLabelResult.text = outcome switch
            {
                CombatOutcome.Win => "Победа!",
                CombatOutcome.Interrupted => "Бой прерван",
                _ => "Поражение..."
            };

        // Блок награды зависит от исхода (у прерванного боя её не бывает) — перерисовываем.
        RenderReward();
    }

    private void RenderReward()
    {
        if (mLabelDrop == null) return;

        // У прерванного боя нет ни победителя, ни лута — обещать дроп нечестно.
        if (mPresenter.Outcome.Value == CombatOutcome.Interrupted)
        {
            mLabelDrop.text = "Бой длился слишком долго и был прерван. HP сохранены.";
            return;
        }

        var reward = mPresenter.Reward.Value;
        if (reward == null)
        {
            // Награды нет вовсе: поражение, либо победа, где моба добил союзник (в N×M награда
            // идёт тому, кто бил его лично). Обещать «см. инвентарь» в этом случае — врать.
            mLabelDrop.text = mPresenter.Outcome.Value == CombatOutcome.Win
                ? "Награды нет."
                : string.Empty;
            return;
        }

        var text = new StringBuilder();
        text.Append($"Опыт: {reward.Experience}   Золото: {reward.Gold}");

        if (reward.RestedBonusApplied)
            text.Append($"\n<color=#7FD8FF>Запас сил применён (осталось зарядов: {reward.RestedChargesLeft})</color>");

        var levelUp = mPresenter.LevelUp.Value;
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

    private void RebuildParticipants(List<BattleReportLine> lines)
    {
        RenderParticipantsHeader();

        if (mParticipantPool == null) return;

        mParticipantPool.ReturnAll();
        if (lines == null) return;

        foreach (var line in lines)
            mParticipantPool.Get().Setup(line);
    }

    /// <summary>
    /// Заголовок-переключатель. Три состояния, и их нельзя схлопывать: «грузим», «не смогли»
    /// и «загрузили, вот столько» — разные вещи, а пустая таблица без пояснения читалась бы
    /// как «в бою никого не было».
    /// </summary>
    private void RenderParticipantsHeader()
    {
        if (mLabelParticipantsHeader == null) return;

        var expandMark = mPresenter.IsParticipantsExpanded.Value ? "▼" : "►";

        if (mPresenter.ParticipantsFailed.Value)
        {
            mLabelParticipantsHeader.text = "Состав боя загрузить не удалось";
            return;
        }

        if (mPresenter.IsParticipantsLoading.Value)
        {
            mLabelParticipantsHeader.text = "Участники: загрузка…";
            return;
        }

        var count = mPresenter.Participants.Value?.Count ?? 0;
        mLabelParticipantsHeader.text = $"{expandMark} Участники ({count})";
    }

    private void OnParticipantsExpandedChanged(bool expanded)
    {
        if (mParticipantsBody != null)
            mParticipantsBody.SetActive(expanded);

        RenderParticipantsHeader();
    }
}
