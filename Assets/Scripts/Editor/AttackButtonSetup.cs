// Положить в Assets/Editor/AttackButtonSetup.cs
// После запуска — удалить файл (или оставить, в билд не попадёт).

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Редакторский скрипт для добавления кнопки «Атаковать» в PopupPlayerContext
/// (ContextMenuPopup). Запуск: верхнее меню → MMORPG → Настроить кнопку Атаковать.
///
/// Почему клонирует, а не создаёт с нуля: точных координат/стиля кнопок в этой сцене
/// у меня нет (нет доступа к .unity-файлу, только к скриптам) — поэтому вместо
/// угадывания RectTransform беру уже существующую кнопку «Написать» как шаблон:
/// клон гарантированно наследует её стиль, шрифт, LayoutElement и корректно
/// встраивается в тот же LayoutGroup, если он есть.
///
/// Что делает:
///   1. Находит PopupPlayerContext в сцене и его компонент ContextMenuPopup.
///   2. Через SerializedObject читает уже подключённое поле mMessageButton —
///      это и есть шаблон для клонирования.
///   3. Клонирует его как "Button_Attack", ставит сразу после шаблона по иерархии.
///   4. Меняет текст на «Атаковать» (первый найденный TMP_Text на клоне).
///   5. Снимает с клона унаследованные onClick-подписки (на всякий случай —
///      обычно там пусто, т.к. AddListener у ContextMenuPopup идёт в Awake в рантайме).
///   6. Подключает клон в новое сериализуемое поле mAttackButton.
/// </summary>
public static class AttackButtonSetup
{
    [MenuItem("MMORPG/Настроить кнопку Атаковать (PopupPlayerContext)")]
    public static void Setup()
    {
        var popupGo = GameObject.Find("PopupPlayerContext");
        if (popupGo == null)
        {
            Debug.LogError("[AttackButtonSetup] Не найден PopupPlayerContext в сцене. Открыта сцена Game?");
            return;
        }

        var popup = popupGo.GetComponent<ContextMenuPopup>();
        if (popup == null)
        {
            Debug.LogError("[AttackButtonSetup] На PopupPlayerContext нет компонента ContextMenuPopup.");
            return;
        }

        var so = new SerializedObject(popup);
        var messageProp = so.FindProperty("mMessageButton");
        var attackProp = so.FindProperty("mAttackButton");

        if (messageProp == null || attackProp == null)
        {
            Debug.LogError("[AttackButtonSetup] Не найдены поля mMessageButton/mAttackButton в " +
                            "ContextMenuPopup — убедись, что скрипт пересобран (mAttackButton добавлено кодом) " +
                            "и Unity успела перекомпилировать перед запуском этого меню.");
            return;
        }

        var messageButton = messageProp.objectReferenceValue as Button;
        if (messageButton == null)
        {
            Debug.LogError("[AttackButtonSetup] Поле mMessageButton пустое в инспекторе PopupPlayerContext — " +
                            "клонировать нечего. Подключи кнопку «Написать» вручную и запусти скрипт снова.");
            return;
        }

        if (attackProp.objectReferenceValue != null)
        {
            Debug.Log("[AttackButtonSetup] mAttackButton уже подключено — повторный запуск не нужен.");
            return;
        }

        // ── Клонируем кнопку «Написать» как шаблон ───────────────────────────
        var clone = Object.Instantiate(messageButton.gameObject, messageButton.transform.parent);
        clone.name = "Button_Attack";
        clone.transform.SetSiblingIndex(messageButton.transform.GetSiblingIndex() + 1);

        var label = clone.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = "Атаковать";

        var cloneButton = clone.GetComponent<Button>();
        cloneButton.onClick.RemoveAllListeners();

        attackProp.objectReferenceValue = cloneButton;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(popupGo);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[AttackButtonSetup] ✅ Готово! Кнопка «Атаковать» создана рядом с «Написать» и " +
                  "подключена в mAttackButton.\n" +
                  "Осталось вручную:\n" +
                  "  1. Проверь глазами позицию/цвет клона в Scene View (клон — точная копия «Написать»,\n" +
                  "     если у вас разные стили кнопок в меню — поправь вручную).\n" +
                  "  2. Сохрани сцену (Ctrl+S).\n" +
                  "  Если PopupPlayerContext — инстанс префаба, тот же результат стоит закрепить\n" +
                  "  через Overrides → Apply, либо прогнать скрипт на самом ассете префаба.");
    }
}
