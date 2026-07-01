using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Экран инвентаря — Panel_Inventory (Game-сцена, поверх Location/Hunting, под Combat по Sort Order).
///
/// Вкладки (расширяемый список из InventoryPresenter.Tabs):
///   Снаряжение — слоты экипировки (6 + пояс + 3 заглушки) и рюкзак;
///   Сундук     — личный сундук (активна только если доступен здесь);
///   Эффекты    — вся расходка с TTL (только просмотр);
///   Ресурсы / Квесты — заглушки.
///
/// View только отображает и зовёт команды Presenter. Логика — в InventoryPresenter.
/// Иерархию собирает Editor/InventorySetup.cs.
///
/// GameObject: Panel_Inventory
/// </summary>
public sealed class View_Inventory : DisposableBehaviour
{
    [Header("Корень / шапка")]
    [SerializeField] private Button   mButtonClose;
    [SerializeField] private TMP_Text mLabelBackpackCount;   // «Рюкзак: 12/15»
    [SerializeField] private TMP_Text mLabelError;
    [SerializeField] private TMP_Text mLabelInfo;
    [SerializeField] private GameObject mSpinner;

    [Header("Вкладки")]
    [SerializeField] private Transform mTabsContainer;
    [SerializeField] private Button    mTabButtonPrefab;     // Button с TMP_Text-потомком

    [Header("Контент-панели вкладок")]
    [SerializeField] private GameObject mPanelEquipment;
    [SerializeField] private GameObject mPanelChest;
    [SerializeField] private GameObject mPanelEffects;
    [SerializeField] private GameObject mPanelPlaceholder;
    [SerializeField] private TMP_Text   mLabelPlaceholder;

    [Header("Снаряжение")]
    [SerializeField] private EquipSlotView[] mEquipSlots;    // фиксированные слоты (по SlotKey)
    [SerializeField] private Transform       mBackpackContainer;
    [SerializeField] private InventoryItemSlotView mItemSlotPrefab;

    [Header("Сундук")]
    [SerializeField] private Transform mChestContainer;
    [SerializeField] private GameObject mChestUnavailableHint;  // «Сундук недоступен здесь»

    [Header("Эффекты (расходка)")]
    [SerializeField] private Transform mStacksContainer;
    [SerializeField] private ConsumableStackItemView mStackRowPrefab;
    [SerializeField] private GameObject mStacksEmptyHint;

    // ─── Инъекции ─────────────────────────────────────────────────────────────

    private InventoryPresenter mPresenter;

    private readonly List<Button> mTabButtons = new();
    private readonly List<InventoryTab> mTabOrder = new();
    private readonly List<InventoryItemSlotView> mBackpackItems = new();
    private readonly List<InventoryItemSlotView> mChestItems = new();
    private readonly List<ConsumableStackItemView> mStackRows = new();

    [Inject]
    public void Construct(InventoryPresenter presenter)
    {
        mPresenter = presenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        BuildTabs();
        BindEquipSlots();
        BindReactive();
        BindButtons();

        gameObject.SetActive(mPresenter.IsOpen.Value);
    }

    // ─── Построение вкладок ───────────────────────────────────────────────────

    private void BuildTabs()
    {
        foreach (var tab in mPresenter.Tabs)
        {
            var btn = Instantiate(mTabButtonPrefab, mTabsContainer);
            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = InventoryTabInfo.Title(tab);

            var captured = tab;
            btn.SubscribeOnClick(() => mPresenter.SelectTab(captured)).DisposeWhenLifeEnded(this);

            mTabButtons.Add(btn);
            mTabOrder.Add(captured);
        }
    }

    private void BindEquipSlots()
    {
        if (mEquipSlots == null) return;
        foreach (var slot in mEquipSlots)
            if (slot != null)
                slot.Bind(mPresenter.OpenItemDetail);
    }

    // ─── Реактивные привязки ──────────────────────────────────────────────────

    private void BindReactive()
    {
        mPresenter.IsOpen
            .SubscribeOnValueChanged(gameObject.SetActive)
            .DisposeWhenLifeEnded(this);

        mPresenter.ActiveTab
            .SubscribeOnValueChanged(OnTabChanged)
            .DisposeWhenLifeEnded(this);

        mPresenter.Equipped
            .SubscribeOnValueChanged(RebuildEquipped)
            .DisposeWhenLifeEnded(this);

        mPresenter.Backpack
            .SubscribeOnValueChanged(RebuildBackpack)
            .DisposeWhenLifeEnded(this);

        mPresenter.Chest
            .SubscribeOnValueChanged(RebuildChest)
            .DisposeWhenLifeEnded(this);

        mPresenter.Stacks
            .SubscribeOnValueChanged(RebuildStacks)
            .DisposeWhenLifeEnded(this);

        mPresenter.ChestAvailableHere
            .SubscribeOnValueChanged(_ => RefreshChestAvailabilityHint())
            .DisposeWhenLifeEnded(this);

        if (mLabelBackpackCount != null)
        {
            var text = ReactiveExtensions.Combine(
                mPresenter.BackpackUsed, mPresenter.BackpackCapacity,
                (used, cap) => $"Рюкзак: {used}/{cap}",
                this);
            mLabelBackpackCount.SetTextSource(text).DisposeWhenLifeEnded(this);
        }

        if (mLabelError != null)
            mPresenter.ErrorMessage
                .SubscribeOnValueChanged(msg =>
                {
                    mLabelError.text = msg;
                    mLabelError.gameObject.SetActive(!string.IsNullOrEmpty(msg));
                })
                .DisposeWhenLifeEnded(this);

        if (mLabelInfo != null)
            mPresenter.InfoMessage
                .SubscribeOnValueChanged(msg =>
                {
                    mLabelInfo.text = msg;
                    mLabelInfo.gameObject.SetActive(!string.IsNullOrEmpty(msg));
                })
                .DisposeWhenLifeEnded(this);

        if (mSpinner != null)
            mPresenter.IsLoading
                .SubscribeOnValueChanged(mSpinner.SetActive)
                .DisposeWhenLifeEnded(this);
    }

