using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Попап деталей предмета (Popup_ItemDetail).
/// Показывает имя, редкость, требование уровня, прочность, статы и набор действий,
/// зависящих от того, ГДЕ лежит предмет (container):
///   — equipped → «Снять»;
///   — backpack → «Надеть» (если это экипировка) + «В сундук» (если сундук здесь) + «Выбросить»;
///   — chest    → «Достать».
/// «Починить» — если прочность не полная и вещь не одноразово изношена (durability_max > 1).
///
/// Выброс идёт через Popup_Confirm (необратимо). Логика — в InventoryPresenter.
///
/// GameObject: Popup_ItemDetail (по умолчанию выключен; показывается по SelectedItem).
/// </summary>
public sealed class Popup_ItemDetail : DisposableBehaviour
{
    [Header("Текст")]
    [SerializeField] private TMP_Text mLabelName;
    [SerializeField] private TMP_Text mLabelMeta;     // редкость • уровень • стиль
    [SerializeField] private TMP_Text mLabelDurability;
    [SerializeField] private TMP_Text mLabelStats;
    [SerializeField] private Image mRarityFrame;

    [Header("Кнопки действий")]
    [SerializeField] private Button mButtonEquip;
    [SerializeField] private Button mButtonUnequip;
    [SerializeField] private Button mButtonRepair;
    [SerializeField] private Button mButtonDeposit;
    [SerializeField] private Button mButtonWithdraw;
    [SerializeField] private Button mButtonDiscard;
    [SerializeField] private Button mButtonClose;

    [Header("Подтверждение выброса")]
    [SerializeField] private Popup_Confirm mConfirmPopup;

    private InventoryPresenter mPresenter;
    private InventoryItemDto mCurrent;

    [Inject]
    public void Construct(InventoryPresenter presenter)
    {
        mPresenter = presenter;
    }

    protected override void SafeAwake()
    {
        // Подписка на выбранный предмет — null прячет попап.
        mPresenter.SelectedItem
            .SubscribeOnValueChanged(OnSelectedItemChanged)
            .DisposeWhenLifeEnded(this);

        if (mButtonEquip != null)
            mButtonEquip.SubscribeOnClick(() => { if (mCurrent != null) mPresenter.Equip(mCurrent.InstanceId); })
                .DisposeWhenLifeEnded(this);
        if (mButtonUnequip != null)
            mButtonUnequip.SubscribeOnClick(() => { if (mCurrent != null) mPresenter.Unequip(mCurrent.InstanceId); })
                .DisposeWhenLifeEnded(this);
        if (mButtonRepair != null)
            mButtonRepair.SubscribeOnClick(() => { if (mCurrent != null) mPresenter.Repair(mCurrent.InstanceId); })
                .DisposeWhenLifeEnded(this);
        if (mButtonDeposit != null)
            mButtonDeposit.SubscribeOnClick(() => { if (mCurrent != null) mPresenter.Deposit(mCurrent.InstanceId); })
                .DisposeWhenLifeEnded(this);
        if (mButtonWithdraw != null)
            mButtonWithdraw.SubscribeOnClick(() => { if (mCurrent != null) mPresenter.Withdraw(mCurrent.InstanceId); })
                .DisposeWhenLifeEnded(this);
        if (mButtonDiscard != null)
            mButtonDiscard.SubscribeOnClick(OnDiscardClicked).DisposeWhenLifeEnded(this);
        if (mButtonClose != null)
            mButtonClose.SubscribeOnClick(() => mPresenter.CloseItemDetail()).DisposeWhenLifeEnded(this);

        gameObject.SetActive(false);
    }

    private void OnSelectedItemChanged(InventoryItemDto item)
    {
        mCurrent = item;

        if (item == null)
        {
            gameObject.SetActive(false);
            return;
        }

        Render(item);
        gameObject.SetActive(true);
    }

    private void Render(InventoryItemDto item)
    {
        bool isEquipped = item.Container == "equipped";
        bool inBackpack = item.Container == "backpack";
        bool inChest = item.Container == "chest";
        bool isGear = item.SlotCategory != null;  // у экипировки есть slot_category

        if (mLabelName != null) mLabelName.text = item.Name ?? item.Code ?? "?";

        if (mRarityFrame != null)
            mRarityFrame.color = InventoryItemSlotView.RarityColor(item.Rarity);

        if (mLabelMeta != null)
        {
            var meta = new StringBuilder();
            meta.Append(RarityName(item.Rarity));
            meta.Append("  •  ур. ").Append(item.LevelRequirement);
            if (!string.IsNullOrEmpty(item.GearStyle)) meta.Append("  •  ").Append(item.GearStyle);
            if (item.IsTwoHanded) meta.Append("  •  двуручное");
            mLabelMeta.text = meta.ToString();
        }

        if (mLabelDurability != null)
            mLabelDurability.text = $"Прочность: {item.DurabilityCurrent} / {item.DurabilityMax}"
                                    + (item.IsBroken ? "  (сломано — нет статов)" : string.Empty);

        if (mLabelStats != null)
            mLabelStats.text = BuildStats(item);

        // Видимость кнопок по контейнеру.
        SetActive(mButtonEquip, inBackpack && isGear);
        SetActive(mButtonUnequip, isEquipped);
        SetActive(mButtonDeposit, inBackpack && mPresenter.ChestAvailableHere.Value);
        SetActive(mButtonWithdraw, inChest);
        SetActive(mButtonDiscard, inBackpack);   // выброс только из рюкзака

        // Починка: только если прочность не полная и вещь ещё чинибельна (max > 1).
        // Ремонт временно убран в беклог: сейчас нет внятного отображения прочности в потоке
        // (только в этом попапе) и нет способа её проверить в деле — рано показывать действие,
        // которое нечем осмысленно протестировать. Серверная логика (POST /api/gear/repair)
        // никуда не делась, просто не вызывается с клиента, пока экран не доработан.
        SetActive(mButtonRepair, false);
    }

    private void OnDiscardClicked()
    {
        if (mCurrent == null) return;
        long id = mCurrent.InstanceId;
        string name = mCurrent.Name ?? mCurrent.Code ?? "предмет";

        if (mConfirmPopup != null)
            mConfirmPopup.Show($"Выбросить «{name}»? Действие необратимо.",
                () => mPresenter.Discard(id));
        else
            mPresenter.Discard(id);   // на случай, если попап не назначен — без подтверждения
    }

    private static string BuildStats(InventoryItemDto i)
    {
        var sb = new StringBuilder();
        AppendStat(sb, "Сила", i.RolledStrength);
        AppendStat(sb, "Ловкость", i.RolledAgility);
        AppendStat(sb, "Интуиция", i.RolledIntuition);
        AppendStat(sb, "Защита", i.RolledDefense);
        AppendStat(sb, "Живучесть", i.RolledVitality);
        AppendStat(sb, "Урон", i.RolledDamage);
        AppendStat(sb, "HP", i.RolledHp);
        return sb.Length > 0 ? sb.ToString() : "Без бонусов к статам";
    }

    private static void AppendStat(StringBuilder sb, string name, int value)
    {
        if (value == 0) return;
        if (sb.Length > 0) sb.Append('\n');
        sb.Append(name).Append(": +").Append(value);
    }

    private static void SetActive(Button btn, bool active)
    {
        if (btn != null) btn.gameObject.SetActive(active);
    }

    private static string RarityName(string rarity) => rarity switch
    {
        "grey" => "Серый",
        "green" => "Зелёный",
        "blue" => "Синий",
        "purple" => "Фиолетовый",
        "red" => "Красный",
        _ => rarity ?? "?"
    };
}
