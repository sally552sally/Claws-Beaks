// Assets/Scripts/Inventory/EquipSlotView.cs
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Один фиксированный слот экипировки на кукле персонажа (Item_EquipSlot).
///
/// Слоту соответствует equip_slot: weapon_main / weapon_off / head / shoulders / body /
/// hands / legs / boots / belt, либо заглушки ring1 / ring2.
/// Амулета нет — вырезан 07.08, в куклу не возвращается.
///
/// Слот hands — ОДИН на перчатки и наручи. Это одна сущность: различаются только иконка
/// и подпись в карточке предмета, зависящие от стиля персонажа (у Ловкача перчатки,
/// у Тяжеловеса наручи). Ключ слота от стиля не зависит и не переименовывается.
///
/// Поведение:
///   — пустой слот: серая рамка, иконки нет, тап объясняет что это за слот;
///   — занятый: иконка предмета + рамка по редкости, тап → callback (открыть детали);
///   — заглушка (кольца): тап объясняет, что предметов пока нет — иначе тестер будет
///     искать кольца и считать слот сломанным;
///   — двуручное оружие: weapon_off отображает ТУ ЖЕ вещь, что и weapon_main (mirror),
///     тап по любому открывает деталь одного предмета (снять можно с любого).
///
/// Подписи слотов на кукле не рисуются (макет v5): пустая ячейка — просто пустая ячейка.
/// Поэтому mLabelSlot опционален и обычно null.
///
/// Слот знает свой SlotKey — назначается editor-скриптом (InventoryDollBuilder),
/// чтобы View_Inventory мог разложить надетые вещи по слотам.
/// </summary>
public sealed class EquipSlotView : MonoBehaviour
{
    [Header("Идентификатор слота (equip_slot или ring1/ring2)")]
    [SerializeField] private string mSlotKey;

    [Header("Это слот-заглушка (кольца — контента нет)")]
    [SerializeField] private bool mIsPlaceholder;

    [Header("Читаемое имя слота (для пояснения по тапу в пустую ячейку)")]
    [SerializeField] private string mDisplayName = "Слот";

    [Header("UI")]
    [SerializeField] private Button mButton;
    [SerializeField] private Image mIcon;           // иконка предмета; выключена, пока спрайта нет
    [SerializeField] private TMP_Text mLabelFallback;  // временная подпись вместо иконки
    [SerializeField] private Image mRarityFrame;
    [SerializeField] private GameObject mBrokenOverlay;

    [Header("UI — опционально (на кукле подписи не рисуются)")]
    [SerializeField] private TMP_Text mLabelSlot;

    /// <summary>Ключ слота — по нему View_Inventory раскладывает надетые вещи.</summary>
    public string SlotKey => mSlotKey;

    /// <summary>Слот-заглушка: предметов для него в игре пока нет.</summary>
    public bool IsPlaceholder => mIsPlaceholder;

    /// <summary>Читаемое имя слота.</summary>
    public string DisplayName => mDisplayName;

    private Action<InventoryItemDto> mOnClicked;
    private Action<string> mOnEmptyTapped;
    private InventoryItemDto mItem;

    private void Awake()
    {
        if (mButton != null) mButton.onClick.AddListener(OnClicked);
    }

    private void OnDestroy()
    {
        if (mButton != null) mButton.onClick.RemoveListener(OnClicked);
    }

    /// <summary>
    /// Назначить обработчики тапа. Вызывается из View_Inventory один раз.
    /// </summary>
    /// <param name="onClicked">Тап по надетой вещи — открыть детали.</param>
    /// <param name="onEmptyTapped">
    /// Тап по пустой ячейке или заглушке — показать пояснение тостом.
    /// Опционален: если не передан, тап по пустой ячейке молча игнорируется.
    /// </param>
    public void Bind(Action<InventoryItemDto> onClicked, Action<string> onEmptyTapped = null)
    {
        mOnClicked = onClicked;
        mOnEmptyTapped = onEmptyTapped;
    }

