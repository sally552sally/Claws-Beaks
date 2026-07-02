using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Сервис уведомлений. Живёт на ProjectContext (singleton на всё приложение) — очередь
/// переживает смену сцен (Auth → Game). Чистый C# (DisposableObject), никакой зависимости
/// от конкретного View: View лишь отражает CurrentToast/CurrentDialog и зовёт RespondToDialog/
/// DismissCurrentToast.
///
/// Поведение очередей — см. INotificationService. Ключевое:
///   — тост-цикл перед показом следующего тоста ждёт, пока не закроется открытый диалог;
///   — закрытие диалога «будит» тост-цикл (mDialogClosedSignal).
/// </summary>
public sealed class NotificationService : DisposableObject, INotificationService
{
    private readonly NotificationConfig mConfig;

    private readonly Reactive<ToastRequest> mCurrentToast = new(null);
    private readonly Reactive<DialogRequest> mCurrentDialog = new(null);

    private readonly Queue<ToastRequest> mToastQueue = new();
    private readonly Queue<DialogRequest> mDialogQueue = new();

    private bool mIsToastLoopRunning;
    private bool mIsDialogLoopRunning;

    // Токен жизни сервиса. LifeEnd в этом проекте — event Action, а НЕ CancellationToken,
    // поэтому для async-циклов держим собственный CTS и отменяем его в OnDispose.
    private readonly CancellationTokenSource mLifetimeCts = new();

    // Отмена ожидания таймера ТЕКУЩЕГО тоста (досрочный дисмисс тапом).
    private CancellationTokenSource mCurrentToastCts;

    // Сигнал «диалог закрылся» — чтобы тост-цикл, ждущий закрытия диалога, проснулся.
    private UniTaskCompletionSource mDialogClosedSignal;

    public ReadonlyReactive<ToastRequest> CurrentToast => mCurrentToast.Readonly;
    public ReadonlyReactive<DialogRequest> CurrentDialog => mCurrentDialog.Readonly;

    public NotificationService(NotificationConfig config)
    {
        mConfig = config;
        AutoDispose(mCurrentToast, mCurrentDialog);
    }

    protected override void OnDispose()
    {
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose();
    }

    // ─── Тосты: публичный API ───────────────────────────────────────────────────

    /// <inheritdoc />
    public void ShowToast(string message, NotificationType type = NotificationType.Info,
        string actionLabel = null, Action onAction = null)
    {
        // Галочка в NotificationConfig (mToastsEnabled) — полностью выключает канал тостов.
        // Диалоги (ShowDialog/ShowConfirm/ShowMessage) не затрагиваются, это отдельный канал.
        if (!mConfig.ToastsEnabled) return;

        mToastQueue.Enqueue(new ToastRequest(message, type, actionLabel, onAction));
        if (!mIsToastLoopRunning)
            RunToastLoopAsync().Forget();
    }

    /// <inheritdoc />
    public void ShowError(string message) => ShowToast(message, NotificationType.Error);

    /// <inheritdoc />
    public void ShowWarning(string message) => ShowToast(message, NotificationType.Warning);

    /// <inheritdoc />
    public void ShowInfo(string message) => ShowToast(message, NotificationType.Info);

    /// <inheritdoc />
    public void DismissCurrentToast()
    {
        // Отменяем только ожидание таймера текущего тоста — цикл сам подхватит следующий.
        mCurrentToastCts?.Cancel();
    }

    // ─── Диалоги: публичный API ─────────────────────────────────────────────────

    /// <inheritdoc />
    public void ShowDialog(string message, string title = null,
        NotificationType type = NotificationType.Message,
        string primaryLabel = "ОК", Action onPrimary = null,
        string secondaryLabel = null, Action onSecondary = null,
        bool persistAcrossScenes = false)
    {
        var request = new DialogRequest(title, message, type,
            primaryLabel, onPrimary, secondaryLabel, onSecondary, persistAcrossScenes);
        mDialogQueue.Enqueue(request);
        if (!mIsDialogLoopRunning)
            RunDialogLoopAsync().Forget();
    }

    /// <inheritdoc />
    public void ShowConfirm(string message, Action onConfirm, Action onCancel = null,
        string title = null, string confirmLabel = "Да", string cancelLabel = "Отмена",
        NotificationType type = NotificationType.Warning)
    {
        ShowDialog(message, title, type,
            primaryLabel: confirmLabel, onPrimary: onConfirm,
            secondaryLabel: cancelLabel, onSecondary: onCancel);
    }

    /// <inheritdoc />
    public void ShowMessage(string message, string title = null, Action onOk = null,
        NotificationType type = NotificationType.Message)
    {
        // Одна кнопка «ОК»: вторичная не задаётся.
        ShowDialog(message, title, type, primaryLabel: "ОК", onPrimary: onOk);
    }

