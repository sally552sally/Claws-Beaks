using System;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Настройки прогона, задаются в окне бота перед стартом.
/// </summary>
public sealed class BotOptions
{
    /// <summary>Пауза (сек) после КАЖДОГО действия (ход в бою, мутация инвентаря, переход,
    /// шаг сценария). 0 = без пауз, полная скорость. Удобно ставить 1-2с, чтобы глазами
    /// следить за ботом на экране.</summary>
    public float ActionDelaySeconds = 0f;

    /// <summary>Остановить прогон после N ошибок+проваленных проверок. 0 = не останавливать.</summary>
    public int StopAfterErrors = 0;

    /// <summary>Остановить прогон при смерти персонажа.</summary>
    public bool StopOnDeath = false;

    /// <summary>Делать скриншот при ошибке шага / проваленной проверке (в BotRuns/screens/).</summary>
    public bool ScreenshotOnError = true;

    /// <summary>Показывать оверлей «что делает бот» поверх игры в Play Mode.</summary>
    public bool ShowOverlay = true;

    /// <summary>Автоматически экспортировать лог в файл по окончании прогона.</summary>
    public bool AutoExportOnFinish = true;
}

/// <summary>
/// Живой прогресс прогона: раннер/шаги пишут сюда, окно и оверлей читают.
/// </summary>
public sealed class BotProgress
{
    public string ScenarioName = "";
    public System.Collections.Generic.List<string> StepTitles = new();
    public int CurrentIndex = -1;
    public int Pass;
    public bool Loop;

    /// <summary>Деталь текущего шага (например «3/10» у KillMobs). Шаги обновляют сами.</summary>
    public string Detail = "";

    public string CurrentTitle =>
        CurrentIndex >= 0 && CurrentIndex < StepTitles.Count ? StepTitles[CurrentIndex] : "";

    /// <summary>Текст для оверлея поверх игры.</summary>
    public string OverlayText
    {
        get
        {
            if (StepTitles.Count == 0 || CurrentIndex < 0) return "";
            var pass = Loop ? $" • круг {Pass}" : "";
            var detail = string.IsNullOrEmpty(Detail) ? "" : $" • {Detail}";
            return $"🤖 {ScenarioName}{pass}\nШаг {CurrentIndex + 1}/{StepTitles.Count}: {CurrentTitle}{detail}";
        }
    }

    public void ResetFor(BotScenario scenario)
    {
        ScenarioName = scenario.Name;
        StepTitles.Clear();
        foreach (var step in scenario.Steps) StepTitles.Add(step.Describe);
        CurrentIndex = -1;
        Pass = 0;
        Loop = scenario.Loop;
        Detail = "";
    }
}

/// <summary>
/// «Руки и глаза» бота: ссылки на живые презенторы и сервисы игры + лог/статистика/
/// настройки/прогресс/токен отмены. Один экземпляр на прогон.
///
/// ВАЖНО: презенторы — те же singleton-объекты, что видит игрок. Бот дёргает их команды
/// и читает их реактивное состояние; View подписаны на это же состояние, поэтому действия
/// бота видны на экране в реальном времени.
/// </summary>
public sealed class BotContext
{
    public LocationPresenter  Location  { get; }
    public CombatPresenter    Combat    { get; }
    public InventoryPresenter Inventory { get; }

    /// <summary>
    /// Нужен, чтобы эмулировать клик по кнопке диалога (например «Воскреснуть»).
    /// TD-C32: диалог закрывается ТОЛЬКО через RespondToDialog — прямой вызов
    /// презентера воскрешает персонажа на сервере, но не закрывает саму панель.
    /// </summary>
    public INotificationService Notifications { get; }

    /// <summary>Сервис локаций — карта мира и авторитетное «где я сейчас».</summary>
    public ILocationService LocationService { get; }

    /// <summary>Сервис инвентаря — авторитетное чтение рюкзака/сундука.</summary>
    public IInventoryService InventoryService { get; }

    public IBotLog  Log   { get; }
    public BotStats Stats { get; }

    /// <summary>Настройки прогона (окно задаёт перед стартом).</summary>
    public BotOptions Options { get; set; } = new();

    /// <summary>Живой прогресс (для окна и оверлея).</summary>
    public BotProgress Progress { get; } = new();

    /// <summary>Токен отмены прогона (Stop / выход из Play Mode).</summary>
    public CancellationToken Ct { get; }

