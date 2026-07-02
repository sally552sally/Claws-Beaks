using UnityEngine;
using Zenject;

/// <summary>
/// Zenject-инсталлер для SceneContext сцены Auth.
/// </summary>
public class AuthInstaller : MonoInstaller
{
    [SerializeField] private AuthFormView mAuthFormView;
    [SerializeField] private BanPopup     mBanPopup;
    [SerializeField] private View_Notifications mNotificationsView;

    public override void InstallBindings()
    {
        Container.Bind<AuthPresenter>()
            .AsSingle()
            .NonLazy();

        Container.Bind<AuthFormView>()
            .FromInstance(mAuthFormView)
            .AsSingle();

        Container.Bind<BanPopup>()
            .FromInstance(mBanPopup)
            .AsSingle();

        // Панель уведомлений сцены Auth (резолвит INotificationService с ProjectContext).
        Container.Bind<View_Notifications>()
            .FromInstance(mNotificationsView)
            .AsSingle();
    }
}
