using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using UnityEngine;
using Zenject;

/// <inheritdoc cref="IChatRealtimeService" />
public sealed class ChatRealtimeService : DisposableObject, IChatRealtimeService, IInitializable
{
    private readonly RealtimeConnection mConnection;
    private readonly ICharacterContext mCharacterContext;
    private readonly CancellationTokenSource mLifetimeCts = new();

    private bool mTradeJoined;
    private long? mPersonalJoinedFor;

    /// <summary>Защита от параллельных вызовов JoinGroupsAsync (реконнект и изменение
    /// CharacterId могут случиться почти одновременно).</summary>
    private readonly SemaphoreSlim mJoinLock = new(1, 1);

    public event Action<ChatMessageView> ChatMessageReceived;

    [Inject]
    public ChatRealtimeService(ApiConfig apiConfig, ITokenStorage tokenStorage, RealtimeConfig config,
        ICharacterContext characterContext)
    {
        mCharacterContext = characterContext;

        mConnection = new RealtimeConnection(apiConfig, tokenStorage, config, HubPaths.CHAT);
        AutoDispose(mConnection);

        mConnection.RegisterHandler<ChatMessageView>("ChatMessage", e => ChatMessageReceived?.Invoke(e));
        mConnection.Resynced += OnConnectionResynced;
    }

    // ─── IInitializable ─────────────────────────────────────────────────────

    public void Initialize()
    {
        mConnection.StartAsync(mLifetimeCts.Token).Forget();

        // callOnSubscribe: true (по умолчанию) — сработает сразу; JoinGroupsAsync внутри
        // сам проверит, что соединение уже Connected, прежде чем реально что-то звать.
        mCharacterContext.CharacterId
            .SubscribeOnValueChanged(OnCharacterIdChanged)
            .DisposeWhenLifeEnded(this);
    }

    public UniTask StartAsync(CancellationToken ct) => mConnection.StartAsync(ct);

    // ─── Обработчики ────────────────────────────────────────────────────────

    private void OnConnectionResynced()
    {
        // Соединение новое (первый коннект или реконнект) — сервер не помнит прошлое
        // членство в группах этого соединения, форсируем повтор обеих подписок.
        mTradeJoined = false;
        mPersonalJoinedFor = null;
        JoinGroupsAsync(mLifetimeCts.Token).Forget();
    }

    private void OnCharacterIdChanged(long? characterId)
    {
        JoinGroupsAsync(mLifetimeCts.Token).Forget();
    }

    // ─── Внутреннее ─────────────────────────────────────────────────────────

    private async UniTask JoinGroupsAsync(CancellationToken ct)
    {
        if (mConnection.State.Value != HubConnectionState.Connected) return;

        await mJoinLock.WaitAsync(ct);
        try
        {
            if (mConnection.State.Value != HubConnectionState.Connected) return;

            if (!mTradeJoined)
            {
                try
                {
                    await mConnection.Connection.InvokeAsync("JoinTrade", ct);
                    mTradeJoined = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ChatRealtimeService] JoinTrade: {ex.Message}");
                }
            }

            var characterId = mCharacterContext.CharacterId.Value;
            if (characterId.HasValue && mPersonalJoinedFor != characterId)
            {
                try
                {
                    await mConnection.Connection.InvokeAsync("JoinPersonal", characterId.Value, ct);
                    mPersonalJoinedFor = characterId;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ChatRealtimeService] JoinPersonal({characterId}): {ex.Message}");
                }
            }
        }
        finally
        {
            mJoinLock.Release();
        }
    }

    protected override void OnDispose()
    {
        mConnection.Resynced -= OnConnectionResynced;
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        mJoinLock.Dispose();
        base.OnDispose();
    }
}