    /// <summary>
    /// Обновить слот. item == null → слот пуст.
    /// Для заглушек item игнорируется: всегда пусто.
    /// </summary>
    public void SetItem(InventoryItemDto item)
    {
        if (mLabelSlot != null) mLabelSlot.text = mDisplayName;

        if (mIsPlaceholder)
        {
            mItem = null;
            ApplyVisual(null, string.Empty, EmptyFrame, false);
            return;
        }

        mItem = item;

        if (item == null)
        {
            ApplyVisual(null, string.Empty, EmptyFrame, false);
            return;
        }

        // Спрайтов предметов пока нет — показываем усечённое имя, чтобы ячейка не была немой.
        // Убрать вместе с подбором иконок.
        //
        // Сломанность берём из IsBroken, а не сравниваем прочность с нулём: правило
        // «что считать сломанным» принадлежит серверу, и клиенту незачем его дублировать.
        ApplyVisual(null, ShortLabel(item.Name ?? item.Code ?? "?"),
                    RarityColor(item.Rarity), item.IsBroken);
    }

    /// <summary>
    /// Единая точка применения визуала — чтобы состояния слота не разъезжались
    /// между ветками (пусто / заглушка / надето / сломано).
    /// </summary>
    private void ApplyVisual(Sprite icon, string fallbackText, Color frameColor, bool broken)
    {
        if (mIcon != null)
        {
            mIcon.sprite = icon;
            mIcon.enabled = icon != null;
        }

        if (mLabelFallback != null)
        {
            mLabelFallback.text = fallbackText;
            mLabelFallback.enabled = icon == null && !string.IsNullOrEmpty(fallbackText);
        }

        if (mRarityFrame != null) mRarityFrame.color = frameColor;
        if (mBrokenOverlay != null) mBrokenOverlay.SetActive(broken);

        // Кнопка активна всегда: пустая ячейка и заглушка тоже должны отвечать на тап,
        // иначе игрок не понимает, что это за место и почему оно пустое.
        if (mButton != null) mButton.interactable = true;
    }

    private void OnClicked()
    {
        if (mIsPlaceholder)
        {
            mOnEmptyTapped?.Invoke("Кольца — далёкий беклог, предметов пока нет.");
            return;
        }

        if (mItem == null)
        {
            mOnEmptyTapped?.Invoke($"{mDisplayName}: пусто");
            return;
        }

        mOnClicked?.Invoke(mItem);
    }

    /// <summary>
    /// Первые символы имени — временная замена иконке.
    /// Ячейка квадратная и узкая, полное имя в неё не влезает.
    /// </summary>
    private static string ShortLabel(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var trimmed = name.Trim();
        return trimmed.Length <= 3 ? trimmed : trimmed.Substring(0, 3);
    }

    /// <summary>
    /// Рамка пустой ячейки. Должна совпадать с EmptyFrame в InventoryDollBuilder,
    /// иначе первый же SetItem затрёт стартовый цвет и пустые слоты «потемнеют».
    /// </summary>
    private static readonly Color EmptyFrame = new(0.28f, 0.31f, 0.38f);

    /// <summary>
    /// Цвет рамки по редкости.
    ///
    /// ВАЖНО: сервер присылает редкость цветом (grey / green / blue / purple / red),
    /// а не английскими названиями (common / uncommon / ...). Макеты используют вторые
    /// в именах CSS-переменных --rar-*, и это ловушка: несовпадение строк не даёт
    /// ошибки компиляции, просто ВСЕ вещи молча становятся серыми.
    /// Источник истины — InventoryItemDto.Rarity.
    /// </summary>
    private static Color RarityColor(string rarity) => rarity switch
    {
        "green" => new Color(0.43f, 0.78f, 0.35f),
        "blue" => new Color(0.30f, 0.58f, 0.93f),
        "purple" => new Color(0.68f, 0.40f, 0.90f),
        "red" => new Color(0.90f, 0.30f, 0.28f),
        "grey" => new Color(0.55f, 0.57f, 0.62f),
        _ => new Color(0.55f, 0.57f, 0.62f),   // неизвестное — как серое
    };
}
