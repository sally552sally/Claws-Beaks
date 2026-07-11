/// <summary>
/// «Кто я» для realtime-слоя — пока только CharacterId (см. CharacterContext).
/// Не путать с будущим полноценным сервисом персонажа (статы/уровень/HP) — это
/// отдельная, более крупная задача за пределами текущего захода.
/// </summary>
public interface ICharacterContext
{
    /// <summary>Id персонажа текущего аккаунта. null, пока не подтянут с сервера
    /// (GET /api/character) или если запрос не удался.</summary>
    ReadonlyReactive<long?> CharacterId { get; }
}