    private void BindButtons()
    {
        if (mButtonClose != null)
            mButtonClose.SubscribeOnClick(() => mPresenter.Close()).DisposeWhenLifeEnded(this);
    }

    // ─── Переключение вкладок ─────────────────────────────────────────────────

    private void OnTabChanged(InventoryTab tab)
    {
        // Подсветка активной кнопки вкладки.
        for (int i = 0; i < mTabButtons.Count; i++)
        {
            var img = mTabButtons[i].GetComponent<Image>();
            if (img != null)
                img.color = mTabOrder[i] == tab
                    ? new Color(0.30f, 0.30f, 0.38f)
                    : new Color(0.18f, 0.18f, 0.22f);
        }

        bool placeholder = InventoryTabInfo.IsPlaceholder(tab);

        SetActive(mPanelEquipment, tab == InventoryTab.Equipment);
        SetActive(mPanelChest,     tab == InventoryTab.Chest);
        SetActive(mPanelEffects,   tab == InventoryTab.Effects);
        SetActive(mPanelPlaceholder, placeholder);

        if (placeholder && mLabelPlaceholder != null)
            mLabelPlaceholder.text = InventoryTabInfo.PlaceholderText(tab);

        if (tab == InventoryTab.Chest)
            RefreshChestAvailabilityHint();
    }

    // ─── Снаряжение ───────────────────────────────────────────────────────────

    private void RebuildEquipped(List<InventoryItemDto> equipped)
    {
        if (mEquipSlots == null) return;

        // Индексируем надетое по equip_slot.
        var bySlot = new Dictionary<string, InventoryItemDto>();
        InventoryItemDto twoHandedMain = null;
        foreach (var item in equipped)
        {
            if (string.IsNullOrEmpty(item.EquipSlot)) continue;
            bySlot[item.EquipSlot] = item;
            if (item.EquipSlot == "weapon_main" && item.IsTwoHanded)
                twoHandedMain = item;
        }

        foreach (var slot in mEquipSlots)
        {
            if (slot == null) continue;
            if (slot.IsPlaceholder) { slot.SetItem(null); continue; }

            // Двуручка: weapon_off зеркалит ту же вещь, что и weapon_main (§6.2).
            if (slot.SlotKey == "weapon_off" && twoHandedMain != null)
            {
                slot.SetItem(twoHandedMain);
                continue;
            }

            slot.SetItem(bySlot.GetValueOrDefault(slot.SlotKey));
        }
    }

    private void RebuildBackpack(List<InventoryItemDto> backpack)
    {
        RebuildItemList(backpack, mBackpackContainer, mBackpackItems);
    }

    // ─── Сундук ───────────────────────────────────────────────────────────────

    private void RebuildChest(List<InventoryItemDto> chest)
    {
        RebuildItemList(chest, mChestContainer, mChestItems);
    }

    private void RefreshChestAvailabilityHint()
    {
        bool available = mPresenter.ChestAvailableHere.Value;
        if (mChestUnavailableHint != null)
            mChestUnavailableHint.SetActive(!available);
        if (mChestContainer != null)
            mChestContainer.gameObject.SetActive(available);
    }

    // ─── Эффекты ──────────────────────────────────────────────────────────────

    private void RebuildStacks(List<ConsumableStackDto> stacks)
    {
        foreach (var row in mStackRows)
            Destroy(row.gameObject);
        mStackRows.Clear();

        if (mStacksContainer == null) return;

        foreach (var stack in stacks)
        {
            var row = Instantiate(mStackRowPrefab, mStacksContainer);
            row.Setup(stack);
            mStackRows.Add(row);
        }

        if (mStacksEmptyHint != null)
            mStacksEmptyHint.SetActive(stacks == null || stacks.Count == 0);
    }

    // ─── Общий хелпер списков предметов ───────────────────────────────────────

    private void RebuildItemList(
        List<InventoryItemDto> items, Transform container, List<InventoryItemSlotView> pool)
    {
        foreach (var view in pool)
            Destroy(view.gameObject);
        pool.Clear();

        if (container == null || items == null) return;

        foreach (var item in items)
        {
            var view = Instantiate(mItemSlotPrefab, container);
            view.Setup(item, mPresenter.OpenItemDetail);
            pool.Add(view);
        }
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}
