using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Контекстное меню действий над игроком.
/// Один экземпляр на сцену — прямой дочерний объект основного Canvas.
///
/// Overlay — отдельный прозрачный GameObject на уровне Canvas (не дочерний попапу).
/// При показе: включаем Overlay + сам попап.
/// При скрытии: выключаем оба.
/// Overlay перехватывает тап мимо попапа → закрывает меню.
///
/// GameObject: PopupPlayerContext
/// Overlay:    Overlay_ContextMenu (дочерний Canvas, перед PopupPlayerContext)
/// </summary>
public class ContextMenuPopup : MonoBehaviour
{
    [Header("Заголовок")]
    [SerializeField] private TMP_Text mPlayerNameLabel;

    [Header("Кнопки действий")]
    [SerializeField] private Button mProfileButton;
    [SerializeField] private Button mMessageButton;
    [SerializeField] private Button mInviteButton;

    [Header("Оверлей (внешний объект в Canvas)")]
    /// <summary>
    /// Прозрачная кнопка на весь экран — дочерний Canvas, не попапа.
    /// Тап по нему → Hide(). Создаётся скриптом HuntingSceneSetup.
    /// </summary>
    [SerializeField] private Button mOverlayButton;

    private RectTransform mSelfRect;
    private RectTransform mCanvasRect;

    private void Awake()
    {
        mSelfRect = GetComponent<RectTransform>();
        mCanvasRect = GetComponentInParent<Canvas>().GetComponent<RectTransform>();

        mProfileButton.onClick.AddListener(OnProfileClicked);
        mMessageButton.onClick.AddListener(OnMessageClicked);
        mInviteButton.onClick.AddListener(OnInviteClicked);
        mOverlayButton.onClick.AddListener(Hide);

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        mProfileButton.onClick.RemoveListener(OnProfileClicked);
        mMessageButton.onClick.RemoveListener(OnMessageClicked);
        mInviteButton.onClick.RemoveListener(OnInviteClicked);
        mOverlayButton.onClick.RemoveListener(Hide);
    }

    // ─── Публичный API ────────────────────────────────────────────────────────

    /// <summary>
    /// Показать попап у кнопки [i] указанного игрока.
    /// screenPosition — позиция кнопки [i] в экранных координатах.
    /// </summary>
    public void Show(PlayerInLocationDto player, Vector2 screenPosition)
    {
        mPlayerNameLabel.text = player.Nickname;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            mCanvasRect,
            screenPosition,
            cam: null,
            out var localPoint);

        mSelfRect.anchoredPosition = localPoint;

        // Сначала включаем оверлей (он под попапом в иерархии)
        mOverlayButton.gameObject.SetActive(true);
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        mOverlayButton.gameObject.SetActive(false);
    }

    // ─── Обработчики (заглушки) ───────────────────────────────────────────────

    private void OnProfileClicked()
    {
        Debug.Log("[ContextMenuPopup] Профиль — заглушка");
        Hide();
    }

    private void OnMessageClicked()
    {
        Debug.Log("[ContextMenuPopup] Написать — заглушка Фазы 5");
        Hide();
    }

    private void OnInviteClicked()
    {
        Debug.Log("[ContextMenuPopup] Пригласить в группу — заглушка Фазы 7");
        Hide();
    }
}
