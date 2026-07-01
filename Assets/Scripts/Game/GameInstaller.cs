using UnityEngine;
using Zenject;

/// <summary>
/// Zenject-инсталлер для SceneContext сцены Game.
/// Разделён на методы по фичам для удобства (беклог: вынести в отдельные MonoInstaller).
/// </summary>
public class GameInstaller : MonoInstaller
{
    [Header("Локация")]
    [SerializeField] private View_Location  mLocationView;
    [SerializeField] private View_Hunting   mHuntingView;

    [Header("Бой")]
    [SerializeField] private View_Combat        mCombatView;
    [SerializeField] private Popup_CombatResult mCombatResultPopup;

    [Header("Инвентарь")]
    [SerializeField] private View_Inventory   mInventoryView;
    [SerializeField] private Popup_ItemDetail mItemDetailPopup;

    public override void InstallBindings()
    {
        InstallLocation();
        InstallCombat();
        InstallInventory();
    }

    // ─── Локация ──────────────────────────────────────────────────────────────

    private void InstallLocation()
    {
        Container.Bind<ILocationService>()
            .To<LocationService>()
            .AsSingle();

        Container.BindInterfacesAndSelfTo<LocationPresenter>()
            .AsSingle()
            .NonLazy();

        Container.Bind<View_Location>()
            .FromInstance(mLocationView)
            .AsSingle();

        Container.Bind<View_Hunting>()
            .FromInstance(mHuntingView)
            .AsSingle();
    }

    // ─── Бой ──────────────────────────────────────────────────────────────────

    private void InstallCombat()
    {
        Container.Bind<ICombatService>()
            .To<CombatService>()
            .AsSingle();

        // NonLazy: Initialize() вызывается сразу при старте сцены →
        // проверяем активный бой (авто-возобновление после вылета)
        Container.BindInterfacesAndSelfTo<CombatPresenter>()
            .AsSingle()
            .NonLazy();

        Container.Bind<View_Combat>()
            .FromInstance(mCombatView)
            .AsSingle();

        Container.Bind<Popup_CombatResult>()
            .FromInstance(mCombatResultPopup)
            .AsSingle();
    }

    // ─── Инвентарь ──────────────────────────────────────────────────────────────

    private void InstallInventory()
    {
        Container.Bind<IInventoryService>()
            .To<InventoryService>()
            .AsSingle();

        // NonLazy: Initialize() подписывается на CombatPresenter.IsInCombat
        // (авто-закрытие инвентаря, если стартовал бой).
        Container.BindInterfacesAndSelfTo<InventoryPresenter>()
            .AsSingle()
            .NonLazy();

        Container.Bind<View_Inventory>()
            .FromInstance(mInventoryView)
            .AsSingle();

        Container.Bind<Popup_ItemDetail>()
            .FromInstance(mItemDetailPopup)
            .AsSingle();
    }
}
