using System;

/// <summary>
/// Тип уведомления. Определяет только визуальное оформление (акцентный цвет полосы у тоста,
/// цвет заголовка у диалога) — на поведение очередей не влияет.
/// </summary>
public enum NotificationType
{
    Info,
    Message,
    Warning,
    Error
}

/// <summary>
/// Данные одного тоста в очереди.
/// Тост — короткое авто-скрывающееся сообщение снизу экрана. Может иметь ОДНУ опциональную
/// кнопку-действие (например «Отменить», «Повторить»); если действие не задано — тост просто
/// исчезает по таймеру или по тапу.
/// </summary>
public sealed class ToastRequest
{
    /// <summary>Текст сообщения.</summary>
    public string Message { get; }

    /// <summary>Тип (цвет акцента).</summary>
    public NotificationType Type { get; }

    /// <summary>Подпись кнопки-действия. null/пусто — кнопки нет, обычный авто-тост.</summary>
    public string ActionLabel { get; }

    /// <summary>Колбэк кнопки-действия. Может быть null (передаётся вызывающим кодом).</summary>
    public Action OnAction { get; }

    public ToastRequest(string message, NotificationType type, string actionLabel = null, Action onAction = null)
    {
        Message = message;
        Type = type;
        ActionLabel = actionLabel;
        OnAction = onAction;
    }

    /// <summary>Есть ли у тоста кнопка-действие.</summary>
    public bool HasAction => !string.IsNullOrEmpty(ActionLabel) && OnAction != null;
}

/// <summary>
/// Данные одного диалога в очереди.
/// Диалог — модальное окно по центру с 1-2 кнопками, блокирует остальной UI до ответа.
/// Кнопки задаются через колбэки (любой может быть null — тогда нажатие просто закрывает диалог).
/// </summary>
public sealed class DialogRequest
{
    /// <summary>Заголовок. null/пусто — строка заголовка скрывается.</summary>
    public string Title { get; }

    /// <summary>Текст сообщения.</summary>
    public string Message { get; }

    /// <summary>Тип (цвет заголовка).</summary>
    public NotificationType Type { get; }

    /// <summary>Подпись основной (правой) кнопки — «ОК», «Да», «Удалить» и т.п.</summary>
    public string PrimaryLabel { get; }

    /// <summary>Колбэк основной кнопки. Может быть null.</summary>
    public Action OnPrimary { get; }

    /// <summary>Подпись вторичной (левой) кнопки. null/пусто — кнопка скрыта (диалог-уведомление с одной кнопкой).</summary>
    public string SecondaryLabel { get; }

    /// <summary>Колбэк вторичной кнопки. Может быть null.</summary>
    public Action OnSecondary { get; }

    /// <summary>
    /// Если true — диалог не сбрасывается при смене сцены (например Auth → Game).
    /// По умолчанию false: диалог принадлежит текущему экрану и уходит вместе с ним.
    /// Сам сервис живёт на ProjectContext, поэтому очередь переживает смену сцены в любом случае —
    /// этот флаг лишь решает, снимать ли конкретный показанный диалог при переходе.
    /// </summary>
    public bool PersistAcrossScenes { get; }

    public DialogRequest(
        string title,
        string message,
        NotificationType type,
        string primaryLabel,
        Action onPrimary,
        string secondaryLabel,
        Action onSecondary,
        bool persistAcrossScenes)
    {
        Title = title;
        Message = message;
        Type = type;
        PrimaryLabel = primaryLabel;
        OnPrimary = onPrimary;
        SecondaryLabel = secondaryLabel;
        OnSecondary = onSecondary;
        PersistAcrossScenes = persistAcrossScenes;
    }

    /// <summary>Есть ли вторичная кнопка (два варианта ответа против одного «ОК»).</summary>
    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLabel);
}
