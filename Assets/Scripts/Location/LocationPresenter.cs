using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Presenter экрана текущей локации (Game-сцена).
/// Управляет реактивным состоянием: название, уровень, соседи, мобы, игроки, таймер перехода.
/// Также владеет состоянием открытости Panel_Hunting (IsHuntingOpen).
///
/// БЕЗОПАСНОСТЬ:
///   — CanMove берётся строго с сервера, не вычисляется на клиенте.
///   — targetLocationId для MoveAsync приходит из NeighborDto (серверный ответ).
///   — CombatEnabled / PvpEnabled — только UX-флаги, сервер проверяет независимо.
///
/// ТАЙМЕР:
///   — Отображает обратный отсчёт до LockedUntilUtc (UTC с сервера).
///   — При достижении нуля → GetCurrentAsync → CanMove обновляется с сервера.
///   TD: дрейф часов клиента/сервера → решить через /api/time синхронизацию (беклог).
/// </summary>
public class LocationPresenter : DisposableObject, IInitializable
{
    // ─── Константы ────────────────────────────────────────────────────────────

    private const int TIMER_RETRY_DELAY_SECONDS = 5;

    // ─── Реактивное состояние (Presenter → View) ──────────────────────────────

    private readonly Reactive<string> mLocationName    = new(string.Empty);
    private readonly Reactive<int>    mLocationLevel   = new(0);
    private readonly Reactive<bool>   mCanMove         = new(false);
    private readonly Reactive<string> mTimerText       = new(string.Empty);
    private readonly Reactive<bool>   mIsLoading       = new(false);
    private readonly Reactive<string> mErrorMessage    = new(string.Empty);
    private readonly Reactive<List<NeighborDto>>       mNeighbors = new(new List<NeighborDto>());
    private readonly Reactive<List<DungeonEntranceDto>> mDungeons = new(new List<DungeonEntranceDto>());
    private readonly Reactive<List<MobSpawnDto>>       mMobs      = new(new List<MobSpawnDto>());
    private readonly Reactive<List<PlayerInLocationDto>> mPlayers  = new(new List<PlayerInLocationDto>());

    /// <summary>Открыта ли Panel_Hunting поверх Panel_LocationMain.</summary>
    private readonly Reactive<bool> mIsHuntingOpen = new(false);

    public ReadonlyReactive<string> LocationName   => mLocationName.Readonly;
    public ReadonlyReactive<int>    LocationLevel  => mLocationLevel.Readonly;
    public ReadonlyReactive<bool>   CanMove        => mCanMove.Readonly;
    public ReadonlyReactive<string> TimerText      => mTimerText.Readonly;
    public ReadonlyReactive<bool>   IsLoading      => mIsLoading.Readonly;
    public ReadonlyReactive<string> ErrorMessage   => mErrorMessage.Readonly;
    public ReadonlyReactive<List<NeighborDto>>        Neighbors => mNeighbors.Readonly;
    public ReadonlyReactive<List<DungeonEntranceDto>> Dungeons  => mDungeons.Readonly;
    public ReadonlyReactive<List<MobSpawnDto>>        Mobs      => mMobs.Readonly;
    public ReadonlyReactive<List<PlayerInLocationDto>> Players  => mPlayers.Readonly;
    public ReadonlyReactive<bool>   IsHuntingOpen  => mIsHuntingOpen.Readonly;

    // ─── Внутреннее состояние ─────────────────────────────────────────────────

    private DateTime?                  mLockedUntilUtc;
    private CancellationTokenSource    mTimerCts;
    private readonly CancellationTokenSource mLifetimeCts = new();

    // ─── Зависимости ──────────────────────────────────────────────────────────

    private readonly ILocationService mLocationService;

    [Inject]
    public LocationPresenter(ILocationService locationService)
    {
        mLocationService = locationService;

        AutoDispose(
            mLocationName, mLocationLevel, mCanMove,
            mTimerText, mIsLoading, mErrorMessage,
            mNeighbors, mDungeons, mMobs, mPlayers,
            mIsHuntingOpen);
    }

    // ─── IInitializable ───────────────────────────────────────────────────────

    public void Initialize()
    {
        RefreshAsync(mLifetimeCts.Token).Forget();
    }

    // ─── Публичные команды ────────────────────────────────────────────────────

    /// <summary>Обновить данные локации с сервера.</summary>
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
            if (!IsDisposed) mIsLoading.Value = false;
        }
    }

    /// <summary>Перейти в соседнюю локацию. locationId строго из NeighborDto.</summary>
    public async UniTask MoveAsync(long targetLocationId, CancellationToken ct)
    {
        if (!mCanMove.Value) return;

        mIsLoading.Value    = true;
        mErrorMessage.Value = string.Empty;

        try
        {
            await mLocationService.MoveAsync(targetLocationId, ct);
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
            if (!IsDisposed) mIsLoading.Value = false;
        }
    }

    /// <summary>Открыть экран охоты (скрыть Panel_LocationMain, показать Panel_Hunting).</summary>
    public void OpenHunting()  => mIsHuntingOpen.Value = true;

    /// <summary>Закрыть экран охоты (вернуться в Panel_LocationMain).</summary>
    public void CloseHunting() => mIsHuntingOpen.Value = false;

    // ─── Внутренняя логика ────────────────────────────────────────────────────

    private void ApplyLocationData(CurrentLocationResponse response)
    {
        mLocationName.Value  = response.Name;
        mLocationLevel.Value = response.Level;
        mCanMove.Value       = response.CanMove;
        mLockedUntilUtc      = response.LockedUntilUtc;
        mNeighbors.Value     = response.Neighbors       ?? new List<NeighborDto>();
        mDungeons.Value      = response.DungeonEntrances ?? new List<DungeonEntranceDto>();
        mMobs.Value          = response.Mobs            ?? new List<MobSpawnDto>();
        mPlayers.Value       = response.Players         ?? new List<PlayerInLocationDto>();

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

                mTimerText.Value = "—";
                var response     = await mLocationService.GetCurrentAsync(ct);

                if (response.CanMove)
                {
                    ApplyLocationData(response);
                    return;
                }

                mLockedUntilUtc = response.LockedUntilUtc;

                var hasNewTimer = response.LockedUntilUtc.HasValue
                               && (response.SecondsUntilCanMove ?? 0) > 0;

                if (!hasNewTimer)
                {
                    Debug.LogWarning("[LocationPresenter] CanMove=false без LockedUntilUtc. " +
                                     $"Retry через {TIMER_RETRY_DELAY_SECONDS}с.");
                    mTimerText.Value = string.Empty;
                    await UniTask.Delay(TimeSpan.FromSeconds(TIMER_RETRY_DELAY_SECONDS), cancellationToken: ct);
                }
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

    private static string FormatTimer(int totalSeconds)
    {
        if (totalSeconds <= 0) return string.Empty;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0 ? $"{minutes}:{seconds:D2}" : $"{seconds}с";
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnDispose()
    {
        StopTimer();
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose();
    }
}
