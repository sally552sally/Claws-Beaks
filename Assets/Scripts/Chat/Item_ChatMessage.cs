using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Строка одного сообщения в общем чат-логе (Panel_Chat). ПЕРЕИСПОЛЬЗУЕТСЯ через
/// IViewPool&lt;Item_ChatMessage&gt; — не создаётся/уничтожается на каждое сообщение
/// (см. View_Chat.RebuildMessages). Никаких подписок в Awake: обработчик клика приходит
/// в Setup и читается в момент тапа — безопасно при переиспользовании через пул.
///
/// Клик разбирается ЗДЕСЬ, потому что на одной строке живут два разных действия:
///   — тап по кликабельному фрагменту («[Результат боя]» в системке) → действие строки;
///   — тап в любое место обычной строки → «ответить» (личка отправителю);
///   — тап мимо фрагмента на системной строке → ничего: отправителя нет, отвечать некому.
///
/// Раньше на всю строку висел Button. С «кликается только фрагмент» это несовместимо в
/// принципе: Button не знает, куда именно попал палец, и любая точка системной строки
/// открывала бы окно боя. Поэтому IPointerClickHandler + TMP_TextUtilities.FindIntersectingLink,
/// который умеет ответить, попали ли мы в &lt;link&gt; внутри текста.
///
/// Prefab: Item_ChatMessage
/// </summary>
public class Item_ChatMessage : MonoBehaviour, IPointerClickHandler
{
    /// <summary>Id TMP-линка. Значение неважно — линк на строке ровно один, ищем факт попадания.</summary>
    private const string LINK_ID = "action";

    [SerializeField] private TMP_Text mTimeLabel;
    [SerializeField] private TMP_Text mChannelTagLabel; // "Торг"/"Личка"/"Система" — скрыт, если тега нет (Локация)
    [SerializeField] private TMP_Text mBodyLabel;       // "Ник: текст" / "Вы → Ник: текст" / текст системки

    private ChatDisplayLine mLine;
    private Action<ChatDisplayLine> mOnReply;
    private Action<ChatDisplayLine> mOnAction;

    /// <summary>
    /// Заполняет строку данными.
    /// </summary>
    /// <param name="line">Что показываем.</param>
    /// <param name="channelTag">Тег канала. null/пусто — тега нет (Локация).</param>
    /// <param name="accentColor">Цвет тега (резолвит View через ChatConfig.ColorFor).</param>
    /// <param name="onReply">Тап по обычной строке — «ответить».</param>
    /// <param name="onAction">Тап по кликабельному фрагменту.</param>
    public void Setup(
        ChatDisplayLine line, string channelTag, Color accentColor,
        Action<ChatDisplayLine> onReply, Action<ChatDisplayLine> onAction)
    {
        mLine = line;
        mOnReply = onReply;
        mOnAction = onAction;

        if (mTimeLabel != null)
            mTimeLabel.text = line.SentAtUtc.ToLocalTime().ToString("HH:mm");

        if (mChannelTagLabel != null)
        {
            var hasTag = !string.IsNullOrEmpty(channelTag);
            mChannelTagLabel.gameObject.SetActive(hasTag);
            if (hasTag)
            {
                mChannelTagLabel.text = channelTag;
                mChannelTagLabel.color = accentColor;
            }
        }

        if (mBodyLabel != null)
            mBodyLabel.text = BuildBodyMarkup(line);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (mLine == null) return;

        // Действие проверяем ПЕРВЫМ: попадание во фрагмент важнее общего тапа по строке.
        if (mLine.HasClickableAction && mBodyLabel != null)
        {
            // pressEventCamera у Screen Space Overlay равен null — это штатно, TMP такой
            // случай обрабатывает и считает координаты прямо в экранных.
            var linkIndex = TMP_TextUtilities.FindIntersectingLink(
                mBodyLabel, eventData.position, eventData.pressEventCamera);

            if (linkIndex >= 0)
            {
                mOnAction?.Invoke(mLine);
                return;
            }
        }

        // Мимо фрагмента. У системной строки отправителя нет — отвечать некому, и это не
        // «вызвали и молча упало»: ветки для неё просто не существует.
        if (!mLine.CanReply) return;

        mOnReply?.Invoke(mLine);
    }

    /// <summary>
    /// Оборачивает кликабельный фрагмент в TMP-линк с подчёркиванием. Разметку накладывает
    /// КЛИЕНТ: сервер намеренно шлёт чистый текст и отдельно подстроку — иначе серверу
    /// пришлось бы знать про TMP, а веб-клиенту разбирать чужую разметку.
    ///
    /// Если фрагмент в тексте не нашёлся, отдаём текст как есть. Сервер такого не пришлёт
    /// (он снимает действие сам, когда подстроки нет), но подчёркивать наугад хуже, чем
    /// не подчеркнуть.
    /// </summary>
    private static string BuildBodyMarkup(ChatDisplayLine line)
    {
        if (!line.HasClickableAction) return line.BodyText;
        if (string.IsNullOrEmpty(line.BodyText)) return line.BodyText;

        var index = line.BodyText.IndexOf(line.ActionLinkText, StringComparison.Ordinal);
        if (index < 0) return line.BodyText;

        var before = line.BodyText[..index];
        var after = line.BodyText[(index + line.ActionLinkText.Length)..];

        return $"{before}<link=\"{LINK_ID}\"><u>{line.ActionLinkText}</u></link>{after}";
    }
}
