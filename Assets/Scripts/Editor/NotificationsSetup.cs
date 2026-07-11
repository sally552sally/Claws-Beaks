// Удалить после использования (Assets/Scripts/Editor/NotificationsSetup.cs)
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor-скрипт автосборки Canvas_Notifications (Фаза 5).
/// Запуск: MMORPG → Setup → Notifications Canvas (в каждой сцене: Auth и Game).
///
/// Создаёт отдельный Canvas (Sort Order 20 — выше Popup_Ban и попапов инвентаря):
///   Panel_Toast  — тост снизу экрана (акцент-полоса по типу, текст, тап-дисмисс,
///                  опциональная кнопка-действие);
///   Panel_Dialog — модальное окно по центру (лёгкий блокер + бокс: заголовок, сообщение,
///                  основная и вторичная кнопки).
/// Назначает все SerializeField у View_Notifications и биндит его в инсталлере сцены
/// (AuthInstaller.mNotificationsView / GameInstaller.mNotificationsView).
///
/// Вёрстка — на явных якорях, без вложенных LayoutGroup (тот же принцип, что в InventorySetup:
/// вложенные LayoutGroup давали визуальные баги).
///
/// После запуска в обеих сценах — проверь Console, назначь NotificationConfig и удали скрипт.
/// </summary>
public static class NotificationsSetup
{
    private const string CANVAS_NAME = "Canvas_Notifications";
    private const int SORT_ORDER = 20;

    private static readonly Color ToastBg = new(0.08f, 0.08f, 0.10f, 0.96f);
    private static readonly Color DialogBoxBg = new(0.10f, 0.10f, 0.13f, 1f);
    private static readonly Color ButtonBg = new(0.20f, 0.16f, 0.05f, 1f);   // тёмно-золотой акцент
    private static readonly Color ButtonText = new(0.90f, 0.75f, 0.35f, 1f);

