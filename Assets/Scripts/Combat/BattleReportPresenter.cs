using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Presenter окна результата боя (Popup_CombatResult).
///
/// Окно открывается ДВУМЯ путями, и оба ведут сюда:
///   — живой бой закончился (View_Combat по CombatPresenter.IsFinished) — ShowLive;
///   — игрок ткнул «[Результат боя]» в системной строке чата (ChatPresenter) — ShowHistorical.
///
/// Раньше попап читал состояние прямо из CombatPresenter, а его кнопка OK звала
/// ExitCombatAsync — то есть окно умело показывать только ТЕКУЩИЙ бой и обязано было
/// завершать его при закрытии. Открыть такое окно из чата нельзя в принципе: боя нет,
/// а выход из несуществующего боя ломал бы состояние. Поэтому знание «что показываем» и
/// «что делать при закрытии» вынесено сюда, а попап стал тупым View поверх реактивных полей.
///
/// Зависимость на CombatPresenter осталась, но перевёрнута: не попап лезет в бой, а отчёт
/// забирает у боя награду живого боя и сообщает ему о закрытии своего окна. Дописывать это
/// в сам CombatPresenter нельзя — он и так на восемнадцати зависимостях и сорока с лишним
/// килобайтах, показ чужого прошедшего боя не его ответственность.
///
/// АРХИТЕКТУРА (UnityStyle): чистый C#, без UnityEngine-логики (Debug допустим, как в
/// остальных Presenter'ах проекта).
/// </summary>
public sealed class BattleReportPresenter : DisposableObject
{
    // ─── Реактивное состояние ─────────────────────────────────────────────────

    private readonly Reactive<bool> mIsOpen = new(false);
    private readonly Reactive<CombatOutcome> mOutcome = new(CombatOutcome.None);
    private readonly Reactive<CombatRewardView> mReward = new(null);
    private readonly Reactive<CombatLevelUpView> mLevelUp = new(null);
    private readonly Reactive<List<BattleReportLine>> mParticipants = new(null);
    private readonly Reactive<bool> mIsParticipantsLoading = new(false);
    private readonly Reactive<bool> mParticipantsFailed = new(false);
    private readonly Reactive<bool> mIsParticipantsExpanded = new(false);

    public ReadonlyReactive<bool> IsOpen => mIsOpen.Readonly;

    /// <summary>Исход показываемого боя. Валиден, пока IsOpen = true.</summary>
    public ReadonlyReactive<CombatOutcome> Outcome => mOutcome.Readonly;

    /// <summary>Награда за показываемый бой. null — награды не было или она не наша.</summary>
    public ReadonlyReactive<CombatRewardView> Reward => mReward.Readonly;

    /// <summary>Повышение уровня за показываемый бой. null — уровень не менялся.</summary>
    public ReadonlyReactive<CombatLevelUpView> LevelUp => mLevelUp.Readonly;

    /// <summary>
    /// Состав боя с уроном. null — ещё грузится или загрузить не удалось (различай по
    /// IsParticipantsLoading/ParticipantsFailed: пустая таблица и несостоявшаяся загрузка —
    /// разные вещи, и врать «в бою никого не было» нельзя).
    /// </summary>
    public ReadonlyReactive<List<BattleReportLine>> Participants => mParticipants.Readonly;

    public ReadonlyReactive<bool> IsParticipantsLoading => mIsParticipantsLoading.Readonly;
    public ReadonlyReactive<bool> ParticipantsFailed => mParticipantsFailed.Readonly;

    /// <summary>Развёрнута ли таблица участников. По умолчанию свёрнута — см. ToggleParticipants.</summary>
    public ReadonlyReactive<bool> IsParticipantsExpanded => mIsParticipantsExpanded.Readonly;

    // ─── Внутреннее состояние ─────────────────────────────────────────────────

    /// <summary>Бой, который показываем сейчас. 0 — окно закрыто.</summary>
    private long mSessionId;

    /// <summary>
    /// Показываем ТОЛЬКО ЧТО завершившийся бой, а не историю из чата. Разница ровно в двух
    /// местах: живому окну награда приезжает из CombatPresenter (она уже пришла с добивающим
    /// ходом, сеть дёргать незачем), и закрытие живого окна выводит персонажа из боя.
    /// </summary>
    private bool mIsLive;

    private CancellationTokenSource mLoadCts;
    private readonly CancellationTokenSource mLifetimeCts = new();

    // ─── Зависимости ──────────────────────────────────────────────────────────

    private readonly ICombatService mCombatService;
    private readonly CombatPresenter mCombatPresenter;

