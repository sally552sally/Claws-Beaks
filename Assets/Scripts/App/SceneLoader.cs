using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using Zenject;

/// <summary>Загружает сцену асинхронно через LoadSceneAsync.</summary>
public class SceneLoader : ISceneLoader
{
    private readonly INotificationService mNotifications;

    [Inject]
    public SceneLoader(INotificationService notifications)
    {
        mNotifications = notifications;
    }

    public async UniTask LoadAsync(string sceneName, CancellationToken ct = default)
    {
        // Перед сменой сцены снимаем показанный диалог, если он не помечен как переживающий
        // переход — его вызвавший Presenter сейчас будет уничтожен.
        mNotifications.OnSceneChanging();

        await SceneManager.LoadSceneAsync(sceneName).WithCancellation(ct);
    }
}
