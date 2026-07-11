using Newtonsoft.Json;

// ─── GET /api/character ────────────────────────────────────────────────────

/// <summary>
/// Минимальный срез ответа GET /api/character — только то, что нужно realtime-слою
/// (см. CharacterContext). Сервер отдаёт больше полей (Level/CurrentHp/MaxHp/Gold/Stats) —
/// намеренно не мапим их здесь, они не нужны за пределами будущего полноценного
/// экрана персонажа (отдельная задача, не этот заход).
/// </summary>
public sealed class MyCharacterResponse
{
    [JsonProperty("id")] public long Id { get; set; }
}
