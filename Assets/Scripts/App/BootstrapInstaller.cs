using UnityEngine;
using Zenject;

/// <summary>
/// Zenject-инсталлер для SceneContext сцены Bootstrap.
/// Регистрирует BootstrapEntryPoint чтобы Zenject инжектнул зависимости.
/// </summary>
public class BootstrapInstaller : MonoInstaller
{
    [SerializeField] private BootstrapEntryPoint mEntryPoint;

    public override void InstallBindings()
    {
        Container.Bind<BootstrapEntryPoint>()
            .FromInstance(mEntryPoint)
            .AsSingle()
            .NonLazy();
    }
}
