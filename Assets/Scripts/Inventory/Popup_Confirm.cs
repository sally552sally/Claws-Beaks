using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Универсальный попап подтверждения (Popup_Confirm).
/// Используется для выброса предмета (и при желании для других необратимых действий).
///
/// Не знает о бизнес-логике — принимает текст и колбэк подтверждения.
/// GameObject: Popup_Confirm (отдельный, по умолчанию выключен).
/// </summary>
public sealed class Popup_Confirm : MonoBehaviour
{
    [SerializeField] private TMP_Text mLabelMessage;
    [SerializeField] private Button   mButtonConfirm;
    [SerializeField] private Button   mButtonCancel;

    private Action mOnConfirm;

    private void Awake()
    {
        if (mButtonConfirm != null) mButtonConfirm.onClick.AddListener(OnConfirm);
        if (mButtonCancel  != null) mButtonCancel.onClick.AddListener(Hide);
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (mButtonConfirm != null) mButtonConfirm.onClick.RemoveListener(OnConfirm);
        if (mButtonCancel  != null) mButtonCancel.onClick.RemoveListener(Hide);
    }

    /// <summary>Показать подтверждение с сообщением и действием при «Да».</summary>
    public void Show(string message, Action onConfirm)
    {
        mOnConfirm = onConfirm;
        if (mLabelMessage != null) mLabelMessage.text = message;
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    private void OnConfirm()
    {
        var cb = mOnConfirm;
        mOnConfirm = null;
        Hide();
        cb?.Invoke();
    }
}
