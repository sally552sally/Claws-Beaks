// Удалить после использования (Assets/Editor/SystemChatSetup.cs)
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor-скрипт под системные сообщения чата.
/// Запуск: MMORPG → Setup → System Chat
///
/// Делает три вещи, которые правкой одних скриптов не решаются:
///
/// 1. ПЕРЕНОСИТ Popup_CombatResult из Panel_Combat на уровень SafeArea. Внутри Panel_Combat
///    окно нельзя открыть из чата в принципе: панель боя выключена, когда боя нет, а
///    SetActive(true) на ребёнке выключенного родителя не делает ничего. Ставит попап
///    последним в порядке — чтобы рисоваться поверх Panel_Chat.
///
/// 2. СОБИРАЕТ сворачиваемую таблицу участников внутри попапа + префаб строки
///    Item_BattleParticipant в скрытом контейнере шаблонов.
///
/// 3. СНИМАЕТ Button с префаба Item_ChatMessage. Строка чата перешла на IPointerClickHandler
///    (кликается только фрагмент), и оставшийся Button ничего не ломает, но подсвечивал бы
///    всю строку при нажатии, обещая клик там, где его нет.
///
/// После запуска — проверь Console, сохрани сцену и удали скрипт.
/// </summary>
public static class SystemChatSetup
{
    private const string TEMPLATES_NAME = "_CombatTemplates";

    private static readonly Color PanelBg = new(0.06f, 0.06f, 0.08f, 0.98f);
    private static readonly Color HeaderBg = new(0.16f, 0.16f, 0.20f);
    private static readonly Color HeaderText = new(0.85f, 0.85f, 0.88f);

