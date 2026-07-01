using System;
using System.Collections.Generic;

/// <summary>Готовый сценарий: имя + список шагов + флаг «крутить по кругу до Stop».</summary>
public sealed class BotScenario
{
    public string Name { get; }
    public IReadOnlyList<IBotStep> Steps { get; }
    public bool Loop { get; }

    public BotScenario(string name, IReadOnlyList<IBotStep> steps, bool loop)
    {
        Name = name;
        Steps = steps;
        Loop = loop;
    }
}

/// <summary>
/// Fluent-билдер сценариев. Каждый метод = одна понятная операция.
/// Пиши сценарии в BotScenarios.cs. Пример:
/// <code>
/// using static BotScenarioBuilder;
///
/// Scenario("Фарм")
///     .GoTo("loc_forest")
///     .EquipSet(1)
///     .KillMobs(10)
///     .Snapshot("итог")
///     .Loop()          // крутить сначала, пока не нажмёшь Stop
///     .Build();
/// </code>
/// </summary>
public sealed class BotScenarioBuilder
{
    private readonly string mName;
    private readonly List<IBotStep> mSteps = new();
    private bool mLoop;

    private BotScenarioBuilder(string name) => mName = name;

    /// <summary>Начать новый сценарий с именем (показывается в окне бота).</summary>
    public static BotScenarioBuilder Scenario(string name) => new(name);

    // ─── Навигация ────────────────────────────────────────────────────────────

    /// <summary>Дойти до локации по её коду (путь бот строит сам).</summary>
    public BotScenarioBuilder GoTo(string locationCode)
    {
        mSteps.Add(new GoToStep(locationCode));
        return this;
    }

    // ─── Бой ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Убить N мобов в текущей локации. policy — стратегия боя (null = простая по умолчанию:
    /// обычная стойка, по комбо, без хила).
    /// </summary>
    public BotScenarioBuilder KillMobs(int count, ICombatPolicy policy = null)
    {
        mSteps.Add(new KillMobsStep(count, policy));
        return this;
    }

    // ─── Инвентарь ──────────────────────────────────────────────────────────────

    /// <summary>Надеть из рюкзака все вещи сета (по SetId).</summary>
    public BotScenarioBuilder EquipSet(long setId)
    {
        mSteps.Add(new EquipSetStep(setId));
        return this;
    }

    /// <summary>Надеть одну вещь по коду.</summary>
    public BotScenarioBuilder EquipItem(string code)
    {
        mSteps.Add(new EquipItemStep(code));
        return this;
    }

    /// <summary>Снять всё надетое в рюкзак.</summary>
    public BotScenarioBuilder UnequipAll()
    {
        mSteps.Add(new UnequipAllStep());
        return this;
    }

    /// <summary>Сложить сет в сундук (нужна локация с сундуком).</summary>
    public BotScenarioBuilder DepositSetToChest(long setId)
    {
        mSteps.Add(new DepositSetStep(setId));
        return this;
    }

    /// <summary>Достать сет из сундука (нужна локация с сундуком).</summary>
    public BotScenarioBuilder WithdrawSetFromChest(long setId)
    {
        mSteps.Add(new WithdrawSetStep(setId));
        return this;
    }

    // ─── Сервисные ──────────────────────────────────────────────────────────────

    /// <summary>Записать снимок состояния в лог (метка — для читаемости).</summary>
    public BotScenarioBuilder Snapshot(string label)
    {
        mSteps.Add(new SnapshotStep(label));
        return this;
    }

    /// <summary>Пауза на N секунд.</summary>
    public BotScenarioBuilder Wait(float seconds)
    {
        mSteps.Add(new WaitStep(seconds));
        return this;
    }

    /// <summary>
    /// Повторить блок шагов N раз. Внутри — тот же билдер:
    /// <code>.Repeat(3, b => b.KillMobs(1).Snapshot("после боя"))</code>
    /// </summary>
    public BotScenarioBuilder Repeat(int times, Action<BotScenarioBuilder> block)
    {
        var inner = new BotScenarioBuilder(mName + " (repeat)");
        block(inner);
        mSteps.Add(new RepeatStep(times, inner.mSteps));
        return this;
    }

    /// <summary>Проверить, что панель UI видна/скрыта (лёгкий UI-смоук).</summary>
    public BotScenarioBuilder VerifyPanel(string objectName, bool shouldBeActive)
    {
        mSteps.Add(new VerifyPanelStep(objectName, shouldBeActive));
        return this;
    }

    // ─── Финализация ──────────────────────────────────────────────────────────

    /// <summary>Пометить сценарий как зацикленный (повторять целиком, пока не нажмёшь Stop).</summary>
    public BotScenarioBuilder Loop()
    {
        mLoop = true;
        return this;
    }

    /// <summary>Собрать сценарий.</summary>
    public BotScenario Build() => new(mName, mSteps, mLoop);
}
