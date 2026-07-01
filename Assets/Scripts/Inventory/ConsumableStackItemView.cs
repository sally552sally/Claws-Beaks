using TMPro;
using UnityEngine;

/// <summary>
/// Строка расходки во вкладке «Эффекты» (Item_StackRow).
/// Показывает имя, количество и оставшийся TTL. Только просмотр (применение — через бой/лоадаут/еду).
/// </summary>
public sealed class ConsumableStackItemView : MonoBehaviour
{
    [SerializeField] private TMP_Text mLabelName;
    [SerializeField] private TMP_Text mLabelQuantity;
    [SerializeField] private TMP_Text mLabelTtl;

    /// <summary>Заполнить строку данными стака.</summary>
    public void Setup(ConsumableStackDto stack)
    {
        if (mLabelName != null)
            mLabelName.text = stack.Name ?? stack.Code ?? "?";

        if (mLabelQuantity != null)
            mLabelQuantity.text = $"×{stack.Quantity}";

        if (mLabelTtl != null)
            mLabelTtl.text = FormatTtl(stack.SecondsUntilExpire);
    }

    /// <summary>Форматирует TTL: бессрочный / N дн / N ч / N мин. Секунды считает сервер.</summary>
    private static string FormatTtl(long? secondsLeft)
    {
        if (secondsLeft is not { } sec)
            return "бессрочно";

        if (sec >= 86400) return $"{sec / 86400} дн";
        if (sec >= 3600)  return $"{sec / 3600} ч";
        if (sec >= 60)    return $"{sec / 60} мин";
        return "< 1 мин";
    }
}
