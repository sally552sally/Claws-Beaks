using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Экран охоты — Panel_Hunting.
/// Показывает мобов (вид сверху, блуждают по зоне) и список игроков в локации.
///
/// Обновление данных:
///   — при открытии панели из View_Location → LocationPresenter.RefreshAsync()
///   — подписывается на LocationPresenter.Mobs и LocationPresenter.Players
///   — DEV_BUILD кнопка «Обновить» — TD-11: убрать в Фазе 5, заменить на SignalR push
///
/// Мёртвые мобы (state == "dead") не отображаются.
/// SignalR появление/исчезновение мобов — Фаза 5.
///
/// Структура ScrollRect:
///   ScrollRect
///   └── Viewport
///       └── Content (VerticalLayoutGroup + ContentSizeFitter)
///           ├── MobsSection (Header + MobsArea с абсолютными позициями мобов)
///           └── PlayersSection (Header + PlayersContainer)
///
/// GameObject: Panel_Hunting
/// </summary>
public class View_Hunting : DisposableBehaviour
{
    // ─── Мобы ─────────────────────────────────────────────────────────────────

    [Header("Мобы")]
    /// <summary>Зона блуждания мобов. Мобы спавнятся внутри неё с абсолютными позициями.</summary>
    [SerializeField] private RectTransform mMobsArea;
    [SerializeField] private MobView       mMobViewPrefab;

    // ─── Игроки ───────────────────────────────────────────────────────────────

    [Header("Игроки")]
    [SerializeField] private Transform      mPlayersContainer;
    [SerializeField] private PlayerListItem mPlayerListItemPrefab;

    // ─── Попап действий ───────────────────────────────────────────────────────

    [Header("Попап игрока")]
    [SerializeField] private ContextMenuPopup mContextMenuPopup;

    // ─── Конфиг ───────────────────────────────────────────────────────────────

    [Header("Конфиг")]
    [SerializeField] private HuntingConfig mHuntingConfig;

    // ─── DEV ──────────────────────────────────────────────────────────────────

#if DEV_BUILD
    [Header("DEV — убрать в Фазе 5 (SignalR)")]
    [SerializeField] private Button mRefreshButton;
#endif

    // ─── Внутренний стейт ─────────────────────────────────────────────────────

    private LocationPresenter mPresenter;

    private readonly List<MobView>        mMobViews    = new();
    private readonly List<PlayerListItem> mPlayerItems = new();

    // ─── Zenject Inject ───────────────────────────────────────────────────────

    [Inject]
    public void Construct(LocationPresenter presenter)
    {
        mPresenter = presenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        // Подписка на реактивные списки мобов и игроков
        mPresenter.Mobs
            .SubscribeOnValueChanged(RebuildMobs)
            .DisposeWhenLifeEnded(this);

        mPresenter.Players
            .SubscribeOnValueChanged(RebuildPlayers)
            .DisposeWhenLifeEnded(this);

#if DEV_BUILD
        // TD-11: убрать кнопку в Фазе 5, заменить на SignalR push из LocationHub
        if (mRefreshButton != null)
            mRefreshButton.SubscribeOnClick(OnRefreshClicked).DisposeWhenLifeEnded(this);
#endif
    }

    // ─── Мобы ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Пересоздаёт mob view-объекты по актуальным данным с сервера.
    /// Мёртвые мобы (state == "dead") не отображаются.
    /// Позиции рандомятся на клиенте — чисто визуал, читерить нечего.
    /// </summary>
    private void RebuildMobs(List<MobSpawnDto> mobs)
    {
        foreach (var view in mMobViews)
            Destroy(view.gameObject);
        mMobViews.Clear();

        if (mobs == null) return;

        foreach (var mob in mobs)
        {
            // Мёртвые мобы не показываем — они исчезают с экрана
            // При респавне появятся при следующем рефреше (Фаза 5: SignalR)
            if (mob.State == "dead") continue;

            var view = Instantiate(mMobViewPrefab, mMobsArea);
            view.Setup(mob, mMobsArea, mHuntingConfig);
            mMobViews.Add(view);
        }
    }

    // ─── Игроки ───────────────────────────────────────────────────────────────

    /// <summary>Пересоздаёт список игроков в локации.</summary>
    private void RebuildPlayers(List<PlayerInLocationDto> players)
    {
        foreach (var item in mPlayerItems)
            Destroy(item.gameObject);
        mPlayerItems.Clear();

        if (players == null) return;

        foreach (var player in players)
        {
            var item = Instantiate(mPlayerListItemPrefab, mPlayersContainer);
            item.Setup(player, OnPlayerInfoClicked);
            mPlayerItems.Add(item);
        }
    }

    // ─── Обработчики ──────────────────────────────────────────────────────────

    private void OnPlayerInfoClicked(PlayerInLocationDto player, Vector2 screenPosition)
    {
        mContextMenuPopup.Show(player, screenPosition);
    }

#if DEV_BUILD
    private void OnRefreshClicked()
    {
        // TD-11: убрать в Фазе 5 — заменить на SignalR push из LocationHub
        mPresenter.RefreshAsync(destroyCancellationToken).Forget();
    }
#endif
}
