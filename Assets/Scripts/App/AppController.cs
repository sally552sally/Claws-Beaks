using Cysharp.Threading.Tasks;
using Zenject;

/// <summary>
/// Глобальный контроллер приложения. Живёт в ProjectContext весь жизненный цикл.
/// Подписывается на SessionExpired и перебрасывает на Auth-сцену из любого места.
/// </summary>
public class AppController : DisposableObject
{
    private readonly IApiClient mApiClient;
    private readonly ISceneLoader mSceneLoader;

    [Inject]
    public AppController(IApiClient apiClient, ISceneLoader sceneLoader)
    {
        mApiClient = apiClient;
        mSceneLoader = sceneLoader;

        mApiClient.SessionExpired += OnSessionExpired;
    }

    private void OnSessionExpired()
    {
        HandleSessionExpiredAsync().Forget();
    }

    private async UniTaskVoid HandleSessionExpiredAsync()
    {
        // Переключаемся на главный поток перед загрузкой сцены
        await UniTask.SwitchToMainThread();
        await mSceneLoader.LoadAsync(SceneNames.AUTH);
    }

    protected override void OnDispose()
    {
        mApiClient.SessionExpired -= OnSessionExpired;
        base.OnDispose();
    }
}