    [Inject]
    public BattleReportPresenter(ICombatService combatService, CombatPresenter combatPresenter)
    {
        mCombatService = combatService;
        mCombatPresenter = combatPresenter;

        AutoDispose(
            mIsOpen, mOutcome, mReward, mLevelUp, mParticipants,
            mIsParticipantsLoading, mParticipantsFailed, mIsParticipantsExpanded);

        // Награда живого боя может доехать ПОЗЖЕ открытия окна: если ответ на добивающий ход
        // потерялся, CombatPresenter дочитывает снимок отдельным запросом. Подписка нужна,
        // чтобы уже открытое окно перерисовалось, а не осталось с «Награды нет».
        // callOnSubscribe:false — на старте сцены награды нет, а Show* и так забирает текущую.
        mCombatPresenter.LastReward
            .SubscribeOnValueChanged(OnLiveRewardChanged, callOnSubscribe: false)
            .DisposeWhenLifeEnded(this);

        mCombatPresenter.LastLevelUp
            .SubscribeOnValueChanged(OnLiveLevelUpChanged, callOnSubscribe: false)
            .DisposeWhenLifeEnded(this);
    }

    // ─── Публичные команды ──────────────────────────────────────────────────────

    /// <summary>
    /// Показать результат ТОЛЬКО ЧТО завершившегося боя. Награду берём из CombatPresenter —
    /// она уже пришла с добивающим ходом. Закрытие такого окна выведет персонажа из боя.
    /// </summary>
    public void ShowLive(long sessionId, CombatOutcome outcome)
    {
        Open(sessionId, outcome, isLive: true);

        mReward.Value = mCombatPresenter.LastReward.Value;
        mLevelUp.Value = mCombatPresenter.LastLevelUp.Value;

        LoadParticipantsAsync(sessionId, mLoadCts.Token).Forget();
    }

    /// <summary>
    /// Показать результат ПРОШЕДШЕГО боя — по клику на «[Результат боя]» в чате.
    /// Персонаж в этот момент не в бою, поэтому закрытие окна просто закрывает окно.
    /// </summary>
    public void ShowHistorical(long sessionId, CombatOutcome outcome)
    {
        Open(sessionId, outcome, isLive: false);

        mReward.Value = null;
        mLevelUp.Value = null;

        // За наградой идём только при победе. У поражения и прерывания её не бывает вовсе,
        // а в снимке last-reward лежала бы награда какого-то ДРУГОГО, более раннего боя —
        // запрос ради заведомо отбракованного ответа.
        if (outcome == CombatOutcome.Win)
            LoadRewardAsync(sessionId, mLoadCts.Token).Forget();

        LoadParticipantsAsync(sessionId, mLoadCts.Token).Forget();
    }

    /// <summary>Свернуть/развернуть таблицу участников.</summary>
    public void ToggleParticipants() =>
        mIsParticipantsExpanded.Value = !mIsParticipantsExpanded.Value;

    /// <summary>
    /// Закрыть окно кнопкой OK. Для живого боя это ещё и выход из боя: дальше решает
    /// LocationPresenter по событию CombatEnded (TD-C32) — при победе и прерывании остаёмся
    /// в охоте, при поражении она закрывается и всплывает диалог воскрешения.
    /// </summary>
    public void Close()
    {
        var wasLive = mIsLive;
        CloseInternal();

        if (wasLive)
            mCombatPresenter.ExitCombatAsync().Forget();
    }

    /// <summary>
    /// Закрыть окно живого боя БЕЗ выхода из боя — состояние уже сброшено кем-то другим
    /// (стартовал новый бой, PvP-нападение, ForceExitCombat из диалога воскрешения).
    /// Историческое окно не трогает: оно к текущему бою отношения не имеет и не должно
    /// схлопываться из-за чужих событий.
    /// </summary>
    public void CloseLiveSilently()
    {
        if (!mIsLive) return;
        CloseInternal();
    }

    // ─── Handle-методы ──────────────────────────────────────────────────────────

    private void OnLiveRewardChanged(CombatRewardView reward)
    {
        if (!mIsOpen.Value || !mIsLive) return;
        mReward.Value = reward;
    }

    private void OnLiveLevelUpChanged(CombatLevelUpView levelUp)
    {
        if (!mIsOpen.Value || !mIsLive) return;
        mLevelUp.Value = levelUp;
    }

    // ─── Внутреннее ─────────────────────────────────────────────────────────────

