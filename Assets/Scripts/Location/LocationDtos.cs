using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// ─── GET /api/location/current ───────────────────────────────────────────────

/// <summary>Полное состояние текущей локации — ответ GET /api/location/current.</summary>
public sealed class CurrentLocationResponse
{
    [JsonProperty("locationId")] public long LocationId { get; set; }
    [JsonProperty("code")] public string Code { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("kind")] public string Kind { get; set; }
    [JsonProperty("zoneType")] public string ZoneType { get; set; }
    [JsonProperty("level")] public int Level { get; set; }

    /// <summary>Бой с мобами разрешён. Только UX — сервер всё равно проверяет при engage.</summary>
    [JsonProperty("combatEnabled")] public bool CombatEnabled { get; set; }

    /// <summary>PvP разрешён. Только UX — сервер всё равно проверяет при engage.</summary>
    [JsonProperty("pvpEnabled")] public bool PvpEnabled { get; set; }

    /// <summary>
    /// В локации есть кузнец — по этому флагу показывается кнопка «Кузнец». Только UX: сервер
    /// всё равно проверяет наличие кузнеца при ремонте и при расчёте цены.
    /// </summary>
    [JsonProperty("blacksmithEnabled")] public bool BlacksmithEnabled { get; set; }

    /// <summary>
    /// Можно ли сейчас перейти в другую локацию (сервер проверил таймер + бой + ExitEnabled).
    /// Источник истины — только это поле, не клиентский таймер.
    /// </summary>
    [JsonProperty("canMove")] public bool CanMove { get; set; }

    /// <summary>До этого момента нельзя переходить. UTC. Используется для отображения таймера.</summary>
    [JsonProperty("lockedUntilUtc")] public DateTime? LockedUntilUtc { get; set; }

    /// <summary>Вспомогательное поле: сколько секунд ещё осталось по мнению сервера.</summary>
    [JsonProperty("secondsUntilCanMove")] public int? SecondsUntilCanMove { get; set; }

    /// <summary>
    /// Персонаж мёртв и ждёт воскрешения. Пока true — CanMove всегда false.
    /// Клиент должен показать окно воскрешения (см. LocationPresenter) вместо обычного экрана локации.
    /// </summary>
    [JsonProperty("isAwaitingResurrection")] public bool IsAwaitingResurrection { get; set; }

    [JsonProperty("neighbors")] public List<NeighborDto> Neighbors { get; set; } = new();
    [JsonProperty("mobs")] public List<MobSpawnDto> Mobs { get; set; } = new();
    [JsonProperty("players")] public List<PlayerInLocationDto> Players { get; set; } = new();
    [JsonProperty("dungeonEntrances")] public List<DungeonEntranceDto> DungeonEntrances { get; set; } = new();
}

/// <summary>Соседняя локация (куда можно перейти).</summary>
public sealed class NeighborDto
{
    [JsonProperty("locationId")] public long LocationId { get; set; }
    [JsonProperty("code")] public string Code { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("level")] public int Level { get; set; }
    [JsonProperty("zoneType")] public string ZoneType { get; set; }

    /// <summary>Вход разрешён. false — показываем локацию как закрытую. UX только.</summary>
    [JsonProperty("entryEnabled")] public bool EntryEnabled { get; set; }
}

/// <summary>Спавн моба в текущей локации.</summary>
public sealed class MobSpawnDto
{
    [JsonProperty("spawnId")] public long SpawnId { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("level")] public int Level { get; set; }
    /// <summary>alive / in_combat / dead</summary>
    [JsonProperty("state")] public string State { get; set; }
    [JsonProperty("respawnAt")] public DateTime? RespawnAt { get; set; }
}

/// <summary>Онлайн-игрок в текущей локации.</summary>
public sealed class PlayerInLocationDto
{
    [JsonProperty("characterId")] public long CharacterId { get; set; }
    [JsonProperty("nickname")] public string Nickname { get; set; }
    [JsonProperty("level")] public int Level { get; set; }
}

/// <summary>Вход в данж, доступный из текущей локации.</summary>
public sealed class DungeonEntranceDto
{
    [JsonProperty("dungeonTemplateId")] public long DungeonTemplateId { get; set; }
    [JsonProperty("code")] public string Code { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("minPlayers")] public int MinPlayers { get; set; }
    [JsonProperty("maxPlayers")] public int MaxPlayers { get; set; }
}

// ─── POST /api/location/move ──────────────────────────────────────────────────

/// <summary>Ответ после успешного перехода в другую локацию.</summary>
public sealed class MoveResponse
{
    [JsonProperty("newLocationId")] public long NewLocationId { get; set; }
    [JsonProperty("locationName")] public string LocationName { get; set; }
    /// <summary>До этого момента нельзя переходить дальше из новой локации.</summary>
    [JsonProperty("lockedUntilUtc")] public DateTime LockedUntilUtc { get; set; }
    [JsonProperty("stayDurationSeconds")] public int StayDurationSeconds { get; set; }
}

// ─── GET /api/location/map ────────────────────────────────────────────────────

/// <summary>Граф всего мира — ответ GET /api/location/map.</summary>
public sealed class MapResponse
{
    [JsonProperty("locations")] public List<MapLocationDto> Locations { get; set; } = new();
    [JsonProperty("edges")] public List<MapEdgeDto> Edges { get; set; } = new();
}

/// <summary>Локация на карте мира.</summary>
public sealed class MapLocationDto
{
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("code")] public string Code { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("kind")] public string Kind { get; set; }
    [JsonProperty("zoneType")] public string ZoneType { get; set; }
    [JsonProperty("level")] public int Level { get; set; }
    [JsonProperty("mapX")] public int MapX { get; set; }
    [JsonProperty("mapY")] public int MapY { get; set; }
    [JsonProperty("isStart")] public bool IsStart { get; set; }
    [JsonProperty("stayDurationSeconds")] public int StayDurationSeconds { get; set; }
}

/// <summary>Ребро графа карты (переход между двумя локациями).</summary>
public sealed class MapEdgeDto
{
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("fromLocationId")] public long FromLocationId { get; set; }
    [JsonProperty("toLocationId")] public long ToLocationId { get; set; }
}
