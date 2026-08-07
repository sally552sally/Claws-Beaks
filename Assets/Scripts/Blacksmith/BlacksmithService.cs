using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;

/// <summary>
/// Реализация IBlacksmithService через IApiClient.
/// <para>
/// Ремонт живёт под префиксом /api/shop/*, а не /api/gear/*: на сервере это услуга магазина —
/// тонкая обёртка над Gear, которая даёт клиенту единый префикс и единый тип ошибки.
/// </para>
/// </summary>
public sealed class BlacksmithService : IBlacksmithService
{
    private readonly IApiClient mClient;

    [Inject]
    public BlacksmithService(IApiClient client) => mClient = client;

    public UniTask<RepairQuoteResponseDto> GetRepairQuoteAsync(CancellationToken ct = default) =>
        mClient.GetAsync<RepairQuoteResponseDto>("/api/shop/repair-quote", ct);

    public UniTask<RepairAllResponseDto> RepairAllAsync(CancellationToken ct = default) =>
        mClient.PostAsync<RepairAllResponseDto>("/api/shop/repair-all", new { }, ct);

    public UniTask<RepairResponseDto> RepairAsync(long instanceId, CancellationToken ct = default) =>
        mClient.PostAsync<RepairResponseDto>("/api/shop/repair", new { itemInstanceId = instanceId }, ct);
}
