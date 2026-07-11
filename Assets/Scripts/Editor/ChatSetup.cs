// Удалить после использования (Assets/Editor/ChatSetup.cs)
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor-скрипт автосборки UI-иерархии Фазы 6 (Чат).
/// Запуск: MMORPG → Setup → Chat Panel (только в сцене Game — Auth чат не касается).
///
/// Создаёт:
///   Panel_Chat (View_Chat) — фильтры приёма, слитный лог (через ViewPool), переключатель
///   канала отправки, поле ввода со счётчиком;
///   Item_ChatMessage — префаб строки лога (в скрытом контейнере шаблонов).
///
/// Также клонирует кнопку «Инвентарь» в View_Location и View_Hunting как соседнюю кнопку
/// «Чат» (та же родительская Transform → наследует существующую вёрстку строки кнопок,
/// а не гадает её с нуля) и назначает mChatButton/mButtonChat.
///
/// Назначает ВСЕ SerializeField через SerializedObject и проставляет mChatView в GameInstaller.
///
/// После запуска — проверь Console и удали скрипт.
/// </summary>
public static class ChatSetup
{
    // ── Палитра: сплошной чёрный фон экрана, БЕЛАЯ область текста внутри (лог + ввод) —
    // по явному запросу разработчика вместо полупрозрачных тёмных тонов из первой версии.
    private static readonly Color ScreenBg = new(0f, 0f, 0f, 1f);           // весь экран — чёрный
    private static readonly Color ContentAreaBg = new(1f, 1f, 1f, 1f);      // лог сообщений — белый
    private static readonly Color InputBg = new(1f, 1f, 1f, 1f);            // поле ввода — тоже белое (это тоже текст)
    private static readonly Color ChromeButtonBg = new(0.22f, 0.22f, 0.26f); // кнопки-«хром» на чёрном фоне
    private static readonly Color RowBg = new(0.10f, 0.10f, 0.12f, 1f);      // лёгкая подложка строк фильтра/отправки
    private static readonly Color InactiveToggleBg = new(0.16f, 0.16f, 0.19f);
    private static readonly Color RowLabelColor = new(0.75f, 0.75f, 0.78f); // подписи "Показать:"/"Писать в:"

