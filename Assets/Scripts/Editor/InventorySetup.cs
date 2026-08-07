// Удалить после использования (Assets/Editor/InventorySetup.cs)
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor-скрипт автосборки UI-иерархии Фазы 4 (Инвентарь).
/// Запуск: MMORPG → Setup → Inventory Panel
///
/// Создаёт:
///   Panel_Inventory (View_Inventory) с вкладками, снаряжением, рюкзаком, сундуком, эффектами;
///   Popup_ItemDetail (детали + действия);
///   (Popup_Confirm убран — подтверждения теперь через сервис уведомлений);
///   префабы Item_InvSlot / Item_StackRow / Button_Tab (в скрытом контейнере шаблонов).
/// Назначает ВСЕ SerializeField через SerializedObject и проставляет ссылки в GameInstaller.
///
/// После запуска — проверь Console и удали скрипт.
/// </summary>
public static class InventorySetup
{
    private static readonly Color PanelBg = new(0.08f, 0.08f, 0.10f, 0.98f);
    private static readonly Color SubPanelBg = new(0.12f, 0.12f, 0.15f, 1f);
    private static readonly Color ButtonBg = new(0.22f, 0.22f, 0.26f);
    private static readonly Color SlotBg = new(0.16f, 0.16f, 0.20f);

