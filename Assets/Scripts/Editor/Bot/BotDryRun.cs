using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

/// <summary>
/// Состояние сухого прогона: реальные данные (карта, инвентарь, сундук), снятые ОДИН раз
/// в начале, + симулируемая текущая локация (GoTo двигает её без реального перехода).
/// Валидаторы шагов читают отсюда и сообщают о проблемах ДО запуска.
/// </summary>
public sealed class BotDryRunState
{
    public MapResponse Map;
    public string SimulatedLocationCode;
    public InventoryResponseDto Inventory;
    public ChestResponseDto Chest; // null, если недоступен/не смогли получить

    public bool LocationExists(string code)
        => Map?.Locations != null && Map.Locations.Any(l => l.Code == code);

    /// <summary>Есть ли путь между локациями (рёбра двусторонние — как в NavigationOps).</summary>
    public bool PathExists(string fromCode, string toCode)
    {
        if (Map?.Locations == null || Map.Edges == null) return false;

        var from = Map.Locations.FirstOrDefault(l => l.Code == fromCode);
        var to = Map.Locations.FirstOrDefault(l => l.Code == toCode);
        if (from == null || to == null) return false;
        if (from.Id == to.Id) return true;

        var adjacency = new Dictionary<long, List<long>>();
        void AddEdge(long a, long b)
        {
            if (!adjacency.TryGetValue(a, out var list)) adjacency[a] = list = new List<long>();
            if (!list.Contains(b)) list.Add(b);
        }
        foreach (var edge in Map.Edges)
        {
            AddEdge(edge.FromLocationId, edge.ToLocationId);
            AddEdge(edge.ToLocationId, edge.FromLocationId);
        }

        var visited = new HashSet<long> { from.Id };
        var queue = new Queue<long>();
        queue.Enqueue(from.Id);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node == to.Id) return true;
            if (!adjacency.TryGetValue(node, out var neighbors)) continue;
            foreach (var next in neighbors)
                if (visited.Add(next)) queue.Enqueue(next);
        }
        return false;
    }
}

/// <summary>
/// Шаг, умеющий проверить себя ДО запуска (сухой прогон).
/// Возвращает null = ок, строку = описание проблемы. Может двигать симулируемое
/// состояние (GoTo меняет SimulatedLocationCode).
/// </summary>
public interface IBotStepValidator
{
    UniTask<string> ValidateAsync(BotDryRunState state);
}

/// <summary>
/// Сухой прогон сценария: собирает реальные данные (карта/инвентарь/сундук) и прогоняет
/// по ним валидаторы шагов. Ловит опечатки в кодах локаций, недостижимые пути,
/// отсутствующие сеты — ДО того, как бот 15 минут будет идти не туда.
/// Ничего в игре не меняет.
/// </summary>
public static class BotDryRun
{
    public static async UniTask RunAsync(BotScenario scenario, BotContext ctx)
    {
        ctx.Log.Info(BotChannel.Assert, $"🔎 Сухой прогон «{scenario.Name}»…");

        var state = new BotDryRunState();
        try
        {
            state.Map = await ctx.LocationService.GetMapAsync(ctx.Ct);
            var current = await ctx.LocationService.GetCurrentAsync(ctx.Ct);
            state.SimulatedLocationCode = current.Code;
            state.Inventory = await ctx.InventoryService.GetInventoryAsync(ctx.Ct);

            try { state.Chest = await ctx.InventoryService.GetChestAsync(ctx.Ct); }
            catch { state.Chest = null; }
        }
        catch (Exception ex)
        {
            ctx.Log.Error(BotChannel.Assert, $"Не смог собрать данные для проверки: {ex.Message}");
            return;
        }

        int issues = 0, checkedSteps = 0;
        await WalkAsync(scenario.Steps, state, ctx, x => issues += x, x => checkedSteps += x);

        if (issues == 0)
            ctx.Log.Info(BotChannel.Assert, $"✅ Сухой прогон чист: проверено шагов {checkedSteps}, проблем нет.");
        else
            ctx.Log.Warn(BotChannel.Assert, $"Сухой прогон: проверено {checkedSteps}, найдено проблем: {issues}. " +
                                            "Это предупреждения, запуск не блокируется.");
    }

    /// <summary>Рекурсивный обход шагов (Repeat раскрывается внутрь).</summary>
    private static async UniTask WalkAsync(IReadOnlyList<IBotStep> steps, BotDryRunState state,
        BotContext ctx, Action<int> addIssues, Action<int> addChecked)
    {
        foreach (var step in steps)
        {
            if (step is RepeatStep repeat)
            {
                // Внутренности повтора проверяем один раз (данные не меняются между кругами).
                await WalkAsync(repeat.InnerSteps, state, ctx, addIssues, addChecked);
                continue;
            }

            if (step is not IBotStepValidator validator) continue;

            addChecked(1);
            string issue;
            try { issue = await validator.ValidateAsync(state); }
            catch (Exception ex) { issue = $"проверка упала: {ex.Message}"; }

            if (issue != null)
            {
                addIssues(1);
                ctx.Log.Warn(BotChannel.Assert, $"«{step.Describe}»: {issue}");
            }
        }
    }
}
