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
///   — RequestAttackPlayer/EngagePlayerAsync: старт PvP-боя (с подтверждением из UI,
///     без него — для бота), дальше бой ведётся тем же кодом, что и с мобом
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
    private readonly Reactive<CombatOutcome> mOutcome = new(CombatOutcome.None);
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

    /// <summary>Исход завершённого боя. Валиден, пока IsFinished = true.</summary>
    public ReadonlyReactive<CombatOutcome> Outcome => mOutcome.Readonly;
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

    // ─── Публичные события ────────────────────────────────────────────────────

    /// <summary>
    /// Бой завершён и состояние сброшено (после любого пути выхода — кнопка OK на
    /// результате, ForceExitCombat). Аргумент — исход ЭТОГО боя, а не текущее значение
    /// Outcome (оно к моменту вызова уже обнулено сбросом).
    /// TD-C32: единая точка для LocationPresenter — понять, что бой закончился,
    /// и обновить данные локации (при поражении также закрыть панель охоты).
    /// </summary>
    public event Action<CombatOutcome> CombatEnded;

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
    private readonly INotificationService mNotifications;
    private readonly ILocationRealtimeService mRealtime;
    private readonly ICharacterContext mCharacterContext;

    [Inject]
    public CombatPresenter(
        ICombatService combatService, INotificationService notifications,
        ILocationRealtimeService realtime, ICharacterContext characterContext)
    {
        mCombatService = combatService;
        mNotifications = notifications;
        mRealtime = realtime;
        mCharacterContext = characterContext;

        AutoDispose(
            mIsInCombat, mIsMyTurn, mIsLoading, mIsFinished, mOutcome, mErrorMessage,
            mMyCurrentHp, mMyMaxHp, mEnemyCurrentHp, mEnemyMaxHp, mEnemyName, mSecondsLeft,
            mSelectedStance, mCurrentComboDisplay, mComboStep, mComboIndex,
            mLoadoutSlots, mCombatLogText);

        // Жертва PvP-атаки узнаёт о бое через тот же CombatStartedEvent, что и
        // LocationPresenter (там — тост, здесь — реальный вход в бой). TD-C18 закрыт:
        // раньше combat-презентор жертвы никак не реагировал на это событие, и
        // View_Combat (который сам реагирует на IsInCombat) просто не появлялся.
        mRealtime.CombatStarted += OnCombatStarted;
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

    /// <summary>
    /// PvP-бой начался где-то в локации (событие приходит всем, как и в LocationPresenter).
    /// Реагируем, только если жертва — это мы. Дальше — тот же путь, что и авто-возобновление
    /// после вылета: подтягиваем текущий бой с сервера и входим в него по-настоящему
    /// (не просто тостом) — View_Combat сам появится, он реагирует на IsInCombat.
    /// </summary>
    private void OnCombatStarted(CombatStartedEvent e)
    {
        if (mCharacterContext.CharacterId.Value != e.DefenderCharacterId) return;
        // Дедуп по конкретной сессии, а не по IsInCombat — тот может годами не сбрасываться
        // в живом процессе, если игрок ни разу не нажал OK на результате (см. EnterCombat).
        if (mIsInCombat.Value && mSessionId == e.CombatId) return;

        TryResumeActiveCombatAsync(mLifetimeCts.Token).Forget();
    }

    protected override void OnDispose()
    {
        mRealtime.CombatStarted -= OnCombatStarted;
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

    /// <summary>
    /// Запросить атаку игрока с подтверждением (PvP — необратимо, бой всегда до конца).
    /// Показывает модальный диалог; при подтверждении вызывает EngagePlayerAsync.
    /// Это единственная точка входа из UI — сам EngagePlayerAsync диалог не показывает,
    /// чтобы его мог напрямую дёргать бот (см. CombatOps.FightPlayerAsync), минуя confirm,
    /// как и остальные автоматизируемые команды бота.
    /// </summary>
    public void RequestAttackPlayer(long characterId, string nickname)
    {
        var name = string.IsNullOrEmpty(nickname) ? "игрока" : nickname;
        mNotifications.ShowConfirm(
            message: $"Напасть на «{name}»? Начнётся PvP-бой, отступить будет нельзя.",
            onConfirm: () => EngagePlayerAsync(characterId).Forget(),
            onCancel: null,
            title: "Атаковать игрока",
            confirmLabel: "Атаковать",
            cancelLabel: "Отмена",
            type: NotificationType.Warning);
    }

    /// <summary>Напасть на игрока по CharacterId (PvP). Зеркало EngageMobAsync.</summary>
    public async UniTaskVoid EngagePlayerAsync(long characterId, CancellationToken viewCt = default)
    {
        if (mIsInCombat.Value || mIsLoading.Value) return;

        mIsLoading.Value = true;
        mErrorMessage.Value = string.Empty;

        try
        {
            using var ct = LinkCts(viewCt);
            await LoadCombatDataAsync(ct.Token);

            var state = await mCombatService.EngagePlayerAsync(characterId, ct.Token);
            mLogBuffer.Clear();
            AddLog("<color=#AAAAAA>── PvP-бой начался ──</color>");
            EnterCombat(state);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = ex.Message;
            Debug.LogError($"[CombatPresenter] EngagePlayer: {ex}");
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

    /// <summary>
    /// Выйти из боя по кнопке OK на результате. Воскрешение сюда больше не входит
    /// (TD-C32) — при поражении персонаж остаётся мёртвым до явного действия в диалоге
    /// «Вы мертвы» на экране локации (см. LocationPresenter.ShowResurrectDialogIfNeeded).
    /// Раньше воскрешение было здесь и молча срабатывало сразу по OK — так игрок не мог
    /// осознанно решить, воскресать ли ему прямо сейчас (единый путь воскрешения — сам
    /// диалог, а не эта кнопка).
    /// </summary>
    public UniTaskVoid ExitCombatAsync(CancellationToken viewCt = default)
    {
        ResetCombatState();
        return default;
    }

    /// <summary>
    /// Принудительно сбросить состояние боя без похода на сервер (воскрешение уже
    /// произошло где-то в другом месте — см. LocationPresenter.ResurrectAsync).
    /// TD-C29: раньше диалог воскрешения на экране локации закрывал боевой попап,
    /// но не трогал CombatPresenter, и mIsInCombat/mIsFinished зависали до следующего
    /// боя (спасал только явный сброс в EnterCombat). Теперь у каждого пути выхода
    /// из боя есть свой явный вызов сброса состояния.
    /// </summary>
    public void ForceExitCombat() => ResetCombatState();

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
        // Явно сбрасываем флаги ПРЕДЫДУЩЕГО боя перед применением нового состояния.
        // Раньше это подразумевалось только через ResetCombatState() (вызывается по кнопке
        // OK на результате) — но если игрок вышел из предыдущего боя другим путём (например,
        // диалог воскрешения в LocationPresenter, который вообще не трогает CombatPresenter),
        // mIsFinished/mOutcome оставались висеть из старого боя. Новый бой на них
        // натыкался: и OnCombatStarted-гвард по IsInCombat молчал навсегда, и — даже если бы
        // резюм всё-таки случился — View_Combat сразу показал бы попап результата ПРЕДЫДУЩЕГО
        // боя вместо начала нового.
        mIsFinished.Value = false;
        mOutcome.Value = CombatOutcome.None;
        mErrorMessage.Value = string.Empty;

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

        // TD-C29-2: выходим из боя не только когда закончилась ВСЯ сессия (state.Finished),
        // но и когда лично умер участник — сессия при этом может продолжаться без нас
        // (N×M — союзники ещё дерутся). См. Server/Combat/... CombatTurnEngine: пересват
        // после смерти не завершает сессию, пока жив хоть кто-то на стороне.
        bool exiting = state.Finished || (state.You != null && !state.You.IsAlive);
        mIsMyTurn.Value = state.IsYourTurn && !exiting;

        // Сессия могла завершиться не победой кого-то, а прерыванием (истёк лимит длительности
        // боя / прерван бой в данже) — сервер отдаёт это в state, отдельно от winnerSide.
        // Важно: наша собственная смерть при живой сессии прерыванием не является, поэтому
        // interrupted учитываем только когда завершилась вся сессия.
        bool interrupted = state.Finished && IsInterrupted(state.State);

        if (exiting) FinishCombat(state.WinnerSide, interrupted);
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

        // ── 2. Если бой закончился (для сессии или лично для нас) нашим ударом — не ждём ──
        if (result.Finished || (result.You != null && !result.You.IsAlive))
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

        if (result.Finished || (result.You != null && !result.You.IsAlive))
        {
            // interrupted: false — прерывание никогда не приходит результатом хода. Сервер
            // ловит истечение сессии ДО обработки хода: на SubmitAction кидает CombatException,
            // на таймауте возвращает null. Прерванный бой клиент узнаёт только из GetState.
            FinishCombat(result.WinnerSide, interrupted: false);
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

        FinishCombat(result.WinnerSide, interrupted: false); // см. пояснение в ApplyTurnResult
    }

    /// <summary>Строка state из ответа сервера означает прерванный бой (combat_state.interrupted).</summary>
    private static bool IsInterrupted(string state) =>
        string.Equals(state, "Interrupted", StringComparison.OrdinalIgnoreCase);

    private void FinishCombat(string winnerSide, bool interrupted)
    {
        StopTimer();

        // Порядок важен: прерывание проверяем ПЕРВЫМ. У прерванной сессии победителя нет
        // вовсе, и раньше она проваливалась в ветку догадки по HP ниже, где «оба живы»
        // читалось как поражение — живой персонаж с полным HP видел «Поражение...».
        CombatOutcome outcome;
        if (interrupted)
            outcome = CombatOutcome.Interrupted;
        else if (!string.IsNullOrEmpty(winnerSide) && !string.IsNullOrEmpty(mMySide))
            outcome = winnerSide == mMySide ? CombatOutcome.Win : CombatOutcome.Loss;
        else
            // Победитель неизвестен, а бой не помечен прерванным: единственный штатный случай —
            // наша смерть при продолжающейся сессии (N×M, союзники ещё дерутся). Для нас это
            // поражение независимо от HP противника, поэтому прежняя догадка «мой HP > 0 и
            // вражеский <= 0» здесь больше не нужна — она и давала ложные исходы.
            outcome = CombatOutcome.Loss;

        Debug.Log($"[Combat] Finish: winner={winnerSide} interrupted={interrupted} side={mMySide} myHP={mMyCurrentHp.Value} enemyHP={mEnemyCurrentHp.Value} → {outcome}");

        AddLog(outcome switch
        {
            CombatOutcome.Win => "<color=#FFD700>══ Победа! ══</color>",
            CombatOutcome.Interrupted => "<color=#AAAAAA>══ Бой прерван ══</color>",
            _ => "<color=#FF6666>══ Поражение... ══</color>"
        });

        mIsMyTurn.Value = false;
        mOutcome.Value = outcome;
        mIsFinished.Value = true;
    }

    private void ResetCombatState()
    {
        // Снимаем исход ДО сброса флагов — mOutcome.Value ниже обнулится вместе со всем
        // остальным состоянием, а подписчикам (LocationPresenter) нужен именно исход
        // только что завершившегося боя, не дефолтное значение после сброса.
        var outcome = mOutcome.Value;

        StopTimer();
        mCombatCts?.Cancel();
        mCombatCts?.Dispose();
        mCombatCts = null;

        mIsInCombat.Value = false;
        mIsMyTurn.Value = false;
        mIsFinished.Value = false;
        mOutcome.Value = CombatOutcome.None;
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

        CombatEnded?.Invoke(outcome);
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
