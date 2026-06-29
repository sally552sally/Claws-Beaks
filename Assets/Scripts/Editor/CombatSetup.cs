// Удалить после использования (Assets/Editor/CombatSetup.cs)
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor-скрипт для автосборки UI-иерархии Фазы 3 (Бой).
/// Запуск: MMORPG → Setup → Combat Panel
///
/// Создаёт Panel_Combat и Popup_CombatResult, 
/// автоматически назначает ВСЕ SerializeField через SerializedObject.
/// После запуска проверь Console — выведет список того, что нужно сделать вручную.
/// </summary>
public static class CombatSetup
{
    [MenuItem("MMORPG/Setup/Combat Panel")]
    public static void CreateCombatPanel()
    {
        // ── Найти Canvas / SafeArea ──────────────────────────────────────────
        var allCanvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas rootCanvas = null;
        foreach (var c in allCanvases)
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            { rootCanvas = c; break; }

        if (rootCanvas == null)
        {
            Debug.LogError("[CombatSetup] Canvas (Screen Space Overlay) не найден. Открой Game-сцену.");
            return;
        }

        Transform safeArea = rootCanvas.transform.Find("SafeArea") ?? rootCanvas.transform;

        // ── Удалить старый Panel_Combat если уже есть ────────────────────────
        var existing = safeArea.Find("Panel_Combat");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
            Debug.Log("[CombatSetup] Старый Panel_Combat удалён.");
        }

        // ── Создать Panel_Combat ─────────────────────────────────────────────
        var panelGo = MakeStretchPanel("Panel_Combat", safeArea, new Color(0.08f, 0.08f, 0.10f, 0.97f));
        panelGo.SetActive(false);

