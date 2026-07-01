using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Один предмет в списке инвентаря/сундука (Item_InvSlot).
/// Показывает короткое имя, редкость (цвет рамки) и состояние «сломано».
/// Тап → callback с InstanceId (Presenter открывает Popup_ItemDetail).
///
/// Используется и для рюкзака, и для сундука, и для отображения надетого.
/// </summary>
public sealed class InventoryItemSlotView : MonoBehaviour
{
    [SerializeField] private Button   mButton;
    [SerializeField] private TMP_Text mLabelName;
    [SerializeField] private TMP_Text mLabelSub;
    [SerializeField] private Image    mRarityFrame;
    [SerializeField] private GameObject mBrokenOverlay;

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

    /// <summary>Заполнить слот данными предмета.</summary>
    public void Setup(InventoryItemDto item, Action<InventoryItemDto> onClicked)
    {
        mItem = item;
        mOnClicked = onClicked;

        if (mLabelName != null)
            mLabelName.text = item.Name ?? item.Code ?? "?";

        if (mLabelSub != null)
            mLabelSub.text = $"ур.{item.LevelRequirement} • {item.DurabilityCurrent}/{item.DurabilityMax}";

        if (mRarityFrame != null)
            mRarityFrame.color = RarityColor(item.Rarity);

        if (mBrokenOverlay != null)
            mBrokenOverlay.SetActive(item.IsBroken);
    }

    private void OnClicked() => mOnClicked?.Invoke(mItem);

    /// <summary>Цвет рамки по редкости. Палитра ориентировочная (балансируем с артом, UI-03).</summary>
    public static Color RarityColor(string rarity) => rarity switch
    {
        "grey"   => new Color(0.55f, 0.55f, 0.55f),
        "green"  => new Color(0.35f, 0.75f, 0.35f),
        "blue"   => new Color(0.30f, 0.55f, 0.95f),
        "purple" => new Color(0.65f, 0.40f, 0.90f),
        "red"    => new Color(0.90f, 0.30f, 0.30f),
        _        => Color.white
    };
}
