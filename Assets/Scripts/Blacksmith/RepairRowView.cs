using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Одна вещь в списке ремонта у кузнеца (Item_RepairRow).
/// Показывает название, прочность «было → станет» и цену; тап по кнопке — починить именно её.
///
/// Почему прочность показывается двумя числами: ремонт снижает МАКСИМАЛЬНУЮ прочность на 1, и
/// без второй цифры игрок не поймёт, почему щит с 10/10 через пять ремонтов стал 5/5.
/// </summary>
public sealed class RepairRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text mLabelName;
    [SerializeField] private TMP_Text mLabelDurability;
    [SerializeField] private TMP_Text mLabelCost;
    [SerializeField] private Button   mButtonRepair;

    private Action<long> mOnRepairClicked;
    private long mInstanceId;

    private void Awake()
    {
        if (mButtonRepair != null) mButtonRepair.onClick.AddListener(OnRepairClicked);
    }

    private void OnDestroy()
    {
        if (mButtonRepair != null) mButtonRepair.onClick.RemoveListener(OnRepairClicked);
    }

    /// <summary>Заполнить строку данными расчёта.</summary>
    /// <param name="item">Позиция расчёта (цена уже посчитана сервером).</param>
    /// <param name="canAfford">Хватает ли золота именно на эту вещь — кнопка гаснет, если нет.</param>
    /// <param name="onRepairClicked">Колбэк «починить эту вещь».</param>
    public void Setup(RepairQuoteItemDto item, bool canAfford, Action<long> onRepairClicked)
    {
        mInstanceId = item.InstanceId;
        mOnRepairClicked = onRepairClicked;

        if (mLabelName != null)
            mLabelName.text = string.IsNullOrEmpty(item.Name) ? "предмет" : item.Name;

        if (mLabelDurability != null)
            mLabelDurability.text =
                $"{item.DurabilityCurrent}/{item.DurabilityMax} → {item.DurabilityMaxAfter}/{item.DurabilityMaxAfter}";

        if (mLabelCost != null)
            mLabelCost.text = $"{item.Cost} з";

        if (mButtonRepair != null)
            mButtonRepair.interactable = canAfford;
    }

    private void OnRepairClicked() => mOnRepairClicked?.Invoke(mInstanceId);
}
