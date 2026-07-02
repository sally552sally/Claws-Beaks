using UnityEngine;
using Zenject;

/// <summary>
/// Zenject-инсталлер для ProjectContext.
/// Биндит всё что живёт весь жизненный цикл приложения.
/// ВАЖНО: ProjectContext — singleton. Сюда не биндить ничего связанного со сценой.
/// </summary>
public class ProjectInstaller : MonoInstaller
{
    [SerializeField] private ApiConfig mApiConfig;
    [SerializeField] private NotificationConfig mNotificationConfig;

    public override void InstallBindings()
    {
        // Конфиг
        Container.Bind<ApiConfig>()
            .FromInstance(mApiConfig)
            .AsSingle();

        // Конфиг уведомлений
        Container.Bind<NotificationConfig>()
            .FromInstance(mNotificationConfig)
            .AsSingle();

        // Сервис уведомлений (тосты/диалоги). ProjectContext — переживает смену сцен,
        // очередь не теряется. View живёт в каждой сцене (Auth/Game) и резолвит этот сервис.
        Container.Bind<INotificationService>()
            .To<NotificationService>()
            .AsSingle();

        // Хранилище токенов
        Container.Bind<ITokenStorage>()
            .To<PlayerPrefsTokenStorage>()
            .AsSingle();

        // HTTP-клиент
        Container.Bind<IApiClient>()
            .To<ApiClient>()
            .AsSingle();

        // Сервисы
        Container.Bind<IAuthService>()
            .To<AuthService>()
            .AsSingle();

        // Загрузчик сцен
        Container.Bind<ISceneLoader>()
            .To<SceneLoader>()
            .AsSingle();

        // Глобальный контроллер (слушает SessionExpired, живёт всегда)
        Container.Bind<AppController>()
            .AsSingle()
            .NonLazy();
    }
}
