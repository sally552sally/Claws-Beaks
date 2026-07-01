/// <summary>
/// Вкладки инвентаря. Расширяемый список (§6.8 — «будут добавляться другие категории»).
/// Добавить новую категорию = добавить значение сюда и в InventoryPresenter.Tabs;
/// View строит кнопки вкладок по этому списку, разметку править не нужно.
/// </summary>
public enum InventoryTab
{
    /// <summary>Экипировка (6+1 слотов) + рюкзак.</summary>
    Equipment,

    /// <summary>Личный сундук (доступен не везде — гейт по локации).</summary>
    Chest,

    /// <summary>Расходка («Эффекты»): хилки/усилки/яды с TTL. Только просмотр.</summary>
    Effects,

    /// <summary>Ресурсы для профессий (заглушка — контента пока нет).</summary>
    Resources,

    /// <summary>Квестовые предметы (заглушка — контента пока нет).</summary>
    Quests
}

/// <summary>Локализация и метаданные вкладок инвентаря (заголовки, заглушки).</summary>
public static class InventoryTabInfo
{
    /// <summary>Заголовок вкладки для кнопки.</summary>
    public static string Title(InventoryTab tab) => tab switch
    {
        InventoryTab.Equipment => "Снаряжение",
        InventoryTab.Chest     => "Сундук",
        InventoryTab.Effects   => "Эффекты",
        InventoryTab.Resources => "Ресурсы",
        InventoryTab.Quests    => "Квесты",
        _ => tab.ToString()
    };

    /// <summary>Текст-заглушка для пустых пока категорий (null — у вкладки реальный контент).</summary>
    public static string PlaceholderText(InventoryTab tab) => tab switch
    {
        InventoryTab.Resources => "Ресурсы появятся с профессиями.",
        InventoryTab.Quests    => "Квестовые предметы появятся позже.",
        _ => null
    };

    /// <summary>Является ли вкладка заглушкой (контента нет).</summary>
    public static bool IsPlaceholder(InventoryTab tab)
        => tab is InventoryTab.Resources or InventoryTab.Quests;
}
