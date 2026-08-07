// Удалить после использования (Assets/Editor/BlacksmithSetup.cs)
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor-скрипт сборки экрана кузнеца (ремонт).
/// Запуск: MMORPG → Setup → Blacksmith
///
/// Делает три вещи:
///
/// 1. СОБИРАЕТ Panel_Blacksmith на уровне SafeArea — рядом с Panel_Chat, а не внутри
///    Panel_Location. Причина та же, на которой когда-то обожглись с попапом результата боя:
///    SetActive(true) на ребёнке выключенного родителя ничего не делает.
///    Панель оставляется АКТИВНОЙ: выключенный на момент сохранения сцены объект не получит
///    Awake() при загрузке, и подписка на IsOpen, которая и должна его прятать, не создастся.
///    Спрячет он себя сам в SafeAwake по текущему значению IsOpen.
///
/// 2. СОЗДАЁТ префаб строки Item_RepairRow в скрытом контейнере шаблонов.
///
/// 3. ДОБАВЛЯЕТ кнопку «Кузнец» рядом с кнопкой «Чат» на View_Location и назначает её в
///    сериализованное поле. Кнопка клонируется с существующей, чтобы не гадать стиль заново.
///
/// После запуска — проверь Console, назначь Panel_Blacksmith в GameInstaller (поле «Кузнец»),
/// сохрани сцену и удали скрипт.
/// </summary>
public static class BlacksmithSetup
{
    private const string PANEL_NAME = "Panel_Blacksmith";
    private const string TEMPLATES_NAME = "_BlacksmithTemplates";

    private static readonly Color ScreenBg = new(0.06f, 0.06f, 0.08f, 0.98f);
    private static readonly Color RowBg = new(1f, 1f, 1f, 0.04f);
    private static readonly Color TextMain = new(0.88f, 0.88f, 0.90f);
    private static readonly Color TextDim = new(0.62f, 0.62f, 0.66f);
    private static readonly Color TextGold = new(0.92f, 0.80f, 0.35f);
    private static readonly Color TextWarn = new(0.90f, 0.55f, 0.35f);