    [MenuItem("MMORPG/Setup/Inventory Panel")]
    public static void CreateInventoryPanel()
    {
        var allCanvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas rootCanvas = null;
        foreach (var c in allCanvases)
            if (c.renderMode == RenderMode.ScreenSpaceOverlay) { rootCanvas = c; break; }

        if (rootCanvas == null)
        {
            Debug.LogError("[InventorySetup] Canvas (Screen Space Overlay) не найден. Открой Game-сцену.");
            return;
        }

        Transform safeArea = rootCanvas.transform.Find("SafeArea") ?? rootCanvas.transform;

        // Удалить старое
        DestroyChildIfExists(safeArea, "Panel_Inventory");
        DestroyChildIfExists(safeArea, "Popup_ItemDetail");
        DestroyChildIfExists(safeArea, "Popup_Confirm");
        DestroyChildIfExists(safeArea, "_InvTemplates");

        // ── Скрытый контейнер шаблонов (префабы списков) ─────────────────────
        var templates = new GameObject("_InvTemplates", typeof(RectTransform));
        templates.transform.SetParent(safeArea, false);
        templates.SetActive(false);

        var itemSlotPrefab = MakeItemSlotPrefab(templates.transform);
        var stackRowPrefab = MakeStackRowPrefab(templates.transform);
        var tabButtonPrefab = MakeTabButtonPrefab(templates.transform);

        // ── Panel_Inventory ──────────────────────────────────────────────────
        var panel = MakeStretchPanel("Panel_Inventory", safeArea, PanelBg);
        // Стартуем активным: Awake не вызывается на выключенном GameObject, и тогда
        // View_Inventory.SafeAwake не отработает — не построятся вкладки и, главное,
        // не встанет подписка на IsOpen, из-за чего панель уже не сможет себя показать.
        // SafeAwake сам скроет себя в конце по IsOpen.Value = false. Так же в ChatSetup.
        var view = panel.AddComponent<View_Inventory>();
        // Жёсткий клип по границам панели: что бы ни случилось с раскладкой внутри,
        // за пределы Panel_Inventory ничего не нарисуется (фон локации не просвечивает).
        panel.AddComponent<RectMask2D>();

        // ВАЖНО: раньше здесь был VerticalLayoutGroup на корне + вложенный Panel_Content
        // с LayoutElement.flexibleHeight внутри него — цепочка «layout-group внутри
        // layout-group» у которой размер зависит от вложенных ScrollRect/ContentSizeFitter.
        // Это и «съезжало» контент вниз на реальном устройстве/сборке. Заменено на явные
        // якоря (top-anchored шапка/вкладки, остальное — область-заливка под ними) —
        // предсказуемо и не зависит от Unity-квирков вложенных LayoutGroup.
        const float padSide = 20f;
        const float padTop = 20f;
        const float padBottom = 20f;
        const float gap = 10f;
        const float headerH = 64f;
        const float tabsH = 64f;
        const float msgH = 30f;

        float headerY = padTop;
        float tabsY = headerY + headerH + gap;
        float msgY = tabsY + tabsH + gap;
        float contentTopInset = msgY + msgH + gap;

        // Шапка: заголовок + рюкзак-счётчик + закрыть (top-anchored, фикс. высота)
        var header = MakeTopBar("Panel_Header", panel.transform, headerY, headerH, padSide);
        var headerHlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerHlg.spacing = 10;
        headerHlg.childAlignment = TextAnchor.MiddleCenter;
        headerHlg.childControlWidth = false;
        headerHlg.childControlHeight = false;
        headerHlg.childForceExpandWidth = true;
        headerHlg.childForceExpandHeight = true;
        MakeLabel("Label_Title", header.transform, "Инвентарь", 26);
        var lblBackpack = MakeLabel("Label_Backpack", header.transform, "Рюкзак: 0/0", 20);
        var btnClose = MakeButton("Button_Close", header.transform, "✕", fixedWidth: 80, height: 56);

        // Вкладки (top-anchored, фикс. высота, сразу под шапкой)
        var tabsRowRt = MakeTopBar("Panel_Tabs", panel.transform, tabsY, tabsH, padSide);
        var tabsRow = tabsRowRt.gameObject;
        var hlgTabs = tabsRow.AddComponent<HorizontalLayoutGroup>();
        hlgTabs.spacing = 6;
        hlgTabs.childAlignment = TextAnchor.MiddleCenter;
        hlgTabs.childControlWidth = false;
        hlgTabs.childControlHeight = false;
        hlgTabs.childForceExpandWidth = true;
        hlgTabs.childForceExpandHeight = true;

        // Полоса Label_Error/Label_Info убрана: сообщения инвентаря теперь идут тостами
        // через INotificationService (панель Canvas_Notifications, Фаза 5).

        // Контент-область: заливка от низа вкладок до низа панели (не layout-group,
        // фиксированные отступы через offsetMin/offsetMax — предсказуемый размер).
        var contentRt = MakeFillBelow("Panel_Content", panel.transform, contentTopInset, padSide, padBottom);
        var content = contentRt.gameObject;

        // ── Вкладка «Снаряжение» ──────────────────────────────────────────────
        var panelEquip = MakeStretchPanel("Panel_Equipment", content.transform, SubPanelBg);
        var equipVlg = panelEquip.AddComponent<VerticalLayoutGroup>();
        equipVlg.padding = new RectOffset(12, 12, 12, 12);
        equipVlg.spacing = 8;
        equipVlg.childControlWidth = true;
        equipVlg.childForceExpandWidth = true;
        equipVlg.childControlHeight = true;

        MakeLabel("Label_EquipHeader", panelEquip.transform, "Надето", 20);
        var equipSlots = InventoryDollBuilder.Build(panelEquip.transform);


        MakeLabel("Label_BackpackHeader", panelEquip.transform, "Рюкзак", 20);
        var backpackScroll = MakeScrollList("Scroll_Backpack", panelEquip.transform, out var backpackContent);

        // ── Вкладка «Сундук» ──────────────────────────────────────────────────
        var panelChest = MakeStretchPanel("Panel_Chest", content.transform, SubPanelBg);
        panelChest.SetActive(false);
        var chestVlg = panelChest.AddComponent<VerticalLayoutGroup>();
        chestVlg.padding = new RectOffset(12, 12, 12, 12);
        chestVlg.spacing = 8;
        chestVlg.childControlWidth = true;
        chestVlg.childForceExpandWidth = true;
        chestVlg.childControlHeight = true;

        MakeLabel("Label_ChestHeader", panelChest.transform, "Личный сундук", 20);
        var chestUnavailable = MakeLabel("Label_ChestUnavailable", panelChest.transform,
            "Сундук недоступен в этой локации.", 18).gameObject;
        var chestScroll = MakeScrollList("Scroll_Chest", panelChest.transform, out var chestContent);

        // ── Вкладка «Эффекты» ─────────────────────────────────────────────────
        var panelEffects = MakeStretchPanel("Panel_Effects", content.transform, SubPanelBg);
        panelEffects.SetActive(false);
        var effVlg = panelEffects.AddComponent<VerticalLayoutGroup>();
        effVlg.padding = new RectOffset(12, 12, 12, 12);
        effVlg.spacing = 8;
        effVlg.childControlWidth = true;
        effVlg.childForceExpandWidth = true;
        effVlg.childControlHeight = true;

        MakeLabel("Label_EffectsHeader", panelEffects.transform, "Расходка", 20);
        var stacksEmpty = MakeLabel("Label_StacksEmpty", panelEffects.transform, "Нет расходки.", 18).gameObject;
        var effectsScroll = MakeScrollList("Scroll_Effects", panelEffects.transform, out var stacksContent);

        // ── Вкладка-заглушка (Ресурсы/Квесты) ─────────────────────────────────
        var panelPlaceholder = MakeStretchPanel("Panel_Placeholder", content.transform, SubPanelBg);
        panelPlaceholder.SetActive(false);
        var lblPlaceholder = MakeLabel("Label_Placeholder", panelPlaceholder.transform, "Пока пусто.", 20);

        // Спиннер
        var spinnerRt = new GameObject("Spinner_Loading", typeof(RectTransform));
        spinnerRt.transform.SetParent(panel.transform, false);
        var spinnerRect = spinnerRt.GetComponent<RectTransform>();
        spinnerRect.anchorMin = new Vector2(0, 0);
        spinnerRect.anchorMax = new Vector2(1, 0);
        spinnerRect.pivot = new Vector2(0.5f, 0);
        spinnerRect.anchoredPosition = new Vector2(0, 16);
        spinnerRect.sizeDelta = new Vector2(-40, 36);
        var spinnerLbl = spinnerRt.AddComponent<TextMeshProUGUI>();
        spinnerLbl.text = "Загрузка...";
        spinnerLbl.fontSize = 20;
        spinnerLbl.color = Color.white;
        spinnerLbl.alignment = TextAlignmentOptions.Center;
        var spinner = spinnerRt;
        spinner.SetActive(false);

        // ── Popup_ItemDetail ──────────────────────────────────────────────────
        var detailGo = MakeStretchPanel("Popup_ItemDetail", safeArea, new Color(0.06f, 0.06f, 0.09f, 0.98f));
        detailGo.AddComponent<RectMask2D>();
        detailGo.SetActive(false);
        var detail = detailGo.AddComponent<Popup_ItemDetail>();

        var detailVlg = detailGo.AddComponent<VerticalLayoutGroup>();
        detailVlg.padding = new RectOffset(40, 40, 60, 40);
        detailVlg.spacing = 14;
        detailVlg.childAlignment = TextAnchor.UpperCenter;
        detailVlg.childControlWidth = true;
        detailVlg.childForceExpandWidth = true;
        detailVlg.childControlHeight = false;

        var dRarity = new GameObject("RarityFrame", typeof(RectTransform));
        dRarity.transform.SetParent(detailGo.transform, false);
        var dRarityImg = dRarity.AddComponent<Image>();
        dRarityImg.color = Color.white;
        dRarity.AddComponent<LayoutElement>().preferredHeight = 8;

        var dName = MakeLabel("Label_Name", detailGo.transform, "Предмет", 30);
        var dMeta = MakeLabel("Label_Meta", detailGo.transform, "Редкость • ур.1", 20);
        var dDur = MakeLabel("Label_Durability", detailGo.transform, "Прочность: 0/0", 20);
        var dStats = MakeLabel("Label_Stats", detailGo.transform, "", 20);

        var actions1 = MakeHRow("Panel_Actions1", detailGo.transform, 70);
        var bEquip = MakeButton("Button_Equip", actions1.transform, "Надеть");
        var bUnequip = MakeButton("Button_Unequip", actions1.transform, "Снять");
        var bRepair = MakeButton("Button_Repair", actions1.transform, "Починить");

        var actions2 = MakeHRow("Panel_Actions2", detailGo.transform, 70);
        var bDeposit = MakeButton("Button_Deposit", actions2.transform, "В сундук");
        var bWithdraw = MakeButton("Button_Withdraw", actions2.transform, "Достать");
        var bDiscard = MakeButton("Button_Discard", actions2.transform, "Выбросить");

        var bDetailClose = MakeButton("Button_DetailClose", detailGo.transform, "Закрыть");

        // Popup_Confirm убран: подтверждение выброса теперь идёт модальным диалогом
        // сервиса уведомлений (INotificationService.ShowConfirm), см. Фаза 5.

        // ════════════════════════════════════════════════════════════════════
        // АВТО-НАЗНАЧЕНИЕ SerializeField
        // ════════════════════════════════════════════════════════════════════

        // View_Inventory
        {
            var so = new SerializedObject(view);
            so.FindProperty("mButtonClose").objectReferenceValue = btnClose;
            so.FindProperty("mLabelBackpackCount").objectReferenceValue = lblBackpack;
            so.FindProperty("mSpinner").objectReferenceValue = spinner;

            so.FindProperty("mTabsContainer").objectReferenceValue = tabsRow.transform;
            so.FindProperty("mTabButtonPrefab").objectReferenceValue = tabButtonPrefab;

            so.FindProperty("mPanelEquipment").objectReferenceValue = panelEquip;
            so.FindProperty("mPanelChest").objectReferenceValue = panelChest;
            so.FindProperty("mPanelEffects").objectReferenceValue = panelEffects;
            so.FindProperty("mPanelPlaceholder").objectReferenceValue = panelPlaceholder;
            so.FindProperty("mLabelPlaceholder").objectReferenceValue = lblPlaceholder;

            var slotsProp = so.FindProperty("mEquipSlots");
            slotsProp.arraySize = equipSlots.Count;
            for (int i = 0; i < equipSlots.Count; i++)
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = equipSlots[i];

            so.FindProperty("mBackpackContainer").objectReferenceValue = backpackContent;
            so.FindProperty("mItemSlotPrefab").objectReferenceValue = itemSlotPrefab;

            so.FindProperty("mChestContainer").objectReferenceValue = chestContent;
            so.FindProperty("mChestUnavailableHint").objectReferenceValue = chestUnavailable;

            so.FindProperty("mStacksContainer").objectReferenceValue = stacksContent;
            so.FindProperty("mStackRowPrefab").objectReferenceValue = stackRowPrefab;
            so.FindProperty("mStacksEmptyHint").objectReferenceValue = stacksEmpty;
            so.ApplyModifiedProperties();
        }

        // Popup_ItemDetail
        {
            var so = new SerializedObject(detail);
            so.FindProperty("mLabelName").objectReferenceValue = dName;
            so.FindProperty("mLabelMeta").objectReferenceValue = dMeta;
            so.FindProperty("mLabelDurability").objectReferenceValue = dDur;
            so.FindProperty("mLabelStats").objectReferenceValue = dStats;
            so.FindProperty("mRarityFrame").objectReferenceValue = dRarityImg;
            so.FindProperty("mButtonEquip").objectReferenceValue = bEquip;
            so.FindProperty("mButtonUnequip").objectReferenceValue = bUnequip;
            so.FindProperty("mButtonRepair").objectReferenceValue = bRepair;
            so.FindProperty("mButtonDeposit").objectReferenceValue = bDeposit;
            so.FindProperty("mButtonWithdraw").objectReferenceValue = bWithdraw;
            so.FindProperty("mButtonDiscard").objectReferenceValue = bDiscard;
            so.FindProperty("mButtonClose").objectReferenceValue = bDetailClose;
            so.ApplyModifiedProperties();
        }

        // GameInstaller — назначить mInventoryView и mItemDetailPopup
        var installer = Object.FindFirstObjectByType<GameInstaller>();
        if (installer != null)
        {
            var so = new SerializedObject(installer);
            var pView = so.FindProperty("mInventoryView");
            var pDetail = so.FindProperty("mItemDetailPopup");
            if (pView != null) pView.objectReferenceValue = view;
            if (pDetail != null) pDetail.objectReferenceValue = detail;
            so.ApplyModifiedProperties();
            Debug.Log("[InventorySetup] GameInstaller: mInventoryView и mItemDetailPopup назначены.");
        }
        else
        {
            Debug.LogWarning("[InventorySetup] GameInstaller не найден — назначь mInventoryView и mItemDetailPopup вручную.");
        }

        // Кнопки «Инвентарь» в View_Location / View_Hunting
        WireInventoryButton<View_Location>("mInventoryButton", panel.transform.parent, "Button_OpenInventory_Loc");
        WireHuntingInventoryButton();

        EditorUtility.SetDirty(panel);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[InventorySetup] ✅ Готово! Panel_Inventory + Popup_ItemDetail созданы.\n" +
                  "Осталось вручную:\n" +
                  "  1. Поставь Panel_Inventory НИЖЕ Panel_Combat в иерархии (или меньший Sort Order),\n" +
                  "     чтобы бой перекрывал инвентарь.\n" +
                  "  2. Добавь кнопки «Инвентарь» на Panel_LocationMain и Panel_Hunting и привяжи их к\n" +
                  "     mInventoryButton (View_Location) / mButtonInventory (View_Hunting) — см. предупреждения ниже.\n" +
                  "  3. Сохрани сцену (Ctrl+S).\n" +
                  "  4. Удали Assets/Editor/InventorySetup.cs");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Подвязка кнопок «Инвентарь» (создаём кнопку и пытаемся назначить поле)
    // ════════════════════════════════════════════════════════════════════════

