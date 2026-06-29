using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Presenter экрана боя.
/// Что нового относительно предыдущей версии:
///   — CombatLogText: реактивный текст лога с rich-text цветами
///   — Лог пишет урон (наш / по нам), комбо-финишер, расходку
///   — ConsumeAsync: всегда делает GetStateAsync после применения → HP обновляется
///   — ApplyTurnResultAsync: async с паузой 1с перед ударом моба
/// </summary>
public sealed class CombatPresenter : DisposableObject, IInitializable
{
    private const string STANCE_NORMAL = "Normal";
    private const int POLL_INTERVAL_MS = 2000;
    private const int MAX_LOG_ENTRIES = 80;

    // ─── Реактивное состояние ─────────────────────────────────────────────────

    private readonly Reactive<bool> mIsInCombat = new(false);
    private readonly Reactive<bool> mIsMyTurn = new(false);
    private readonly Reactive<bool> mIsLoading = new(false);
    private readonly Reactive<bool> mIsFinished = new(false);
    private readonly Reactive<bool> mDidWin = new(false);
    private readonly Reactive<string> mErrorMessage = new(string.Empty);

    private readonly Reactive<int> mMyCurrentHp = new(0);
    private readonly Reactive<int> mMyMaxHp = new(1);
    private readonly Reactive<int> mEnemyCurrentHp = new(0);
    private readonly Reactive<int> mEnemyMaxHp = new(1);
    private readonly Reactive<string> mEnemyName = new(string.Empty);
    private readonly Reactive<int> mSecondsLeft = new(0);

    private readonly Reactive<string> mSelectedStance = new(STANCE_NORMAL);
    private readonly Reactive<List<string>> mCurrentComboDisplay = new(new List<string>());
    private readonly Reactive<int> mComboStep = new(0);
    private readonly Reactive<int> mComboIndex = new(0);
    private readonly Reactive<List<CombatLoadoutSlotDto>> mLoadoutSlots = new(new List<CombatLoadoutSlotDto>());

    /// <summary>Rich-text строка лога боя. Показывается в CombatLogView.</summary>
    private readonly Reactive<string> mCombatLogText = new(string.Empty);

    public ReadonlyReactive<bool> IsInCombat => mIsInCombat.Readonly;
    public ReadonlyReactive<bool> IsMyTurn => mIsMyTurn.Readonly;
    public ReadonlyReactive<bool> IsLoading => mIsLoading.Readonly;
    public ReadonlyReactive<bool> IsFinished => mIsFinished.Readonly;
    public ReadonlyReactive<bool> DidWin => mDidWin.Readonly;
    public ReadonlyReactive<string> ErrorMessage => mErrorMessage.Readonly;

    public ReadonlyReactive<int> MyCurrentHp => mMyCurrentHp.Readonly;
    public ReadonlyReactive<int> MyMaxHp => mMyMaxHp.Readonly;
    public ReadonlyReactive<int> EnemyCurrentHp => mEnemyCurrentHp.Readonly;
    public ReadonlyReactive<int> EnemyMaxHp => mEnemyMaxHp.Readonly;
    public ReadonlyReactive<string> EnemyName => mEnemyName.Readonly;
    public ReadonlyReactive<int> SecondsLeft => mSecondsLeft.Readonly;

    public ReadonlyReactive<string> SelectedStance => mSelectedStance.Readonly;
    public ReadonlyReactive<List<string>> CurrentComboDisplay => mCurrentComboDisplay.Readonly;
    public ReadonlyReactive<int> ComboStep => mComboStep.Readonly;
    public ReadonlyReactive<int> ComboIndex => mComboIndex.Readonly;
    public ReadonlyReactive<List<CombatLoadoutSlotDto>> LoadoutSlots => mLoadoutSlots.Readonly;
    public ReadonlyReactive<string> CombatLogText => mCombatLogText.Readonly;

    // ─── Внутреннее состояние ─────────────────────────────────────────────────

