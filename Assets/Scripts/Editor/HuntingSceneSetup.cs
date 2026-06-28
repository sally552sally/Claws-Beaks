// Положить в Assets/Editor/HuntingSceneSetup.cs
// После запуска — удалить файл (или оставить, в билд не попадёт).

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Редакторский скрипт для настройки Panel_Hunting.
/// Запуск: верхнее меню → MMORPG → Настроить Panel_Hunting.
///
/// Что делает:
///   1. Удаляет Scrollbar Horizontal / Vertical
///   2. Включает оба направления прокрутки в ScrollRect
///   3. Убирает ContentSizeFitter и VerticalLayoutGroup с Content
///   4. Задаёт Content фиксированный размер 1400×2400 (скроллится в обе стороны)
///   5. Настраивает MobsArea: 1400×1400, тёмный Image-плейсхолдер, anchor top-left
///   6. Настраивает PlayersSection: под MobsArea, VerticalLayoutGroup + ContentSizeFitter
///   7. Переносит PopupPlayerContext в основной Canvas (не Zenject)
///   8. Создаёт Overlay_ContextMenu в Canvas (прозрачная кнопка на весь экран)
/// </summary>
public static class HuntingSceneSetup
{
    [MenuItem("MMORPG/Настроить Panel_Hunting")]
    public static void Setup()
    {
        // ── Найти нужные объекты ─────────────────────────────────────────────

        var canvas = FindMainCanvas();
        if (canvas == null)
        {
            Debug.LogError("[HuntingSetup] Не найден Canvas в сцене. Убедись что сцена Game открыта.");
            return;
        }

        var panelHunting = FindInScene("PanelHunting");
        if (panelHunting == null)
        {
            Debug.LogError("[HuntingSetup] Не найден PanelHunting.");
            return;
        }

        var scrollRectTr = panelHunting.transform.Find("ScrollRectHunting");
        if (scrollRectTr == null)
        {
            Debug.LogError("[HuntingSetup] Не найден ScrollRectHunting внутри PanelHunting.");
            return;
        }

        var viewportTr = scrollRectTr.Find("Viewport");
        var contentTr  = viewportTr?.Find("Content");
        if (contentTr == null)
        {
            Debug.LogError("[HuntingSetup] Не найден Viewport/Content.");
            return;
        }

        var mobsAreaTr      = contentTr.Find("MobsArea");
        var playersSectionTr = contentTr.Find("PlayersSection");
        if (mobsAreaTr == null || playersSectionTr == null)
        {
            Debug.LogError("[HuntingSetup] Не найдены MobsArea или PlayersSection внутри Content.");
            return;
        }

        // ── 1. Удалить Scrollbar'ы ───────────────────────────────────────────

        DestroyChild(scrollRectTr, "Scrollbar Horizontal");
        DestroyChild(scrollRectTr, "Scrollbar Vertical");

        // ── 2. ScrollRect — оба направления ──────────────────────────────────

        var scrollRect = scrollRectTr.GetComponent<ScrollRect>();
        scrollRect.horizontal             = true;
        scrollRect.vertical               = true;
        scrollRect.movementType           = ScrollRect.MovementType.Clamped;
        scrollRect.horizontalScrollbar    = null;
        scrollRect.verticalScrollbar      = null;

        // ── 3. Content — убрать авто-sizing, задать фиксированный размер ─────

        DestroyComponentIfExists<ContentSizeFitter>(contentTr.gameObject);
        DestroyComponentIfExists<VerticalLayoutGroup>(contentTr.gameObject);

        var contentRt = contentTr.GetComponent<RectTransform>();
        // Якорь — верхний левый угол Viewport
        contentRt.anchorMin        = new Vector2(0f, 1f);
        contentRt.anchorMax        = new Vector2(0f, 1f);
        contentRt.pivot            = new Vector2(0f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        // 1400 по горизонтали (MobsArea), 2400 по вертикали (MobsArea + Players)
        contentRt.sizeDelta        = new Vector2(1400f, 2400f);

        // ── 4. MobsArea — карта охоты 1400×1400 ──────────────────────────────

        DestroyComponentIfExists<LayoutElement>(mobsAreaTr.gameObject);

        var mobsRt = mobsAreaTr.GetComponent<RectTransform>();
        mobsRt.anchorMin        = new Vector2(0f, 1f);
        mobsRt.anchorMax        = new Vector2(0f, 1f);
        mobsRt.pivot            = new Vector2(0f, 1f);
        mobsRt.anchoredPosition = Vector2.zero;
        mobsRt.sizeDelta        = new Vector2(1400f, 1400f);

        // Плейсхолдер фона (тёмный) — потом заменишь на арт
        var mobsImg = GetOrAdd<Image>(mobsAreaTr.gameObject);
        mobsImg.color = new Color(0.08f, 0.10f, 0.08f, 1f);

        // ── 5. PlayersSection — под MobsArea ─────────────────────────────────

        var playersSectionRt = playersSectionTr.GetComponent<RectTransform>();
        playersSectionRt.anchorMin        = new Vector2(0f, 1f);
        playersSectionRt.anchorMax        = new Vector2(0f, 1f);
        playersSectionRt.pivot            = new Vector2(0f, 1f);
        // Сразу под MobsArea (y = -1400)
        playersSectionRt.anchoredPosition = new Vector2(0f, -1400f);
        playersSectionRt.sizeDelta        = new Vector2(1400f, 0f);

        // VerticalLayoutGroup на PlayersSection
        var pVlg = GetOrAdd<VerticalLayoutGroup>(playersSectionTr.gameObject);
        pVlg.padding               = new RectOffset(20, 20, 20, 20);
        pVlg.spacing               = 10f;
        pVlg.childControlWidth     = true;
        pVlg.childControlHeight    = false;
        pVlg.childForceExpandWidth = true;
        pVlg.childForceExpandHeight = false;
        pVlg.childAlignment        = TextAnchor.UpperLeft;

        // ContentSizeFitter на PlayersSection — высота растёт по игрокам
        var pCsf = GetOrAdd<ContentSizeFitter>(playersSectionTr.gameObject);
        pCsf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        pCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // ── 6. Overlay_ContextMenu — прозрачная кнопка в Canvas ───────────────

        // Удалить старый если есть
        var oldOverlay = canvas.transform.Find("Overlay_ContextMenu");
        if (oldOverlay != null)
            Object.DestroyImmediate(oldOverlay.gameObject);

        var overlayGo = new GameObject("Overlay_ContextMenu");
        overlayGo.transform.SetParent(canvas.transform, false);

        var overlayRt          = overlayGo.AddComponent<RectTransform>();
        overlayRt.anchorMin    = Vector2.zero;
        overlayRt.anchorMax    = Vector2.one;
        overlayRt.offsetMin    = Vector2.zero;
        overlayRt.offsetMax    = Vector2.zero;

        var overlayImg   = overlayGo.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0f); // полностью прозрачный

        var overlayBtn        = overlayGo.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;

        overlayGo.SetActive(false);

        // ── 7. PopupPlayerContext — убедиться что в основном Canvas ──────────

        var popupGo = FindInScene("PopupPlayerContext");
        if (popupGo != null && popupGo.transform.parent != canvas.transform)
        {
            popupGo.transform.SetParent(canvas.transform, false);
            Debug.Log("[HuntingSetup] PopupPlayerContext перенесён в основной Canvas.");
        }

        // Overlay должен быть ДО попапа в иерархии (рендерится под ним)
        if (popupGo != null)
        {
            int popupIndex = popupGo.transform.GetSiblingIndex();
            overlayGo.transform.SetSiblingIndex(popupIndex);
        }

        // ── Финал ─────────────────────────────────────────────────────────────

        EditorUtility.SetDirty(canvas);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[HuntingSetup] ✅ Готово!\n" +
                  "Осталось вручную:\n" +
                  "  1. На PopupPlayerContext → ContextMenuPopup → mOverlayButton: перетащи Overlay_ContextMenu\n" +
                  "  2. Сохрани сцену (Ctrl+S)");
    }

    // ── Хелперы ───────────────────────────────────────────────────────────────

    /// <summary>Находит основной Canvas — тот, у которого есть SafeArea.</summary>
    private static Canvas FindMainCanvas()
    {
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.transform.Find("SafeArea") != null)
                return canvas;
        }
        return null;
    }

    private static GameObject FindInScene(string name)
    {
        return GameObject.Find(name);
    }

    private static void DestroyChild(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        if (child != null)
        {
            Object.DestroyImmediate(child.gameObject);
            Debug.Log($"[HuntingSetup] Удалён: {childName}");
        }
    }

    private static void DestroyComponentIfExists<T>(GameObject go) where T : Component
    {
        var comp = go.GetComponent<T>();
        if (comp != null)
        {
            Object.DestroyImmediate(comp);
            Debug.Log($"[HuntingSetup] Удалён компонент {typeof(T).Name} с {go.name}");
        }
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var comp = go.GetComponent<T>();
        if (comp == null)
            comp = go.AddComponent<T>();
        return comp;
    }
}