    /// <summary>Запрошена мягкая остановка (стоп-условия). Раннер проверяет между шагами.</summary>
    public bool StopRequested { get; private set; }
    public string StopReason { get; private set; }

    public BotContext(
        LocationPresenter location, CombatPresenter combat, InventoryPresenter inventory,
        ILocationService locationService, IInventoryService inventoryService,
        INotificationService notifications,
        IBotLog log, BotStats stats, CancellationToken ct)
    {
        Location = location;
        Combat = combat;
        Inventory = inventory;
        LocationService = locationService;
        InventoryService = inventoryService;
        Notifications = notifications;
        Log = log;
        Stats = stats;
        Ct = ct;
    }

    /// <summary>Мягко запросить остановку (доработает текущий шаг и встанет).</summary>
    public void RequestStop(string reason)
    {
        if (StopRequested) return;
        StopRequested = true;
        StopReason = reason;
        Log.Warn(BotChannel.System, $"Запрошена остановка: {reason}");
    }

    /// <summary>
    /// Пауза после действия (см. Options.ActionDelaySeconds). Зовётся операциями после
    /// каждого хода боя, мутации инвентаря, перехода и раннером после каждого шага.
    /// </summary>
    public async UniTask PauseAfterActionAsync()
    {
        if (Options.ActionDelaySeconds <= 0f) return;
        await UniTask.Delay(TimeSpan.FromSeconds(Options.ActionDelaySeconds), cancellationToken: Ct);
    }

    /// <summary>Скриншот игры в BotRuns/screens/. Возвращает путь или null.</summary>
    public string CaptureScreenshot(string tag)
    {
        try
        {
            Directory.CreateDirectory(BotPaths.ScreensDir);
            var safeTag = string.Concat(tag.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            var file = Path.Combine(BotPaths.ScreensDir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{safeTag}.png");
            ScreenCapture.CaptureScreenshot(file);
            Log.Info(BotChannel.System, $"📷 скриншот: {file}");
            return file;
        }
        catch (Exception ex)
        {
            Log.Warn(BotChannel.System, $"Скриншот не удался: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Мост между Editor-окном (вне Zenject) и живой игрой.
/// Находит SceneContext Game-сцены в Play Mode и резолвит презенторы/сервисы.
/// </summary>
public static class BotGameAccess
{
    /// <summary>
    /// Попробовать собрать контекст из запущенной игры.
    /// false + текст причины, если игра не готова.
    /// </summary>
    public static bool TryCreate(IBotLog log, BotStats stats, CancellationToken ct,
        out BotContext context, out string error)
    {
        context = null;
        error = null;

        var contexts = UnityEngine.Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None);
        if (contexts == null || contexts.Length == 0)
        {
            error = "SceneContext не найден. Ты в Play Mode и Game-сцена загружена?";
            return false;
        }

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
            var notifications = container.TryResolve<INotificationService>();

            if (combat == null || inventory == null ||
                locationService == null || inventoryService == null || notifications == null)
            {
                error = "Game-сцена найдена, но не все зависимости поднялись " +
                        "(бой/инвентарь/сервисы). Дождись полной загрузки сцены.";
                return false;
            }

            context = new BotContext(
                location, combat, inventory,
                locationService, inventoryService, notifications,
                log, stats, ct);
            return true;
        }

        error = "Найден SceneContext, но без игровых презенторов. " +
                "Скорее всего активна не Game-сцена (Bootstrap/Auth?).";
        return false;
    }

    /// <summary>Лёгкая проверка готовности игры — для активации кнопок в окне.</summary>
    public static bool IsGameReady()
    {
        var contexts = UnityEngine.Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None);
        if (contexts == null) return false;

        foreach (var sceneContext in contexts)
        {
            var container = sceneContext.Container;
            if (container != null && container.TryResolve<LocationPresenter>() != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Достать только сервисы (без полного контекста) — окну для выпадашек
    /// локаций/сетов и прочих подсказок.
    /// </summary>
    public static bool TryGetServices(out ILocationService locationService, out IInventoryService inventoryService)
    {
        locationService = null;
        inventoryService = null;

        var contexts = UnityEngine.Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None);
        if (contexts == null) return false;

        foreach (var sceneContext in contexts)
        {
            var container = sceneContext.Container;
            if (container == null) continue;

            locationService = container.TryResolve<ILocationService>();
            inventoryService = container.TryResolve<IInventoryService>();
            if (locationService != null && inventoryService != null) return true;
        }
        return false;
    }
}