    private long mSessionId;
    private long mOpponentParticipantId;
    private long mMyParticipantId;
    private string mMySide = string.Empty;

    private List<CombatComboDto> mCombos = new();
    private int mLocalComboStep;

    // Текстовый буфер лога
    private readonly StringBuilder mLogBuffer = new();

    private CancellationTokenSource mCombatCts;
    private CancellationTokenSource mTimerCts;
    private readonly CancellationTokenSource mLifetimeCts = new();

    private readonly ICombatService mCombatService;

    [Inject]
    public CombatPresenter(ICombatService combatService)
    {
        mCombatService = combatService;

        AutoDispose(
            mIsInCombat, mIsMyTurn, mIsLoading, mIsFinished, mDidWin, mErrorMessage,
            mMyCurrentHp, mMyMaxHp, mEnemyCurrentHp, mEnemyMaxHp, mEnemyName, mSecondsLeft,
            mSelectedStance, mCurrentComboDisplay, mComboStep, mComboIndex,
            mLoadoutSlots, mCombatLogText);
    }

    // ─── IInitializable ───────────────────────────────────────────────────────

    public async void Initialize()
    {
        await TryResumeActiveCombatAsync(mLifetimeCts.Token);
    }

    private async UniTask TryResumeActiveCombatAsync(CancellationToken ct)
    {
        try
        {
            await LoadCombatDataAsync(ct);
            var state = await mCombatService.GetCurrentAsync(ct);
            if (state == null) return;
            Debug.Log($"[CombatPresenter] Возобновляем бой SessionId={state.SessionId}");
            AddLog("<color=#AAAAAA>── Бой возобновлён ──</color>");
            EnterCombat(state);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogWarning($"[CombatPresenter] TryResume: {ex.Message}"); }
    }

    protected override void OnDispose()
    {
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        mCombatCts?.Cancel();
        mCombatCts?.Dispose();
        StopTimer();
    }

    // ─── Публичный API ────────────────────────────────────────────────────────

    public async UniTaskVoid EngageMobAsync(long spawnId, CancellationToken viewCt = default)
    {
        if (mIsInCombat.Value || mIsLoading.Value) return;

        mIsLoading.Value = true;
        mErrorMessage.Value = string.Empty;

        try
        {
            using var ct = LinkCts(viewCt);
            await LoadCombatDataAsync(ct.Token);

            var state = await mCombatService.EngageMobAsync(spawnId, ct.Token);
            mLogBuffer.Clear();
            AddLog("<color=#AAAAAA>── Бой начался ──</color>");
            EnterCombat(state);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ex.Message;
            Debug.LogError($"[CombatPresenter] EngageMob: {ex}");
        }
        finally { mIsLoading.Value = false; }
    }

