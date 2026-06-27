using System;
using UnityEngine;

/// <summary>
/// Базовый класс для MonoBehaviour с управляемым временем жизни.
/// Используется для View. OnDestroy автоматически вызывает Dispose().
/// 
/// Паттерн использования:
/// <code>
/// class LoginView : DisposableBehaviour
/// {
///     [SerializeField] private TMP_Text mUsernameLabel;
///     private LoginPresenter mPresenter;
/// 
///     protected override void SafeAwake()
///     {
///         mPresenter.IsLoading
///             .SubscribeOnValueChanged(SetLoadingVisible)
///             .DisposeWhenLifeEnded(this);
///     }
/// }
/// </code>
/// </summary>
public class DisposableBehaviour : MonoBehaviour, IDisposable, ILifeScope
{
    public bool IsDisposed { get; private set; }

    public event Action LifeEnd;

    /// <summary>
    /// Вместо Awake переопределяй SafeAwake.
    /// Гарантированно не вызывается на уничтоженном объекте.
    /// </summary>
    protected virtual void SafeAwake() { }

    private void Awake()
    {
        if (!IsDisposed)
            SafeAwake();
    }

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

    private void OnDestroy()
    {
        Dispose();
    }

    /// <summary>
    /// Привязывает время жизни дочерних объектов к этому.
    /// Удобен в SafeAwake для регистрации подписок.
    /// </summary>
    protected void AutoDispose(params IDisposable[] disposables)
    {
        foreach (var d in disposables)
            d.DisposeWhenLifeEnded(this);
    }

    /// <summary>Поиск компонента на этом GameObject. Бросает исключение если не найден.</summary>
    protected T GetRequiredComponent<T>()
    {
        var component = GetComponent<T>();
        if (component == null)
            throw new MissingComponentException($"{typeof(T).Name} не найден на {gameObject.name}");
        return component;
    }

    /// <summary>Поиск компонента вниз по иерархии. Бросает исключение если не найден.</summary>
    protected T GetRequiredComponentInChildren<T>(bool includeInactive = true)
    {
        var component = GetComponentInChildren<T>(includeInactive);
        if (component == null)
            throw new MissingComponentException($"{typeof(T).Name} не найден в дочерних объектах {gameObject.name}");
        return component;
    }
}
