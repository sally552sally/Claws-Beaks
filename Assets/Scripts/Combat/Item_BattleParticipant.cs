using TMPro;
using UnityEngine;

/// <summary>
/// Одна строка таблицы участников в окне результата боя: имя, урон, признак смерти.
/// ПЕРЕИСПОЛЬЗУЕТСЯ через IViewPool&lt;Item_BattleParticipant&gt; — не создаётся заново
/// на каждую перерисовку (см. Popup_CombatResult.RebuildParticipants).
///
/// Своих цветов не придумывает: свои/чужие и живые/мёртвые красятся константами ниже,
/// потому что Presenter (BattleReportPresenter) про UnityEngine.Color не знает по правилам
/// слоёв проекта и отдаёт только признаки.
///
/// Prefab: Item_BattleParticipant
/// </summary>
public class Item_BattleParticipant : MonoBehaviour
{
    private static readonly Color AllyColor = new(0.60f, 0.85f, 0.60f);
    private static readonly Color EnemyColor = new(0.90f, 0.65f, 0.60f);
    private static readonly Color DeadColor = new(0.45f, 0.45f, 0.48f);

    [SerializeField] private TMP_Text mNameLabel;
    [SerializeField] private TMP_Text mDamageLabel;

    /// <summary>Заполняет строку данными отчёта.</summary>
    public void Setup(BattleReportLine line)
    {
        if (line == null) return;

        if (mNameLabel != null)
        {
            // Мёртвых помечаем крестиком, а не вычёркиванием: в TMP <s> на кириллице
            // с некоторыми шрифтами едет по базовой линии.
            mNameLabel.text = line.IsAlive ? line.Name : $"† {line.Name}";
            mNameLabel.color = ColorFor(line);
        }

        if (mDamageLabel != null)
        {
            mDamageLabel.text = line.DamageDealt.ToString();
            mDamageLabel.color = ColorFor(line);
        }
    }

    /// <summary>Мёртвый приглушается независимо от стороны — иначе строка кричит цветом о трупе.</summary>
    private static Color ColorFor(BattleReportLine line)
    {
        if (!line.IsAlive) return DeadColor;
        return line.IsAlly ? AllyColor : EnemyColor;
    }
}
