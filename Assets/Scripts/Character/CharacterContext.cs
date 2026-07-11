using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <inheritdoc cref="ICharacterContext" />
public sealed class CharacterContext : DisposableObject, ICharacterContext, IInitializable
{
    private readonly IApiClient mApiClient;
    private readonly CancellationTokenSource mLifetimeCts = new();
    private readonly Reactive<long?> mCharacterId = new(null);

    public ReadonlyReactive<long?> CharacterId => mCharacterId.Readonly;

    [Inject]
    public CharacterContext(IApiClient apiClient)
    {
        mApiClient = apiClient;
        AutoDispose(mCharacterId);
    }

    // ─── IInitializable ─────────────────────────────────────────────────────

    public void Initialize()
    {
        LoadAsync(mLifetimeCts.Token).Forget();
    }

    // ─── Внутреннее ─────────────────────────────────────────────────────────

    private async UniTask LoadAsync(CancellationToken ct)
    {
        try
        {
            var response = await mApiClient.GetAsync<MyCharacterResponse>("/api/character", ct);
            if (IsDisposed) return;
            mCharacterId.Value = response.Id;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Не критично для запуска сцены: пока CharacterId не подтянулся, самофильтрация
            // CombatStarted в LocationPresenter просто не сработает ни разу (безопасный отказ,
            // не покажет чужой бой как "напали на вас"). Тостом не беспокоим — это вспомогательные
            // данные, не блокирующие игру.
            Debug.LogWarning($"[CharacterContext] Не удалось получить свой characterId: {ex.Message}");
        }
    }

    protected override void OnDispose()
    {
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose();
    }
}
