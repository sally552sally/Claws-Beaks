using System;

/// <summary>
/// Базовый класс для чистых C# объектов с управляемым временем жизни.
/// Используется для Presenter, Service и любых non-Unity объектов.
/// 
/// Паттерн использования:
/// <code>
/// class LoginPresenter : DisposableObject
/// {
///     private readonly Reactive&lt;bool&gt; mIsLoading = new(false);
///     public ReadonlyReactive&lt;bool&gt; IsLoading => mIsLoading.Readonly;
/// 
///     public LoginPresenter(IAuthService authService)
///     {
///         // Подписки привязываем к себе — отпишутся при Dispose()
///         AutoDispose(
///             someService.SomeEvent.SubscribeOnValueChanged(OnSomethingChanged)
///         );
///     }
/// }
/// </code>
/// </summary>
public class DisposableObject : IDisposable, ILifeScope
{
    public bool IsDisposed { get; private set; }

    public event Action LifeEnd;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        OnDispose();
    }

    /// <summary>
    /// Переопределить для кастомной логики при уничтожении.
    /// Обязательно вызывать base.OnDispose() в конце.
    /// </summary>
    protected virtual void OnDispose()
    {
        LifeEnd?.Invoke();
        LifeEnd = null;
    }

    /// <summary>
    /// Привязывает время жизни дочерних объектов к этому.
    /// Удобен в конструкторе для регистрации подписок.
    /// </summary>
    protected void AutoDispose(params IDisposable[] disposables)
    {
        foreach (var d in disposables)
            d.DisposeWhenLifeEnded(this);
    }
}
