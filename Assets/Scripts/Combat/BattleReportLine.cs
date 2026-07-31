/// <summary>
/// Одна строка таблицы участников в окне результата боя: кто был в замесе и сколько отработал.
///
/// Собирается BattleReportPresenter из CombatStateResponse.SideA/SideB. Presenter не ссылается
/// на UnityEngine, поэтому цвета и значки резолвит View (см. Item_BattleParticipant) — здесь
/// только признаки, по которым он это делает.
/// </summary>
public sealed class BattleReportLine
{
    public string Name { get; set; }

    /// <summary>
    /// Боец нашей стороны. Считается сравнением Side с нашей собственной стороной из
    /// CombatStateResponse.You — «своя» сторона у каждого участника боя своя, абсолютного
    /// понятия «SideA — хорошие» не существует.
    /// </summary>
    public bool IsAlly { get; set; }

    /// <summary>Моб, а не игрок. Мобы показываются наравне: они тоже бьют и тоже участники.</summary>
    public bool IsMob { get; set; }

    /// <summary>Пережил бой.</summary>
    public bool IsAlive { get; set; }

    /// <summary>Суммарный урон за бой, включая урон ядом (кредит идёт отравителю).</summary>
    public long DamageDealt { get; set; }
}
