using UnityEngine;

/// <summary>
/// Конфиг экрана охоты. Все числовые параметры — здесь, не в коде.
/// Создать: ПКМ в Project → Create → MMORPG → HuntingConfig.
/// Хранить в Assets/Configs/HuntingConfig.asset.
/// </summary>
[CreateAssetMenu(fileName = "HuntingConfig", menuName = "MMORPG/HuntingConfig")]
public class HuntingConfig : ScriptableObject
{
    [Header("Блуждание мобов")]
    [Tooltip("Минимальное время между сменой цели (сек)")]
    public float WanderIntervalMin = 2f;

    [Tooltip("Максимальное время между сменой цели (сек)")]
    public float WanderIntervalMax = 5f;

    [Tooltip("Скорость перемещения моба (пикселей в секунду)")]
    public float WanderSpeed = 80f;

    [Tooltip("Отступ от края зоны мобов (пиксели). Моб не выходит за эту границу.")]
    public float WanderPadding = 70f;

    [Header("Размеры зон")]
    [Tooltip("Высота зоны мобов в ScrollRect Content (пиксели). ~1 экран = 1920.")]
    public float MobsAreaHeight = 1000f;

    [Tooltip("Высота строки игрока в списке (пиксели)")]
    public float PlayerRowHeight = 100f;
}
