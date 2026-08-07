// Assets/Scripts/Editor/InventoryDollBuilder.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Сборка куклы персонажа для вкладки «Снаряжение» (Panel_Doll).
///
/// Вынесено из InventorySetup отдельным файлом: кукла — самая объёмная и самая
/// переделываемая часть инвентаря, и держать её вместе с шапкой, вкладками, сундуком
/// и попапом деталей означает растить один Editor-скрипт до неуправляемого размера.
/// Билдер самодостаточен — не зависит от приватных хелперов InventorySetup.
///
/// Раскладка соответствует mockup_inventory_v5.html:
///   слева сверху вниз по телу — Голова, Плечи, Доспех, Наручи/Перчатки;
///   справа — Оружие, Оружие, Поножи, Сапоги;
///   кольца замыкают обе колонки симметрично (аксессуары, в боевые восемь не входят);
///   пояс — отдельной ячейкой под фигурой, по той же причине.
///
/// Всего 11 ячеек: 8 боевых + Пояс + 2 Кольца. Амулета нет — вырезан 07.08.
///
/// Слот hands — ОДИН на перчатки и наручи: это одна сущность, различаются только
/// иконка и подпись в карточке предмета, зависящие от стиля персонажа.
/// Ключ слота от стиля не зависит и не переименовывается (решение 07.08).
/// </summary>
public static class InventoryDollBuilder
{
    // ════════════════════════════════════════════════════════════════════════
    // Размеры
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Сторона квадратной ячейки экипировки.
    ///
    /// Макет v5 нарисован при ширине телефона 400px и ячейке 52px. Канвас проекта крупнее
    /// (диалоги 840×500, кнопки высотой 64) — сохранена пропорция, а не пиксели макета.
    /// Если сменишь CanvasScaler.referenceResolution — правится только эта константа.
    /// </summary>
    private const float Cell = 132f;

    /// <summary>Зазор между ячейками внутри колонки.</summary>
    private const float Spacing = 28f;

    /// <summary>Ширина центральной зоны с фигурой персонажа.</summary>
    private const float FigureWidth = 260f;

    /// <summary>Число ячеек в самой длинной колонке — от него считается высота куклы.</summary>
    private const int ColumnCells = 5;

    // ════════════════════════════════════════════════════════════════════════
    // Цвета
    // ════════════════════════════════════════════════════════════════════════

    // Пустая ячейка должна читаться на фоне панели. В макете её отделяет пунктирная
    // рамка — пунктира в uGUI нет, поэтому разделяем контрастом: рамка заметно светлее
    // подложки, подложка светлее фона панели.
    private static readonly Color SlotBg = new(0.13f, 0.14f, 0.17f);
    private static readonly Color EmptyFrame = new(0.28f, 0.31f, 0.38f);
    private static readonly Color FigureBg = new(0.10f, 0.11f, 0.14f);
    private static readonly Color FigureStub = new(0.35f, 0.37f, 0.42f);
    private static readonly Color FallbackTxt = new(0.62f, 0.65f, 0.72f);
    private static readonly Color BrokenTint = new(0.60f, 0.00f, 0.00f, 0.35f);

    // ════════════════════════════════════════════════════════════════════════
    // Точка входа
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Построить куклу как дочерний объект переданного родителя.
    /// Родитель предполагается вертикальным layout-контейнером вкладки «Снаряжение».
    /// </summary>
    /// <param name="parent">Panel_Equipment (или любой вертикальный контейнер).</param>
    /// <returns>
    /// Список созданных слотов в порядке создания — уходит в View_Inventory.mEquipSlots.
    /// Порядок на раскладку не влияет: View раскладывает вещи по SlotKey, важен только состав.
    /// </returns>
    public static List<EquipSlotView> Build(Transform parent)
    {
        var doll = new GameObject("Panel_Doll", typeof(RectTransform));
        doll.transform.SetParent(parent, false);

        var hlg = doll.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 16, 14);
        hlg.spacing = 24;
        hlg.childAlignment = TextAnchor.UpperCenter;
        // childControl* = true ОБЯЗАТЕЛЬНО: при false группа игнорирует LayoutElement
        // детей и берёт размер из их RectTransform.sizeDelta — колонки тогда наезжают
        // друг на друга. Force expand при этом выключен, чтобы ширины не раздувались.
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Высота фиксированная: пять ячеек в колонке + зазоры + вертикальные паддинги.
        // Фиксируем намеренно — родитель тоже LayoutGroup, а вложенные группы с
        // «плавающей» высотой в этом проекте уже давали съезжающий контент в сборке.
        float dollHeight = Cell * ColumnCells + Spacing * (ColumnCells - 1) + 30f;