    private void Open(long sessionId, CombatOutcome outcome, bool isLive)
    {
        CancelLoad();
        mLoadCts = CancellationTokenSource.CreateLinkedTokenSource(mLifetimeCts.Token);

        mSessionId = sessionId;
        mIsLive = isLive;

        mOutcome.Value = outcome;
        mParticipants.Value = null;
        mParticipantsFailed.Value = false;
        // Таблица всегда открывается свёрнутой: главное в окне — исход и награда, состав
        // смотрят по желанию. Иначе окно каждого рядового боя с мобом было бы в полэкрана.
        mIsParticipantsExpanded.Value = false;

        mIsOpen.Value = true;
    }

    private void CloseInternal()
    {
        CancelLoad();

        mIsOpen.Value = false;
        mIsLive = false;
        mSessionId = 0;
    }

    private void CancelLoad()
    {
        if (mLoadCts == null) return;

        mLoadCts.Cancel();
        mLoadCts.Dispose();
        mLoadCts = null;
    }

    /// <summary>
    /// Тянет состав боя и накопленный урон. Работает и на завершённых сессиях: сервер
    /// не фильтрует GetState по состоянию боя, проверяет только участие вызывающего —
    /// а системку о бое получают ровно его участники.
    /// </summary>
    private async UniTaskVoid LoadParticipantsAsync(long sessionId, CancellationToken ct)
    {
        mIsParticipantsLoading.Value = true;
        try
        {
            var state = await mCombatService.GetStateAsync(sessionId, ct);

            // Пока ходили в сеть, игрок мог закрыть окно или открыть другой бой — старый
            // ответ применять нельзя, иначе в окне окажется состав чужого замеса.
            if (IsDisposed || !mIsOpen.Value || mSessionId != sessionId) return;

            mParticipants.Value = BuildLines(state);
            mParticipantsFailed.Value = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsDisposed || !mIsOpen.Value || mSessionId != sessionId) return;

            // Молча пустая таблица соврала бы, что в бою никого не было. Показываем отказ.
            mParticipants.Value = null;
            mParticipantsFailed.Value = true;
            Debug.LogWarning($"[BattleReport] Состав боя {sessionId} не загрузился: {ex.Message}");
        }
        finally
        {
            if (!IsDisposed) mIsParticipantsLoading.Value = false;
        }
    }

    /// <summary>
    /// Дочитывает снимок награды для исторического окна. Снимок на сервере ОДИН на персонажа
    /// и перетирается следующим боем, поэтому чужой sessionId — штатная ситуация, а не ошибка:
    /// значит награда от другого боя, и показывать её здесь нельзя.
    /// </summary>
    private async UniTaskVoid LoadRewardAsync(long sessionId, CancellationToken ct)
    {
        try
        {
            var snapshot = await mCombatService.GetLastRewardAsync(ct);
            if (IsDisposed || !mIsOpen.Value || mSessionId != sessionId) return;

            if (snapshot == null) return;
            if (snapshot.SessionId != sessionId)
            {
                Debug.Log($"[BattleReport] Снимок награды от боя {snapshot.SessionId} — " +
                          $"не тот, что в строке ({sessionId}), не показываем.");
                return;
            }

            mReward.Value = snapshot.Reward;
            mLevelUp.Value = snapshot.LevelUp;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BattleReport] Награда боя {sessionId} не загрузилась: {ex.Message}");
        }
    }

    /// <summary>
    /// Раскладывает обе стороны в плоский список: сначала свои, потом чужие, внутри стороны —
    /// по убыванию урона. Своя сторона определяется по нам самим (state.You): абсолютного
    /// «SideA — наши» не существует, для противника всё зеркально.
    /// </summary>
    private static List<BattleReportLine> BuildLines(CombatStateResponse state)
    {
        var result = new List<BattleReportLine>();
        if (state == null) return result;

        var mySide = state.You?.Side;

        AppendSide(result, state.SideA, mySide);
        AppendSide(result, state.SideB, mySide);

        result.Sort((a, b) =>
        {
            if (a.IsAlly != b.IsAlly) return a.IsAlly ? -1 : 1;
            return b.DamageDealt.CompareTo(a.DamageDealt);
        });

        return result;
    }

    private static void AppendSide(
        List<BattleReportLine> target, List<CombatParticipantView> side, string mySide)
    {
        if (side == null) return;

        foreach (var p in side)
        {
            if (p == null) continue;

            target.Add(new BattleReportLine
            {
                Name = p.Name,
                IsAlly = !string.IsNullOrEmpty(mySide) && p.Side == mySide,
                IsMob = p.IsMob,
                IsAlive = p.IsAlive,
                DamageDealt = p.DamageDealt
            });
        }
    }

    protected override void OnDispose()
    {
        CancelLoad();
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose();
    }
}
