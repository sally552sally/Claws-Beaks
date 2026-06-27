using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

/// <summary>Загружает сцену асинхронно через LoadSceneAsync.</summary>
public class SceneLoader : ISceneLoader
{
    public async UniTask LoadAsync(string sceneName, CancellationToken ct = default)
    {
        await SceneManager.LoadSceneAsync(sceneName).WithCancellation(ct);
    }
}
