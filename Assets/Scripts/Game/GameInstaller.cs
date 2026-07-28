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

    [Header("Чат")]
    [SerializeField] private View_Chat mChatView;

    [Header("Уведомления")]
    [SerializeField] private View_Notifications mNotificationsView;

    public override void InstallBindings()
    {
        InstallRealtime();
        InstallLocation();
        InstallCombat();
        InstallInventory();
        InstallChat();
        InstallNotifications();
    }

    // ─── Реалтайм (SignalR) ──────────────────────────────────────────────────────

    private void InstallRealtime()
    {
        // Минимальный контекст "кто я" (CharacterId) — нужен LocationPresenter для
        // самофильтрации CombatStarted. NonLazy: тянет /api/character сразу при старте
        // сцены, параллельно с LocationPresenter.RefreshAsync.
        Container.BindInterfacesAndSelfTo<CharacterContext>()
            .AsSingle()
            .NonLazy();

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: официальный SignalR-клиент несовместим с браузерным wasm-рантаймом
        // (падает с "function signature mismatch" — HttpClient/потоки недоступны).
        // До готовности jslib-моста живём на Null-заглушках — локация/чат работают
        // только через REST, без live-пушей. Подробности — TD-C35 в PROGRESS_CLIENT.md.
        Container.Bind<ILocationRealtimeService>()
            .To<NullLocationRealtimeService>()
            .AsSingle();

        Container.Bind<IChatRealtimeService>()
            .To<NullChatRealtimeService>()
            .AsSingle();
#else
        // Живые события локации (мобы/игроки/PvP/чат локации) через SignalR. NonLazy:
        // соединение поднимается сразу при старте Game-сцены, не дожидаясь первого обращения.
        Container.BindInterfacesAndSelfTo<LocationRealtimeService>()
            .AsSingle()
            .NonLazy();

        // Живые сообщения торгового чата и лички через SignalR (второе соединение,
        // /hubs/chat — сервер держит его отдельно от LocationHub, см. HubPaths).
        // NonLazy: подписка на Trade/Personal идёт сразу, не только когда открыт чат.
        Container.BindInterfacesAndSelfTo<ChatRealtimeService>()
            .AsSingle()
            .NonLazy();
#endif
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

    // ─── Чат ──────────────────────────────────────────────────────────────────

    private void InstallChat()
    {
        Container.Bind<IChatService>()
            .To<ChatService>()
            .AsSingle();

        // Буфер сообщений (в памяти, не история с сервера). NonLazy: копит сообщения
        // с самого старта сцены, не только пока открыта панель чата.
        Container.BindInterfacesAndSelfTo<ChatHistoryService>()
            .AsSingle()
            .NonLazy();

        // NonLazy: подписывается на ChatMessageReceived (оба realtime-сервиса) и на
        // LocationPresenter.CurrentLocationId в конструкторе — должен существовать
        // с самого старта сцены, иначе сообщения, пришедшие до открытия панели, потеряются.
        Container.BindInterfacesAndSelfTo<ChatPresenter>()
            .AsSingle()
            .NonLazy();

        Container.Bind<View_Chat>()
            .FromInstance(mChatView)
            .AsSingle();
    }

    // ─── Уведомления ────────────────────────────────────────────────────────────

    private void InstallNotifications()
    {
        // Сервис (INotificationService) биндится на ProjectContext (ProjectInstaller).
        // Здесь — только View сцены Game, которая его резолвит.
        Container.Bind<View_Notifications>()
            .FromInstance(mNotificationsView)
            .AsSingle();
    }
}