    public async UniTaskVoid ActionAsync(string direction, CancellationToken viewCt = default)
    {
        if (!mIsInCombat.Value || !mIsMyTurn.Value || mIsLoading.Value) return;

        mIsLoading.Value = true;
        mErrorMessage.Value = string.Empty;
        StopTimer();

        try
        {
            using var ct = LinkCts(viewCt);
            var result = await mCombatService.ActionAsync(
                mSessionId, mOpponentParticipantId, mSelectedStance.Value, direction, ct.Token);

            await ApplyTurnResultAsync(result, direction, ct.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ex.Message;
            Debug.LogError($"[CombatPresenter] Action: {ex}");
        }
        finally { mIsLoading.Value = false; }
    }

    public async UniTaskVoid SkipAsync(CancellationToken viewCt = default)
    {
        if (!mIsInCombat.Value || !mIsMyTurn.Value || mIsLoading.Value) return;

        mIsLoading.Value = true;
        StopTimer();

        try
        {
            using var ct = LinkCts(viewCt);
            var result = await mCombatService.SkipAsync(mSessionId, ct.Token);
            AddLog("<color=#AAAAAA>Ты пропустил ход</color>");
            await ApplyTurnResultAsync(result, direction: null, ct.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { mErrorMessage.Value = ex.Message; }
        finally { mIsLoading.Value = false; }
    }

    /// <summary>
    /// Применить расходку. После применения всегда обновляем стейт с сервера.
    /// </summary>
    public async UniTaskVoid ConsumeAsync(long templateId, CancellationToken viewCt = default)
    {
        if (!mIsInCombat.Value || mIsLoading.Value) return;

        mIsLoading.Value = true;

        try
        {
            using var ct = LinkCts(viewCt);

            // Применяем расходку
            await mCombatService.ConsumeAsync(mSessionId, templateId, ct.Token);

            // Всегда перечитываем стейт — только так гарантированно получим актуальный HP
            var state = await mCombatService.GetStateAsync(mSessionId, ct.Token);
            if (state.You != null)
                mMyCurrentHp.Value = state.You.CurrentHp;

            // Перечитываем лоадаут — количество уменьшилось
            var loadout = await mCombatService.GetLoadoutAsync(ct.Token);
            mLoadoutSlots.Value = loadout?.Slots ?? new List<CombatLoadoutSlotDto>();

            // Лог
            var slotName = GetConsumableName(templateId);
            AddLog($"<color=#66CCFF>💊 Применено: {slotName}</color>");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ex.Message;
            Debug.LogError($"[CombatPresenter] Consume: {ex}");
        }
        finally { mIsLoading.Value = false; }
    }

    public void SetStance(string stance) => mSelectedStance.Value = stance;

    public void NextCombo()
    {
        if (mCombos.Count == 0) return;
        mComboIndex.Value = (mComboIndex.Value + 1) % mCombos.Count;
        RebuildComboDisplay();
    }

    public void PrevCombo()
    {
        if (mCombos.Count == 0) return;
        mComboIndex.Value = (mComboIndex.Value - 1 + mCombos.Count) % mCombos.Count;
        RebuildComboDisplay();
    }

    public async UniTaskVoid ExitCombatAsync(CancellationToken viewCt = default)
    {
        if (mMyCurrentHp.Value <= 0)
        {
            try
            {
                using var ct = LinkCts(viewCt);
                await mCombatService.ResurrectAsync(ct.Token);
            }
            catch (Exception ex) { Debug.LogError($"[CombatPresenter] Resurrect: {ex}"); }
        }

        ResetCombatState();
    }

    // ─── Внутренняя логика ────────────────────────────────────────────────────

    private async UniTask LoadCombatDataAsync(CancellationToken ct)
    {
        try
        {
            var (combosResp, loadoutResp) = await UniTask.WhenAll(
                mCombatService.GetCombosAsync(ct),
                mCombatService.GetLoadoutAsync(ct));

            mCombos = combosResp?.Combos ?? new List<CombatComboDto>();
            mLoadoutSlots.Value = loadoutResp?.Slots ?? new List<CombatLoadoutSlotDto>();
            mComboIndex.Value = 0;
            mLocalComboStep = 0;
            RebuildComboDisplay();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Debug.LogWarning($"[CombatPresenter] LoadData: {ex.Message}"); }
    }

    private void EnterCombat(CombatStateResponse state)
    {
        mSessionId = state.SessionId;

        if (state.You != null)
        {
            mMyParticipantId = state.You.ParticipantId;
            mMySide = state.You.Side ?? string.Empty;
        }

        if (state.YourOpponent != null)
            mOpponentParticipantId = state.YourOpponent.ParticipantId;

        ApplyState(state);
        mIsInCombat.Value = true;

        if (!state.IsYourTurn && !state.Finished)
            PollOpponentTurnAsync(mCombatCts?.Token ?? mLifetimeCts.Token).Forget();
    }

    private void ApplyState(CombatStateResponse state)
    {
        if (state.You != null)
        {
            mMyCurrentHp.Value = state.You.CurrentHp;
            mMyMaxHp.Value = Mathf.Max(1, state.You.MaxHp);
        }

        if (state.YourOpponent != null)
        {
            mEnemyCurrentHp.Value = state.YourOpponent.CurrentHp;
            mEnemyMaxHp.Value = Mathf.Max(1, state.YourOpponent.MaxHp);
            mEnemyName.Value = state.YourOpponent.Name ?? string.Empty;
            mOpponentParticipantId = state.YourOpponent.ParticipantId;
        }

        mIsMyTurn.Value = state.IsYourTurn && !state.Finished;

        if (state.Finished) FinishCombat(state.WinnerSide);
        else if (state.IsYourTurn) StartTimer(state.TurnDeadlineUtc);
    }

    /// <summary>
    /// Асинхронное применение результата хода:
    ///   — Сразу: наш удар (лог + HP врага)
    ///   — Пауза 1 сек
    ///   — Удар(ы) моба (лог + HP наш)
    /// </summary>
    private async UniTask ApplyTurnResultAsync(
        CombatTurnResultResponse result, string direction, CancellationToken ct)
    {
        // ── 1. Наш удар ──────────────────────────────────────────────────────
        if (result.YourHit != null)
        {
            mEnemyCurrentHp.Value = result.YourHit.TargetHpAfter;
            LogHit(result.YourHit, isOurs: true, direction);
            TrackComboProgress(direction, result.YourHit.WasComboFinisher);
        }

        // ── 2. Если бой закончился нашим ударом — не ждём ────────────────────
        if (result.Finished)
        {
            FinalizeFromResult(result);
            return;
        }

        // ── 3. Пауза (моб «думает») ───────────────────────────────────────────
        if (result.ResponseHits is { Count: > 0 })
            await UniTask.Delay(1000, cancellationToken: ct);

        // ── 4. Удары моба по нам ─────────────────────────────────────────────
        if (result.ResponseHits != null)
        {
            foreach (var hit in result.ResponseHits)
            {
                mMyCurrentHp.Value = hit.TargetHpAfter;
                LogHit(hit, isOurs: false, direction: null);
            }
        }

        // ── 5. Финальные значения ─────────────────────────────────────────────
        if (result.You != null)
        {
            mMyCurrentHp.Value = result.You.CurrentHp;
            mMyMaxHp.Value = Mathf.Max(1, result.You.MaxHp);
        }

        if (result.YourOpponent != null)
        {
            mEnemyCurrentHp.Value = result.YourOpponent.CurrentHp;
            mEnemyMaxHp.Value = Mathf.Max(1, result.YourOpponent.MaxHp);
            mEnemyName.Value = result.YourOpponent.Name ?? string.Empty;
            mOpponentParticipantId = result.YourOpponent.ParticipantId;
        }
        else if (result.Finished)
        {
            mEnemyCurrentHp.Value = 0;
        }

        if (result.Finished)
        {
            FinishCombat(result.WinnerSide);
            return;
        }

        mIsMyTurn.Value = result.IsYourTurn;
        if (result.IsYourTurn) StartTimer(result.TurnDeadlineUtc);
        else PollOpponentTurnAsync(mCombatCts?.Token ?? mLifetimeCts.Token).Forget();
    }

    private void FinalizeFromResult(CombatTurnResultResponse result)
    {
        if (result.You != null)
        {
            mMyCurrentHp.Value = result.You.CurrentHp;
            mMyMaxHp.Value = Mathf.Max(1, result.You.MaxHp);
        }

        if (result.YourOpponent != null)
        {
            mEnemyCurrentHp.Value = result.YourOpponent.CurrentHp;
            mEnemyMaxHp.Value = Mathf.Max(1, result.YourOpponent.MaxHp);
            mOpponentParticipantId = result.YourOpponent.ParticipantId;
        }
        else
        {
            mEnemyCurrentHp.Value = 0;
        }

        FinishCombat(result.WinnerSide);
    }

    private void FinishCombat(string winnerSide)
    {
        StopTimer();

        bool won;
        if (!string.IsNullOrEmpty(winnerSide) && !string.IsNullOrEmpty(mMySide))
            won = winnerSide == mMySide;
        else
            won = mMyCurrentHp.Value > 0 && mEnemyCurrentHp.Value <= 0;

        Debug.Log($"[Combat] Finish: winner={winnerSide} side={mMySide} myHP={mMyCurrentHp.Value} enemyHP={mEnemyCurrentHp.Value} → {(won ? "ПОБЕДА" : "ПОРАЖЕНИЕ")}");

        AddLog(won
            ? "<color=#FFD700>══ Победа! ══</color>"
            : "<color=#FF6666>══ Поражение... ══</color>");

        mIsMyTurn.Value = false;
        mDidWin.Value = won;
        mIsFinished.Value = true;
    }

    private void ResetCombatState()
    {
        StopTimer();
        mCombatCts?.Cancel();
        mCombatCts?.Dispose();
        mCombatCts = null;

        mIsInCombat.Value = false;
        mIsMyTurn.Value = false;
        mIsFinished.Value = false;
        mDidWin.Value = false;
        mIsLoading.Value = false;
        mErrorMessage.Value = string.Empty;
        mMyCurrentHp.Value = 0;
        mEnemyCurrentHp.Value = 0;
        mEnemyName.Value = string.Empty;
        mSecondsLeft.Value = 0;
        mSelectedStance.Value = STANCE_NORMAL;
        mMySide = string.Empty;
        mSessionId = 0;
        mOpponentParticipantId = 0;
        mLocalComboStep = 0;
        mComboStep.Value = 0;
        mLogBuffer.Clear();
        mCombatLogText.Value = string.Empty;
    }

    // ─── Polling ──────────────────────────────────────────────────────────────

    private async UniTaskVoid PollOpponentTurnAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !mIsFinished.Value && !mIsMyTurn.Value)
            {
                await UniTask.Delay(POLL_INTERVAL_MS, cancellationToken: ct);
                var state = await mCombatService.GetStateAsync(mSessionId, ct);
                ApplyState(state);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogWarning($"[CombatPresenter] Poll: {ex.Message}"); }
    }

    // ─── Таймер ───────────────────────────────────────────────────────────────

    private void StartTimer(DateTime? deadline)
    {
        StopTimer();
        if (!deadline.HasValue) return;
        mTimerCts = CancellationTokenSource.CreateLinkedTokenSource(mLifetimeCts.Token);
        RunTimerAsync(deadline.Value, mTimerCts.Token).Forget();
    }

    private void StopTimer()
    {
        mTimerCts?.Cancel();
        mTimerCts?.Dispose();
        mTimerCts = null;
        mSecondsLeft.Value = 0;
    }

    private async UniTaskVoid RunTimerAsync(DateTime deadline, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int sec = Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalSeconds);
                mSecondsLeft.Value = sec;
                if (sec <= 0) break;
                await UniTask.Delay(1000, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ─── Комбо ────────────────────────────────────────────────────────────────

    private void TrackComboProgress(string direction, bool wasFinisher)
    {
        if (wasFinisher) { mLocalComboStep = 0; mComboStep.Value = 0; return; }
        if (mCombos.Count == 0) return;

        var seq = mCombos[mComboIndex.Value]?.Sequence;
        if (seq == null) return;

        string dir = direction?.Length > 0
            ? char.ToUpper(direction[0]) + direction.Substring(1).ToLower()
            : direction ?? string.Empty;

        if (mLocalComboStep < seq.Length &&
            string.Equals(seq[mLocalComboStep], dir, StringComparison.OrdinalIgnoreCase))
            mLocalComboStep++;
        else if (seq.Length > 0 &&
                 string.Equals(seq[0], dir, StringComparison.OrdinalIgnoreCase))
            mLocalComboStep = 1;
        else
            mLocalComboStep = 0;

        mComboStep.Value = mLocalComboStep;
    }

    private void RebuildComboDisplay()
    {
        if (mCombos.Count == 0) { mCurrentComboDisplay.Value = new List<string>(); return; }
        var seq = mCombos[mComboIndex.Value]?.Sequence;
        if (seq == null) { mCurrentComboDisplay.Value = new List<string>(); return; }
        var display = new List<string>(seq.Length);
        foreach (var dir in seq)
            display.Add(dir?.ToLower() switch { "head" => "Г", "body" => "Т", "legs" => "Н", _ => "?" });
        mCurrentComboDisplay.Value = display;
        mLocalComboStep = 0;
        mComboStep.Value = 0;
    }

    // ─── Лог ──────────────────────────────────────────────────────────────────

    private void LogHit(CombatHitView hit, bool isOurs, string direction)
    {
        if (hit == null) return;

        string dir = (direction ?? hit.Direction)?.ToLower() switch
        {
            "head" => "Г",
            "body" => "Т",
            "legs" => "Н",
            _ => "?"
        };

        string stanceName = hit.Stance?.ToLower() switch
        {
            "defensive" => " [Защита]",
            "aggressive" => " [Агрессия]",
            _ => string.Empty
        };

        // Результат удара
        string resultText;
        if (hit.WasBlock) resultText = "Заблокировано";
        else if (hit.WasDodge) resultText = "Уворот";
        else resultText = $"{hit.Damage} урона";

        string crit = hit.WasCrit ? " <color=#FF8800>[КРИТ]</color>" : string.Empty;

        if (isOurs)
        {
            AddLog($"<color=white>→ Ты [{dir}]{stanceName}: {resultText}{crit}</color>");

            if (hit.WasComboFinisher)
            {
                string finisherText = GetFinisherText(mCombos.Count > 0 && mComboIndex.Value < mCombos.Count
                    ? mCombos[mComboIndex.Value]?.Finisher
                    : null);
                AddLog($"<color=#FFD700>⚡ КОМБО ур.{hit.ComboLevel}! {finisherText} (включён в урон выше)</color>");
            }
        }
        else
        {
            string enemyName = mEnemyName.Value.Length > 0 ? mEnemyName.Value : "Враг";
            AddLog($"<color=#FF6666>← {enemyName} [{dir}]: {resultText}{crit}</color>");
        }
    }

    private void AddLog(string line)
    {
        Debug.Log($"[CombatLog] {line}");

        if (mLogBuffer.Length > 0) mLogBuffer.AppendLine();
        mLogBuffer.Append(line);

        // Обрезаем если лог слишком длинный (по кол-ву переносов строк)
        string text = mLogBuffer.ToString();
        int newlines = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') newlines++;

        if (newlines > MAX_LOG_ENTRIES)
        {
            int cutAt = text.IndexOf('\n') + 1;
            mLogBuffer.Remove(0, cutAt);
        }

        mCombatLogText.Value = mLogBuffer.ToString();
    }

    private static string GetFinisherText(string finisher) => finisher?.ToLower() switch
    {
        "extra_damage" => "Урон ×2",
        "bleed" => "Кровотечение",
        "stun" => "Оглушение",
        "vampirism" => "Вампиризм",
        _ => "Спецэффект"
    };

    private string GetConsumableName(long templateId)
    {
        var slot = mLoadoutSlots.Value?.Find(s => s.ConsumableTemplateId == templateId);
        if (slot?.ConsumableCode != null)
            return slot.ConsumableCode;
        return $"id={templateId}";
    }

    // ─── Вспомогательные ──────────────────────────────────────────────────────

    private CancellationTokenSource LinkCts(CancellationToken viewCt)
    {
        if (mCombatCts == null || mCombatCts.IsCancellationRequested)
        {
            mCombatCts?.Dispose();
            mCombatCts = CancellationTokenSource.CreateLinkedTokenSource(mLifetimeCts.Token);
        }

        return viewCt == default
            ? CancellationTokenSource.CreateLinkedTokenSource(mCombatCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(mCombatCts.Token, viewCt);
    }
}
