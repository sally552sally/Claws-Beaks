using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Строка одного сообщения в общем чат-логе (Panel_Chat). ПЕРЕИСПОЛЬЗУЕТСЯ через
/// IViewPool&lt;Item_ChatMessage&gt; — не создаётся/уничтожается на каждое сообщение
/// (см. View_Chat.RebuildMessages). Слушатель кнопки вешается один раз в Awake и
/// читает mOnClicked каждый раз заново — безопасно при переиспользовании через пул,
/// повторный Awake после Return→Get не вызывается (GameObject не уничтожается).
///
/// Тап по строке → "ответить" (личка отправителю, см. ChatPresenter.SetPrivateTargetFromLine).
///
/// Prefab: Item_ChatMessage
/// </summary>
public class Item_ChatMessage : MonoBehaviour
{
    [SerializeField] private TMP_Text mTimeLabel;
    [SerializeField] private TMP_Text mChannelTagLabel; // "Торг"/"Личка" — скрыт, если тега нет (Локация)
    [SerializeField] private TMP_Text mBodyLabel;       // "Ник: текст" / "Вы → Ник: текст"
    [SerializeField] private Button mClickArea;         // вся строка кликабельна

    private ChatDisplayLine mLine;
    private Action<ChatDisplayLine> mOnClicked;

    private void Awake()
    {
        if (mClickArea != null)
            mClickArea.onClick.AddListener(OnClicked);
    }

    private void OnDestroy()
    {
        if (mClickArea != null)
            mClickArea.onClick.RemoveListener(OnClicked);
    }

    /// <summary>Заполняет строку данными. channelTag — null/пусто, если тег не нужен (Локация).</summary>
    public void Setup(ChatDisplayLine line, string channelTag, Color accentColor, Action<ChatDisplayLine> onClicked)
    {
        mLine = line;
        mOnClicked = onClicked;

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
            mBodyLabel.text = line.BodyText;
    }

    private void OnClicked() => mOnClicked?.Invoke(mLine);
}
