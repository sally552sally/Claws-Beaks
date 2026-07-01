using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Один фиксированный слот экипировки на персонаже (Item_EquipSlot).
/// Каждому слоту соответствует equip_slot: weapon_main / weapon_off / body / legs / hands / head / belt
/// либо заглушки ring1 / ring2 / amulet.
///
/// Поведение:
///   — пустой слот: подпись слота, тап ничего не делает (нет предмета);
///   — занятый: имя + редкость, тап → callback с предметом (открыть детали);
///   — заглушка (кольца/амулет): задизейблен, подпись «скоро»;
///   — двуручное оружие: weapon_off отображает ТУ ЖЕ вещь, что и weapon_main (mirror),
///     тап по любому из них открывает деталь одного и того же предмета (снять можно с любого).
///
/// Слот знает свой SlotKey (строка equip_slot или ring1/ring2/amulet) — назначается в инспекторе
/// editor-скриптом, чтобы View_Inventory мог разложить надетые вещи по слотам.
/// </summary>
public sealed class EquipSlotView : MonoBehaviour
{
    [Header("Идентификатор слота (equip_slot или ring1/ring2/amulet)")]
    [SerializeField] private string mSlotKey;

    [Header("Это слот-заглушка (кольца/амулет — контента нет)")]
    [SerializeField] private bool mIsPlaceholder;

    [Header("UI")]
    [SerializeField] private Button   mButton;
    [SerializeField] private TMP_Text mLabelSlot;     // подпись слота («Голова», «Кольцо»)
    [SerializeField] private TMP_Text mLabelItem;     // имя надетой вещи или «—»
    [SerializeField] private Image    mRarityFrame;
    [SerializeField] private GameObject mBrokenOverlay;

    public string SlotKey => mSlotKey;
    public bool IsPlaceholder => mIsPlaceholder;

    private Action<InventoryItemDto> mOnClicked;
    private InventoryItemDto mItem;

    private void Awake()
    {
        if (mButton != null) mButton.onClick.AddListener(OnClicked);
    }

    private void OnDestroy()
    {
        if (mButton != null) mButton.onClick.RemoveListener(OnClicked);
    }

    /// <summary>Назначить обработчик тапа (открыть детали). Вызывается из View_Inventory один раз.</summary>
    public void Bind(Action<InventoryItemDto> onClicked) => mOnClicked = onClicked;

    /// <summary>
    /// Обновить слот. item == null → слот пуст.
    /// Для заглушек item игнорируется (всегда пусто + неактивно).
    /// </summary>
    public void SetItem(InventoryItemDto item)
    {
        if (mIsPlaceholder)
        {
            mItem = null;
            if (mButton != null) mButton.interactable = false;
            if (mLabelItem != null) mLabelItem.text = "скоро";
            if (mRarityFrame != null) mRarityFrame.color = new Color(0.3f, 0.3f, 0.3f);
            if (mBrokenOverlay != null) mBrokenOverlay.SetActive(false);
            return;
        }

        mItem = item;

        if (mButton != null) mButton.interactable = item != null;

        if (mLabelItem != null)
            mLabelItem.text = item != null ? (item.Name ?? item.Code ?? "?") : "—";

        if (mRarityFrame != null)
            mRarityFrame.color = item != null
                ? InventoryItemSlotView.RarityColor(item.Rarity)
                : new Color(0.2f, 0.2f, 0.2f);

        if (mBrokenOverlay != null)
            mBrokenOverlay.SetActive(item != null && item.IsBroken);
    }

    private void OnClicked()
    {
        if (mItem != null) mOnClicked?.Invoke(mItem);
    }
}
