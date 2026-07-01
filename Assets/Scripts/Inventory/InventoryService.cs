using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;

/// <summary>
/// Реализация IInventoryService через IApiClient (UnityWebRequest + UniTask).
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IApiClient mClient;

    [Inject]
    public InventoryService(IApiClient client) => mClient = client;

    public UniTask<InventoryResponseDto> GetInventoryAsync(CancellationToken ct = default) =>
        mClient.GetAsync<InventoryResponseDto>("/api/gear/inventory", ct);

    public UniTask<ChestResponseDto> GetChestAsync(CancellationToken ct = default) =>
        mClient.GetAsync<ChestResponseDto>("/api/gear/chest", ct);

    public UniTask<ConsumableStacksResponseDto> GetStacksAsync(CancellationToken ct = default) =>
        mClient.GetAsync<ConsumableStacksResponseDto>("/api/consumables/stacks", ct);

    public UniTask EquipAsync(long instanceId, CancellationToken ct = default) =>
        mClient.PostAsync("/api/gear/equip", new { itemInstanceId = instanceId }, ct);

    public UniTask UnequipAsync(long instanceId, CancellationToken ct = default) =>
        mClient.PostAsync("/api/gear/unequip", new { itemInstanceId = instanceId }, ct);

    public UniTask<RepairResponseDto> RepairAsync(long instanceId, CancellationToken ct = default) =>
        mClient.PostAsync<RepairResponseDto>("/api/gear/repair", new { itemInstanceId = instanceId }, ct);

    public UniTask<ChestMoveResponseDto> DepositToChestAsync(long instanceId, CancellationToken ct = default) =>
        mClient.PostAsync<ChestMoveResponseDto>("/api/gear/chest/deposit", new { itemInstanceId = instanceId }, ct);

    public UniTask<ChestMoveResponseDto> WithdrawFromChestAsync(long instanceId, CancellationToken ct = default) =>
        mClient.PostAsync<ChestMoveResponseDto>("/api/gear/chest/withdraw", new { itemInstanceId = instanceId }, ct);

    public UniTask DiscardAsync(long instanceId, CancellationToken ct = default) =>
        mClient.PostAsync("/api/gear/discard", new { itemInstanceId = instanceId }, ct);
}
