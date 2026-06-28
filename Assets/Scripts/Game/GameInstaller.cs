using UnityEngine;
using Zenject;

/// <summary>
/// Zenject-инсталлер для SceneContext сцены Game.
///
/// LocationPresenter биндится через BindInterfacesAndSelfTo:
///   — IInitializable → Zenject вызовет Initialize() автоматически после инъекций
///   — IDisposable    → Zenject вызовет Dispose() при выгрузке сцены
///   — LocationPresenter (self) → можно инжектить по конкретному типу во View
/// </summary>
public class GameInstaller : MonoInstaller
{
    [SerializeField] private View_Location mLocationView;

    public override void InstallBindings()
    {
        // Сервис локаций — чистый HTTP-клиент
        Container.Bind<ILocationService>()
            .To<LocationService>()
            .AsSingle();

        // Presenter:
        //   BindInterfacesAndSelfTo → биндит IInitializable + IDisposable + LocationPresenter
        //   NonLazy → создаётся сразу (Initialize() вызовется до первого кадра)
        Container.BindInterfacesAndSelfTo<LocationPresenter>()
            .AsSingle()
            .NonLazy();

        // View — инстанс из сцены
        Container.Bind<View_Location>()
            .FromInstance(mLocationView)
            .AsSingle();
    }
}
