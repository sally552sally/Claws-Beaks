using System;

/// <summary>
/// Объект с управляемым временем жизни.
/// Все подписки привязываются к этому интерфейсу через DisposeWhenLifeEnded.
/// </summary>
public interface ILifeScope
{
    /// <summary>true — объект уничтожен, подписки больше не принимаются.</summary>
    bool IsDisposed { get; }

    /// <summary>Вызывается при уничтожении объекта. Служит сигналом для автоотписки.</summary>
    event Action LifeEnd;
}