    /// <inheritdoc />
    public void RespondToDialog(bool primary)
    {
        var dialog = mCurrentDialog.Value;
        if (dialog == null) return; // лишний/повторный клик после закрытия — безопасно игнорируем

        // Сначала снимаем показ (чтобы колбэк, который сам может открыть новый диалог/тост,
        // не конфликтовал с ещё «висящим» текущим), затем зовём колбэк, затем будим циклы.
        CompleteCurrentDialog();

        var callback = primary ? dialog.OnPrimary : dialog.OnSecondary;
        try
        {
            callback?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NotificationService] Ошибка в колбэке диалога: {ex}");
        }
    }

    // ─── Жизненный цикл сцены ───────────────────────────────────────────────────

    /// <inheritdoc />
    public void OnSceneChanging()
    {
        var dialog = mCurrentDialog.Value;
        if (dialog != null && !dialog.PersistAcrossScenes)
        {
            // Экран, вызвавший диалог, уничтожается — снимаем показ без вызова колбэков.
            // (Колбэк мог захватить уничтожаемый Presenter — вызывать его небезопасно.)
            CompleteCurrentDialog();
        }
    }

    // ─── Тосты: цикл ────────────────────────────────────────────────────────────

    private async UniTaskVoid RunToastLoopAsync()
    {
        mIsToastLoopRunning = true;
        try
        {
            while (mToastQueue.Count > 0)
            {
                // Тост уступает диалогу: пока открыт диалог — ждём его закрытия.
                await WaitWhileDialogOpenAsync();

                var request = mToastQueue.Dequeue();
                mCurrentToast.Value = request;

                await WaitToastDurationAsync();
                if (mLifetimeCts.IsCancellationRequested) return;

                mCurrentToast.Value = null;

                if (mToastQueue.Count > 0 && mConfig.ToastGapSeconds > 0f)
                    await DelaySafeAsync(mConfig.ToastGapSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            // Отмена по уничтожению сервиса — штатный выход.
        }
        finally
        {
            mIsToastLoopRunning = false;
        }
    }

    /// <summary>Ждёт таймер текущего тоста; прерывается досрочно при DismissCurrentToast().</summary>
    private async UniTask WaitToastDurationAsync()
    {
        mCurrentToastCts = CancellationTokenSource.CreateLinkedTokenSource(mLifetimeCts.Token);
        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(mConfig.ToastDurationSeconds),
                cancellationToken: mCurrentToastCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Досрочный дисмисс тапом — ожидаемо. Если это отмена по жизни сервиса — пробросим выше.
            if (mLifetimeCts.IsCancellationRequested) throw;
        }
        finally
        {
            mCurrentToastCts.Dispose();
            mCurrentToastCts = null;
        }
    }

    /// <summary>Блокирует тост-цикл, пока открыт диалог. Просыпается по сигналу закрытия диалога.</summary>
    private async UniTask WaitWhileDialogOpenAsync()
    {
        while (mCurrentDialog.Value != null)
        {
            mDialogClosedSignal ??= new UniTaskCompletionSource();
            await mDialogClosedSignal.Task.AttachExternalCancellation(mLifetimeCts.Token);
        }
    }

    private async UniTask DelaySafeAsync(float seconds)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: mLifetimeCts.Token);
    }

    // ─── Диалоги: цикл ──────────────────────────────────────────────────────────

    private async UniTaskVoid RunDialogLoopAsync()
    {
        mIsDialogLoopRunning = true;
        try
        {
            while (mDialogQueue.Count > 0)
            {
                var request = mDialogQueue.Dequeue();
                mCurrentDialog.Value = request;

                // Ждём, пока текущий диалог не будет закрыт (RespondToDialog или OnSceneChanging).
                mDialogAnswered = new UniTaskCompletionSource();
                await mDialogAnswered.Task.AttachExternalCancellation(mLifetimeCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Отмена по уничтожению сервиса — штатный выход.
        }
        finally
        {
            mIsDialogLoopRunning = false;
        }
    }

    // Сигнал «на текущий диалог получен ответ» — двигает диалог-цикл к следующему.
    private UniTaskCompletionSource mDialogAnswered;

    /// <summary>Снимает показ текущего диалога и будит оба цикла (диалоговый и тост-цикл).</summary>
    private void CompleteCurrentDialog()
    {
        mCurrentDialog.Value = null;

        // Двигаем диалог-цикл к следующему диалогу из очереди.
        mDialogAnswered?.TrySetResult();
        mDialogAnswered = null;

        // Будим тост-цикл, если он ждал закрытия диалога.
        mDialogClosedSignal?.TrySetResult();
        mDialogClosedSignal = null;
    }
}
