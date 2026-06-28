using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Presenter экрана текущей локации (Game-сцена).
/// Управляет реактивным состоянием: название, уровень, соседи, таймер перехода.
///
/// БЕЗОПАСНОСТЬ:
///   — CanMove берётся строго с сервера, не вычисляется на клиенте.
///   — targetLocationId для MoveAsync приходит из NeighborDto (серверный ответ).
///   — CombatEnabled / PvpEnabled — только UX-флаги, сервер проверяет независимо.
///
/// ТАЙМЕР:
///   — Отображает обратный отсчёт до LockedUntilUtc (UTC с сервера).
///   — При достижении нуля → GetCurrentAsync → CanMove обновляется с сервера.
///   — Если сервер вернул CanMove=false повторно → таймер перезапускается от нового LockedUntilUtc.
///   TD: дрейф часов клиента/сервера → решить через /api/time синхронизацию (беклог).
///   TD: если сервер возвращает CanMove=false без LockedUntilUtc → ждём 5 сек, retry.
///       В PvP это может давать пользователю неверную информацию на ~5с.
///       Решение то же: синхронизация времени (беклог).
/// </summary>
public class LocationPresenter : DisposableObject, IInitializable
{
    // ─── Константы ────────────────────────────────────────────────────────────

    private const int TIMER_RETRY_DELAY_SECONDS = 5;

    // ─── Реактивное состояние (Presenter → View) ──────────────────────────────

    private readonly Reactive<string>                   mLocationName  = new(string.Empty);
    private readonly Reactive<int>                      mLocationLevel = new(0);
    private readonly Reactive<bool>                     mCanMove       = new(false);
    private readonly Reactive<string>                   mTimerText     = new(string.Empty);
    private readonly Reactive<bool>                     mIsLoading     = new(false);
    private readonly Reactive<string>                   mErrorMessage  = new(string.Empty);
    private readonly Reactive<List<NeighborDto>>        mNeighbors     = new(new List<NeighborDto>());
    private readonly Reactive<List<DungeonEntranceDto>> mDungeons      = new(new List<DungeonEntranceDto>());

    public ReadonlyReactive<string>                   LocationName  => mLocationName.Readonly;
    public ReadonlyReactive<int>                      LocationLevel => mLocationLevel.Readonly;
    public ReadonlyReactive<bool>                     CanMove       => mCanMove.Readonly;
    public ReadonlyReactive<string>                   TimerText     => mTimerText.Readonly;
    public ReadonlyReactive<bool>                     IsLoading     => mIsLoading.Readonly;
    public ReadonlyReactive<string>                   ErrorMessage  => mErrorMessage.Readonly;
    public ReadonlyReactive<List<NeighborDto>>        Neighbors     => mNeighbors.Readonly;
    public ReadonlyReactive<List<DungeonEntranceDto>> Dungeons      => mDungeons.Readonly;

    // ─── Внутреннее состояние ─────────────────────────────────────────────────

    /// <summary>Метка сервера: до этого UTC нельзя перейти. Используем только для отображения.</summary>
    private DateTime? mLockedUntilUtc;

    /// <summary>CTS для таймера обратного отсчёта. Создаётся при старте, отменяется при StopTimer().</summary>
    private CancellationTokenSource mTimerCts;

    /// <summary>CTS для всего времени жизни Presenter. Отменяется в OnDispose().</summary>
    private readonly CancellationTokenSource mLifetimeCts = new();

    // ─── Зависимости ──────────────────────────────────────────────────────────

    private readonly ILocationService mLocationService;

    [Inject]
    public LocationPresenter(ILocationService locationService)
    {
        mLocationService = locationService;

        // Все Reactive-поля уничтожаются вместе с Presenter
        AutoDispose(
            mLocationName, mLocationLevel, mCanMove,
            mTimerText, mIsLoading, mErrorMessage,
            mNeighbors, mDungeons);
    }

    // ─── IInitializable ───────────────────────────────────────────────────────

    /// <summary>
    /// Zenject вызывает после инъекции всех зависимостей.
    /// Загружаем данные текущей локации при старте Game-сцены.
    /// </summary>
    public void Initialize()
    {
        RefreshAsync(mLifetimeCts.Token).Forget();
    }

    // ─── Публичные команды ────────────────────────────────────────────────────

    /// <summary>
    /// Обновить данные текущей локации с сервера.
    /// Вызывается при инициализации, после перехода и вручную (DEV_BUILD кнопка).
    /// </summary>
    public async UniTask RefreshAsync(CancellationToken ct)
    {
        mIsLoading.Value    = true;
        mErrorMessage.Value = string.Empty;

        try
        {
            var response = await mLocationService.GetCurrentAsync(ct);
            ApplyLocationData(response);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            mErrorMessage.Value = ex is ApiException apiEx
                ? apiEx.ServerError
                : "Нет подключения к серверу";
            Debug.LogError($"[LocationPresenter] RefreshAsync: {ex}");
        }
        finally
        {
            if (!IsDisposed)
                mIsLoading.Value = false;
        }
    }