    [MenuItem("MMORPG/Setup/Chat Panel")]
    public static void CreateChatPanel()
    {
        // Канвас находим ЧЕРЕЗ уже существующий View_Location — не перебором "первый попавшийся
        // Screen Space Overlay канвас". В сцене их минимум два (основной игровой +
        // Canvas_Notifications, тоже Overlay, Фаза 5) — порядок FindObjectsByType не
        // гарантирован, скрипт мог попадать в разные канвасы при разных запусках и плодить
        // осиротевшие дубли Panel_Chat в чужом канвасе (см. баг "два чата").
        var locationViewForCanvas = Object.FindFirstObjectByType<View_Location>();
        if (locationViewForCanvas == null)
        {
            Debug.LogError("[ChatSetup] View_Location не найден в сцене. Открой Game-сцену " +
                "с уже собранным экраном локации — чат встраивается в тот же канвас.");
            return;
        }

        var rootCanvas = locationViewForCanvas.GetComponentInParent<Canvas>();
        if (rootCanvas == null)
        {
            Debug.LogError("[ChatSetup] У найденного View_Location нет родительского Canvas.");
            return;
        }

        Transform safeArea = rootCanvas.transform.Find("SafeArea") ?? rootCanvas.transform;

        // Подчищаем ЛЮБЫЕ Panel_Chat/_ChatTemplates во ВСЕЙ сцене, не только в выбранном
        // канвасе — на случай, если предыдущий прогон (до этого фикса) попал в другой канвас
        // и оставил осиротевший дубль.
        foreach (var stray in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (stray != null && (stray.name == "Panel_Chat" || stray.name == "_ChatTemplates"))
                Object.DestroyImmediate(stray.gameObject);
        }

        // ── Скрытый контейнер шаблонов ────────────────────────────────────────
        var templates = new GameObject("_ChatTemplates", typeof(RectTransform));
        templates.transform.SetParent(safeArea, false);
        templates.SetActive(false);

        var messageItemPrefab = MakeChatMessagePrefab(templates.transform);

        // ── Panel_Chat ───────────────────────────────────────────────────────
        var panel = MakeStretchPanel("Panel_Chat", safeArea, ScreenBg);
        panel.SetActive(true); // см. фикс бага "кнопка не открывает чат": неактивный при
                                // сохранении сцены объект не получает Awake() при загрузке —
                                // подписка на IsOpen (которая и должна прятать/показывать
                                // панель) физически не успевает создаться. Стартуем активным,
                                // SafeAwake() сам скроет себя по IsOpen.Value=false в самом конце.
        var view = panel.AddComponent<View_Chat>();
        panel.AddComponent<RectMask2D>();
        // Страховка на случай, если что-то ещё допишется в SafeArea после этого скрипта —
        // чат должен рендериться поверх экрана локации, а не под ним.
        panel.transform.SetAsLastSibling();

        const float padSide = 24f;
        const float padTop = 24f;
        const float padBottom = 24f;
        const float gap = 16f;
        const float headerH = 72f;
        const float filterH = 60f;
        const float sendRowH = 64f;
        const float inputRowH = 84f;

        var headerY = padTop;
        var filterY = headerY + headerH + gap;
        var contentTopInset = filterY + filterH + gap;

        var inputBottomOffset = padBottom;
        var sendRowBottomOffset = inputBottomOffset + inputRowH + gap;
        var contentBottomInset = sendRowBottomOffset + sendRowH + gap;

        // Шапка: заголовок + закрыть (текстом, не юникод-символом — см. баг с "тофу"-квадратом:
        // "▶"/"✕" не было в шрифте, показывались как \u25A1, конкретно это и было видно в консоли)
        var header = MakeTopBar("Panel_Header", panel.transform, headerY, headerH, padSide);
        var headerHlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerHlg.spacing = 10;
        headerHlg.childAlignment = TextAnchor.MiddleCenter;
        headerHlg.childControlWidth = false;
        headerHlg.childControlHeight = false;
        headerHlg.childForceExpandWidth = true;
        headerHlg.childForceExpandHeight = true;
        MakeLabel("Label_Title", header.transform, "Чат", 28);
        var btnClose = MakeButton("Button_Close", header.transform, "Закрыть", fixedWidth: 140, height: 60);

        // Фильтры приёма: [Локация] [Торговый] — Личка/Система всегда видны, без чекбокса
        var filterRow = MakeTopBar("Panel_Filters", panel.transform, filterY, filterH, padSide);
        filterRow.gameObject.AddComponent<Image>().color = RowBg;
        var filterHlg = filterRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        filterHlg.spacing = 10;
        filterHlg.padding = new RectOffset(10, 10, 6, 6);
        filterHlg.childAlignment = TextAnchor.MiddleLeft;
        filterHlg.childControlWidth = false;
        filterHlg.childControlHeight = true;
        MakeSmallLabel("Label_FilterCaption", filterRow.transform, "Показать:");
        var (filterLocationBtn, filterLocationBg) = MakeToggleButton("Toggle_Location", filterRow.transform, "Локация");
        var (filterTradeBtn, filterTradeBg) = MakeToggleButton("Toggle_Trade", filterRow.transform, "Торговый");
        var alwaysOnLabel = MakeLabel("Label_AlwaysOn", filterRow.transform, "Личка • Система — всегда", 15);
        alwaysOnLabel.color = RowLabelColor;
        alwaysOnLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var alwaysOnLe = alwaysOnLabel.gameObject.AddComponent<LayoutElement>();
        alwaysOnLe.flexibleWidth = 1;

        // Лог сообщений — БЕЛАЯ область (см. задачу), заливка между фильтрами и переключателем
        // канала отправки
        var contentRt = MakeFillBetween("Panel_MessagesArea", panel.transform, contentTopInset, contentBottomInset, padSide);
        var scrollGo = MakeScrollList("Scroll_Messages", contentRt.transform, out var messagesContent);
        // contentRt — не LayoutGroup, а fill-заливка (MakeFillBetween) → растягиваем
        // ScrollRect на всю область явными якорями, MakeScrollList сама якоря не выставляет.
        MakeStretchRT(scrollGo);
        var scrollRect = scrollGo.GetComponent<ScrollRect>();

        // Переключатель канала отправки: (Локация)(Торговый)(Личка → …)
        var sendRow = MakeBottomBar("Panel_SendChannel", panel.transform, sendRowBottomOffset, sendRowH, padSide);
        sendRow.gameObject.AddComponent<Image>().color = RowBg;
        var sendHlg = sendRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        sendHlg.spacing = 10;
        sendHlg.padding = new RectOffset(10, 10, 6, 6);
        sendHlg.childAlignment = TextAnchor.MiddleCenter;
        sendHlg.childControlWidth = false;
        sendHlg.childControlHeight = true;
        MakeSmallLabel("Label_SendCaption", sendRow.transform, "Писать в:");
        var (sendLocationBtn, sendLocationBg) = MakeToggleButton("Send_Location", sendRow.transform, "Локация");
        var (sendTradeBtn, sendTradeBg) = MakeToggleButton("Send_Trade", sendRow.transform, "Торговый");
        var (sendPrivateBtn, sendPrivateBg) = MakeToggleButton("Send_Private", sendRow.transform, "Личка");
        var sendPrivateLabel = sendPrivateBtn.GetComponentInChildren<TextMeshProUGUI>();

        // Ввод: поле (тоже белое — это текст, который печатает игрок) + счётчик + отправить
        var inputRow = MakeBottomBar("Panel_Input", panel.transform, inputBottomOffset, inputRowH, padSide);
        var inputHlg = inputRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        inputHlg.spacing = 10;
        inputHlg.childAlignment = TextAnchor.MiddleCenter;
        inputHlg.childControlWidth = false;
        inputHlg.childControlHeight = true;
        var inputField = MakeInputField("InputField_Message", inputRow.transform, "Написать...");
        var counterLabel = MakeLabel("Label_Counter", inputRow.transform, "0/250", 16);
        counterLabel.color = RowLabelColor;
        counterLabel.gameObject.GetComponent<LayoutElement>().flexibleWidth = 0;
        counterLabel.gameObject.GetComponent<LayoutElement>().preferredWidth = 80;
        var sendButton = MakeButton("Button_Send", inputRow.transform, "Отправить", fixedWidth: 160, height: 76);

        // ── Назначение SerializeField через SerializedObject ──────────────────
        {
            var so = new SerializedObject(view);
            so.FindProperty("mButtonClose").objectReferenceValue = btnClose;

            so.FindProperty("mFilterLocationButton").objectReferenceValue = filterLocationBtn;
            so.FindProperty("mFilterLocationBg").objectReferenceValue = filterLocationBg;
            so.FindProperty("mFilterTradeButton").objectReferenceValue = filterTradeBtn;
            so.FindProperty("mFilterTradeBg").objectReferenceValue = filterTradeBg;

            so.FindProperty("mScrollRect").objectReferenceValue = scrollRect;
            so.FindProperty("mMessagesContent").objectReferenceValue = messagesContent;
            so.FindProperty("mMessageItemPrefab").objectReferenceValue = messageItemPrefab;

            so.FindProperty("mSendLocationButton").objectReferenceValue = sendLocationBtn;
            so.FindProperty("mSendLocationBg").objectReferenceValue = sendLocationBg;
            so.FindProperty("mSendTradeButton").objectReferenceValue = sendTradeBtn;
            so.FindProperty("mSendTradeBg").objectReferenceValue = sendTradeBg;
            so.FindProperty("mSendPrivateButton").objectReferenceValue = sendPrivateBtn;
            so.FindProperty("mSendPrivateBg").objectReferenceValue = sendPrivateBg;
            so.FindProperty("mSendPrivateLabel").objectReferenceValue = sendPrivateLabel;

            so.FindProperty("mInputField").objectReferenceValue = inputField;
            so.FindProperty("mCounterLabel").objectReferenceValue = counterLabel;
            so.FindProperty("mSendButton").objectReferenceValue = sendButton;
            so.ApplyModifiedProperties();
        }

        // ── GameInstaller.mChatView ────────────────────────────────────────────
        var installer = Object.FindFirstObjectByType<GameInstaller>();
        if (installer != null)
        {
            var so = new SerializedObject(installer);
            so.FindProperty("mChatView").objectReferenceValue = view;
            so.ApplyModifiedProperties();
            Debug.Log("[ChatSetup] GameInstaller.mChatView назначен.");
        }
        else
        {
            Debug.LogWarning("[ChatSetup] GameInstaller не найден — назначь mChatView вручную.");
        }

        // ── Кнопки «Чат» рядом с «Инвентарь» в View_Location / View_Hunting ────
        var locationView = Object.FindFirstObjectByType<View_Location>();
        if (locationView != null)
            AddChatButtonNextToInventory(locationView, "mInventoryButton", "mChatButton");
        else
            Debug.LogWarning("[ChatSetup] View_Location не найден в сцене — кнопку «Чат» добавь вручную.");

        var huntingView = Object.FindFirstObjectByType<View_Hunting>();
        if (huntingView != null)
            AddChatButtonNextToInventory(huntingView, "mButtonInventory", "mButtonChat");
        else
            Debug.LogWarning("[ChatSetup] View_Hunting не найден в сцене — кнопку «Чат» добавь вручную.");

        Debug.Log("[ChatSetup] Готово. Проверь Panel_Chat и кнопки «Чат» в редакторе, затем удали ChatSetup.cs.");
    }

