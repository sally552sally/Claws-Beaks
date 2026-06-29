using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// HTTP-клиент для боевых эндпоинтов.
/// Все расчёты на сервере — клиент только отправляет намерения и отображает результат.
/// </summary>
public interface ICombatService
{
    /// <summary>Напасть на моба по SpawnId.</summary>
    UniTask<CombatStateResponse> EngageMobAsync(long spawnId, CancellationToken ct = default);

    /// <summary>Напасть на игрока по CharacterId (PvP).</summary>
    UniTask<CombatStateResponse> EngagePlayerAsync(long characterId, CancellationToken ct = default);

    /// <summary>Сделать ход: стойка + направление удара.</summary>
    UniTask<CombatTurnResultResponse> ActionAsync(
        long sessionId, long targetParticipantId, string stance, string direction,
        CancellationToken ct = default);

    /// <summary>Пропустить ход вручную.</summary>
    UniTask<CombatTurnResultResponse> SkipAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Текущее состояние боя (для polling когда не наш ход).</summary>
    UniTask<CombatStateResponse> GetStateAsync(long sessionId, CancellationToken ct = default);

    /// <summary>
    /// Активная боевая сессия персонажа.
    /// Возвращает null если нет активного боя.
    /// Используется при старте Game-сцены для авто-возобновления после вылета.
    /// </summary>
    UniTask<CombatStateResponse> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Воскресить персонажа после поражения.</summary>
    UniTask ResurrectAsync(CancellationToken ct = default);

    /// <summary>Применить расходник в бою на себя.</summary>
    UniTask<CombatConsumeResponse> ConsumeAsync(
        long sessionId, long templateId, CancellationToken ct = default);

    /// <summary>Комбо-последовательности персонажа (для комбо-индикатора).</summary>
    UniTask<CombosResponse> GetCombosAsync(CancellationToken ct = default);

    /// <summary>Текущий лоадаут боевых слотов расходки.</summary>
    UniTask<CombatLoadoutResponse> GetLoadoutAsync(CancellationToken ct = default);
}