        var dollLe = doll.AddComponent<LayoutElement>();
        dollLe.preferredHeight = dollHeight;
        dollLe.minHeight = dollHeight;
        // Ширина куклы — сумма трёх колонок, растягивать нечего.
        dollLe.preferredWidth = Cell * 2 + FigureWidth + 24 * 2 + 32;
        dollLe.flexibleWidth = 0;

        var colLeft = MakeColumn("Col_Left", doll.transform);
        var center = MakeCenter(doll.transform, dollHeight);
        var colRight = MakeColumn("Col_Right", doll.transform);

        var slots = new List<EquipSlotView>
        {
            // ── Левая колонка: сверху вниз по телу ──────────────────────────
            MakeSlot(colLeft.transform,  "head",      "Голова",            false),
            MakeSlot(colLeft.transform,  "shoulders", "Плечи",             false),
            MakeSlot(colLeft.transform,  "body",      "Доспех",            false),
            MakeSlot(colLeft.transform,  "hands",     "Наручи / Перчатки", false),
            MakeSlot(colLeft.transform,  "ring1",     "Кольцо",            true),

            // ── Правая колонка ──────────────────────────────────────────────
            // Оба слота оружия идут подряд намеренно: двуручка рисуется одним
            // предметом на двух ячейках, а для этого они должны быть смежными.
            MakeSlot(colRight.transform, "weapon_main", "Оружие",  false),
            MakeSlot(colRight.transform, "weapon_off",  "Оружие",  false),
            MakeSlot(colRight.transform, "legs",        "Поножи",  false),
            MakeSlot(colRight.transform, "boots",       "Сапоги",  false),
            MakeSlot(colRight.transform, "ring2",       "Кольцо",  true),

            // ── Под фигурой ─────────────────────────────────────────────────
            MakeSlot(center.transform,   "belt",        "Пояс",    false),
        };

        return slots;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Колонки
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Боковая колонка куклы — вертикальный стек ячеек фиксированной ширины.</summary>
    private static GameObject MakeColumn(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        go.GetComponent<RectTransform>().sizeDelta = new Vector2(Cell, 0);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = Spacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = Cell;
        le.minWidth = Cell;
        le.flexibleWidth = 0;
        return go;
    }