    // ── Клонирование кнопки «Инвентарь» как соседа ──────────────────────────────

    /// <summary>
    /// Ищет существующую кнопку по имени поля (через SerializedObject), клонирует её как
    /// соседа (та же родительская Transform → наследует существующую вёрстку строки, не
    /// гадает её заново), переименовывает подпись на «Чат», назначает в chatFieldName.
    /// Если поле-образец не найдено/не назначено — ничего не создаёт, только предупреждает,
    /// разработчик добавит кнопку и назначит поле вручную.
    /// </summary>
    private static void AddChatButtonNextToInventory(Object owner, string inventoryFieldName, string chatFieldName)
    {
        var so = new SerializedObject(owner);
        var invProp = so.FindProperty(inventoryFieldName);
        if (invProp == null || invProp.objectReferenceValue == null)
        {
            Debug.LogWarning($"[ChatSetup] {owner.GetType().Name}.{inventoryFieldName} не найдено/не " +
                $"назначено в сцене — кнопку «Чат» добавь и назначь в {chatFieldName} вручную.");
            return;
        }

        var inventoryButton = (Button)invProp.objectReferenceValue;
        var parent = inventoryButton.transform.parent;

        var chatButtonGo = Object.Instantiate(inventoryButton.gameObject, parent);
        chatButtonGo.name = "Button_Chat";
        var label = chatButtonGo.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = "Чат";

        var chatProp = so.FindProperty(chatFieldName);
        if (chatProp == null)
        {
            Debug.LogWarning($"[ChatSetup] Поле {chatFieldName} не найдено на {owner.GetType().Name} " +
                "— кнопка создана, но не назначена. Проверь имя поля в коде.");
            return;
        }

        chatProp.objectReferenceValue = chatButtonGo.GetComponent<Button>();
        so.ApplyModifiedProperties();
        Debug.Log($"[ChatSetup] {owner.GetType().Name}.{chatFieldName} создана рядом с {inventoryFieldName}.");
    }