        // VerticalLayoutGroup на Panel_Combat
        var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 24, 24);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // ── View_Combat на Panel_Combat ──────────────────────────────────────
        var viewCombat = panelGo.AddComponent<View_Combat>();

        // ── Спиннер ─────────────────────────────────────────────────────────
        var spinner = new GameObject("Spinner_Loading", typeof(RectTransform));
        spinner.transform.SetParent(panelGo.transform, false);
        MakeStretchRT(spinner);
        var spinnerLbl = spinner.AddComponent<TextMeshProUGUI>();
        spinnerLbl.text = "Загрузка...";
        spinnerLbl.fontSize = 28;
        spinnerLbl.alignment = TextAlignmentOptions.Center;
        spinnerLbl.color = Color.white;
        spinner.SetActive(false);

        // ── Секция Header ────────────────────────────────────────────────────
        var header     = MakeHRow("Panel_Header", panelGo.transform, 72);
        var lblTitle   = MakeLabel("Label_Title", header.transform, "⚔ Бой", 26);
        var btnLog     = MakeButton("Button_Log", header.transform, "Лог", fixedWidth: 120, height: 60);

        // ── Секция HUD (HP / Ход / Таймер) ───────────────────────────────────
        var hud        = MakeHRow("Panel_HUD", panelGo.transform, 130);

        var panelPlayer = MakeVGroup("Panel_Player", hud.transform);
        var lblPlayerName = MakeLabel("Label_PlayerName", panelPlayer.transform, "Персонаж", 18);
        var lblPlayerHp   = MakeLabel("Label_PlayerHp",   panelPlayer.transform, "HP: 100/100", 24);

        var panelTurn = MakeVGroup("Panel_TurnInfo", hud.transform);
        var lblTurnStatus = MakeLabel("Label_TurnStatus", panelTurn.transform, "Твой ход", 22);
        var lblTimer      = MakeLabel("Label_Timer",      panelTurn.transform, "30с", 30);

        var panelEnemy = MakeVGroup("Panel_Enemy", hud.transform);
        var lblEnemyName = MakeLabel("Label_EnemyName", panelEnemy.transform, "Враг", 18);
        var lblEnemyHp   = MakeLabel("Label_EnemyHp",   panelEnemy.transform, "HP: 100/100", 24);

        // ── Стойки ───────────────────────────────────────────────────────────
        var stances    = MakeHRow("Panel_Stances", panelGo.transform, 90);
        var btnNormal  = MakeButton("Button_Normal",     stances.transform, "Обычная");
        var btnDef     = MakeButton("Button_Defensive",  stances.transform, "Защита");
        var btnAgg     = MakeButton("Button_Aggressive", stances.transform, "Агрессия");

        // ── Направления ──────────────────────────────────────────────────────
        var dirs       = MakeHRow("Panel_Directions", panelGo.transform, 90);
        var btnHead    = MakeButton("Button_Head", dirs.transform, "Голова");
        var btnBody    = MakeButton("Button_Body", dirs.transform, "Тело");
        var btnLegs    = MakeButton("Button_Legs", dirs.transform, "Ноги");

        // ── Комбо-индикатор ───────────────────────────────────────────────────
        var comboRow   = MakeHRow("Panel_Combo", panelGo.transform, 70);
        var btnPrev    = MakeButton("Button_PrevCombo", comboRow.transform, "◄", fixedWidth: 70);
        var lblCombo   = MakeLabel("Label_ComboSeq", comboRow.transform, "Г  Т  Н", 28);
        var btnNext    = MakeButton("Button_NextCombo", comboRow.transform, "►", fixedWidth: 70);

        var comboIndicator = comboRow.AddComponent<ComboIndicatorView>();

        // ── Пропустить ───────────────────────────────────────────────────────
        var skipRow    = MakeHRow("Panel_Skip", panelGo.transform, 70);
        var btnSkip    = MakeButton("Button_Skip", skipRow.transform, "⏭ Пропустить ход");

        // ── Расходка (4 слота) ────────────────────────────────────────────────
        var slotsRow       = MakeHRow("Panel_Slots", panelGo.transform, 110);
        var (slot0, csvw0) = MakeConsumableSlot("Slot_0", slotsRow.transform);
        var (slot1, csvw1) = MakeConsumableSlot("Slot_1", slotsRow.transform);
        var (slot2, csvw2) = MakeConsumableSlot("Slot_2", slotsRow.transform);
        var (slot3, csvw3) = MakeConsumableSlot("Slot_3", slotsRow.transform);

        // ── Лог (пустой попап — заглушка) ────────────────────────────────────
        var logPopup   = MakeStretchPanel("Popup_Log", panelGo.transform, new Color(0, 0, 0, 0.9f));
        MakeLabel("Label_LogContent", logPopup.transform, "(лог боя — беклог)", 20);
        logPopup.SetActive(false);

        // ── Popup_CombatResult ────────────────────────────────────────────────
        var popupGo    = MakeStretchPanel("Popup_CombatResult", panelGo.transform, new Color(0.06f, 0.06f, 0.08f, 0.98f));
        popupGo.SetActive(false);
        var popupComp  = popupGo.AddComponent<Popup_CombatResult>();

        var popupVG    = MakeVGroup("Panel_ResultContent", popupGo.transform);
        MakeStretchRT(popupVG);
        var vlgResult  = popupVG.GetComponent<VerticalLayoutGroup>() ?? popupVG.AddComponent<VerticalLayoutGroup>();
        vlgResult.childAlignment = TextAnchor.MiddleCenter;
        vlgResult.childControlWidth = true;
        vlgResult.childForceExpandWidth = true;
        vlgResult.spacing = 24;
        vlgResult.padding = new RectOffset(40, 40, 80, 40);

        var lblResult  = MakeLabel("Label_Result",  popupVG.transform, "Победа!",           40);
        var lblDrop    = MakeLabel("Label_Drop",    popupVG.transform, "Дроп: см. инвентарь", 22);
        var btnOk      = MakeButton("Button_OK",    popupVG.transform, "OK");

        // ════════════════════════════════════════════════════════════════════
        // АВТО-НАЗНАЧЕНИЕ SerializeField через SerializedObject
        // ════════════════════════════════════════════════════════════════════

        // ── View_Combat ──────────────────────────────────────────────────────
        {
            var so = new SerializedObject(viewCombat);
            so.FindProperty("mLabelPlayerHp")   .objectReferenceValue = lblPlayerHp;
            so.FindProperty("mLabelPlayerName") .objectReferenceValue = lblPlayerName;
            so.FindProperty("mLabelEnemyHp")    .objectReferenceValue = lblEnemyHp;
            so.FindProperty("mLabelEnemyName")  .objectReferenceValue = lblEnemyName;
            so.FindProperty("mLabelTurnStatus") .objectReferenceValue = lblTurnStatus;
            so.FindProperty("mLabelTimer")      .objectReferenceValue = lblTimer;
            so.FindProperty("mButtonNormal")    .objectReferenceValue = btnNormal;
            so.FindProperty("mButtonDefensive") .objectReferenceValue = btnDef;
            so.FindProperty("mButtonAggressive").objectReferenceValue = btnAgg;
            so.FindProperty("mButtonHead")      .objectReferenceValue = btnHead;
            so.FindProperty("mButtonBody")      .objectReferenceValue = btnBody;
            so.FindProperty("mButtonLegs")      .objectReferenceValue = btnLegs;
            so.FindProperty("mButtonSkip")      .objectReferenceValue = btnSkip;
            so.FindProperty("mComboIndicator")  .objectReferenceValue = comboIndicator;
            so.FindProperty("mSlot0")           .objectReferenceValue = csvw0;
            so.FindProperty("mSlot1")           .objectReferenceValue = csvw1;
            so.FindProperty("mSlot2")           .objectReferenceValue = csvw2;
            so.FindProperty("mSlot3")           .objectReferenceValue = csvw3;
            so.FindProperty("mLogPopup")        .objectReferenceValue = logPopup;
            so.FindProperty("mButtonLog")       .objectReferenceValue = btnLog;
            so.FindProperty("mResultPopup")     .objectReferenceValue = popupComp;
            so.FindProperty("mSpinner")         .objectReferenceValue = spinner;
            so.ApplyModifiedProperties();
            Debug.Log("[CombatSetup] View_Combat: все поля назначены.");
        }

        // ── ComboIndicatorView ───────────────────────────────────────────────
        {
            var so = new SerializedObject(comboIndicator);
            so.FindProperty("mLabelSequence").objectReferenceValue = lblCombo;
            so.FindProperty("mButtonPrev")   .objectReferenceValue = btnPrev;
            so.FindProperty("mButtonNext")   .objectReferenceValue = btnNext;
            so.ApplyModifiedProperties();
            Debug.Log("[CombatSetup] ComboIndicatorView: все поля назначены.");
        }

        // ── Popup_CombatResult ────────────────────────────────────────────────
        {
            var so = new SerializedObject(popupComp);
            so.FindProperty("mLabelResult").objectReferenceValue = lblResult;
            so.FindProperty("mLabelDrop")  .objectReferenceValue = lblDrop;
            so.FindProperty("mButtonOk")   .objectReferenceValue = btnOk;
            so.ApplyModifiedProperties();
            Debug.Log("[CombatSetup] Popup_CombatResult: все поля назначены.");
        }

        // ── GameInstaller (назначить Panel_Combat и Popup) ───────────────────
        var installer = Object.FindObjectsByType<GameInstaller>(FindObjectsSortMode.None);
        if (installer.Length > 0)
        {
            var so = new SerializedObject(installer[0]);
            so.FindProperty("mCombatView")        .objectReferenceValue = viewCombat;
            so.FindProperty("mCombatResultPopup") .objectReferenceValue = popupComp;
            so.ApplyModifiedProperties();
            Debug.Log("[CombatSetup] GameInstaller: mCombatView и mCombatResultPopup назначены.");
        }
        else
        {
            Debug.LogWarning("[CombatSetup] GameInstaller не найден — назначь mCombatView и mCombatResultPopup вручную.");
        }

        // ── Пометить сцену как изменённую ────────────────────────────────────
        EditorUtility.SetDirty(panelGo);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[CombatSetup] ✅ Готово! Panel_Combat создан, все SerializeField назначены.\n" +
                  "Осталось:\n" +
                  "  1. Сохрани сцену (Ctrl+S)\n" +
                  "  2. Удали Assets/Editor/CombatSetup.cs\n" +
                  "  3. Запусти игру и нажми «Атаковать»");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    private static GameObject MakeStretchPanel(string name, Transform parent, Color bg)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        MakeStretchRT(go);
        var img = go.AddComponent<Image>();
        img.color = bg;
        return go;
    }

    private static void MakeStretchRT(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
    }

    /// <summary>Горизонтальная строка с фиксированной высотой.</summary>
    private static GameObject MakeHRow(string name, Transform parent, float height)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing            = 10;
        hlg.childAlignment     = TextAnchor.MiddleCenter;
        hlg.childControlWidth  = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth   = 1;
        return go;
    }

    private static GameObject MakeVGroup(string name, Transform parent)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment     = TextAnchor.MiddleCenter;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.spacing = 4;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        return go;
    }

    private static TextMeshProUGUI MakeLabel(string name, Transform parent, string text, int fontSize = 22)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, fontSize + 10);

        var lbl = go.AddComponent<TextMeshProUGUI>();
        lbl.text      = text;
        lbl.fontSize  = fontSize;
        lbl.color     = Color.white;
        lbl.alignment = TextAlignmentOptions.Center;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize + 10;
        le.flexibleWidth   = 1;
        return lbl;
    }

    private static Button MakeButton(string name, Transform parent, string label,
        float fixedWidth = 0, float height = 70)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(fixedWidth > 0 ? fixedWidth : 200, height);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.22f, 0.22f, 0.26f);

        var btn = go.AddComponent<Button>();

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        if (fixedWidth > 0) le.preferredWidth = fixedWidth;
        else                le.flexibleWidth  = 1;

        MakeLabel(name + "_Lbl", go.transform, label, 20);
        return btn;
    }

    /// <summary>Один слот расходки: ConsumableSlotView + Button + подписи.</summary>
    private static (GameObject go, ConsumableSlotView csv) MakeConsumableSlot(string name, Transform parent)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(120, 110);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.18f, 0.22f);

        var btn = go.AddComponent<Button>();
        var csv = go.AddComponent<ConsumableSlotView>();

        var le  = go.AddComponent<LayoutElement>();
        le.preferredWidth  = 120;
        le.preferredHeight = 110;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment     = TextAnchor.MiddleCenter;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.spacing = 4;

        var lblCode  = MakeLabel("Label_Code",  go.transform, "—", 18);
        var lblCount = MakeLabel("Label_Count", go.transform, "",  16);

        // Пустой оверлей (серая подложка когда слот пуст)
        var emptyOverlay = new GameObject("Empty_Overlay", typeof(RectTransform));
        emptyOverlay.transform.SetParent(go.transform, false);
        MakeStretchRT(emptyOverlay);
        var emptyImg    = emptyOverlay.AddComponent<Image>();
        emptyImg.color  = new Color(0, 0, 0, 0.45f);
        emptyOverlay.SetActive(true); // по умолчанию слот пустой

        // Назначаем поля ConsumableSlotView
        var so = new SerializedObject(csv);
        so.FindProperty("mButton")       .objectReferenceValue = btn;
        so.FindProperty("mLabelCode")    .objectReferenceValue = lblCode;
        so.FindProperty("mLabelCount")   .objectReferenceValue = lblCount;
        so.FindProperty("mEmptyOverlay") .objectReferenceValue = emptyOverlay;
        so.ApplyModifiedProperties();

        return (go, csv);
    }
}
#endif
