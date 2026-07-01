using static BotScenarioBuilder;

/// <summary>
/// ЗДЕСЬ ТЫ ПИШЕШЬ СВОИ СЦЕНАРИИ.
///
/// Правило простое: любой public static метод без параметров, возвращающий BotScenario,
/// автоматически появляется в выпадающем списке окна «MMORPG → Bot». Добавил метод — готово.
///
/// Коды локаций ("loc_forest", "city_home") и номера сетов (1, 2) — ПЛЕЙСХОЛДЕРЫ.
/// Подставь свои реальные коды локаций (из карты мира) и SetId (из вещей).
/// </summary>
public static class BotScenarios
{
    /// <summary>
    /// Твой сценарий: фарм двумя сетами по кругу.
    /// Одеть сет 1 → набить 10 мобов → снимок → в город к сундуку →
    /// снять всё → сет1 в сундук → сет2 из сундука → одеть сет2 →
    /// назад в лес → набить 10 → снимок → и сначала.
    /// </summary>
    public static BotScenario TwoSetsFarm()
        => Scenario("Фарм двух сетов")
            .GoTo("loc_forest")            // ← подставь код своей боевой локации
            .EquipSet(1)                   // ← подставь SetId первого сета
            .KillMobs(10)
            .Snapshot("после сета 1")
            .GoTo("city_home")             // ← подставь код локации с сундуком
            .UnequipAll()
            .DepositSetToChest(1)
            .WithdrawSetFromChest(2)       // ← подставь SetId второго сета
            .EquipSet(2)
            .GoTo("loc_forest")
            .KillMobs(10)
            .Snapshot("после сета 2")
            .Loop()                        // крутить по кругу, пока не нажмёшь Stop
            .Build();

    /// <summary>Просто набить 5 мобов в текущей локации (без хождения).</summary>
    public static BotScenario SimpleKill()
        => Scenario("Набить 5 мобов")
            .KillMobs(5)
            .Snapshot("итог")
            .Build();

    /// <summary>Дойти до локации и набить там 10 мобов.</summary>
    public static BotScenario WalkAndKill()
        => Scenario("Дойти и набить")
            .GoTo("loc_forest")            // ← подставь свой код
            .KillMobs(10)
            .Snapshot("итог")
            .Build();

    /// <summary>
    /// Пример с настроенной боевой политикой и авто-хилом из слота 0.
    /// Показывает, как переопределить поведение боя.
    /// </summary>
    public static BotScenario AggressiveWithHeal()
    {
        var policy = new SimpleCombatPolicy
        {
            Stance = "Aggressive",   // агрессивная стойка
            FollowCombo = true,      // бить по активному комбо
            AutoHeal = true,         // хилиться при низком HP…
            HealSlotIndex = 0,       // …из первого боевого слота
            HealBelowFraction = 0.4f // …когда HP < 40%
        };

        return Scenario("Агрессивно + хил")
            .KillMobs(10, policy)
            .Snapshot("итог")
            .Build();
    }
}
