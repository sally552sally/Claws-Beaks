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

    private readonly Reactive<string> mLocationName = new(string.Empty);
    private readonly Reactive<int> mLocationLevel = new(0);
    private readonly Reactive<bool> mCanMove = new(false);
    private readonly Reactive<string> mTimerText = new(string.Empty);
    private readonly Reactive<bool> mIsLoading = new(false);
    private readonly Reactive<List<NeighborDto>> mNeighbors = new(new List<NeighborDto>());
    private readonly Reactive<List<DungeonEntranceDto>> mDungeons = new(new List<DungeonEntranceDto>());
    private readonly Reactive<List<MobSpawnDto>> mMobs = new(new List<MobSpawnDto>());
    private readonly Reactive<List<PlayerInLocationDto>> mPlayers = new(new List<PlayerInLocationDto>());

    /// <summary>Открыта ли Panel_Hunting поверх Panel_LocationMain.</summary>
    private readonly Reactive<bool> mIsHuntingOpen = new(false);

    /// <summary>Персонаж мёртв и ждёт воскрешения. Пока true — обычный экран локации
    /// заблокирован модальным диалогом воскрешения (см. ShowResurrectDialogIfNeeded).</summary>
    private readonly Reactive<bool> mIsAwaitingResurrection = new(false);

    public ReadonlyReactive<string> LocationName => mLocationName.Readonly;
    public ReadonlyReactive<int> LocationLevel => mLocationLevel.Readonly;
    public ReadonlyReactive<bool> CanMove => mCanMove.Readonly;
    public ReadonlyReactive<string> TimerText => mTimerText.Readonly;
    public ReadonlyReactive<bool> IsLoading => mIsLoading.Readonly;
    public ReadonlyReactive<List<NeighborDto>> Neighbors => mNeighbors.Readonly;
    public ReadonlyReactive<List<DungeonEntranceDto>> Dungeons => mDungeons.Readonly;
    public ReadonlyReactive<List<MobSpawnDto>> Mobs => mMobs.Readonly;
    public ReadonlyReactive<List<PlayerInLocationDto>> Players => mPlayers.Readonly;
    public ReadonlyReactive<bool> IsHuntingOpen => mIsHuntingOpen.Readonly;
    public ReadonlyReactive<bool> IsAwaitingResurrection => mIsAwaitingResurrection.Readonly;

    /// <summary>Локация, в которой мы сейчас, по данным последнего REST-ответа. null пока
    /// не пришёл первый ответ. Публична для ChatPresenter — чистит буфер чата локации при смене.</summary>
    public ReadonlyReactive<long?> CurrentLocationId => mCurrentLocationId.Readonly;

    // ─── Внутреннее состояние ─────────────────────────────────────────────────

    private DateTime? mLockedUntilUtc;
    private CancellationTokenSource mTimerCts;
    private readonly CancellationTokenSource mLifetimeCts = new();

    // ─── Зависимости ──────────────────────────────────────────────────────────

    private readonly ILocationService mLocationService;
    private readonly ICombatService mCombatService;
    private readonly INotificationService mNotifications;
    private readonly ILocationRealtimeService mRealtime;
    private readonly ICharacterContext mCharacterContext;
    private readonly IAuthService mAuthService;
    private readonly ISceneLoader mSceneLoader;

    /// <summary>Нужен только для ForceExitCombat() при воскрешении через диалог
    /// локации (TD-C29) — сам бой LocationPresenter не ведёт и не должен.</summary>
    private readonly CombatPresenter mCombatPresenter;

    /// <summary>Диалог воскрешения уже показан и ждёт ответа — не дублировать при повторных Refresh.</summary>
    private bool mResurrectDialogPending;

    /// <summary>Используется, чтобы понять, что locationId сменился и нужно перевступить
    /// в SignalR-группу (см. ApplyLocationData → mRealtime.SetCurrentLocationAsync).
    /// Reactive, а не плоское поле — публично читается ChatPresenter (см. CurrentLocationId выше).</summary>
    private readonly Reactive<long?> mCurrentLocationId = new(null);

    [Inject]
    public LocationPresenter(ILocationService locationService, ICombatService combatService,
        INotificationService notifications, ILocationRealtimeService realtime, ICharacterContext characterContext,
        IAuthService authService, ISceneLoader sceneLoader, CombatPresenter combatPresenter)
    {
        mLocationService = locationService;
        mCombatService = combatService;
        mNotifications = notifications;
        mRealtime = realtime;
        mCharacterContext = characterContext;
        mAuthService = authService;
        mSceneLoader = sceneLoader;
        mCombatPresenter = combatPresenter;

        AutoDispose(
            mLocationName, mLocationLevel, mCanMove,
            mTimerText, mIsLoading,
            mNeighbors, mDungeons, mMobs, mPlayers,
            mIsHuntingOpen, mIsAwaitingResurrection, mCurrentLocationId);

        mRealtime.MobStateChanged += OnMobStateChanged;
        mRealtime.PlayerEntered += OnPlayerEntered;
        mRealtime.PlayerLeft += OnPlayerLeft;
        mRealtime.CombatStarted += OnCombatStarted;
        mRealtime.Resynced += OnRealtimeResynced;
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
        mIsLoading.Value = true;

        try
        {
            var response = await mLocationService.GetCurrentAsync(ct);
            ApplyLocationData(response);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            mNotifications.ShowError(ex is ApiException apiEx
                ? apiEx.ServerError
                : "Нет подключения к серверу");
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

        mIsLoading.Value = true;

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
            mNotifications.ShowError(ex.ServerError);
            Debug.LogError($"[LocationPresenter] MoveAsync: {ex.ServerError}");
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            mNotifications.ShowError("Нет подключения к серверу");
            Debug.LogError($"[LocationPresenter] MoveAsync: {ex}");
        }
        finally
        {
            if (!IsDisposed) mIsLoading.Value = false;
        }
    }

    /// <summary>Открыть экран охоты (скрыть Panel_LocationMain, показать Panel_Hunting).</summary>
    public void OpenHunting() => mIsHuntingOpen.Value = true;

    /// <summary>Закрыть экран охоты (вернуться в Panel_LocationMain).</summary>
    public void CloseHunting() => mIsHuntingOpen.Value = false;

    /// <summary>Выйти из аккаунта — токены чистятся локально ПЕРВЫМ делом внутри
    /// IAuthService.LogoutAsync (сессия считается завершённой, даже если сервер
    /// недоступен — там же и обёрнута попытка сообщить об этом серверу).
    /// Для кнопки «Выйти»: SubscribeOnClick(() => mPresenter.LogoutAsync(destroyCancellationToken).Forget()).</summary>
    public async UniTask LogoutAsync(CancellationToken ct)
    {
        await mAuthService.LogoutAsync(ct);
        await mSceneLoader.LoadAsync(SceneNames.AUTH, ct);
    }

    // ─── Внутренняя логика ────────────────────────────────────────────────────

    private void ApplyLocationData(CurrentLocationResponse response)
    {
        mLocationName.Value = response.Name;
        mLocationLevel.Value = response.Level;
        mCanMove.Value = response.CanMove;
        mLockedUntilUtc = response.LockedUntilUtc;
        mNeighbors.Value = response.Neighbors ?? new List<NeighborDto>();
        mDungeons.Value = response.DungeonEntrances ?? new List<DungeonEntranceDto>();
        mMobs.Value = response.Mobs ?? new List<MobSpawnDto>();
        mPlayers.Value = response.Players ?? new List<PlayerInLocationDto>();
        mIsAwaitingResurrection.Value = response.IsAwaitingResurrection;

        if (mCurrentLocationId.Value != response.LocationId)
        {
            mCurrentLocationId.Value = response.LocationId;
            // Не блокируем применение остального стейта ожиданием сети — вступление
            // в SignalR-группу локации идёт в фоне (см. ILocationRealtimeService).
            mRealtime.SetCurrentLocationAsync(response.LocationId, mLifetimeCts.Token).Forget();
        }

        StopTimer();

        if (!response.CanMove && response.LockedUntilUtc.HasValue)
            StartTimer();
        else
            mTimerText.Value = string.Empty;

        if (response.IsAwaitingResurrection)
            ShowResurrectDialogIfNeeded();
        else
            mResurrectDialogPending = false; // персонаж жив — разрешаем показать диалог заново, если умрёт снова
    }

    // ─── Воскрешение ──────────────────────────────────────────────────────────

    /// <summary>
    /// Показывает модальный диалог «вы мертвы» с одной кнопкой «Воскреснуть», если он ещё
    /// не показан. Не даёт задублироваться при повторных RefreshAsync, пока ждём ответа.
    /// Диалог без вторичной кнопки — закрыть его можно только воскрешением (по требованию).
    /// </summary>
    private void ShowResurrectDialogIfNeeded()
    {
        if (mResurrectDialogPending) return;
        mResurrectDialogPending = true;

        mNotifications.ShowDialog(
            message: "Вы погибли и ожидаете воскрешения.",
            title: "Вы мертвы",
            type: NotificationType.Warning,
            primaryLabel: "Воскреснуть",
            onPrimary: () => ResurrectAsync(mLifetimeCts.Token).Forget());
    }

    /// <summary>Воскресить персонажа (POST /api/combat/resurrect) и обновить локацию.</summary>
    public async UniTask ResurrectAsync(CancellationToken ct)
    {
        try
        {
            await mCombatService.ResurrectAsync(ct);
            mResurrectDialogPending = false;

            // TD-C29: этот путь воскрешения не проходит через CombatPresenter.ExitCombatAsync
            // (у него своя кнопка OK на результате боя), поэтому флаги боя (IsInCombat и т.д.)
            // без явного вызова оставались бы висеть до следующего EnterCombat.
            mCombatPresenter.ForceExitCombat();

            await RefreshAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex)
        {
            if (IsDisposed) return;
            mResurrectDialogPending = false; // разрешаем показать диалог заново при следующем Refresh
            mNotifications.ShowError(ex.ServerError);
            Debug.LogError($"[LocationPresenter] ResurrectAsync: {ex.ServerError}");
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            mResurrectDialogPending = false;
            mNotifications.ShowError("Нет подключения к серверу");
            Debug.LogError($"[LocationPresenter] ResurrectAsync: {ex}");
        }
    }

    // ─── Realtime-события локации (SignalR) ────────────────────────────────────

    /// <summary>Состояние спавна моба изменилось (alive/in_combat/dead) — точечно обновляем
    /// запись в списке, не трогая остальные (полная перезагрузка — только на Resynced).</summary>
    private void OnMobStateChanged(MobStateChangedEvent e)
    {
        var current = mMobs.Value;
        var index = current.FindIndex(m => m.SpawnId == e.SpawnId);
        if (index < 0)
        {
            // Моба нет в текущем REST-снимке (гонка при входе в локацию) — ближайший
            // RefreshAsync/Resynced подтянет его. Не создаём частичную запись без Name/Level,
            // которых это событие не несёт.
            return;
        }

        var original = current[index];
        var updated = new List<MobSpawnDto>(current);
        updated[index] = new MobSpawnDto
        {
            SpawnId = original.SpawnId,
            Name = original.Name,
            Level = original.Level,
            State = e.State,
            RespawnAt = e.RespawnAt
        };
        mMobs.Value = updated;
    }

    /// <summary>Игрок вошёл в текущую локацию — добавляем в список (идемпотентно).</summary>
    private void OnPlayerEntered(PlayerEnteredEvent e)
    {
        var current = mPlayers.Value;
        if (current.Exists(p => p.CharacterId == e.CharacterId)) return;

        var updated = new List<PlayerInLocationDto>(current)
        {
            new PlayerInLocationDto
            {
                CharacterId = e.CharacterId,
                Nickname = e.Nickname,
                Level = e.Level
            }
        };
        mPlayers.Value = updated;
    }

    /// <summary>Игрок покинул текущую локацию — убираем из списка.</summary>
    private void OnPlayerLeft(PlayerLeftEvent e)
    {
        var current = mPlayers.Value;
        if (!current.Exists(p => p.CharacterId == e.CharacterId)) return;

        var updated = new List<PlayerInLocationDto>(current);
        updated.RemoveAll(p => p.CharacterId == e.CharacterId);
        mPlayers.Value = updated;
    }

    /// <summary>PvP-бой начался где-то в локации — событие приходит всем, показываем
    /// предупреждение, только если жертва — это мы (см. CombatStartedEvent).</summary>
    private void OnCombatStarted(CombatStartedEvent e)
    {
        if (mCharacterContext.CharacterId.Value != e.DefenderCharacterId) return;

        mNotifications.ShowWarning($"{e.AttackerNickname} напал на вас!");

        // Сюда намеренно НЕ добавлен автопереход в экран боя — это отдельная задача
        // (нужна привязка CombatId к CombatPresenter), не в этом заходе.
    }

    /// <summary>Соединение (пере)установлено — пропущенные за паузу дельты сервер не хранит,
    /// тянем полный снимок локации заново.</summary>
    private void OnRealtimeResynced()
    {
        RefreshAsync(mLifetimeCts.Token).Forget();
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

                var remaining = mLockedUntilUtc.Value - DateTime.UtcNow;
                var secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);

                if (secondsLeft > 0)
                {
                    mTimerText.Value = FormatTimer(secondsLeft);
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
                    continue;
                }

                mTimerText.Value = "—";
                var response = await mLocationService.GetCurrentAsync(ct);

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
        mRealtime.MobStateChanged -= OnMobStateChanged;
        mRealtime.PlayerEntered -= OnPlayerEntered;
        mRealtime.PlayerLeft -= OnPlayerLeft;
        mRealtime.CombatStarted -= OnCombatStarted;
        mRealtime.Resynced -= OnRealtimeResynced;

        StopTimer();
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose();
    }
}

