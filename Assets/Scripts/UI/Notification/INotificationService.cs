using System;

/// <summary>
/// Единая точка входа для показа тостов и модальных диалогов из любого Presenter в проекте.
/// Инжектируется напрямую (без отдельного презентера-посредника), живёт на ProjectContext —
/// переживает смену сцен, очередь не теряется.
///
/// ДВЕ НЕЗАВИСИМЫЕ ОЧЕРЕДИ, но со связью «тост уступает диалогу»:
///   — Тосты показываются по одному, следующий стартует только после того как текущий скрылся
///     (по таймеру или по тапу). Новый тост не «съедает» ещё не увиденный — он ждёт в очереди.
///   — Диалоги показываются по одному, следующий — только после ответа на текущий.
///   — Пока открыт диалог, показ тостов приостановлен: они копятся и пойдут после закрытия
///     диалога (модалка не должна перекрываться тостом).
///
/// API — на колбэках (Action), т.к. все текущие сценарии проекта покрываются связкой
/// «нажал кнопку → выполнить действие». Колбэк может быть null (тогда кнопка просто закрывает).
///
/// РАСШИРЕНИЕ НА БУДУЩЕЕ: при появлении многошаговых сценариев (когда после ответа идёт общий
/// «хвост» кода, который не хочется дублировать в двух колбэках) поверх этого сервиса можно
/// добавить тонкую async-обёртку — метод ShowDialogAsync, возвращающий UniTask&lt;bool&gt; через
/// UniTaskCompletionSource, который под капотом подписывается на те же OnPrimary/OnSecondary.
/// Существующий колбэковый API при этом не меняется. Пока не требуется — не добавляем.
/// </summary>
public interface INotificationService
{
    /// <summary>Текущий показываемый тост, null — очередь пуста или показ на паузе из-за диалога.</summary>
    ReadonlyReactive<ToastRequest> CurrentToast { get; }

    /// <summary>Текущий показываемый диалог, null — очередь диалогов пуста.</summary>
    ReadonlyReactive<DialogRequest> CurrentDialog { get; }

    // ─── Тосты ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Поставить тост в очередь. actionLabel/onAction — опциональная кнопка-действие.
    /// Если в NotificationConfig выключен ToastsEnabled — вызов молча ничего не делает
    /// (канал тостов полностью выключен, диалоги при этом продолжают работать).
    /// </summary>
    void ShowToast(string message, NotificationType type = NotificationType.Info,
        string actionLabel = null, Action onAction = null);

    /// <summary>Шорткат: тост-ошибка (красный).</summary>
    void ShowError(string message);

    /// <summary>Шорткат: тост-предупреждение (жёлтый).</summary>
    void ShowWarning(string message);

    /// <summary>Шорткат: тост-инфо (синий).</summary>
    void ShowInfo(string message);

    /// <summary>Досрочно скрыть текущий тост (тап пользователя) — следующий из очереди показывается сразу.</summary>
    void DismissCurrentToast();

    // ─── Диалоги ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Полный диалог: заголовок, сообщение, тип, две кнопки (вторичная опциональна).
    /// Любой колбэк может быть null. Возврат нужного из очереди — автоматически.
    /// </summary>
    void ShowDialog(string message, string title = null,
        NotificationType type = NotificationType.Message,
        string primaryLabel = "ОК", Action onPrimary = null,
        string secondaryLabel = null, Action onSecondary = null,
        bool persistAcrossScenes = false);

    /// <summary>
    /// Шорткат подтверждения (да/нет). onConfirm — при подтверждении, onCancel — при отмене (может быть null).
    /// </summary>
    void ShowConfirm(string message, Action onConfirm, Action onCancel = null,
        string title = null, string confirmLabel = "Да", string cancelLabel = "Отмена",
        NotificationType type = NotificationType.Warning);

    /// <summary>Шорткат уведомления с одной кнопкой «ОК» (аналог старого BanPopup-подобного окна).</summary>
    void ShowMessage(string message, string title = null, Action onOk = null,
        NotificationType type = NotificationType.Message);

    /// <summary>
    /// Ответ пользователя на текущий диалог (вызывается View по клику).
    /// primary=true — нажата основная кнопка, false — вторичная. Сервис вызовет нужный колбэк
    /// и покажет следующий диалог из очереди.
    /// </summary>
    void RespondToDialog(bool primary);

    // ─── Жизненный цикл сцены ───────────────────────────────────────────────────

    /// <summary>
    /// Уведомить сервис о смене сцены. Снимает текущий диалог, если он не помечен
    /// PersistAcrossScenes (его вызвавший экран уничтожен). Вызывается из AppController/SceneLoader.
    /// </summary>
    void OnSceneChanging();
}
