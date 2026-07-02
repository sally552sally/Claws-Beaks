using static BotScenarioBuilder;

/// <summary>
/// ЗДЕСЬ ТЫ ПИШЕШЬ СВОИ СЦЕНАРИИ.
///
/// Правила:
///   1) Любой public static метод, возвращающий BotScenario, появляется в списке окна.
///      Метод может быть БЕЗ параметров или с одним параметром BotParams.
///   2) BotParams — параметры, редактируемые ИЗ ОКНА без перекомпиляции:
///      p.Int / p.Float / p.Bool / p.Text — обычные поля;
///      p.Location — выпадашка реальных кодов локаций (тянется с карты);
///      p.SetId — выпадашка реальных SetId из рюкзака/сундука.
///      Дефолты ниже ("loc_forest", 1) — плейсхолдеры на случай пустой выпадашки.
///   3) Кнопка «Проверить» в окне гоняет сухой прогон: ловит опечатки в кодах,
///      недостижимые локации и отсутствующие сеты ДО запуска.
/// </summary>
public static class BotScenarios
{
    /// <summary>
    /// Фарм двумя сетами по кругу: одеть сет 1 → набить мобов → к сундуку →
    /// переодеться в сет 2 (SwapSets) → назад → набить мобов → и сначала.
    /// Все локации/сеты/количество — настраиваются в окне.
    /// </summary>
    public static BotScenario TwoSetsFarm(BotParams p)
    {
        var farm = p.Location("Боевая локация", "loc_forest");
        var city = p.Location("Город с сундуком", "city_home");
        var set1 = p.SetId("Сет 1", 1);
        var set2 = p.SetId("Сет 2", 2);
        var mobs = p.Int("Мобов за заход", 10);

        return Scenario("Фарм двух сетов")
            .GoTo(farm)
            .EquipSet(set1)
            .KillMobs(mobs)
            .Snapshot("после сета 1")
            .GoTo(city)
            .SwapSets(set1, set2)              // снять всё → сет1 в сундук → сет2 из сундука → одеть
            .AssertEquippedSet(set2)           // убеждаемся, что реально переоделись
            .GoTo(farm)
            .KillMobs(mobs)
            .Snapshot("после сета 2")
            .Loop()                            // крутить, пока не нажмёшь Stop
            .Build();
    }

    /// <summary>Просто набить мобов в текущей локации (без хождения).</summary>
    public static BotScenario SimpleKill(BotParams p)
    {
        var count = p.Int("Сколько мобов", 5);

        return Scenario("Набить мобов здесь")
            .KillMobs(count)
            .Snapshot("итог")
            .Build();
    }

    /// <summary>Дойти до локации и набить там мобов.</summary>
    public static BotScenario WalkAndKill(BotParams p)
    {
        var where = p.Location("Куда идти", "loc_forest");
        var count = p.Int("Сколько мобов", 10);

        return Scenario("Дойти и набить")
            .GoTo(where)
            .AssertLocation(where)
            .KillMobs(count)
            .Snapshot("итог")
            .Build();
    }

    /// <summary>
    /// Пример настроенной боевой политики: агрессивная стойка + авто-хил из слота.
    /// Показывает, как переопределить поведение боя.
    /// </summary>
    public static BotScenario AggressiveWithHeal(BotParams p)
    {
        var count = p.Int("Сколько мобов", 10);
        var healSlot = p.Int("Слот хилки (0-3)", 0);

        var policy = new SimpleCombatPolicy
        {
            Stance = "Aggressive",     // агрессивная стойка
            FollowCombo = true,        // бить по активному комбо
            AutoHeal = true,           // хилиться при низком HP…
            HealSlotIndex = healSlot,  // …из указанного боевого слота
            HealBelowFraction = 0.4f   // …когда HP < 40%
        };

        return Scenario("Агрессивно + хил")
            .KillMobs(count, policy)
            .Snapshot("итог")
            .Build();
    }

    /// <summary>
    /// Регресс-тест сундука: положить сет → проверить, что он там → достать →
    /// проверить, что надет обратно. Пример превращения сценария в автотест.
    /// Запускать в локации с сундуком, с надетым сетом.
    /// </summary>
    public static BotScenario ChestRegress(BotParams p)
    {
        var set = p.SetId("Сет для теста", 1);

        return Scenario("Регресс: сундук")
            .UnequipAll()
            .DepositSetToChest(set)
            .AssertChestContains(set)          // сет реально лёг в сундук?
            .WithdrawSetFromChest(set)
            .EquipSet(set)
            .AssertEquippedSet(set)            // сет реально надет обратно?
            .Snapshot("после цикла сундука")
            .Build();
    }
}
