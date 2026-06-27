using System;

/// <summary>
/// Readonly-представление Reactive&lt;T&gt;.
/// Передаётся из Presenter во View — View может подписаться, но не изменить значение.
/// Время жизни привязано к источнику: при Dispose источника ReadonlyReactive тоже уничтожается.
/// </summary>
public class ReadonlyReactive<T> : DisposableObject
{
    private readonly Reactive<T> mSource;

    /// <summary>Текущее значение источника.</summary>
    public T Value => mSource.Value;

    public ReadonlyReactive(Reactive<T> source)
    {
        mSource = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Подписаться на изменение значения.
    /// </summary>
    /// <param name="handler">Обработчик нового значения.</param>
    /// <param name="callOnSubscribe">
    /// Если true (по умолчанию) — handler вызывается сразу с текущим значением.
    /// </param>
    /// <returns>IDisposable для отписки. Используй с DisposeWhenLifeEnded.</returns>
    public IDisposable SubscribeOnValueChanged(Action<T> handler, bool callOnSubscribe = true)
        => mSource.SubscribeOnValueChanged(handler, callOnSubscribe);

    protected override void OnDispose()
    {
        // Dispose источника при уничтожении ReadonlyReactive (shared lifetime)
        if (!mSource.IsDisposed)
            mSource.Dispose();
        base.OnDispose();
    }

    public override string ToString() => Value?.ToString();
}