    [MenuItem("MMORPG/Setup/Blacksmith")]
    public static void Setup()
    {
        // Канвас ищем ЧЕРЕЗ View_Location, а не «первый попавшийся Overlay»: в сцене их минимум
        // два (основной + Canvas_Notifications), и порядок FindObjectsByType не гарантирован —
        // ровно на этом ChatSetup когда-то плодил дубли панели в чужом канвасе.
        var locationView = Object.FindFirstObjectByType<View_Location>();
        if (locationView == null)
        {
            Debug.LogError("[BlacksmithSetup] View_Location не найден. Открой Game-сцену.");
            return;
        }

        var rootCanvas = locationView.GetComponentInParent<Canvas>();
        if (rootCanvas == null)
        {
            Debug.LogError("[BlacksmithSetup] Canvas над View_Location не найден.");
            return;
        }

        var safeArea = rootCanvas.transform.Find("SafeArea") ?? rootCanvas.transform;

        // Подчищаем прошлые прогоны во ВСЕЙ сцене, включая выключенные объекты.
        foreach (var stray in Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (stray != null && (stray.name == PANEL_NAME || stray.name == TEMPLATES_NAME))
                Object.DestroyImmediate(stray.gameObject);
        }

        // ── Панель ───────────────────────────────────────────────────────────
        var panel = MakeStretchPanel(PANEL_NAME, safeArea, ScreenBg);
        panel.transform.SetAsLastSibling();   // поверх Panel_Chat
        var view = panel.AddComponent<View_Blacksmith>();

        // Шапка
        var topBar = MakeTopBar("TopBar", panel.transform, topOffset: -20f, height: 60f);
        var title = MakeLabel("Label_Title", topBar, "Кузнец", 28, TextMain, TextAlignmentOptions.Left);
        StretchLeft(title.rectTransform, width: 200f);

        var gold = MakeLabel("Label_Gold", topBar, "Золото: —", 22, TextGold, TextAlignmentOptions.Right);
        StretchRight(gold.rectTransform, width: 240f, rightPad: 90f);

        var close = MakeButton("Button_Close", topBar, "✕");
        StretchRight(close.GetComponent<RectTransform>(), width: 60f, rightPad: 0f);

        var spinner = MakeLabel("Label_Spinner", topBar, "…", 24, TextDim, TextAlignmentOptions.Center);
        StretchRight(spinner.rectTransform, width: 60f, rightPad: 150f);

        // Подсказки
        var empty = MakeLabel("Hint_Empty", panel.transform,
            "Всё целое — чинить нечего.", 22, TextDim, TextAlignmentOptions.Center);
        MakeStretchRT(empty.gameObject);

        var wornOutBar = MakeBottomBar("WornOutBar", panel.transform, bottomOffset: 100f, height: 44f);
        var wornOut = MakeLabel("Label_WornOut", wornOutBar, "", 18, TextWarn, TextAlignmentOptions.Center);
        MakeStretchRT(wornOut.gameObject);

        // Список
        var listRt = MakeFillBetween("ListArea", panel.transform, topInset: 90f, bottomInset: 150f);
        var scroll = MakeScrollList(listRt, out var content);

        // Кнопка «Починить всё»
        var bottomBar = MakeBottomBar("BottomBar", panel.transform, bottomOffset: 20f, height: 64f);
        var repairAll = MakeButton("Button_RepairAll", bottomBar, "Починить всё");
        MakeStretchRT(repairAll.gameObject);
        var repairAllLabel = repairAll.GetComponentInChildren<TMP_Text>();

        // Префаб строки — в скрытом контейнере, иначе он был бы виден как обычный объект сцены.
        var templates = new GameObject(TEMPLATES_NAME, typeof(RectTransform));
        templates.transform.SetParent(safeArea, false);
        templates.SetActive(false);
        var rowPrefab = MakeRepairRowPrefab(templates.transform);

        // ── Проставляем сериализованные поля ────────────────────────────────
        var so = new SerializedObject(view);
        Set(so, "mButtonClose", close);
        Set(so, "mLabelGold", gold);
        Set(so, "mSpinner", spinner.gameObject);
        Set(so, "mRowsContainer", content);
        Set(so, "mRowPrefab", rowPrefab);
        Set(so, "mEmptyHint", empty.gameObject);
        Set(so, "mWornOutHint", wornOutBar.gameObject);
        Set(so, "mLabelWornOut", wornOut);
        Set(so, "mButtonRepairAll", repairAll);
        Set(so, "mLabelRepairAll", repairAllLabel);
        so.ApplyModifiedProperties();

        // ── Кнопка «Кузнец» рядом с «Чат» ───────────────────────────────────
        AddBlacksmithButton(locationView);

        EditorUtility.SetDirty(view);
        EditorUtility.SetDirty(locationView);

        Debug.Log("[BlacksmithSetup] Готово. ОСТАЛОСЬ ВРУЧНУЮ: назначить " + PANEL_NAME +
                  " в GameInstaller → «Кузнец» → Blacksmith View. Затем сохрани сцену и удали скрипт.");
    }

    /// <summary>
    /// Клонирует кнопку «Чат» на View_Location, переименовывает в «Кузнец» и назначает в
    /// mBlacksmithButton. Если поля-образца нет — только предупреждает: разработчик добавит
    /// кнопку сам, код клика уже готов.
    /// </summary>
    private static void AddBlacksmithButton(View_Location locationView)
    {
        var so = new SerializedObject(locationView);
        var chatProp = so.FindProperty("mChatButton");
        if (chatProp == null || chatProp.objectReferenceValue == null)
        {
            Debug.LogWarning("[BlacksmithSetup] View_Location.mChatButton не назначен — кнопку " +
                             "«Кузнец» добавь и назначь в mBlacksmithButton вручную.");
            return;
        }

        var chatButton = (Button)chatProp.objectReferenceValue;
        var clone = Object.Instantiate(chatButton.gameObject, chatButton.transform.parent);
        clone.name = "Button_Blacksmith";

        var label = clone.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = "Кузнец";

        var prop = so.FindProperty("mBlacksmithButton");
        if (prop == null)
        {
            Debug.LogWarning("[BlacksmithSetup] Поле mBlacksmithButton не найдено на View_Location " +
                             "— кнопка создана, но не назначена. Проверь имя поля в коде.");
            return;
        }

        prop.objectReferenceValue = clone.GetComponent<Button>();
        so.ApplyModifiedProperties();
        Debug.Log("[BlacksmithSetup] Кнопка «Кузнец» создана рядом с «Чат».");
    }

