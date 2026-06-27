using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Модальный попап при бане (403 от сервера или SignalR "Banned").
/// Стартует неактивным. Вызов Show(message) активирует.
/// GameObject: Popup_Ban
/// </summary>
public class BanPopup : DisposableBehaviour
{
    [SerializeField] private TMP_Text mMessageLabel;
    [SerializeField] private Button   mCloseButton;

    protected override void SafeAwake()
    {
        gameObject.SetActive(false);
        mCloseButton.SubscribeOnClick(Hide).DisposeWhenLifeEnded(this);
    }

    /// <summary>Показать попап с текстом бана от сервера.</summary>
    public void Show(string message)
    {
        mMessageLabel.text = message;
        gameObject.SetActive(true);
    }

    private void Hide() => gameObject.SetActive(false);
}
