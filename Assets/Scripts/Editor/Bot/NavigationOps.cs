using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

/// <summary>
/// Навигация по миру. UI даёт ходить только к соседям, готового «дойти до X» нет —
/// поэтому строим путь сами по графу карты (GetMapAsync) и шагаем по одному переходу,
/// уважая таймер каждой локации (CanMove с сервера).
///
/// Цель задаётся КОДОМ локации (человекочитаемо), id разруливаем по карте.
/// </summary>
public static class NavigationOps
{
    /// <summary>
    /// Дойти до локации с кодом targetCode. Возвращает false с логом, если пути нет
    /// или переход сорвался (например вход в локацию закрыт).
    /// </summary>
    public static async UniTask<bool> GoToAsync(BotContext ctx, string targetCode)
    {
        var ct = ctx.Ct;

        // Где мы сейчас (авторитетно, с сервера).
        var current = await ctx.LocationService.GetCurrentAsync(ct);

        var map = await ctx.LocationService.GetMapAsync(ct);
        var target = map.Locations.FirstOrDefault(l => l.Code == targetCode);
        if (target == null)
        {
            ctx.Log.Error($"Локация с кодом «{targetCode}» не найдена на карте.");
            return false;
        }

        if (current.LocationId == target.Id)
        {
            ctx.Log.Info($"Уже в «{target.Name}».");
            return true;
        }

        var path = BuildPath(map, current.LocationId, target.Id);
        if (path == null)
        {
            ctx.Log.Error($"Нет пути из «{current.Name}» до «{target.Name}».");
            return false;
        }

        ctx.Log.Info($"Маршрут до «{target.Name}»: {path.Count} перех.");

        foreach (var nextId in path)
        {
            ct.ThrowIfCancellationRequested();

            // Ждём, пока можно перейти (таймер локации).
            await WaitUntilCanMoveAsync(ctx);

            // Переход (MoveAsync внутри гардится по CanMove — мы его уже дождались).
            await ctx.Location.MoveAsync(nextId, ct);
            await BotWait.Until(() => !ctx.Location.IsLoading.Value, BotConfig.MOVE_TIMEOUT, ct);

            // Проверяем авторитетно, что реально перешли.
            current = await ctx.LocationService.GetCurrentAsync(ct);
            if (current.LocationId != nextId)
            {
                ctx.Log.Error($"Переход сорвался: ожидал id={nextId}, оказался в «{current.Name}» " +
                              $"(вход мог быть закрыт).");
                return false;
            }

            ctx.Log.Info($"→ «{current.Name}».");
        }

        return true;
    }

    /// <summary>Ждать CanMove=true, попутно печатая оставшийся таймер (heartbeat).</summary>
    private static async UniTask WaitUntilCanMoveAsync(BotContext ctx)
    {
        if (ctx.Location.CanMove.Value) return;

        ctx.Log.Info("Жду таймер локации…");
        await BotWait.UntilForever(
            () => ctx.Location.CanMove.Value,
            ctx.Ct,
            heartbeat: () =>
            {
                var timer = ctx.Location.TimerText.Value;
                if (!string.IsNullOrEmpty(timer))
                    ctx.Log.Info($"…до перехода {timer}");
            });
    }

    /// <summary>
    /// Поиск в ширину по графу карты. Рёбра считаем двусторонними
    /// (по миру можно ходить туда-обратно). Возвращает список id-переходов
    /// от текущей (не включая) до цели (включая) или null, если пути нет.
    /// </summary>
    private static List<long> BuildPath(MapResponse map, long fromId, long toId)
    {
        // Список смежности (двусторонний).
        var adjacency = new Dictionary<long, List<long>>();
        void AddEdge(long a, long b)
        {
            if (!adjacency.TryGetValue(a, out var list))
            {
                list = new List<long>();
                adjacency[a] = list;
            }
            if (!list.Contains(b)) list.Add(b);
        }

        foreach (var edge in map.Edges)
        {
            AddEdge(edge.FromLocationId, edge.ToLocationId);
            AddEdge(edge.ToLocationId, edge.FromLocationId);
        }

        var queue = new Queue<long>();
        var cameFrom = new Dictionary<long, long>();
        var visited = new HashSet<long> { fromId };
        queue.Enqueue(fromId);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node == toId) break;
            if (!adjacency.TryGetValue(node, out var neighbors)) continue;

            foreach (var next in neighbors)
            {
                if (visited.Contains(next)) continue;
                visited.Add(next);
                cameFrom[next] = node;
                queue.Enqueue(next);
            }
        }

        if (!visited.Contains(toId)) return null;

        // Разворачиваем путь.
        var path = new List<long>();
        var cur = toId;
        while (cur != fromId)
        {
            path.Add(cur);
            cur = cameFrom[cur];
        }
        path.Reverse();
        return path;
    }
}
