using System;
using Newtonsoft.Json;

// ─── Push-события LocationHub (сервер → клиент), см. ILocationNotifier ────────
// Имена полей — camelCase, как сервер сериализует все остальные DTO проекта.

/// <summary>Состояние спавна моба изменилось.</summary>
public sealed class MobStateChangedEvent
{
    [JsonProperty("spawnId")] public long SpawnId { get; set; }

    /// <summary>alive / in_combat / dead</summary>
    [JsonProperty("state")] public string State { get; set; }

    [JsonProperty("respawnAt")] public DateTime? RespawnAt { get; set; }
}

/// <summary>Игрок вошёл в текущую локацию.</summary>
public sealed class PlayerEnteredEvent
{
    [JsonProperty("characterId")] public long CharacterId { get; set; }
    [JsonProperty("nickname")] public string Nickname { get; set; }
    [JsonProperty("level")] public int Level { get; set; }
}

/// <summary>Игрок покинул текущую локацию.</summary>
public sealed class PlayerLeftEvent
{
    [JsonProperty("characterId")] public long CharacterId { get; set; }
}

/// <summary>
/// PvP-бой начался. Событие приходит ВСЕМ в локации, не только участникам —
/// сервер не фильтрует адресата (см. комментарий в ILocationNotifier), фильтрация
/// по DefenderCharacterId — на клиенте (см. LocationPresenter.OnCombatStarted).
/// </summary>
public sealed class CombatStartedEvent
{
    [JsonProperty("combatId")] public long CombatId { get; set; }
    [JsonProperty("attackerCharacterId")] public long AttackerCharacterId { get; set; }
    [JsonProperty("attackerNickname")] public string AttackerNickname { get; set; }
    [JsonProperty("defenderCharacterId")] public long DefenderCharacterId { get; set; }
}
