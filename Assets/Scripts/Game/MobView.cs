using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Вид одного моба на экране охоты.
/// Создаётся динамически из префаба в View_Hunting.
/// Мёртвые мобы не создаются вообще — View_Hunting их фильтрует.
///
/// Блуждание: каждые WanderIntervalMin–WanderIntervalMax секунд
/// перемещается к случайной точке в пределах MobsArea.
/// Анимация: плавное смещение через Lerp.
/// Петля привязана к destroyCancellationToken — останавливается при Destroy().
///
/// Кнопка «Атаковать» вызывает mOnAttackClicked(SpawnId).
/// Callback устанавливается из View_Hunting.RebuildMobs() через Setup().
///
/// Prefab: Item_MobView
/// </summary>
public class MobView : MonoBehaviour
{
    [Header("UI-элементы")]
    [SerializeField] private TMP_Text   mNameLabel;
    [SerializeField] private TMP_Text   mLevelLabel;

    /// <summary>Иконка/метка «В бою» — показывается рядом с мобом, не поверх.</summary>
    [SerializeField] private GameObject mCombatBadge;

    [SerializeField] private Button     mAttackButton;

    // ─── Данные ───────────────────────────────────────────────────────────────

    /// <summary>SpawnId строго из MobSpawnDto — передаётся в callback атаки.</summary>
    private long mSpawnId;

    /// <summary>Callback который вызывается при нажатии «Атаковать». Устанавливается из View_Hunting.</summary>
    private Action<long> mOnAttackClicked;

    private HuntingConfig mConfig;
    private RectTransform mAreaRect;
    private RectTransform mSelfRect;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        mSelfRect = GetComponent<RectTransform>();
        mAttackButton.onClick.AddListener(OnAttackButtonClicked);
    }

    private void OnDestroy()
    {
        mAttackButton.onClick.RemoveListener(OnAttackButtonClicked);
    }

    // ─── Инициализация ────────────────────────────────────────────────────────

    /// <summary>
    /// Инициализация данными с сервера.
    /// onAttackClicked — вызывается при нажатии «Атаковать» со SpawnId моба.
    /// areaRect — RectTransform зоны мобов, внутри которой моб блуждает.
    /// </summary>
    public void Setup(
        MobSpawnDto mob, RectTransform areaRect, HuntingConfig config,
        Action<long> onAttackClicked)
    {
        // Awake() мог ещё не отработать — Instantiate() в НЕАКТИВНУЮ иерархию (Panel_Hunting
        // закрыта в момент, когда пришли данные о локации — например, SignalR-обновление
        // мобов, пока игрок стоит на Panel_LocationMain) не вызывает Awake() синхронно,
        // Unity откладывает его до активации. Раньше это было недостижимо (мобы обновлялись
        // только пока охота открыта), с живым SignalR — стало реальным сценарием.
        if (mSelfRect == null) mSelfRect = GetComponent<RectTransform>();

        mSpawnId         = mob.SpawnId;
        mConfig          = config;
        mAreaRect        = areaRect;
        mOnAttackClicked = onAttackClicked;

        mNameLabel.text  = mob.Name;
        mLevelLabel.text = $"[{mob.Level}]";

        mCombatBadge.SetActive(mob.State == "in_combat");

        mSelfRect.anchoredPosition = GetRandomPosition();
        WanderAsync(destroyCancellationToken).Forget();
    }

    // ─── Блуждание ────────────────────────────────────────────────────────────

    private async UniTaskVoid WanderAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                float interval = UnityEngine.Random.Range(
                    mConfig.WanderIntervalMin,
                    mConfig.WanderIntervalMax);

                await UniTask.Delay(
                    TimeSpan.FromSeconds(interval),
                    cancellationToken: ct);

                var startPos = mSelfRect.anchoredPosition;
                var target   = GetRandomPosition();
                float distance = Vector2.Distance(startPos, target);
                float duration = distance / mConfig.WanderSpeed;
                float elapsed  = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    mSelfRect.anchoredPosition = Vector2.Lerp(startPos, target, t);
                    await UniTask.NextFrame(ct);
                }

                mSelfRect.anchoredPosition = target;
            }
        }
        catch (OperationCanceledException) { }
    }

    private Vector2 GetRandomPosition()
    {
        var rect  = mAreaRect.rect;
        float pad = mConfig.WanderPadding;
        return new Vector2(
            UnityEngine.Random.Range(rect.xMin + pad, rect.xMax - pad),
            UnityEngine.Random.Range(rect.yMin + pad, rect.yMax - pad));
    }

    // ─── Обработчики ──────────────────────────────────────────────────────────

    private void OnAttackButtonClicked()
    {
        mOnAttackClicked?.Invoke(mSpawnId);
    }
}