    private static void WireInventoryButton<T>(string fieldName, Transform _, string buttonName)
        where T : MonoBehaviour
    {
        var target = Object.FindFirstObjectByType<T>();
        if (target == null)
        {
            Debug.LogWarning($"[InventorySetup] {typeof(T).Name} не найден — кнопку «Инвентарь» добавь вручную.");
            return;
        }

        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[InventorySetup] Поле {fieldName} в {typeof(T).Name} не найдено.");
            return;
        }

        // Кнопку размещаем рядом с самим View (его GameObject) — точное место подберёшь вручную.
        var btn = MakeButton(buttonName, target.transform, "Инвентарь", height: 64);
        prop.objectReferenceValue = btn;
        so.ApplyModifiedProperties();
        Debug.Log($"[InventorySetup] {typeof(T).Name}.{fieldName} ← {buttonName} (перемести кнопку в нужное место).");
    }

    private static void WireHuntingInventoryButton()
    {
        var hunting = Object.FindFirstObjectByType<View_Hunting>();
        if (hunting == null)
        {
            Debug.LogWarning("[InventorySetup] View_Hunting не найден — кнопку «Инвентарь» добавь вручную.");
            return;
        }
        var so = new SerializedObject(hunting);
        var prop = so.FindProperty("mButtonInventory");
        if (prop == null)
        {
            Debug.LogWarning("[InventorySetup] Поле mButtonInventory в View_Hunting не найдено.");
            return;
        }
        var btn = MakeButton("Button_OpenInventory_Hunt", hunting.transform, "Инвентарь", height: 64);
        prop.objectReferenceValue = btn;
        so.ApplyModifiedProperties();
        Debug.Log("[InventorySetup] View_Hunting.mButtonInventory ← Button_OpenInventory_Hunt (перемести в нужное место).");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Префабы списков
    // ════════════════════════════════════════════════════════════════════════

    private static InventoryItemSlotView MakeItemSlotPrefab(Transform parent)
    {
        var go = new GameObject("Item_InvSlot", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 72);
        go.AddComponent<LayoutElement>().preferredHeight = 72;

        var frame = go.AddComponent<Image>();
        frame.color = Color.white;

        var inner = MakeStretchPanel("Inner", go.transform, SlotBg);
        inner.GetComponent<RectTransform>().offsetMin = new Vector2(3, 3);
        inner.GetComponent<RectTransform>().offsetMax = new Vector2(-3, -3);
        var innerVlg = inner.AddComponent<VerticalLayoutGroup>();
        innerVlg.padding = new RectOffset(10, 10, 6, 6);
        innerVlg.childControlWidth = true;
        innerVlg.childForceExpandWidth = true;
        innerVlg.childControlHeight = false;

        var btn = go.AddComponent<Button>();
        var name = MakeLabel("Label_Name", inner.transform, "Предмет", 18);
        name.alignment = TextAlignmentOptions.Left;
        name.overflowMode = TextOverflowModes.Ellipsis;
        var sub = MakeLabel("Label_Sub", inner.transform, "ур.1 • 0/0", 14);
        sub.alignment = TextAlignmentOptions.Left;
        sub.color = new Color(0.7f, 0.7f, 0.7f);

        var broken = MakeStretchPanel("Broken_Overlay", go.transform, new Color(0.6f, 0, 0, 0.35f));
        broken.SetActive(false);

        var view = go.AddComponent<InventoryItemSlotView>();
        var so = new SerializedObject(view);
        so.FindProperty("mButton").objectReferenceValue = btn;
        so.FindProperty("mLabelName").objectReferenceValue = name;
        so.FindProperty("mLabelSub").objectReferenceValue = sub;
        so.FindProperty("mRarityFrame").objectReferenceValue = frame;
        so.FindProperty("mBrokenOverlay").objectReferenceValue = broken;
        so.ApplyModifiedProperties();
        return view;
    }

    private static ConsumableStackItemView MakeStackRowPrefab(Transform parent)
    {
        var go = new GameObject("Item_StackRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 56);
        go.AddComponent<LayoutElement>().preferredHeight = 56;
        var bg = go.AddComponent<Image>();
        bg.color = SlotBg;

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(12, 12, 6, 6);
        hlg.spacing = 10;
        hlg.childControlWidth = true;
        hlg.childForceExpandWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandHeight = true;

        var name = MakeLabel("Label_Name", go.transform, "Расходка", 18);
        name.alignment = TextAlignmentOptions.Left;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.GetComponent<LayoutElement>().flexibleWidth = 3;   // имени — больше места, не зажимать в треть строки

        var qty = MakeLabel("Label_Qty", go.transform, "×0", 18);
        qty.GetComponent<LayoutElement>().flexibleWidth = 1;
        qty.GetComponent<LayoutElement>().preferredWidth = 70;

        var ttl = MakeLabel("Label_Ttl", go.transform, "бессрочно", 16);
        ttl.color = new Color(0.7f, 0.7f, 0.7f);
        ttl.GetComponent<LayoutElement>().flexibleWidth = 1;
        ttl.GetComponent<LayoutElement>().preferredWidth = 110;

        var view = go.AddComponent<ConsumableStackItemView>();
        var so = new SerializedObject(view);
        so.FindProperty("mLabelName").objectReferenceValue = name;
        so.FindProperty("mLabelQuantity").objectReferenceValue = qty;
        so.FindProperty("mLabelTtl").objectReferenceValue = ttl;
        so.ApplyModifiedProperties();
        return view;
    }

    private static Button MakeTabButtonPrefab(Transform parent)
    {
        var go = new GameObject("Button_Tab", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(140, 56);
        var img = go.AddComponent<Image>();
        img.color = ButtonBg;
        var btn = go.AddComponent<Button>();
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.preferredHeight = 56;
        MakeLabel("Label", go.transform, "Вкладка", 18);
        return btn;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Слоты экипировки
    // ════════════════════════════════════════════════════════════════════════

    private static EquipSlotView MakeEquipSlot(Transform parent, string slotKey, string title, bool placeholder)
    {
        var go = new GameObject($"Slot_{slotKey}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 70);

        var frame = go.AddComponent<Image>();
        frame.color = new Color(0.25f, 0.25f, 0.25f);

        var inner = MakeStretchPanel("Inner", go.transform, SlotBg);
        inner.GetComponent<RectTransform>().offsetMin = new Vector2(3, 3);
        inner.GetComponent<RectTransform>().offsetMax = new Vector2(-3, -3);
        var innerVlg = inner.AddComponent<VerticalLayoutGroup>();
        innerVlg.childControlWidth = true;
        innerVlg.childForceExpandWidth = true;
        innerVlg.childControlHeight = false;
        innerVlg.childAlignment = TextAnchor.MiddleCenter;

        var btn = go.AddComponent<Button>();

        var slotLbl = MakeLabel("Label_Slot", inner.transform, title, 14);
        slotLbl.color = new Color(0.6f, 0.6f, 0.6f);
        var itemLbl = MakeLabel("Label_Item", inner.transform, placeholder ? "скоро" : "—", 16);
        itemLbl.overflowMode = TextOverflowModes.Ellipsis;

        var broken = MakeStretchPanel("Broken_Overlay", go.transform, new Color(0.6f, 0, 0, 0.35f));
        broken.SetActive(false);

        var view = go.AddComponent<EquipSlotView>();
        var so = new SerializedObject(view);
        so.FindProperty("mSlotKey").stringValue = slotKey;
        so.FindProperty("mIsPlaceholder").boolValue = placeholder;
        so.FindProperty("mButton").objectReferenceValue = btn;
        so.FindProperty("mLabelSlot").objectReferenceValue = slotLbl;
        so.FindProperty("mLabelItem").objectReferenceValue = itemLbl;
        so.FindProperty("mRarityFrame").objectReferenceValue = frame;
        so.FindProperty("mBrokenOverlay").objectReferenceValue = broken;
        so.ApplyModifiedProperties();
        return view;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Базовые хелперы (как в CombatSetup)
    // ════════════════════════════════════════════════════════════════════════

    private static void DestroyChildIfExists(Transform parent, string name)
    {
        var ex = parent.Find(name);
        if (ex != null)
        {
            Object.DestroyImmediate(ex.gameObject);
            Debug.Log($"[InventorySetup] Старый {name} удалён.");
        }
    }

    private static GameObject MakeStretchPanel(string name, Transform parent, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        MakeStretchRT(go);
        go.AddComponent<Image>().color = bg;
        return go;
    }

    private static void MakeStretchRT(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Полоса, приклеенная к верху родителя на фиксированном отступе/высоте.
    /// Не зависит от LayoutGroup родителя — предсказуемый размер всегда.
    /// </summary>
    private static RectTransform MakeTopBar(string name, Transform parent, float topOffset, float height, float sidePad = 20f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -topOffset);
        rt.sizeDelta = new Vector2(-sidePad * 2, height);
        return rt;
    }

    /// <summary>
    /// Область-заливка от заданного отступа сверху до низа родителя (с боковыми/нижним паддингом).
    /// Используется вместо flexible-child внутри вложенного LayoutGroup — фиксированный,
    /// предсказуемый размер вместо зависимости от чужого пересчёта layout.
    /// </summary>
    private static RectTransform MakeFillBelow(string name, Transform parent, float topInset, float sidePad = 20f, float bottomPad = 20f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(sidePad, bottomPad);
        rt.offsetMax = new Vector2(-sidePad, -topInset);
        return rt;
    }

    private static GameObject MakeHRow(string name, Transform parent, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, height);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1;
        return go;
    }

    private static GameObject MakeGrid(string name, Transform parent, Vector2 cell, int cols)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var grid = go.AddComponent<GridLayoutGroup>();
        grid.cellSize = cell;
        grid.spacing = new Vector2(8, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = cell.y * 4 + 24;
        return go;
    }

    private static GameObject MakeScrollList(string name, Transform parent, out Transform content)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.flexibleHeight = 1;
        le.flexibleWidth = 1;
        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.2f);
        var scroll = go.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        MakeStretchRT(viewport);
        // RectMask2D вместо Mask: простое прямоугольное клипование без stencil-буфера —
        // надёжнее на разных масштабах канваса/превью редактора, рекомендация Unity для ScrollRect.
        viewport.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewport.transform, false);
        var crt = contentGo.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = true;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = false;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = crt;

        content = contentGo.transform;
        return go;
    }

    private static TextMeshProUGUI MakeLabel(string name, Transform parent, string text, int fontSize = 22)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(300, fontSize + 10);
        var lbl = go.AddComponent<TextMeshProUGUI>();
        lbl.text = text;
        lbl.fontSize = fontSize;
        lbl.color = Color.white;
        lbl.alignment = TextAlignmentOptions.Center;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize + 10;
        le.flexibleWidth = 1;
        return lbl;
    }

    private static Button MakeButton(string name, Transform parent, string label,
        float fixedWidth = 0, float height = 70)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(fixedWidth > 0 ? fixedWidth : 200, height);
        go.AddComponent<Image>().color = ButtonBg;
        var btn = go.AddComponent<Button>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        if (fixedWidth > 0) le.preferredWidth = fixedWidth;
        else le.flexibleWidth = 1;
        MakeLabel(name + "_Lbl", go.transform, label, 20);
        return btn;
    }
}
#endif