    // ── Префаб строки чата ──────────────────────────────────────────────────────

    private static Item_ChatMessage MakeChatMessagePrefab(Transform parent)
    {
        var go = new GameObject("Item_ChatMessage", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 40);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.02f); // почти невидимый — только чтобы Button было по чему кликать
        var button = go.AddComponent<Button>();

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.padding = new RectOffset(6, 6, 2, 2);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        // ВАЖНО: true, не false. При false HorizontalLayoutGroup игнорирует flexibleWidth
        // у детей вообще — Label_Body (flexibleWidth=1, должен занимать всё оставшееся
        // место) оставался на дефолтных ~100px и текст обрезался по Ellipsis после
        // первых нескольких символов ("client_ad…" вместо полного текста).
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 40;
        le.flexibleWidth = 1;

        var time = MakeRowLabel("Label_Time", go.transform, "00:00", 16, 56);
        time.color = new Color(0.35f, 0.35f, 0.35f); // серый, но тёмный — читаемо на белом

        var tag = MakeRowLabel("Label_Tag", go.transform, "Тег", 16, 56);
        tag.fontStyle = FontStyles.Bold;
        // Цвет тега проставляет Item_ChatMessage.Setup из ChatConfig.ColorFor — тут только дефолт-заглушка
        tag.color = new Color(0.2f, 0.2f, 0.2f);

        var body = MakeRowLabel("Label_Body", go.transform, "Ник: текст", 18, 0);
        body.color = new Color(0.08f, 0.08f, 0.08f); // почти чёрный текст на белом
        body.GetComponent<LayoutElement>().flexibleWidth = 1;

        var item = go.AddComponent<Item_ChatMessage>();

        var so = new SerializedObject(item);
        so.FindProperty("mTimeLabel").objectReferenceValue = time;
        so.FindProperty("mChannelTagLabel").objectReferenceValue = tag;
        so.FindProperty("mBodyLabel").objectReferenceValue = body;
        so.FindProperty("mClickArea").objectReferenceValue = button;
        so.ApplyModifiedProperties();

