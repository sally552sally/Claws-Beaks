using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Комбо-индикатор. Показывает текущую последовательность ударов в виде букв.
///   Г = Голова, Т = Тело, Н = Ноги
///
/// Примеры (комбо [Г,Т,Н], прогресс на шаге 1):
///   [Г] Т Н  — текущий шаг в скобках
///   Г [Т] Н
///   Г Т [Н]
///
/// При нескольких комбо — кнопки ← / → для переключения.
///
/// Инициализируется из View_Combat через Init(presenter, lifeScope).
/// </summary>
public sealed class ComboIndicatorView : MonoBehaviour
{
    [SerializeField] private TMP_Text mLabelSequence;
    [SerializeField] private Button   mButtonPrev;
    [SerializeField] private Button   mButtonNext;
    [SerializeField] private TMP_Text mLabelFinisher;

    private CombatPresenter mPresenter;
    private ILifeScope      mLifeScope;

    // ─── Инициализация ────────────────────────────────────────────────────────

    /// <summary>
    /// Вызывается из View_Combat.SafeAwake().
    /// Нельзя инжектить через Zenject напрямую — ComboIndicatorView вложен в Panel_Combat.
    /// </summary>
    public void Init(CombatPresenter presenter, ILifeScope lifeScope)
    {
        mPresenter = presenter;
        mLifeScope = lifeScope;

        mPresenter.CurrentComboDisplay
            .SubscribeOnValueChanged(_ => Rebuild())
            .DisposeWhenLifeEnded(mLifeScope);

        mPresenter.ComboStep
            .SubscribeOnValueChanged(_ => Rebuild())
            .DisposeWhenLifeEnded(mLifeScope);

        if (mButtonPrev != null)
            mButtonPrev.SubscribeOnClick(mPresenter.PrevCombo).DisposeWhenLifeEnded(mLifeScope);

        if (mButtonNext != null)
            mButtonNext.SubscribeOnClick(mPresenter.NextCombo).DisposeWhenLifeEnded(mLifeScope);
    }

    // ─── Обновление ───────────────────────────────────────────────────────────

    private void Rebuild()
    {
        if (mPresenter == null || mLabelSequence == null) return;

        var seq  = mPresenter.CurrentComboDisplay.Value;
        int step = mPresenter.ComboStep.Value;

        if (seq == null || seq.Count == 0)
        {
            mLabelSequence.text = "—";
            return;
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < seq.Count; i++)
        {
            if (i > 0) sb.Append(' ');

            bool isCurrent = (i == step && step < seq.Count);
            if (isCurrent)
            {
                // Подсветка текущего шага жёлтым через rich text
                sb.Append("<color=#FFD700>[");
                sb.Append(seq[i]);
                sb.Append("]</color>");
            }
            else if (i < step)
            {
                // Уже выполненные шаги — серые
                sb.Append("<color=#888888>");
                sb.Append(seq[i]);
                sb.Append("</color>");
            }
            else
            {
                sb.Append(seq[i]);
            }
        }

        mLabelSequence.text = sb.ToString();
    }
}
