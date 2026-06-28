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

    /// <summary>SpawnId строго из MobSpawnDto — для передачи в CombatService (Фаза 3).</summary>
    private long mSpawnId;

    private HuntingConfig mConfig;
    private RectTransform mAreaRect;
    private RectTransform mSelfRect;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        mSelfRect = GetComponent<RectTransform>();
        mAttackButton.onClick.AddListener(OnAttackClicked);
    }

    private void OnDestroy()
    {
        mAttackButton.onClick.RemoveListener(OnAttackClicked);
    }

    // ─── Инициализация ────────────────────────────────────────────────────────

    /// <summary>
    /// Инициализация данными с сервера.
    /// Вызывается из View_Hunting после Instantiate.
    /// areaRect — RectTransform зоны мобов (MobsArea), внутри которой моб блуждает.
    /// </summary>
    public void Setup(MobSpawnDto mob, RectTransform areaRect, HuntingConfig config)
    {
        // SpawnId из серверного ответа — для Фазы 3
        mSpawnId  = mob.SpawnId;
        mConfig   = config;
        mAreaRect = areaRect;

        mNameLabel.text  = mob.Name;
        mLevelLabel.text = $"[{mob.Level}]";

        // Иконка «В бою» — рядом с именем, не перекрывает спрайт
        mCombatBadge.SetActive(mob.State == "in_combat");

        // Стартовая позиция — случайная внутри области
        mSelfRect.anchoredPosition = GetRandomPosition();

        // Блуждание: петля живёт пока моб не Destroy()-нут
        WanderAsync(destroyCancellationToken).Forget();
    }

    // ─── Блуждание ────────────────────────────────────────────────────────────

    /// <summary>
    /// Петля блуждания. Каждые WanderInterval секунд — новая случайная цель.
    /// Плавное перемещение через Lerp по скорости WanderSpeed (px/сек).
    /// Останавливается автоматически при Destroy() через destroyCancellationToken.
    /// </summary>
    private async UniTaskVoid WanderAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                // Пауза перед следующим движением
                float interval = UnityEngine.Random.Range(
                    mConfig.WanderIntervalMin,
                    mConfig.WanderIntervalMax);

                await UniTask.Delay(
                    TimeSpan.FromSeconds(interval),
                    cancellationToken: ct);

                // Новая случайная цель
                var startPos = mSelfRect.anchoredPosition;
                var target   = GetRandomPosition();
                float distance = Vector2.Distance(startPos, target);
                float duration = distance / mConfig.WanderSpeed;
                float elapsed  = 0f;

                // Плавное движение к цели
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
        catch (OperationCanceledException)
        {
            // Нормальное завершение при Destroy() моба — ничего не делаем
        }
    }

    /// <summary>
    /// Случайная позиция внутри MobsArea с учётом отступа.
    /// Использует rect самой области — работает при любом pivot.
    /// </summary>
    private Vector2 GetRandomPosition()
    {
        var rect    = mAreaRect.rect;
        float pad   = mConfig.WanderPadding;
        return new Vector2(
            UnityEngine.Random.Range(rect.xMin + pad, rect.xMax - pad),
            UnityEngine.Random.Range(rect.yMin + pad, rect.yMax - pad));
    }

    // ─── Обработчики ──────────────────────────────────────────────────────────

    private void OnAttackClicked()
    {
        // TODO Фаза 3: передать mSpawnId в ICombatService.EngageAsync()
        Debug.Log($"[MobView] Атаковать SpawnId={mSpawnId} — заглушка Фазы 3");
    }
}