        return item;
    }

    private static TextMeshProUGUI MakeRowLabel(string name, Transform parent, string text, int fontSize, float fixedWidth)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var lbl = go.AddComponent<TextMeshProUGUI>();
        lbl.text = text;
        lbl.fontSize = fontSize;
        lbl.color = Color.white;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.overflowMode = TextOverflowModes.Ellipsis;
        var le = go.AddComponent<LayoutElement>();
        if (fixedWidth > 0)
        {
            le.preferredWidth = fixedWidth;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(fixedWidth, fontSize + 6);
        }
        else
        {
            le.preferredHeight = fontSize + 6;
        }
        return lbl;
    }

    // ── Toggle-кнопка (фильтр / канал отправки) ─────────────────────────────────

    private static (Button button, Image bg) MakeToggleButton(string name, Transform parent, string label)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color = InactiveToggleBg;
        var btn = go.AddComponent<Button>();
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.preferredHeight = 44;
        MakeLabel(name + "_Lbl", go.transform, label, 18);
        return (btn, bg);
    }

    // ── TMP_InputField ───────────────────────────────────────────────────────────

    private static TMP_InputField MakeInputField(string name, Transform parent, string placeholder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = InputBg;
        var input = go.AddComponent<TMP_InputField>();
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.preferredHeight = 76; // строка (Panel_Input) — 84px, поле почти вплотную к ней, не меньше

        var textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(go.transform, false);
        var textAreaRt = textArea.GetComponent<RectTransform>();
        textAreaRt.anchorMin = Vector2.zero;
        textAreaRt.anchorMax = Vector2.one;
        textAreaRt.offsetMin = new Vector2(12, 6);
        textAreaRt.offsetMax = new Vector2(-12, -6);
        textArea.AddComponent<RectMask2D>();

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGo.transform.SetParent(textArea.transform, false);
        MakeStretchRT(placeholderGo);
        var placeholderTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholderTmp.text = placeholder;
        placeholderTmp.fontSize = 20;
        placeholderTmp.color = new Color(0f, 0f, 0f, 0.4f);
        placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(textArea.transform, false);
        MakeStretchRT(textGo);
        var textTmp = textGo.AddComponent<TextMeshProUGUI>();
        textTmp.fontSize = 20;
        textTmp.color = new Color(0.05f, 0.05f, 0.05f);
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;

        input.textViewport = textAreaRt;
        input.textComponent = textTmp;
        input.placeholder = placeholderTmp;
        input.characterLimit = 250; // дефолт-заглушка — View_Chat.BindInput переустановит из ChatConfig в рантайме

        return input;
    }

    // ── Базовые хелперы вёрстки (тот же стиль, что InventorySetup/NotificationsSetup) ──

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

    /// <summary>Полоса, приклеенная к верху родителя на фиксированном отступе/высоте.</summary>
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

    /// <summary>Полоса, приклеенная к низу родителя на фиксированном отступе/высоте.</summary>
    private static RectTransform MakeBottomBar(string name, Transform parent, float bottomOffset, float height, float sidePad = 20f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, bottomOffset);
        rt.sizeDelta = new Vector2(-sidePad * 2, height);
        return rt;
    }

    /// <summary>Область-заливка между отступом сверху и отступом снизу (оба — от краёв родителя).</summary>
    private static RectTransform MakeFillBetween(string name, Transform parent, float topInset, float bottomInset, float sidePad = 20f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(sidePad, bottomInset);
        rt.offsetMax = new Vector2(-sidePad, -topInset);
        return rt;
    }

    private static GameObject MakeScrollList(string name, Transform parent, out Transform content)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = ContentAreaBg;
        var scroll = go.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        MakeStretchRT(viewport);
        viewport.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewport.transform, false);
        var crt = contentGo.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2;
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

    /// <summary>Короткая подпись фиксированной ширины перед рядом кнопок ("Показать:",
    /// "Писать в:") — в отличие от MakeLabel не растягивается на 300px, не съедает строку.</summary>
    private static TextMeshProUGUI MakeSmallLabel(string name, Transform parent, string text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 30);
        var lbl = go.AddComponent<TextMeshProUGUI>();
        lbl.text = text;
        lbl.fontSize = 16;
        lbl.color = RowLabelColor;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 100;
        le.preferredHeight = 30;
        return lbl;
    }

    private static Button MakeButton(string name, Transform parent, string label,
        float fixedWidth = 0, float height = 70)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(fixedWidth > 0 ? fixedWidth : 200, height);
        go.AddComponent<Image>().color = ChromeButtonBg;
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
