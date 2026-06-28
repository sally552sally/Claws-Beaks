using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Сервис взаимодействия с локациями через REST API.
/// Все данные — с сервера. Клиент не вычисляет ничего самостоятельно.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Получить полное состояние текущей локации персонажа.
    /// Вызывается при инициализации, после перехода и при обновлении по запросу.
    /// </summary>
    UniTask<CurrentLocationResponse> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>
    /// Перейти в соседнюю локацию.
    /// targetLocationId берётся строго из NeighborDto.LocationId (серверный ответ).
    /// После успеха — вызвать GetCurrentAsync для получения данных новой локации.
    /// </summary>
    UniTask<MoveResponse> MoveAsync(long targetLocationId, CancellationToken ct = default);

    /// <summary>
    /// Получить граф всего мира для отрисовки карты.
    /// Карта статична — запрашивается один раз при открытии View_Map.
    /// </summary>
    UniTask<MapResponse> GetMapAsync(CancellationToken ct = default);
}
