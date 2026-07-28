using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Экран боя — Panel_Combat.
/// Показывается поверх Panel_Hunting когда персонаж входит в бой.
/// Прячется после выхода из боя (нажатие OK на результате).
///
/// Управление: стойки (тогглы) + направление удара (кнопки) + пропуск + расходка.
/// Логика — в CombatPresenter. View только подписывается на реактивное состояние.
///
/// Структура (создаётся CombatSetup editor-скриптом):
///   Panel_Combat
///   ├── Panel_Header (Label_Title, Button_Log)
///   ├── Panel_HUD
///   │   ├── Panel_Player (Label_PlayerHp, Label_PlayerName)
///   │   ├── Panel_TurnInfo (Label_TurnStatus, Label_Timer)
///   │   └── Panel_Enemy  (Label_EnemyHp, Label_EnemyName)
///   ├── Panel_Stances   (Button_Normal, Button_Defensive, Button_Aggressive)
///   ├── Panel_Directions (Button_Head, Button_Body, Button_Legs)
///   ├── Button_Skip
///   ├── Panel_Combo  (ComboIndicatorView)
///   └── Panel_Slots  (ConsumableSlotView × 4)
///
/// GameObject: Panel_Combat
/// </summary>
public sealed class View_Combat : DisposableBehaviour
{
    // ─── HUD ──────────────────────────────────────────────────────────────────

    [Header("HUD")]
    [SerializeField] private TMP_Text mLabelPlayerHp;
    [SerializeField] private TMP_Text mLabelPlayerName;
    [SerializeField] private TMP_Text mLabelEnemyHp;
    [SerializeField] private TMP_Text mLabelEnemyName;
    [SerializeField] private TMP_Text mLabelTurnStatus;
    [SerializeField] private TMP_Text mLabelTimer;

    // ─── Стойки ───────────────────────────────────────────────────────────────

    [Header("Стойки")]
    [SerializeField] private Button mButtonNormal;
    [SerializeField] private Button mButtonDefensive;
    [SerializeField] private Button mButtonAggressive;

    // Цвет выделения выбранной стойки
    [SerializeField] private Color mStanceSelectedColor    = new Color(1f, 0.8f, 0f);
    [SerializeField] private Color mStanceUnselectedColor  = Color.white;

    // ─── Направления ──────────────────────────────────────────────────────────

    [Header("Направления удара")]
    [SerializeField] private Button mButtonHead;
    [SerializeField] private Button mButtonBody;
    [SerializeField] private Button mButtonLegs;

    // ─── Пропустить ───────────────────────────────────────────────────────────

    [Header("Пропустить")]
    [SerializeField] private Button mButtonSkip;

    // ─── Комбо-индикатор ──────────────────────────────────────────────────────

    [Header("Комбо")]
    [SerializeField] private ComboIndicatorView mComboIndicator;

    // ─── Расходка ─────────────────────────────────────────────────────────────

    [Header("Расходка")]
    [SerializeField] private ConsumableSlotView mSlot0;
    [SerializeField] private ConsumableSlotView mSlot1;
    [SerializeField] private ConsumableSlotView mSlot2;
    [SerializeField] private ConsumableSlotView mSlot3;

    // ─── Лог боя (попап) ──────────────────────────────────────────────────────

    [Header("Лог")]
    [SerializeField] private GameObject mLogPopup;
    [SerializeField] private Button mButtonLog;

    // ─── Результат боя ────────────────────────────────────────────────────────

    [Header("Результат")]
    [SerializeField] private Popup_CombatResult mResultPopup;

    // ─── Прочее ───────────────────────────────────────────────────────────────

    [Header("Спиннер")]
    [SerializeField] private GameObject mSpinner;

    // ─── Инъекции ─────────────────────────────────────────────────────────────

    private CombatPresenter mPresenter;

