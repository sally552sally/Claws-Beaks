using System;

/// <summary>
/// Расширения для работы с IDisposable и ILifeScope.
/// Главный паттерн: subscription.DisposeWhenLifeEnded(this) — автоотписка при уничтожении объекта.
/// </summary>
public static class DisposableExtensions
{
    /// <summary>
    /// Привязывает время жизни disposable к lifeScope.
    /// Когда lifeScope уничтожится — Dispose() вызовется автоматически.
    /// Возвращает disposable для цепочки вызовов.
    /// </summary>
    public static IDisposable DisposeWhenLifeEnded(this IDisposable disposable, ILifeScope lifeScope)
    {
        if (lifeScope.IsDisposed)
        {
            // Если скоуп уже уничтожен — сразу диспозим
            disposable.Dispose();
            return disposable;
        }

        void OnLifeEnd() => disposable.Dispose();
        lifeScope.LifeEnd += OnLifeEnd;

        return disposable;
    }

    /// <summary>
    /// Подписывается на событие окончания жизни объекта.
    /// Возвращает IDisposable для ручной отписки.
    /// </summary>
    public static IDisposable SubscribeLifeEnded(this ILifeScope lifeScope, Action handler)
    {
        if (lifeScope.IsDisposed)
        {
            handler();
            return new ActionDisposable(null);
        }

        lifeScope.LifeEnd += handler;
        return new ActionDisposable(() => lifeScope.LifeEnd -= handler);
    }
}
