using System;
using System.Threading;

/// <summary>
/// IDisposable, вызывающий переданный Action при Dispose().
/// Используется для создания отписок через лямбды.
/// Потокобезопасен — Action гарантированно вызовется не более одного раза.
/// </summary>
public sealed class ActionDisposable : IDisposable
{
    private Action mAction;

    public ActionDisposable(Action action)
    {
        mAction = action;
    }

    public void Dispose()
    {
        // Interlocked.Exchange — атомарная замена на null, защита от двойного вызова
        Interlocked.Exchange(ref mAction, null)?.Invoke();
    }
}