    [Inject]
    public void Construct(CombatPresenter presenter)
    {
        mPresenter = presenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        gameObject.SetActive(mPresenter.IsInCombat.Value);

        // Показывать/скрывать панель
        mPresenter.IsInCombat
            .SubscribeOnValueChanged(gameObject.SetActive)
            .DisposeWhenLifeEnded(this);

        // HUD — HP
        mPresenter.MyCurrentHp
            .SubscribeOnValueChanged(_ => UpdatePlayerHpText())
            .DisposeWhenLifeEnded(this);
        mPresenter.MyMaxHp
            .SubscribeOnValueChanged(_ => UpdatePlayerHpText())
            .DisposeWhenLifeEnded(this);
        mPresenter.EnemyCurrentHp
            .SubscribeOnValueChanged(_ => UpdateEnemyHpText())
            .DisposeWhenLifeEnded(this);
        mPresenter.EnemyMaxHp
            .SubscribeOnValueChanged(_ => UpdateEnemyHpText())
            .DisposeWhenLifeEnded(this);
        mLabelEnemyName
            .SetTextSource(mPresenter.EnemyName)
            .DisposeWhenLifeEnded(this);

        // Статус хода и таймер
        mPresenter.IsMyTurn
            .SubscribeOnValueChanged(UpdateTurnStatus)
            .DisposeWhenLifeEnded(this);
        mPresenter.SecondsLeft
            .SubscribeOnValueChanged(s => mLabelTimer.text = s > 0 ? $"{s}с" : string.Empty)
            .DisposeWhenLifeEnded(this);

        // Интерактивность кнопок направлений и пропуска
        mPresenter.IsMyTurn
            .SubscribeOnValueChanged(UpdateControlsInteractable)
            .DisposeWhenLifeEnded(this);
        mPresenter.IsLoading
            .SubscribeOnValueChanged(_ => UpdateControlsInteractable(mPresenter.IsMyTurn.Value))
            .DisposeWhenLifeEnded(this);

        // Стойка — подсветка выбранной кнопки
        mPresenter.SelectedStance
            .SubscribeOnValueChanged(UpdateStanceHighlight)
            .DisposeWhenLifeEnded(this);

        // Спиннер
        mPresenter.IsLoading
            .SubscribeOnValueChanged(active => mSpinner.SetActive(active))
            .DisposeWhenLifeEnded(this);

        // Результат боя
        mPresenter.IsFinished
            .SubscribeOnValueChanged(OnFinished)
            .DisposeWhenLifeEnded(this);

        // Добавить после подписок на IsMyTurn и IsLoading:
        mPresenter.IsFinished
            .SubscribeOnValueChanged(_ => UpdateControlsInteractable(mPresenter.IsMyTurn.Value))
            .DisposeWhenLifeEnded(this);

        // Расходка — пересборка при изменении лоадаута
        mPresenter.LoadoutSlots
            .SubscribeOnValueChanged(RebuildSlots)
            .DisposeWhenLifeEnded(this);

        // Кнопки стоек
        mButtonNormal    .SubscribeOnClick(() => mPresenter.SetStance("Normal"))   .DisposeWhenLifeEnded(this);
        mButtonDefensive .SubscribeOnClick(() => mPresenter.SetStance("Defensive")).DisposeWhenLifeEnded(this);
        mButtonAggressive.SubscribeOnClick(() => mPresenter.SetStance("Aggressive")).DisposeWhenLifeEnded(this);

        // Кнопки направлений — каждая немедленно отправляет удар
        mButtonHead.SubscribeOnClick(() => mPresenter.ActionAsync("Head", destroyCancellationToken).Forget())
            .DisposeWhenLifeEnded(this);
        mButtonBody.SubscribeOnClick(() => mPresenter.ActionAsync("Body", destroyCancellationToken).Forget())
            .DisposeWhenLifeEnded(this);
        mButtonLegs.SubscribeOnClick(() => mPresenter.ActionAsync("Legs", destroyCancellationToken).Forget())
            .DisposeWhenLifeEnded(this);

        // Пропустить ход
        mButtonSkip.SubscribeOnClick(() => mPresenter.SkipAsync(destroyCancellationToken).Forget())
            .DisposeWhenLifeEnded(this);

        // Лог (тоггл)
        if (mButtonLog != null && mLogPopup != null)
            mButtonLog.SubscribeOnClick(() => mLogPopup.SetActive(!mLogPopup.activeSelf))
                .DisposeWhenLifeEnded(this);

        // Комбо-индикатор
        if (mComboIndicator != null)
            mComboIndicator.Init(mPresenter, this);

        // По умолчанию скрыта
        gameObject.SetActive(false);
    }

    // ─── UI-обновления ────────────────────────────────────────────────────────

    private void UpdatePlayerHpText()
    {
        if (mLabelPlayerHp != null)
            mLabelPlayerHp.text = $"{mPresenter.MyCurrentHp.Value} / {mPresenter.MyMaxHp.Value}";
    }

    private void UpdateEnemyHpText()
    {
        if (mLabelEnemyHp != null)
            mLabelEnemyHp.text = $"{mPresenter.EnemyCurrentHp.Value} / {mPresenter.EnemyMaxHp.Value}";
    }

    private void UpdateTurnStatus(bool isMyTurn)
    {
        if (mLabelTurnStatus != null)
            mLabelTurnStatus.text = isMyTurn ? "Твой ход" : "Ход противника";
    }

    private void UpdateControlsInteractable(bool isMyTurn)
    {
        bool enabled = isMyTurn && !mPresenter.IsLoading.Value && !mPresenter.IsFinished.Value;
        mButtonHead.interactable = enabled;
        mButtonBody.interactable = enabled;
        mButtonLegs.interactable = enabled;
        mButtonSkip.interactable = enabled;
    }

    private void UpdateStanceHighlight(string stance)
    {
        SetStanceColor(mButtonNormal,     stance == "Normal");
        SetStanceColor(mButtonDefensive,  stance == "Defensive");
        SetStanceColor(mButtonAggressive, stance == "Aggressive");
    }

    private void SetStanceColor(Button btn, bool selected)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null)
            img.color = selected ? mStanceSelectedColor : mStanceUnselectedColor;
    }

    private void RebuildSlots(List<CombatLoadoutSlotDto> slots)
    {
        if (slots == null) return;
        SetupSlot(mSlot0, slots, 0);
        SetupSlot(mSlot1, slots, 1);
        SetupSlot(mSlot2, slots, 2);
        SetupSlot(mSlot3, slots, 3);
    }

    private void SetupSlot(ConsumableSlotView slot, List<CombatLoadoutSlotDto> slots, int index)
    {
        if (slot == null) return;
        var dto = index < slots.Count ? slots[index] : null;
        slot.Setup(dto, templateId => mPresenter.ConsumeAsync(templateId, destroyCancellationToken).Forget());
    }

    private void OnFinished(bool isFinished)
    {
        if (mResultPopup == null) return;

        if (isFinished)
            mResultPopup.Show(mPresenter.Outcome.Value);
        else
            // Раньше здесь была только ветка показа: попап скрывался исключительно из
            // собственной кнопки OK. Если бой сбрасывался мимо неё (новый бой поднят
            // авто-возобновлением или PvP-нападением, ForceExitCombat из диалога
            // воскрешения), попап оставался висеть поверх уже идущего нового боя.
            mResultPopup.Hide();
    }
}
