using System.Threading;
using UnityEngine;
using Zenject;

/// <summary>
/// «Руки и глаза» бота: ссылки на живые презенторы и сервисы игры + лог/статистика/токен отмены.
/// Один экземпляр на прогон. Передаётся в каждый шаг/операцию.
///
/// ВАЖНО: презенторы — те же самые singleton-объекты, что видит игрок. Бот дёргает их
/// команды и читает их реактивное состояние. Views подписаны на это состояние, поэтому
/// действия бота видны на экране в реальном времени.
/// </summary>
public sealed class BotContext
{
    public LocationPresenter  Location  { get; }
    public CombatPresenter    Combat    { get; }
    public InventoryPresenter Inventory { get; }

    /// <summary>Сервис локаций — нужен для карты мира и авторитетного «где я сейчас».</summary>
    public ILocationService LocationService { get; }

    /// <summary>Сервис инвентаря — нужен для авторитетного чтения рюкзака/сундука при принятии решений.</summary>
    public IInventoryService InventoryService { get; }

    public IBotLog  Log   { get; }
    public BotStats Stats { get; }

    /// <summary>Токен отмены прогона (Stop / выход из Play Mode).</summary>
    public CancellationToken Ct { get; }

    public BotContext(
        LocationPresenter location, CombatPresenter combat, InventoryPresenter inventory,
        ILocationService locationService, IInventoryService inventoryService,
        IBotLog log, BotStats stats, CancellationToken ct)
    {
        Location = location;
        Combat = combat;
        Inventory = inventory;
        LocationService = locationService;
        InventoryService = inventoryService;
        Log = log;
        Stats = stats;
        Ct = ct;
    }
}

/// <summary>
/// Мост между Editor-окном (вне Zenject) и живой игрой.
/// Находит SceneContext Game-сцены в Play Mode и резолвит из его контейнера презенторы/сервисы.
/// </summary>
public static class BotGameAccess
{
    /// <summary>
    /// Попробовать собрать контекст из запущенной игры.
    /// Возвращает false с текстом причины, если игра ещё не готова (не в Play Mode,
    /// Game-сцена не загружена, контейнер не поднялся).
    /// </summary>
    public static bool TryCreate(IBotLog log, BotStats stats, CancellationToken ct,
        out BotContext context, out string error)
    {
        context = null;
        error = null;

        // Ищем все SceneContext'ы (у ProjectContext отдельный тип — сюда не попадёт).
        var contexts = Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None);
        if (contexts == null || contexts.Length == 0)
        {
            error = "SceneContext не найден. Ты в Play Mode и Game-сцена загружена?";
            return false;
        }

        // Берём тот контейнер, который умеет отдать наши игровые презенторы.
        foreach (var sceneContext in contexts)
        {
            var container = sceneContext.Container;
            if (container == null) continue;

            var location = container.TryResolve<LocationPresenter>();
            if (location == null) continue; // это не Game-сцена

            var combat = container.TryResolve<CombatPresenter>();
            var inventory = container.TryResolve<InventoryPresenter>();
            var locationService = container.TryResolve<ILocationService>();
            var inventoryService = container.TryResolve<IInventoryService>();

            if (combat == null || inventory == null ||
                locationService == null || inventoryService == null)
            {
                error = "Game-сцена найдена, но не все зависимости поднялись " +
                        "(бой/инвентарь/сервисы). Дождись полной загрузки сцены.";
                return false;
            }

            context = new BotContext(
                location, combat, inventory,
                locationService, inventoryService,
                log, stats, ct);
            return true;
        }

        error = "Найден SceneContext, но без игровых презенторов. " +
                "Скорее всего активна не Game-сцена (Bootstrap/Auth?).";
        return false;
    }

    /// <summary>Лёгкая проверка готовности игры — для активации кнопки «Старт» в окне.</summary>
    public static bool IsGameReady()
    {
        var contexts = Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None);
        if (contexts == null) return false;

        foreach (var sceneContext in contexts)
        {
            var container = sceneContext.Container;
            if (container != null && container.TryResolve<LocationPresenter>() != null)
                return true;
        }
        return false;
    }
}
