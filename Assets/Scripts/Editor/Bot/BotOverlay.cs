using UnityEngine;

/// <summary>
/// Оверлей «что делает бот» поверх игры (верх экрана) в Play Mode.
/// Снимает эффект «оно работает, но я не вижу как»: всегда видно текущий шаг,
/// круг цикла и деталь (например «Бой с Волком • 3/10»).
///
/// Живёт в Editor-сборке, но это ок: MonoBehaviour из editor-сборки можно создавать
/// в Play Mode внутри редактора (в билд эта сборка не попадает в принципе).
/// Объект скрыт из иерархии и не сохраняется (HideAndDontSave).
/// </summary>
public sealed class BotOverlay : MonoBehaviour
{
    private static BotOverlay sInstance;
    private static string sText = "";

    private GUIStyle mStyle;

    /// <summary>Показать оверлей (создаёт скрытый GameObject, если ещё нет).</summary>
    public static void Show()
    {
        if (sInstance != null) return;
        if (!Application.isPlaying) return;

        var go = new GameObject("~BotOverlay") { hideFlags = HideFlags.HideAndDontSave };
        sInstance = go.AddComponent<BotOverlay>();
    }

    /// <summary>Спрятать и удалить оверлей.</summary>
    public static void Hide()
    {
        if (sInstance == null) return;
        Destroy(sInstance.gameObject);
        sInstance = null;
        sText = "";
    }

    /// <summary>Обновить текст (пустой текст — плашка не рисуется).</summary>
    public static void SetText(string text) => sText = text ?? "";

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(sText)) return;

        // Стиль создаём лениво: GUI.skin доступен только внутри OnGUI.
        mStyle ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
            richText = false,
            padding = new RectOffset(14, 14, 8, 8),
            normal = { textColor = Color.white }
        };

        var content = new GUIContent(sText);
        var size = mStyle.CalcSize(content);
        var rect = new Rect((Screen.width - size.x) / 2f, 10f, size.x, size.y);

        GUI.Box(rect, content, mStyle);
    }
}
