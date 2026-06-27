using System;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// Расширения для uGUI-компонентов.
/// Позволяют подписываться на события кнопок через IDisposable + DisposeWhenLifeEnded.
/// </summary>
public static class UIExtensions
{
    /// <summary>
    /// Подписаться на нажатие кнопки. Возвращает IDisposable для автоотписки.
    /// 
    /// Использование:
    /// <code>
    /// mLoginButton.SubscribeOnClick(OnLoginClicked).DisposeWhenLifeEnded(this);
    /// </code>
    /// </summary>
    public static IDisposable SubscribeOnClick(this Button button, UnityAction handler)
    {
        button.onClick.AddListener(handler);
        return new ActionDisposable(() => button.onClick.RemoveListener(handler));
    }
}
