using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>Загрузчик сцен. Обёртка над SceneManager — не дублировать строки сцен в коде.</summary>
public interface ISceneLoader
{
    UniTask LoadAsync(string sceneName, CancellationToken ct = default);
}
