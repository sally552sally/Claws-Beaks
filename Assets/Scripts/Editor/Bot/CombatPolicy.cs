/// <summary>
/// Что бот делает на одном своём ходу. Ровно повторяет «язык» боя из диздока:
/// опционально расходка → стойка → направление удара (или пропуск).
/// </summary>
public struct BotCombatMove
{
    /// <summary>Если задано — сначала применить эту расходку (templateId из слота лоадаута).</summary>
    public long? ConsumeTemplateId;

    /// <summary>Стойка: "Normal" / "Defensive" / "Aggressive".</summary>
    public string Stance;

    /// <summary>Направление удара: "Head" / "Body" / "Legs".</summary>
    public string Direction;

    /// <summary>true — пропустить ход вместо удара.</summary>
    public bool Skip;
}

/// <summary>
/// Стратегия боя. Позволяет прогонять разное поведение (тупое, по комбо, с хилом),
/// не трогая цикл боя. Читает только публичное реактивное состояние презентора.
/// </summary>
public interface ICombatPolicy
{
    /// <summary>Короткое имя для лога/выбора в UI.</summary>
    string Name { get; }

    /// <summary>Решить, что делать на текущем своём ходу.</summary>
    BotCombatMove Decide(CombatPresenter combat);
}

/// <summary>
/// Простая надёжная политика по умолчанию:
///   — стойка фиксированная (по умолчанию Normal);
///   — направление идём по активному комбо (следующий символ последовательности),
///     если комбо нет/кончилось — бьём в голову;
///   — авто-хил ОПЦИОНАЛЬНЫЙ и выключен по умолчанию: включив, бот при HP ниже порога
///     применяет расходку из указанного слота (мы не «угадываем», где хилка — слот задаёшь ты).
///
/// Настройки — публичные поля, меняешь при создании политики в сценарии.
/// </summary>
public sealed class SimpleCombatPolicy : ICombatPolicy
{
    /// <summary>Стойка на каждый ход.</summary>
    public string Stance = "Normal";

    /// <summary>Идти по активному комбо (true) или всегда бить FixedDirection (false).</summary>
    public bool FollowCombo = true;

    /// <summary>Направление, когда комбо не используется.</summary>
    public string FixedDirection = "Head";

    /// <summary>Включить авто-хил из слота при низком HP.</summary>
    public bool AutoHeal = false;

    /// <summary>Порог HP (доля от макс), ниже которого применяем расходку.</summary>
    public float HealBelowFraction = 0.35f;

    /// <summary>Индекс боевого слота, из которого хилимся (по умолчанию первый).</summary>
    public int HealSlotIndex = 0;

    public string Name => FollowCombo ? $"Простая ({Stance}, по комбо)" : $"Простая ({Stance}, {FixedDirection})";

    public BotCombatMove Decide(CombatPresenter combat)
    {
        var move = new BotCombatMove { Stance = Stance, Direction = ResolveDirection(combat), Skip = false };

        if (AutoHeal)
        {
            int maxHp = combat.MyMaxHp.Value;
            int curHp = combat.MyCurrentHp.Value;
            if (maxHp > 0 && (float)curHp / maxHp <= HealBelowFraction)
            {
                var slots = combat.LoadoutSlots.Value;
                if (slots != null && HealSlotIndex >= 0 && HealSlotIndex < slots.Count)
                {
                    var slot = slots[HealSlotIndex];
                    if (slot?.ConsumableTemplateId != null && slot.QuantityInInventory > 0)
                        move.ConsumeTemplateId = slot.ConsumableTemplateId;
                }
            }
        }

        return move;
    }

    /// <summary>Следующее направление удара с учётом активного комбо.</summary>
    private string ResolveDirection(CombatPresenter combat)
    {
        if (!FollowCombo) return FixedDirection;

        // CurrentComboDisplay — это символы «Г/Т/Н», ComboStep — сколько уже набрано.
        var display = combat.CurrentComboDisplay.Value;
        int step = combat.ComboStep.Value;

        if (display != null && display.Count > 0 && step >= 0 && step < display.Count)
        {
            return display[step] switch
            {
                "Г" => "Head",
                "Т" => "Body",
                "Н" => "Legs",
                _ => FixedDirection
            };
        }

        return FixedDirection;
    }
}
