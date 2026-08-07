using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// HTTP-клиент кузнеца: предварительный расчёт ремонта и сам ремонт.
/// Все цены и правила — на сервере; клиент шлёт намерения и отображает результат.
/// </summary>
public interface IBlacksmithService
{
    /// <summary>Что и почём починится прямо сейчас. Ничего не меняет.</summary>
    UniTask<RepairQuoteResponseDto> GetRepairQuoteAsync(CancellationToken ct = default);

    /// <summary>Починить всё повреждённое надетое. Атомарно: не хватит золота — не починится ничего.</summary>
    UniTask<RepairAllResponseDto> RepairAllAsync(CancellationToken ct = default);

    /// <summary>Починить одну вещь.</summary>
    UniTask<RepairResponseDto> RepairAsync(long instanceId, CancellationToken ct = default);
}