    /// <summary>
    /// Перейти в соседнюю локацию.
    /// После успеха — запрашиваем полные данные новой локации (GetCurrentAsync).
    ///
    /// БЕЗОПАСНОСТЬ: targetLocationId берётся строго из NeighborDto.LocationId
    /// (серверный ответ), никогда не вычисляется на клиенте.
    /// </summary>
    public async UniTask MoveAsync(long targetLocationId, CancellationToken ct)
    {
        // Быстрая UX-проверка — сервер всё равно валидирует
        if (!mCanMove.Value) return;

        mIsLoading.Value    = true;
        mErrorMessage.Value = string.Empty;

        try
        {
            await mLocationService.MoveAsync(targetLocationId, ct);

            // Получаем полную картину новой локации одним запросом
            var response = await mLocationService.GetCurrentAsync(ct);
            ApplyLocationData(response);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex)
        {
            if (IsDisposed) return;
            mErrorMessage.Value = ex.ServerError;
            Debug.LogError($"[LocationPresenter] MoveAsync: {ex.ServerError}");
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            mErrorMessage.Value = "Нет подключения к серверу";
            Debug.LogError($"[LocationPresenter] MoveAsync: {ex}");
        }
        finally
        {
            if (!IsDisposed)
                mIsLoading.Value = false;
        }
    }

    // ─── Внутренняя логика ────────────────────────────────────────────────────

    /// <summary>
    /// Применяет данные ответа сервера к реактивному состоянию.
    /// CanMove берётся только отсюда — никогда не вычисляется клиентом.
    /// </summary>
    private void ApplyLocationData(CurrentLocationResponse response)
    {
        mLocationName.Value  = response.Name;
        mLocationLevel.Value = response.Level;
        mCanMove.Value       = response.CanMove;
        mLockedUntilUtc      = response.LockedUntilUtc;
        mNeighbors.Value     = response.Neighbors ?? new List<NeighborDto>();
        mDungeons.Value      = response.DungeonEntrances ?? new List<DungeonEntranceDto>();

        StopTimer();

        if (!response.CanMove && response.LockedUntilUtc.HasValue)
            StartTimer();
        else
            mTimerText.Value = string.Empty;
    }

    // ─── Таймер ───────────────────────────────────────────────────────────────

    private void StartTimer()
    {
        if (IsDisposed) return;
        StopTimer();
        mTimerCts = new CancellationTokenSource();
        RunTimerAsync(mTimerCts.Token).Forget();
    }

    private void StopTimer()
    {
        mTimerCts?.Cancel();
        mTimerCts?.Dispose();
        mTimerCts = null;
    }

    /// <summary>
    /// Петля таймера. Считает секунды до LockedUntilUtc (UTC с сервера).
    /// При достижении нуля → GetCurrentAsync → обновляем CanMove с сервера.
    ///
    /// Не даём пользователю нажать переход до подтверждения сервера.
    /// Таймер — только UX, не источник истины.
    /// </summary>
    private async UniTaskVoid RunTimerAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                if (!mLockedUntilUtc.HasValue) break;

                var remaining   = mLockedUntilUtc.Value - DateTime.UtcNow;
                var secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);

                if (secondsLeft > 0)
                {
                    mTimerText.Value = FormatTimer(secondsLeft);
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
                    continue;
                }

                // Таймер достиг нуля — подтверждаем у сервера
                // Кнопки остаются заблокированными до ответа сервера
                mTimerText.Value = "—";

                var response = await mLocationService.GetCurrentAsync(ct);

                if (response.CanMove)
                {
                    // Сервер подтвердил — применяем (StopTimer вызовется внутри ApplyLocationData)
                    ApplyLocationData(response);
                    return;
                }

                // Сервер говорит "ещё нет" — перезапускаем от нового значения
                // TD: причина — дрейф часов клиента/сервера.
                //     Решение: синхронизация через /api/time (беклог).
                mLockedUntilUtc = response.LockedUntilUtc;

                var hasNewTimer = response.LockedUntilUtc.HasValue
                               && (response.SecondsUntilCanMove ?? 0) > 0;

                if (!hasNewTimer)
                {
                    // Аномальный кейс: сервер CanMove=false, но время не дал
                    // TD: в PvP это даёт неверную инфу на TIMER_RETRY_DELAY_SECONDS
                    Debug.LogWarning("[LocationPresenter] Сервер: CanMove=false без LockedUntilUtc. " +
                                     $"Retry через {TIMER_RETRY_DELAY_SECONDS}с.");
                    mTimerText.Value = string.Empty;
                    await UniTask.Delay(TimeSpan.FromSeconds(TIMER_RETRY_DELAY_SECONDS), cancellationToken: ct);
                }
                // else — цикл пересчитает с обновлённым mLockedUntilUtc
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            Debug.LogError($"[LocationPresenter] RunTimerAsync: {ex}");
            mTimerText.Value = string.Empty;
        }
    }

    /// <summary>Форматирует секунды в читаемый вид: "4:05" или "42с".</summary>
    private static string FormatTimer(int totalSeconds)
    {
        if (totalSeconds <= 0) return string.Empty;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0
            ? $"{minutes}:{seconds:D2}"
            : $"{seconds}с";
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnDispose()
    {
        StopTimer();
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose(); // стреляет LifeEnd → чистит AutoDispose-подписки
    }
}