    // ── Префаб строки ───────────────────────────────────────────────────────

    private static RepairRowView MakeRepairRowPrefab(Transform parent)
    {
        var go = new GameObject("Item_RepairRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 56);
        go.AddComponent<Image>().color = RowBg;

        var name = MakeLabel("Label_Name", go.transform, "Предмет", 20, TextMain, TextAlignmentOptions.Left);
        StretchLeft(name.rectTransform, width: 260f);

        var dur = MakeLabel("Label_Durability", go.transform, "0/0 → 0/0", 17, TextDim, TextAlignmentOptions.Left);
        var durRt = dur.rectTransform;
        durRt.anchorMin = new Vector2(0, 0);
        durRt.anchorMax = new Vector2(0, 1);
        durRt.pivot = new Vector2(0, 0.5f);
        durRt.anchoredPosition = new Vector2(270f, 0);
        durRt.sizeDelta = new Vector2(220f, 0);

        var cost = MakeLabel("Label_Cost", go.transform, "0 з", 20, TextGold, TextAlignmentOptions.Right);
        StretchRight(cost.rectTransform, width: 120f, rightPad: 140f);

        var button = MakeButton("Button_Repair", go.transform, "Починить");
        StretchRight(button.GetComponent<RectTransform>(), width: 130f, rightPad: 0f);

        var row = go.AddComponent<RepairRowView>();
        var so = new SerializedObject(row);
        Set(so, "mLabelName", name);
        Set(so, "mLabelDurability", dur);
        Set(so, "mLabelCost", cost);
        Set(so, "mButtonRepair", button);
        so.ApplyModifiedProperties();

        return row;
    }

    // ── Хелперы разметки ────────────────────────────────────────────────────

    private static void Set(SerializedObject so, string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[BlacksmithSetup] Поле {field} не найдено — назначь вручную.");
            return;
        }
        prop.objectReferenceValue = value;
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
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Transform MakeTopBar(string name, Transform parent, float topOffset, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(20f, -height + topOffset);
        rt.offsetMax = new Vector2(-20f, topOffset);
        return go.transform;
    }

    private static Transform MakeBottomBar(string name, Transform parent, float bottomOffset, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.offsetMin = new Vector2(20f, bottomOffset);
        rt.offsetMax = new Vector2(-20f, bottomOffset + height);
        return go.transform;
    }

    private static RectTransform MakeFillBetween(string name, Transform parent, float topInset, float bottomInset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(20f, bottomInset);
        rt.offsetMax = new Vector2(-20f, -topInset);
        return rt;
    }

    private static ScrollRect MakeScrollList(RectTransform parent, out Transform content)
    {
        var scrollGo = new GameObject("Scroll_Rows", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        MakeStretchRT(scrollGo);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scrollGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(scrollGo.transform, false);
        var crt = contentGo.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        crt.sizeDelta = new Vector2(0, 0);

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childControlWidth = true;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = crt;
        scroll.viewport = scrollGo.GetComponent<RectTransform>();

        content = contentGo.transform;
        return scroll;
    }

    private static TextMeshProUGUI MakeLabel(string name, Transform parent, string text,
        int fontSize, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button MakeButton(string name, Transform parent, string label)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = new Color(0.20f, 0.20f, 0.26f);
        var button = go.AddComponent<Button>();

        var tmp = MakeLabel("Label", go.transform, label, 20, TextMain, TextAlignmentOptions.Center);
        MakeStretchRT(tmp.gameObject);

        return button;
    }

    private static void StretchLeft(RectTransform rt, float width)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, 0);
    }

    private static void StretchRight(RectTransform rt, float width, float rightPad)
    {
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-rightPad, 0);
        rt.sizeDelta = new Vector2(width, 0);
    }
}
#endif
