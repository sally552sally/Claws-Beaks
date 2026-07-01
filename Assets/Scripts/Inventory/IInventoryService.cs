using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// HTTP-клиент инвентаря/шмота/сундука/расходки.
/// Все расчёты и проверки — на сервере; клиент шлёт намерения и отображает результат.
/// </summary>
public interface IInventoryService
{
    /// <summary>Рюкзак + надетое + флаг доступности сундука здесь.</summary>
    UniTask<InventoryResponseDto> GetInventoryAsync(CancellationToken ct = default);

    /// <summary>Содержимое личного сундука (если доступен в локации).</summary>
    UniTask<ChestResponseDto> GetChestAsync(CancellationToken ct = default);

    /// <summary>Вся расходка персонажа (вкладка «Эффекты»).</summary>
    UniTask<ConsumableStacksResponseDto> GetStacksAsync(CancellationToken ct = default);

    /// <summary>Надеть предмет из рюкзака.</summary>
    UniTask EquipAsync(long instanceId, CancellationToken ct = default);

    /// <summary>Снять предмет в рюкзак (или сундук, если рюкзак полон — решает сервер).</summary>
    UniTask UnequipAsync(long instanceId, CancellationToken ct = default);

    /// <summary>Починить предмет за золото.</summary>
    UniTask<RepairResponseDto> RepairAsync(long instanceId, CancellationToken ct = default);

    /// <summary>Положить вещь из рюкзака в сундук.</summary>
    UniTask<ChestMoveResponseDto> DepositToChestAsync(long instanceId, CancellationToken ct = default);

    /// <summary>Достать вещь из сундука в рюкзак.</summary>
    UniTask<ChestMoveResponseDto> WithdrawFromChestAsync(long instanceId, CancellationToken ct = default);

    /// <summary>Выбросить (уничтожить) предмет из рюкзака.</summary>
    UniTask DiscardAsync(long instanceId, CancellationToken ct = default);
}
