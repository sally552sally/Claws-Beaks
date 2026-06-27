using System;

/// <summary>
/// Реактивное значение типа T.
/// При изменении Value автоматически уведомляет всех подписчиков.
/// 
/// Presenter объявляет приватный Reactive и открывает ReadonlyReactive:
/// <code>
/// private readonly Reactive&lt;bool&gt; mIsLoading = new(false);
/// public ReadonlyReactive&lt;bool&gt; IsLoading => mIsLoading.Readonly;
/// 
/// // Изменение только внутри Presenter:
/// mIsLoading.Value = true;
/// </code>
/// </summary>
public class Reactive<T> : DisposableObject
{
    private T mValue;
    private Action<T> mValueChanged;

    /// <summary>
    /// Текущее значение. При изменении уведомляет подписчиков.
    /// Если значение не изменилось — подписчики не вызываются.
    /// </summary>
    public T Value
    {
        get => mValue;
        set
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(Reactive<T>));

            if (Equals(mValue, value)) return;

            mValue = value;
            mValueChanged?.Invoke(mValue);
        }
    }

    /// <summary>
    /// Readonly-представление для передачи во View.
    /// View подписывается, но не может изменить значение.
    /// </summary>
    public ReadonlyReactive<T> Readonly { get; }

    public Reactive()
    {
        Readonly = new ReadonlyReactive<T>(this);
    }

    public Reactive(T initialValue) : this()
    {
        mValue = initialValue;
    }

    /// <summary>
    /// Подписаться на изменение значения.
    /// </summary>
    /// <param name="handler">Обработчик нового значения.</param>
    /// <param name="callOnSubscribe">
    /// Если true (по умолчанию) — handler вызывается сразу с текущим значением.
    /// Позволяет View инициализироваться без отдельного вызова.
    /// </param>
    /// <returns>IDisposable для отписки. Используй с DisposeWhenLifeEnded.</returns>
    public IDisposable SubscribeOnValueChanged(Action<T> handler, bool callOnSubscribe = true)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(Reactive<T>));

        if (callOnSubscribe)
            handler(mValue);

        mValueChanged += handler;
        return new ActionDisposable(() => mValueChanged -= handler);
    }

    /// <summary>Shortcut для Value = value. Удобен как делегат: someEvent += reactive.SetValue.</summary>
    public void SetValue(T value) => Value = value;

    protected override void OnDispose()
    {
        mValueChanged = null;
        Readonly.Dispose();
        base.OnDispose();
    }

    public override string ToString() => mValue?.ToString();
}