    /// <summary>
    /// Центральная зона: фигура персонажа сверху, ячейка пояса под ней.
    /// Фигура — заглушка до подбора арта (три статичные позы на стиль, см. PLAN_STAGES).
    /// </summary>
    private static GameObject MakeCenter(Transform parent, float dollHeight)
    {
        var go = new GameObject("Col_Center", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        go.GetComponent<RectTransform>().sizeDelta = new Vector2(FigureWidth, 0);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        // flexibleWidth НЕ ставим: растянутая центральная колонка съедает зазоры
        // и налезает на боковые ячейки. Ширина строго фиксированная.
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = FigureWidth;
        le.minWidth = FigureWidth;
        le.flexibleWidth = 0;

        // Высота фигуры = вся кукла минус ячейка пояса, зазор и паддинги.
        float figureHeight = dollHeight - Cell - 60f;

        var figure = new GameObject("Image_Figure", typeof(RectTransform));
        figure.transform.SetParent(go.transform, false);
        figure.GetComponent<RectTransform>().sizeDelta = new Vector2(FigureWidth, figureHeight);

        var figImg = figure.AddComponent<Image>();
        figImg.color = FigureBg;

        var figLe = figure.AddComponent<LayoutElement>();
        figLe.preferredWidth = FigureWidth;
        figLe.preferredHeight = figureHeight;

        var stub = MakeLabel("Label_FigureStub", figure.transform, "фигура", 16, FigureStub);
        Stretch(stub.gameObject);
        stub.alignment = TextAlignmentOptions.Center;

        return go;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Ячейка
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Одна ячейка куклы: квадратная, иконочная.
    /// Подпись слота не рисуется (макет v5) — пустая ячейка отличается от занятой
    /// только рамкой, а что это за слот, объясняет тап.
    /// </summary>
    /// <param name="slotKey">equip_slot либо ring1 / ring2 — по нему View_Inventory кладёт вещь.</param>
    /// <param name="title">Читаемое имя слота — для пояснения по тапу в пустую ячейку.</param>
    /// <param name="placeholder">Заглушка (кольца) — предметов в игре пока нет.</param>
    private static EquipSlotView MakeSlot(Transform parent, string slotKey, string title, bool placeholder)
    {
        var go = new GameObject($"Slot_{slotKey}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(Cell, Cell);

        // Квадрат держим и preferred, и min, и flexible=0. Работает только потому,
        // что родительская колонка имеет childControlWidth/Height = true — иначе
        // LayoutElement игнорируется и справа вылезает полоска голой рамки.
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = Cell;
        le.preferredHeight = Cell;
        le.minWidth = Cell;
        le.minHeight = Cell;
        le.flexibleWidth = 0;
        le.flexibleHeight = 0;

        // Внешний Image — рамка редкости. Её цвет меняет EquipSlotView в рантайме.
        var frame = go.AddComponent<Image>();
        frame.color = EmptyFrame;

        // Внутренняя подложка — на 3px меньше со всех сторон, отсюда видимая рамка.
        var inner = new GameObject("Inner", typeof(RectTransform));
        inner.transform.SetParent(go.transform, false);
        Stretch(inner);
        var innerRt = inner.GetComponent<RectTransform>();
        innerRt.offsetMin = new Vector2(3, 3);
        innerRt.offsetMax = new Vector2(-3, -3);
        inner.AddComponent<Image>().color = SlotBg;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = frame;

        // Иконка предмета. Спрайта пока нет — компонент выключен, включится из SetItem.
        var iconGo = new GameObject("Image_Icon", typeof(RectTransform));
        iconGo.transform.SetParent(inner.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta = new Vector2(Cell * 0.62f, Cell * 0.62f);
        var icon = iconGo.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = false;

        // Временный текстовый фолбэк, пока иконок нет.
        // Эмодзи намеренно не используем: на WebGL шрифтовой разнобой между браузерами
        // заметнее, чем на мобилке (решение DECISIONS_LOG — иконки вместо эмодзи).
        var fallback = MakeLabel("Label_Fallback", inner.transform, string.Empty, 22, FallbackTxt);
        Stretch(fallback.gameObject);
        fallback.alignment = TextAlignmentOptions.Center;
        fallback.raycastTarget = false;

        var broken = new GameObject("Broken_Overlay", typeof(RectTransform));
        broken.transform.SetParent(go.transform, false);
        Stretch(broken);
        var brokenImg = broken.AddComponent<Image>();
        brokenImg.color = BrokenTint;
        brokenImg.raycastTarget = false;
        broken.SetActive(false);

        var view = go.AddComponent<EquipSlotView>();
        var so = new SerializedObject(view);
        so.FindProperty("mSlotKey").stringValue = slotKey;
        so.FindProperty("mIsPlaceholder").boolValue = placeholder;
        so.FindProperty("mDisplayName").stringValue = title;
        so.FindProperty("mButton").objectReferenceValue = btn;
        so.FindProperty("mIcon").objectReferenceValue = icon;
        so.FindProperty("mLabelFallback").objectReferenceValue = fallback;
        so.FindProperty("mRarityFrame").objectReferenceValue = frame;
        so.FindProperty("mBrokenOverlay").objectReferenceValue = broken;
        // mLabelSlot намеренно не назначается — подписей на кукле нет, поле опционально.
        so.ApplyModifiedProperties();

        return view;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Локальные хелперы (билдер намеренно не зависит от приватных методов InventorySetup)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Растянуть RectTransform на весь родительский прямоугольник.</summary>
    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Текстовая метка TMP с заданным размером и цветом.</summary>
    private static TMP_Text MakeLabel(string name, Transform parent, string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }
}
#endif