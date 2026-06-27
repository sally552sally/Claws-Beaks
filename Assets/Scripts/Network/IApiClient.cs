using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// HTTP-клиент. Все запросы к серверу идут через него.
/// Автоматически добавляет Authorization-заголовок и обновляет токен при 401.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Сессия истекла — refresh-токен невалиден.
    /// Подписчик (AppController) перебрасывает на экран авторизации.
    /// </summary>
    event Action SessionExpired;

    /// <summary>GET-запрос с десериализацией ответа в T.</summary>
    UniTask<T> GetAsync<T>(string endpoint, CancellationToken ct = default);

    /// <summary>POST-запрос с десериализацией ответа в T.</summary>
    UniTask<T> PostAsync<T>(string endpoint, object body, CancellationToken ct = default);

    /// <summary>POST-запрос без тела ответа (204 No Content).</summary>
    UniTask PostAsync(string endpoint, object body, CancellationToken ct = default);

    /// <summary>PUT-запрос с десериализацией ответа в T.</summary>
    UniTask<T> PutAsync<T>(string endpoint, object body, CancellationToken ct = default);

    /// <summary>DELETE-запрос без тела ответа.</summary>
    UniTask DeleteAsync(string endpoint, CancellationToken ct = default);
}
