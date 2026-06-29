using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Один слот расходки на экране боя.
/// Показывает тип (код расходки) и количество.
/// При тапе вызывает callback с templateId — Presenter применяет расходку.
///
/// Пустой слот: кнопка неактивна, текст «—».
/// </summary>
public sealed class ConsumableSlotView : MonoBehaviour
{
    [SerializeField] private Button   mButton;
    [SerializeField] private TMP_Text mLabelCode;
    [SerializeField] private TMP_Text mLabelCount;
    [SerializeField] private GameObject mEmptyOverlay;

    private Action<long> mOnConsumed;
    private long mTemplateId;

    private void Awake()
    {
        mButton.onClick.AddListener(OnClicked);
    }

    private void OnDestroy()
    {
        mButton.onClick.RemoveListener(OnClicked);
    }

    /// <summary>
    /// Инициализация слота данными с сервера.
    /// dto == null → пустой слот.
    /// </summary>
    public void Setup(CombatLoadoutSlotDto dto, Action<long> onConsumed)
    {
        mOnConsumed = onConsumed;

        bool hasItem = dto?.ConsumableTemplateId.HasValue == true && dto.QuantityInInventory > 0;

        mTemplateId = hasItem ? dto.ConsumableTemplateId.Value : 0;
        mButton.interactable = hasItem;

        if (mEmptyOverlay != null)
            mEmptyOverlay.SetActive(!hasItem);

        if (mLabelCode != null)
            mLabelCode.text = hasItem ? ShortCode(dto.ConsumableCode) : "—";

        if (mLabelCount != null)
            mLabelCount.text = hasItem ? dto.QuantityInInventory.ToString() : string.Empty;
    }

    private void OnClicked()
    {
        if (mTemplateId > 0)
            mOnConsumed?.Invoke(mTemplateId);
    }

    /// <summary>Короткий код для отображения (первые 3 символа + тип).</summary>
    private static string ShortCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return "?";

        // heal_small → ХЛ, attack_buff → АТ, poison → ЯД, cleanse → ОЧ
        if (code.StartsWith("heal"))    return "ХЛ";
        if (code.StartsWith("attack"))  return "АТ";
        if (code.StartsWith("poison"))  return "ЯД";
        if (code.StartsWith("cleanse")) return "ОЧ";
        if (code.StartsWith("perm"))    return "ПМ";
        return code.Length >= 2 ? code.Substring(0, 2).ToUpper() : code.ToUpper();
    }
}