    [MenuItem("MMORPG/Setup/System Chat")]
    public static void Setup()
    {
        // ── Канвас и SafeArea ────────────────────────────────────────────────
        // Канвас находим ЧЕРЕЗ View_Location, а не «первый попавшийся Overlay»: в сцене их
        // минимум два (основной + Canvas_Notifications), порядок FindObjectsByType не
        // гарантирован — ровно на этом ChatSetup когда-то плодил дубли чата в чужом канвасе.
        var locationView = Object.FindFirstObjectByType<View_Location>();
        if (locationView == null)
        {
            Debug.LogError("[SystemChatSetup] View_Location не найден. Открой Game-сцену.");
            return;
        }

        var rootCanvas = locationView.GetComponentInParent<Canvas>();
        if (rootCanvas == null)
        {
            Debug.LogError("[SystemChatSetup] Canvas над View_Location не найден.");
            return;
        }

        var safeArea = rootCanvas.transform.Find("SafeArea") ?? rootCanvas.transform;

        // ── 1. Попап результата боя ──────────────────────────────────────────
        var popup = Object.FindFirstObjectByType<Popup_CombatResult>(FindObjectsInactive.Include);
        if (popup == null)
        {
            Debug.LogError("[SystemChatSetup] Popup_CombatResult не найден в сцене. " +
                           "Сначала собери экран боя (MMORPG → Setup → Combat Panel).");
            return;
        }

        var popupGo = popup.gameObject;
        popupGo.transform.SetParent(safeArea, false);
        Stretch(popupGo);
        popupGo.transform.SetAsLastSibling(); // поверх Panel_Chat, который тоже last sibling

        // ВАЖНО: оставляем АКТИВНЫМ. Объект, выключенный на момент сохранения сцены, не
        // получает Awake() при загрузке — подписка на IsOpen, которая и должна его прятать,
        // просто не создастся, и окно залипнет навсегда. SafeAwake сам скроет себя по
        // IsOpen.Value = false. Ровно эта же грабля описана в ChatSetup для Panel_Chat.
        popupGo.SetActive(true);

        var content = popupGo.transform.Find("Panel_ResultContent");
        if (content == null)
        {
            Debug.LogError("[SystemChatSetup] Panel_ResultContent внутри попапа не найден — " +
                           "иерархия отличается от собранной CombatSetup, дальше вручную.");
            return;
        }

        // ── Скрытый контейнер шаблонов ───────────────────────────────────────
        var oldTemplates = safeArea.Find(TEMPLATES_NAME);
        if (oldTemplates != null) Object.DestroyImmediate(oldTemplates.gameObject);

        var templates = new GameObject(TEMPLATES_NAME, typeof(RectTransform));
        templates.transform.SetParent(safeArea, false);
        templates.SetActive(false);

        var rowPrefab = MakeParticipantRow(templates.transform);

        // ── 2. Секция участников ─────────────────────────────────────────────
        var oldSection = content.Find("Panel_Participants");
        if (oldSection != null) Object.DestroyImmediate(oldSection.gameObject);

        var section = new GameObject("Panel_Participants", typeof(RectTransform));
        section.transform.SetParent(content, false);

        var sectionVlg = section.AddComponent<VerticalLayoutGroup>();
        sectionVlg.spacing = 4;
        sectionVlg.childControlWidth = true;
        sectionVlg.childControlHeight = true;
        sectionVlg.childForceExpandWidth = true;
        sectionVlg.childForceExpandHeight = false;

        var sectionFitter = section.AddComponent<ContentSizeFitter>();
        sectionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sectionLe = section.AddComponent<LayoutElement>();
        sectionLe.flexibleWidth = 1;

        // Заголовок-переключатель
        var headerGo = new GameObject("Button_ParticipantsHeader", typeof(RectTransform));
        headerGo.transform.SetParent(section.transform, false);
        var headerImg = headerGo.AddComponent<Image>();
        headerImg.color = HeaderBg;
        var headerButton = headerGo.AddComponent<Button>();
        var headerLe = headerGo.AddComponent<LayoutElement>();
        headerLe.preferredHeight = 52;
        headerLe.flexibleWidth = 1;

        var headerLabel = MakeLabel("Label_ParticipantsHeader", headerGo.transform,
            "► Участники (0)", 20, HeaderText, TextAlignmentOptions.Left);
        Stretch(headerLabel.gameObject);
        headerLabel.margin = new Vector4(12, 0, 12, 0);

        // Тело, которое сворачивается
        var bodyGo = new GameObject("Panel_ParticipantsBody", typeof(RectTransform));
        bodyGo.transform.SetParent(section.transform, false);

        var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
        bodyVlg.spacing = 2;
        bodyVlg.padding = new RectOffset(8, 8, 6, 6);
        bodyVlg.childControlWidth = true;
        bodyVlg.childControlHeight = true;
        bodyVlg.childForceExpandWidth = true;
        bodyVlg.childForceExpandHeight = false;

        var bodyFitter = bodyGo.AddComponent<ContentSizeFitter>();
        bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bodyLe = bodyGo.AddComponent<LayoutElement>();
        bodyLe.flexibleWidth = 1;

        bodyGo.SetActive(false); // таблица открывается свёрнутой — см. BattleReportPresenter.Open

        // Кнопка OK должна остаться последней в колонке
        var okButton = content.Find("Button_OK");
        if (okButton != null) okButton.SetAsLastSibling();

        // ── Назначение SerializeField ────────────────────────────────────────
        {
            var so = new SerializedObject(popup);
            AssignIfPresent(so, "mButtonParticipants", headerButton);
            AssignIfPresent(so, "mLabelParticipantsHeader", headerLabel);
            AssignIfPresent(so, "mParticipantsBody", bodyGo);
            AssignIfPresent(so, "mParticipantsContent", bodyGo.transform);
            AssignIfPresent(so, "mParticipantItemPrefab", rowPrefab);
            so.ApplyModifiedProperties();
            Debug.Log("[SystemChatSetup] Popup_CombatResult: поля таблицы участников назначены. " +
                      "mLabelResult/mLabelDrop/mButtonOk не трогали — они уже были.");
        }

        // GameInstaller: ссылка на попап осталась той же, но объект переехал — переназначаем
        // на всякий случай (Unity сохраняет ссылку при переносе, но лучше явно).
        var installers = Object.FindObjectsByType<GameInstaller>(FindObjectsSortMode.None);
        if (installers.Length > 0)
        {
            var so = new SerializedObject(installers[0]);
            AssignIfPresent(so, "mCombatResultPopup", popup);
            so.ApplyModifiedProperties();
            Debug.Log("[SystemChatSetup] GameInstaller: mCombatResultPopup переназначен.");
        }

        // ── 3. Префаб строки чата: снять Button ──────────────────────────────
        var chatItems = Object.FindObjectsByType<Item_ChatMessage>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var stripped = 0;
        foreach (var item in chatItems)
        {
            var button = item.GetComponent<Button>();
            if (button == null) continue;

            Object.DestroyImmediate(button);
            stripped++;
        }

        Debug.Log(stripped > 0
            ? $"[SystemChatSetup] Item_ChatMessage: Button снят ({stripped} шт.). " +
              "Image остаётся — он и ловит рейкаст для IPointerClickHandler."
            : "[SystemChatSetup] Item_ChatMessage: Button уже снят или префаб не найден.");

        // ── Финал ────────────────────────────────────────────────────────────
        EditorUtility.SetDirty(popupGo);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[SystemChatSetup] ✅ Готово.\n" +
                  "Осталось:\n" +
                  "  1. Проверь в иерархии: Popup_CombatResult теперь под SafeArea, ПОСЛЕ Panel_Chat, активен.\n" +
                  "  2. Сохрани сцену (Ctrl+S).\n" +
                  "  3. Удали Assets/Editor/SystemChatSetup.cs.\n" +
                  "  4. Проверка: сервер с Dev:EnableTestEndpoints=true, Swagger →\n" +
                  "     POST /api/dev/system-message {\"sessionId\":<id реального боя>,\"outcome\":\"loss\"}");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Строка таблицы участников: имя слева, урон справа.</summary>
    private static Item_BattleParticipant MakeParticipantRow(Transform parent)
    {
        var go = new GameObject("Item_BattleParticipant", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 30);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 30;
        le.flexibleWidth = 1;

        var name = MakeLabel("Label_Name", go.transform, "Имя", 18, Color.white, TextAlignmentOptions.Left);
        name.GetComponent<LayoutElement>().flexibleWidth = 1;

        var damage = MakeLabel("Label_Damage", go.transform, "0", 18, Color.white, TextAlignmentOptions.Right);
        var damageLe = damage.GetComponent<LayoutElement>();
        damageLe.preferredWidth = 90;
        damageLe.flexibleWidth = 0;

        var row = go.AddComponent<Item_BattleParticipant>();

        var so = new SerializedObject(row);
        AssignIfPresent(so, "mNameLabel", name);
        AssignIfPresent(so, "mDamageLabel", damage);
        so.ApplyModifiedProperties();

        return row;
    }

    private static TextMeshProUGUI MakeLabel(
        string name, Transform parent, string text, int fontSize, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = align;
        label.raycastTarget = false; // клик ловит подложка строки, а не текст

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize + 8;

        return label;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Назначает поле, если оно существует. Без проверки FindProperty вернул бы null и
    /// скрипт падал бы с NullReference на первом же переименованном поле — а он одноразовый,
    /// и разбираться в его трейсе никто не должен.
    /// </summary>
    private static void AssignIfPresent(SerializedObject so, string propertyName, Object value)
    {
        var property = so.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"[SystemChatSetup] Поле «{propertyName}» не найдено у {so.targetObject.GetType().Name} — назначь вручную.");
            return;
        }

        property.objectReferenceValue = value;
    }
}
#endif
