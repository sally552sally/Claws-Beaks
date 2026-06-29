using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;

/// <summary>
/// Реализация ICombatService через IApiClient (UnityWebRequest + UniTask).
/// </summary>
public sealed class CombatService : ICombatService
{
    private readonly IApiClient mClient;

    [Inject]
    public CombatService(IApiClient client) => mClient = client;

    public UniTask<CombatStateResponse> EngageMobAsync(long spawnId, CancellationToken ct = default) =>
        mClient.PostAsync<CombatStateResponse>(
            "/api/combat/engage", new { targetMobSpawnId = spawnId }, ct);

    public UniTask<CombatStateResponse> EngagePlayerAsync(long characterId, CancellationToken ct = default) =>
        mClient.PostAsync<CombatStateResponse>(
            "/api/combat/engage", new { targetCharacterId = characterId }, ct);

    public UniTask<CombatTurnResultResponse> ActionAsync(
        long sessionId, long targetParticipantId, string stance, string direction,
        CancellationToken ct = default) =>
        mClient.PostAsync<CombatTurnResultResponse>(
            "/api/combat/action",
            new { combatId = sessionId, targetParticipantId, stance, direction }, ct);

    public UniTask<CombatTurnResultResponse> SkipAsync(long sessionId, CancellationToken ct = default) =>
        mClient.PostAsync<CombatTurnResultResponse>(
            $"/api/combat/{sessionId}/skip", new { }, ct);

    public UniTask<CombatStateResponse> GetStateAsync(long sessionId, CancellationToken ct = default) =>
        mClient.GetAsync<CombatStateResponse>($"/api/combat/{sessionId}", ct);

    /// <summary>
    /// Активная сессия персонажа (если есть).
    /// Возвращает null если нет активного боя (сервер вернул JSON null).
    /// </summary>
    public async UniTask<CombatStateResponse> GetCurrentAsync(CancellationToken ct = default)
    {
        try
        {
            // Сервер возвращает null → Newtonsoft десериализует как null ссылку
            return await mClient.GetAsync<CombatStateResponse>("/api/combat/current", ct);
        }
        catch (Exception)
        {
            // Любая ошибка (нет сети, 4xx) = нет активного боя
            return null;
        }
    }

    public UniTask ResurrectAsync(CancellationToken ct = default) =>
        mClient.PostAsync("/api/combat/resurrect", new { }, ct);

    public UniTask<CombatConsumeResponse> ConsumeAsync(
        long sessionId, long templateId, CancellationToken ct = default) =>
        mClient.PostAsync<CombatConsumeResponse>(
            "/api/combat/consume",
            new { combatId = sessionId, consumableTemplateId = templateId }, ct);

    public UniTask<CombosResponse> GetCombosAsync(CancellationToken ct = default) =>
        mClient.GetAsync<CombosResponse>("/api/character/combos", ct);

    public UniTask<CombatLoadoutResponse> GetLoadoutAsync(CancellationToken ct = default) =>
        mClient.GetAsync<CombatLoadoutResponse>("/api/consumables/loadout", ct);
}