    [MenuItem("MMORPG/Setup/Notifications Canvas")]
    public static void CreateNotificationsCanvas()
    {
        // Снести старое, если запускаем повторно.
        var existing = GameObject.Find(CANVAS_NAME);
        if (existing != null) Object.DestroyImmediate(existing);

        // ── Canvas ────────────────────────────────────────────────────────────
        var canvasGo = new GameObject(CANVAS_NAME,
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SORT_ORDER;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var safeArea = MakeStretch(canvasGo.transform, "SafeArea");
        safeArea.gameObject.AddComponent<SafeAreaAdapter>();

        var view = canvasGo.AddComponent<View_Notifications>();

        var toast = BuildToast(safeArea);
        var dialog = BuildDialog(safeArea);

        AssignFields(view, toast, dialog);
        BindInInstaller(view);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = canvasGo;

        Debug.Log("[NotificationsSetup] ✅ Canvas_Notifications создан.\n" +
                  "Осталось вручную:\n" +
                  "  1. Назначь NotificationConfig.asset в поле View_Notifications.mConfig\n" +
                  "     (если ассета нет — Create → MMORPG → NotificationConfig, положи в Assets/Configs).\n" +
                  "  2. Прогони этот скрипт также во второй сцене (Auth и Game — обе нужны).\n" +
                  "  3. Проверь, что INotificationService/NotificationService и NotificationConfig\n" +
                  "     забиндены в ProjectInstaller (mNotificationConfig — SerializeField).\n" +
                  "  4. Сохрани сцену (Ctrl+S).\n" +
                  "  5. Удали Assets/Scripts/Editor/NotificationsSetup.cs после прогона в обеих сценах.\n" +
                  "  6. Старые Label_Error в этой сцене можно найти через " +
                  "MMORPG → Setup → Find Legacy Error Labels.");
    }

    // ─── Поиск/очистка осиротевших Label_Error ──────────────────────────────────

    /// <summary>
    /// Ищет в открытой сцене GameObject'ы с TMP_Text, на которые больше не ссылается
    /// ни один SerializeField ни у одного MonoBehaviour сцены (значит, поле было удалено
    /// из кода — как mErrorLabel в AuthFormView/View_Location после Фазы 5 — а объект
    /// в сцене остался). НЕ удаляет автоматически — только подсвечивает список и по
    /// подтверждению удаляет отмеченные. Так безопаснее: имена полей/объектов в разных
    /// сценах могли отличаться, слепое совпадение по имени рискованно.
    /// </summary>
    [MenuItem("MMORPG/Setup/Find Legacy Error Labels")]
    public static void FindLegacyErrorLabels()
    {
        var referenced = new HashSet<Object>();
        var allBehaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var mb in allBehaviours)
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            var prop = so.GetIterator();
            while (prop.NextVisible(true))
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null)
                    referenced.Add(prop.objectReferenceValue);
            }
        }

        var candidates = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
        var orphans = new List<GameObject>();
        foreach (var label in candidates)
        {
            bool looksLikeErrorLabel = label.name.IndexOf("Error", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool isReferenced = referenced.Contains(label) || referenced.Contains(label.gameObject)
                                 || referenced.Contains(label.rectTransform);
            if (looksLikeErrorLabel && !isReferenced)
                orphans.Add(label.gameObject);
        }

        if (orphans.Count == 0)
        {
            Debug.Log("[NotificationsSetup] Осиротевших Label_Error-подобных объектов не найдено в этой сцене.");
            return;
        }

        Selection.objects = orphans.ToArray();
        var names = string.Join("\n  ", orphans.ConvertAll(o => GetHierarchyPath(o.transform)));
        Debug.Log($"[NotificationsSetup] Найдено {orphans.Count} потенциально осиротевших объектов " +
                  $"(выделены в Hierarchy):\n  {names}");

        bool delete = EditorUtility.DisplayDialog(
            "Найдены осиротевшие Label_Error",
            $"Найдено {orphans.Count} объектов с текстом «Error» в имени, на которые не ссылается " +
            "ни один SerializeField в сцене (см. Console — путь каждого). Удалить их сейчас?\n\n" +
            "Если сомневаешься — жми «Нет», объекты уже выделены в Hierarchy для ручной проверки.",
            "Удалить", "Нет, проверю сам");

        if (!delete) return;

        foreach (var go in orphans)
            Object.DestroyImmediate(go);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log($"[NotificationsSetup] Удалено {orphans.Count} осиротевших объектов.");
    }

    private static string GetHierarchyPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // ─── Тост ────────────────────────────────────────────────────────────────

    private static ToastRefs BuildToast(Transform parent)
    {
        var root = MakeChild(parent, "Panel_Toast");
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(920, 150);
        rect.anchoredPosition = new Vector2(0, 150);

        var bg = root.gameObject.AddComponent<Image>();
        bg.color = ToastBg;
        root.gameObject.AddComponent<RectMask2D>();

        // Акцент-полоса слева.
        var accent = MakeChild(root, "Image_Accent");
        var aRect = accent.GetComponent<RectTransform>();
        aRect.anchorMin = new Vector2(0f, 0f);
        aRect.anchorMax = new Vector2(0f, 1f);
        aRect.pivot = new Vector2(0f, 0.5f);
        aRect.sizeDelta = new Vector2(12, 0);
        aRect.anchoredPosition = Vector2.zero;
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.color = Color.white;

        // Текст.
        var label = MakeChild(root, "Label_Toast");
        var lRect = label.GetComponent<RectTransform>();
        lRect.anchorMin = new Vector2(0f, 0f);
        lRect.anchorMax = new Vector2(1f, 1f);
        lRect.offsetMin = new Vector2(40, 12);
        lRect.offsetMax = new Vector2(-230, -12);  // место справа под кнопку-действие
        var text = label.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 34;
        text.color = Color.white;
        text.enableWordWrapping = true;
        text.verticalAlignment = VerticalAlignmentOptions.Middle;
        text.text = "Сообщение";

        // Кнопка-действие (справа, по умолчанию скрыта).
        var actionBtn = MakeChild(root, "Button_ToastAction");
        var abRect = actionBtn.GetComponent<RectTransform>();
        abRect.anchorMin = new Vector2(1f, 0.5f);
        abRect.anchorMax = new Vector2(1f, 0.5f);
        abRect.pivot = new Vector2(1f, 0.5f);
        abRect.sizeDelta = new Vector2(190, 90);
        abRect.anchoredPosition = new Vector2(-20, 0);
        var abImg = actionBtn.gameObject.AddComponent<Image>();
        abImg.color = ButtonBg;
        var actionButton = actionBtn.gameObject.AddComponent<Button>();
        var abLabel = MakeCenteredLabel(actionBtn, "Действие", 30, ButtonText);
        actionBtn.gameObject.SetActive(false);

        // Невидимая тап-область на весь тост (для дисмисса). Кладём ПОД кнопку-действие
        // в иерархии, но кнопка-действие визуально/по рейкасту перекрывает её в своей зоне.
        var tapArea = MakeStretch(root, "Button_TapArea");
        var tapImg = tapArea.gameObject.AddComponent<Image>();
        tapImg.color = new Color(0, 0, 0, 0);   // прозрачная, но raycast target = true
        var tapButton = tapArea.gameObject.AddComponent<Button>();
        // tap-area первой по иерархии среди интерактивных → кнопка-действие (позже) выше по рейкасту.
        tapArea.SetSiblingIndex(actionBtn.GetSiblingIndex());

        root.gameObject.SetActive(false);

        return new ToastRefs
        {
            Root = root.gameObject,
            Label = text,
            Accent = accentImg,
            TapButton = tapButton,
            ActionButton = actionButton,
            ActionLabel = abLabel
        };
    }

    // ─── Диалог ──────────────────────────────────────────────────────────────

    private static DialogRefs BuildDialog(Transform parent)
    {
        var root = MakeStretch(parent, "Panel_Dialog");

        // Блокер на весь экран (лёгкое затемнение + перехват кликов = модальность).
        var blocker = MakeStretch(root, "Image_Blocker");
        var blockerImg = blocker.gameObject.AddComponent<Image>();
        blockerImg.color = new Color(0, 0, 0, 0.1f);

        // Бокс по центру.
        var box = MakeChild(root, "Panel_DialogBox");
        var boxRect = box.GetComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0.5f, 0.5f);
        boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.sizeDelta = new Vector2(840, 500);
        boxRect.anchoredPosition = Vector2.zero;
        var boxImg = box.gameObject.AddComponent<Image>();
        boxImg.color = DialogBoxBg;
        box.gameObject.AddComponent<RectMask2D>();

        // Заголовок.
        var title = MakeChild(box, "Label_Title");
        var tRect = title.GetComponent<RectTransform>();
        tRect.anchorMin = new Vector2(0f, 1f);
        tRect.anchorMax = new Vector2(1f, 1f);
        tRect.pivot = new Vector2(0.5f, 1f);
        tRect.sizeDelta = new Vector2(0, 96);
        tRect.anchoredPosition = new Vector2(0, -28);
        var titleText = title.gameObject.AddComponent<TextMeshProUGUI>();
        titleText.fontSize = 42;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.text = "Заголовок";

        // Сообщение.
        var msg = MakeChild(box, "Label_Message");
        var mRect = msg.GetComponent<RectTransform>();
        mRect.anchorMin = new Vector2(0f, 0f);
        mRect.anchorMax = new Vector2(1f, 1f);
        mRect.offsetMin = new Vector2(48, 150);
        mRect.offsetMax = new Vector2(-48, -140);
        var msgText = msg.gameObject.AddComponent<TextMeshProUGUI>();
        msgText.fontSize = 36;
        msgText.color = new Color(0.85f, 0.85f, 0.85f);
        msgText.alignment = TextAlignmentOptions.Center;
        msgText.enableWordWrapping = true;
        msgText.text = "Сообщение диалога";

        // Кнопки (внизу): вторичная слева, основная справа.
        var secondary = MakeDialogButton(box, "Button_Secondary", new Vector2(-150, 40), "Отмена");
        var primary = MakeDialogButton(box, "Button_Primary", new Vector2(150, 40), "ОК");

        root.gameObject.SetActive(false);

        return new DialogRefs
        {
            Root = root.gameObject,
            Blocker = blockerImg,
            Title = titleText,
            Message = msgText,
            PrimaryButton = primary.button,
            PrimaryLabel = primary.label,
            SecondaryButton = secondary.button,
            SecondaryLabel = secondary.label
        };
    }

    private static (Button button, TMP_Text label) MakeDialogButton(
        Transform parent, string name, Vector2 anchoredPos, string caption)
    {
        var go = MakeChild(parent, name);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(260, 100);
        rect.anchoredPosition = anchoredPos;
        var img = go.gameObject.AddComponent<Image>();
        img.color = ButtonBg;
        var button = go.gameObject.AddComponent<Button>();
        var label = MakeCenteredLabel(go, caption, 34, ButtonText);
        return (button, label);
    }

    // ─── Назначение полей / биндинг ─────────────────────────────────────────────

    private static void AssignFields(View_Notifications view, ToastRefs t, DialogRefs d)
    {
        var so = new SerializedObject(view);

        so.FindProperty("mToastRoot").objectReferenceValue = t.Root;
        so.FindProperty("mToastLabel").objectReferenceValue = t.Label;
        so.FindProperty("mToastAccent").objectReferenceValue = t.Accent;
        so.FindProperty("mToastTapArea").objectReferenceValue = t.TapButton;
        so.FindProperty("mToastActionButton").objectReferenceValue = t.ActionButton;
        so.FindProperty("mToastActionLabel").objectReferenceValue = t.ActionLabel;

        so.FindProperty("mDialogRoot").objectReferenceValue = d.Root;
        so.FindProperty("mDialogBlocker").objectReferenceValue = d.Blocker;
        so.FindProperty("mDialogTitleLabel").objectReferenceValue = d.Title;
        so.FindProperty("mDialogMessageLabel").objectReferenceValue = d.Message;
        so.FindProperty("mDialogPrimaryButton").objectReferenceValue = d.PrimaryButton;
        so.FindProperty("mDialogPrimaryLabel").objectReferenceValue = d.PrimaryLabel;
        so.FindProperty("mDialogSecondaryButton").objectReferenceValue = d.SecondaryButton;
        so.FindProperty("mDialogSecondaryLabel").objectReferenceValue = d.SecondaryLabel;

        // mConfig оставляем на ручное назначение (ассет создаётся один раз, см. лог).
        so.ApplyModifiedProperties();
    }

    private static void BindInInstaller(View_Notifications view)
    {
        // Пытаемся назначить mNotificationsView в инсталлере активной сцены.
        var game = Object.FindFirstObjectByType<GameInstaller>();
        if (game != null)
        {
            TryAssign(game, "mNotificationsView", view, "GameInstaller");
            return;
        }

        var auth = Object.FindFirstObjectByType<AuthInstaller>();
        if (auth != null)
        {
            TryAssign(auth, "mNotificationsView", view, "AuthInstaller");
            return;
        }

        Debug.LogWarning("[NotificationsSetup] Инсталлер сцены не найден — назначь " +
                         "mNotificationsView вручную (AuthInstaller или GameInstaller).");
    }

    private static void TryAssign(Object target, string field, Object value, string label)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop != null)
        {
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            Debug.Log($"[NotificationsSetup] {label}.{field} назначен.");
        }
        else
        {
            Debug.LogWarning($"[NotificationsSetup] Поле {field} не найдено в {label} — назначь вручную.");
        }
    }

    // ─── Хелперы вёрстки ────────────────────────────────────────────────────────

    private static RectTransform MakeChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static RectTransform MakeStretch(Transform parent, string name)
    {
        var rect = MakeChild(parent, name);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static TMP_Text MakeCenteredLabel(Transform parent, string text, float size, Color color)
    {
        var rect = MakeStretch(parent, "Label");
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    // ─── Контейнеры ссылок ──────────────────────────────────────────────────────

    private struct ToastRefs
    {
        public GameObject Root;
        public TMP_Text Label;
        public Image Accent;
        public Button TapButton;
        public Button ActionButton;
        public TMP_Text ActionLabel;
    }

    private struct DialogRefs
    {
        public GameObject Root;
        public Image Blocker;
        public TMP_Text Title;
        public TMP_Text Message;
        public Button PrimaryButton;
        public TMP_Text PrimaryLabel;
        public Button SecondaryButton;
        public TMP_Text SecondaryLabel;
    }
}
#endif
