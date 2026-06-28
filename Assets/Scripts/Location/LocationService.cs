using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;

/// <summary>
/// Реализация сервиса локаций.
/// Тонкая обёртка над IApiClient — никакой логики, только HTTP-вызовы.
/// Вся валидация и логика — на сервере.
/// </summary>
public class LocationService : ILocationService
{
    private readonly IApiClient mApiClient;

    [Inject]
    public LocationService(IApiClient apiClient)
    {
        mApiClient = apiClient;
    }

    /// <inheritdoc />
    public UniTask<CurrentLocationResponse> GetCurrentAsync(CancellationToken ct = default)
        => mApiClient.GetAsync<CurrentLocationResponse>("/api/location/current", ct);

    /// <inheritdoc />
    public UniTask<MoveResponse> MoveAsync(long targetLocationId, CancellationToken ct = default)
        => mApiClient.PostAsync<MoveResponse>(
            "/api/location/move",
            new { targetLocationId },
            ct);

    /// <inheritdoc />
    public UniTask<MapResponse> GetMapAsync(CancellationToken ct = default)
        => mApiClient.GetAsync<MapResponse>("/api/location/map", ct);
}
