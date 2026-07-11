using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using UnityEngine;
using Zenject;

/// <inheritdoc cref="ILocationRealtimeService" />
public sealed class LocationRealtimeService : DisposableObject, ILocationRealtimeService, IInitializable
{
    private readonly RealtimeConnection mConnection;
    private readonly CancellationTokenSource mLifetimeCts = new();

    /// <summary>Куда presenter просит нас вступить (последнее значение из SetCurrentLocationAsync).</summary>
    private long? mDesiredLocationId;

    /// <summary>В какой группе реально состоим на сервере прямо сейчас.</summary>
    private long? mJoinedLocationId;

    /// <summary>Защита от гонки при быстрой смене локации подряд или совпадении
    /// SetCurrentLocationAsync с реконнектом.</summary>
    private readonly SemaphoreSlim mGroupLock = new(1, 1);

    public event Action<MobStateChangedEvent> MobStateChanged;
    public event Action<PlayerEnteredEvent> PlayerEntered;
    public event Action<PlayerLeftEvent> PlayerLeft;
    public event Action<CombatStartedEvent> CombatStarted;
    public event Action<ChatMessageView> ChatMessageReceived;
    public event Action Resynced;

    [Inject]
    public LocationRealtimeService(ApiConfig apiConfig, ITokenStorage tokenStorage, RealtimeConfig config)
    {
        mConnection = new RealtimeConnection(apiConfig, tokenStorage, config, HubPaths.LOCATION);
        AutoDispose(mConnection);

        mConnection.RegisterHandler<MobStateChangedEvent>("MobStateChanged", e => MobStateChanged?.Invoke(e));
        mConnection.RegisterHandler<PlayerEnteredEvent>("PlayerEntered", e => PlayerEntered?.Invoke(e));
        mConnection.RegisterHandler<PlayerLeftEvent>("PlayerLeft", e => PlayerLeft?.Invoke(e));
        mConnection.RegisterHandler<CombatStartedEvent>("CombatStarted", e => CombatStarted?.Invoke(e));
        // Чат локации приходит "ChatMessage" на ЭТОМ ЖЕ соединении — сервер переиспользует
        // группу "location:{id}", отдельная подписка/соединение не нужны (см. ChatHub.cs).
        mConnection.RegisterHandler<ChatMessageView>("ChatMessage", e => ChatMessageReceived?.Invoke(e));

        mConnection.Resynced += OnConnectionResynced;
    }

    // ─── IInitializable ─────────────────────────────────────────────────────

    public void Initialize()
    {
        mConnection.StartAsync(mLifetimeCts.Token).Forget();
    }

    // ─── Публичные команды ──────────────────────────────────────────────────

    public UniTask StartAsync(CancellationToken ct) => mConnection.StartAsync(ct);

    public async UniTask SetCurrentLocationAsync(long locationId, CancellationToken ct)
    {
        mDesiredLocationId = locationId;
        await RejoinIfNeededAsync(ct);
    }

    // ─── Обработчики ────────────────────────────────────────────────────────

    private void OnConnectionResynced()
    {
        // Соединение новое (первый коннект или реконнект) — сервер не помнит наше
        // прошлое членство в группах, форсируем повторный Join текущей желаемой локации.
        mJoinedLocationId = null;
        RejoinIfNeededAsync(mLifetimeCts.Token).Forget();
        Resynced?.Invoke();
    }

    // ─── Внутреннее ─────────────────────────────────────────────────────────

    private async UniTask RejoinIfNeededAsync(CancellationToken ct)
    {
        if (mConnection.State.Value != HubConnectionState.Connected) return;
        if (mDesiredLocationId == mJoinedLocationId) return;

        await mGroupLock.WaitAsync(ct);
        try
        {
            // Повторная проверка — за время ожидания лока состояние уже могло совпасть.
            if (mDesiredLocationId == mJoinedLocationId) return;
            if (mConnection.State.Value != HubConnectionState.Connected) return;

            var previous = mJoinedLocationId;
            var target = mDesiredLocationId;

            if (previous.HasValue)
            {
                try
                {
                    await mConnection.Connection.InvokeAsync("LeaveLocation", previous.Value, ct);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LocationRealtimeService] LeaveLocation({previous}): {ex.Message}");
                }
            }

            if (target.HasValue)
            {
                try
                {
                    await mConnection.Connection.InvokeAsync("JoinLocation", target.Value, ct);
                    mJoinedLocationId = target;
                    Debug.Log($"[LocationRealtimeService] Вступил в группу локации {target}.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LocationRealtimeService] JoinLocation({target}): {ex.Message}");
                }
            }
            else
            {
                mJoinedLocationId = null;
            }
        }
        finally
        {
            mGroupLock.Release();
        }
    }

    protected override void OnDispose()
    {
        mConnection.Resynced -= OnConnectionResynced;
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        mGroupLock.Dispose();
        base.OnDispose();
    }
}
