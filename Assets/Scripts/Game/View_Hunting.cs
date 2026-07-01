using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Экран охоты — Panel_Hunting.
/// Видимость управляется извне через View_Location (который слушает LocationPresenter.IsHuntingOpen).
/// Кнопка «Назад» вызывает LocationPresenter.CloseHunting().
///
/// Обновление данных происходит при открытии охоты (View_Location.OnHuntingStateChanged).
///
/// GameObject: Panel_Hunting
/// </summary>
public class View_Hunting : DisposableBehaviour
{
    // ─── Мобы ─────────────────────────────────────────────────────────────────

    [Header("Мобы")]
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

    // ─── Навигация ────────────────────────────────────────────────────────────

    [Header("Навигация")]
    /// <summary>Кнопка «← Назад» — возвращает в Panel_LocationMain.</summary>
    [SerializeField] private Button mButtonBack;
    /// <summary>Кнопка «Инвентарь» — открывает Panel_Inventory поверх охоты.</summary>
    [SerializeField] private Button mButtonInventory;

    // ─── DEV ──────────────────────────────────────────────────────────────────

#if DEV_BUILD
    [Header("DEV — убрать в Фазе 5 (SignalR)")]
    [SerializeField] private Button mRefreshButton;
#endif

    // ─── Внутренний стейт ─────────────────────────────────────────────────────

    private LocationPresenter mPresenter;
    private CombatPresenter   mCombatPresenter;
    private InventoryPresenter mInventoryPresenter;

    private readonly List<MobView>        mMobViews    = new();
    private readonly List<PlayerListItem> mPlayerItems = new();

    // ─── Zenject Inject ───────────────────────────────────────────────────────

    [Inject]
    public void Construct(LocationPresenter presenter, CombatPresenter combatPresenter,
        InventoryPresenter inventoryPresenter)
    {
        mPresenter       = presenter;
        mCombatPresenter = combatPresenter;
        mInventoryPresenter = inventoryPresenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        // Мобы и игроки
        mPresenter.Mobs
            .SubscribeOnValueChanged(RebuildMobs)
            .DisposeWhenLifeEnded(this);

        mPresenter.Players
            .SubscribeOnValueChanged(RebuildPlayers)
            .DisposeWhenLifeEnded(this);

        // Кнопка «Назад» → закрыть охоту
        if (mButtonBack != null)
            mButtonBack.SubscribeOnClick(() => mPresenter.CloseHunting())
                .DisposeWhenLifeEnded(this);

        // Кнопка «Инвентарь» → открыть Panel_Inventory поверх охоты
        if (mButtonInventory != null)
            mButtonInventory.SubscribeOnClick(() => mInventoryPresenter.Open())
                .DisposeWhenLifeEnded(this);

#if DEV_BUILD
        if (mRefreshButton != null)
            mRefreshButton.SubscribeOnClick(OnRefreshClicked).DisposeWhenLifeEnded(this);
#endif
    }

    // ─── Мобы ─────────────────────────────────────────────────────────────────

    private void RebuildMobs(List<MobSpawnDto> mobs)
    {
        foreach (var view in mMobViews)
            Destroy(view.gameObject);
        mMobViews.Clear();

        if (mobs == null) return;

        foreach (var mob in mobs)
        {
            if (mob.State == "dead") continue;

            var view = Instantiate(mMobViewPrefab, mMobsArea);
            view.Setup(mob, mMobsArea, mHuntingConfig, OnMobAttackClicked);
            mMobViews.Add(view);
        }
    }

    // ─── Игроки ───────────────────────────────────────────────────────────────

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

    private void OnMobAttackClicked(long spawnId)
    {
        mCombatPresenter.EngageMobAsync(spawnId, destroyCancellationToken).Forget();
    }

    private void OnPlayerInfoClicked(PlayerInLocationDto player, Vector2 screenPosition)
    {
        mContextMenuPopup.Show(player, screenPosition);
    }

#if DEV_BUILD
    private void OnRefreshClicked()
    {
        mPresenter.RefreshAsync(destroyCancellationToken).Forget();
    }
#endif
}
