using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Точка входа в приложение. Живёт в сцене Bootstrap.
/// Проверяет сохранённую сессию и роутит в Auth или Game.
/// В DEV_BUILD пропускает проверку и сразу логинится с захардкоженными кредами.
/// </summary>
public class BootstrapEntryPoint : DisposableBehaviour
{
    private IAuthService mAuthService;
    private ISceneLoader mSceneLoader;

    [Inject]
    public void Construct(IAuthService authService, ISceneLoader sceneLoader)
    {
        mAuthService = authService;
        mSceneLoader = sceneLoader;
    }

    private void Start()
    {
#if DEV_BUILD
        StartDevAsync().Forget();
#else
        StartAsync().Forget();
#endif
    }

    private async UniTaskVoid StartAsync()
    {
        try
        {
            var hasSession = await mAuthService.TryRestoreSessionAsync(destroyCancellationToken);
            var target = hasSession ? SceneNames.GAME : SceneNames.AUTH;
            await mSceneLoader.LoadAsync(target, destroyCancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.LogError($"[Bootstrap] Ошибка при старте: {ex.Message}");
            await mSceneLoader.LoadAsync(SceneNames.AUTH, destroyCancellationToken);
        }
    }

#if DEV_BUILD
    private async UniTaskVoid StartDevAsync()
    {
        try
        {
            await mAuthService.LoginAsync(
                DevCredentials.EMAIL,
                DevCredentials.PASSWORD,
                destroyCancellationToken);

            await mSceneLoader.LoadAsync(SceneNames.GAME, destroyCancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.LogWarning($"[DEV] Автологин не удался: {ex.Message}. Перехожу на Auth.");
            await mSceneLoader.LoadAsync(SceneNames.AUTH, destroyCancellationToken);
        }
    }
#endif
}
