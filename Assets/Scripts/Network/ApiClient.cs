using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;
using Zenject;

/// <summary>
/// HTTP-клиент на базе UnityWebRequest + UniTask.
///
/// Важно: UniTask бросает UnityWebRequestException для ЛЮБЫХ HTTP-ошибок (4xx, 5xx).
/// Мы перехватываем ProtocolError (сервер ответил, но с ошибкой) и читаем тело.
/// ConnectionError (сервер недоступен) — не перехватываем, летит наверх как есть.
///
/// Логика 401:
///   — Если эндпоинт НЕ авторизационный и это первая попытка → тихий refresh → повтор
///   — Если refresh вернул 401/403 → SessionExpired → Auth-сцена
/// </summary>
public class ApiClient : IApiClient
{
    public event Action SessionExpired;

    private readonly ApiConfig mConfig;
    private readonly ITokenStorage mTokenStorage;
    private readonly SemaphoreSlim mRefreshLock = new SemaphoreSlim(1, 1);

    [Inject]
    public ApiClient(ApiConfig config, ITokenStorage tokenStorage)
    {
        mConfig = config;
        mTokenStorage = tokenStorage;
    }

    // ─── Публичный API ───────────────────────────────────────────────────────

    public async UniTask<T> GetAsync<T>(string endpoint, CancellationToken ct = default)
    {
        var json = await ExecuteAsync("GET", endpoint, null, ct);
        return Deserialize<T>(json);
    }

    public async UniTask<T> PostAsync<T>(string endpoint, object body, CancellationToken ct = default)
    {
        var json = await ExecuteAsync("POST", endpoint, Serialize(body), ct);
        return Deserialize<T>(json);
    }

    public async UniTask PostAsync(string endpoint, object body, CancellationToken ct = default)
    {
        await ExecuteAsync("POST", endpoint, Serialize(body), ct);
    }

    public async UniTask<T> PutAsync<T>(string endpoint, object body, CancellationToken ct = default)
    {
        var json = await ExecuteAsync("PUT", endpoint, Serialize(body), ct);
        return Deserialize<T>(json);
    }

    public async UniTask DeleteAsync(string endpoint, CancellationToken ct = default)
    {
        await ExecuteAsync("DELETE", endpoint, null, ct);
    }

    // ─── Внутренняя логика ───────────────────────────────────────────────────

    private async UniTask<string> ExecuteAsync(
        string method,
        string endpoint,
        string body,
        CancellationToken ct,
        bool isRetry = false)
    {
        var staleToken = mTokenStorage.GetAccessToken();

        using var request = BuildRequest(method, endpoint, body);

        if (!string.IsNullOrEmpty(staleToken))
            request.SetRequestHeader("Authorization", $"Bearer {staleToken}");

        await SendRequestAsync(request, ct);

        var statusCode = (int)request.responseCode;
        var responseText = request.downloadHandler?.text ?? string.Empty;

        // 401 на не-авторизационном эндпоинте → тихий refresh → повтор
        if (statusCode == 401 && !isRetry && !IsAuthEndpoint(endpoint))
        {
            await EnsureTokenRefreshedAsync(staleToken, ct);
            return await ExecuteAsync(method, endpoint, body, ct, isRetry: true);
        }

        if (!IsSuccess(statusCode))
            throw new ApiException(statusCode, ParseError(responseText));

        return responseText;
    }

    /// <summary>
    /// Отправляет запрос. Перехватывает ProtocolError (4xx/5xx) — сервер ответил,
    /// просто с кодом ошибки. ConnectionError и прочие летят наверх.
    /// </summary>
    private static async UniTask SendRequestAsync(UnityWebRequest request, CancellationToken ct)
    {
        try
        {
            await request.SendWebRequest().WithCancellation(ct);
        }
        catch (UnityWebRequestException) when
            (request.result == UnityWebRequest.Result.ProtocolError)
        {
            // Сервер ответил с 4xx/5xx — читаем статус и тело ниже в ExecuteAsync
        }
        // ConnectionError / DataProcessingError — не ловим, летят как есть
    }

    /// <summary>
    /// Обновляет токен если он ещё не был обновлён параллельным запросом.
    /// SemaphoreSlim гарантирует что только один поток делает refresh за раз.
    /// </summary>
    private async UniTask EnsureTokenRefreshedAsync(string staleToken, CancellationToken ct)
    {
        await mRefreshLock.WaitAsync(ct);
        try
        {
            // Токен уже обновился пока мы стояли в очереди
            if (mTokenStorage.GetAccessToken() != staleToken)
                return;

            await DoRefreshAsync(ct);
        }
        finally
        {
            mRefreshLock.Release();
        }
    }

    private async UniTask DoRefreshAsync(CancellationToken ct)
    {
        var refreshToken = mTokenStorage.GetRefreshToken();

        if (string.IsNullOrEmpty(refreshToken))
        {
            FireSessionExpired();
            throw new ApiException(401, "Refresh-токен отсутствует");
        }

        var body = Serialize(new { refreshToken });
        using var request = BuildRequest("POST", "/api/auth/refresh", body);

        await SendRequestAsync(request, ct);

        var statusCode = (int)request.responseCode;
        var responseText = request.downloadHandler?.text ?? string.Empty;

        if (statusCode == 401 || statusCode == 403)
        {
            mTokenStorage.Clear();
            FireSessionExpired();
            throw new ApiException(statusCode, ParseError(responseText));
        }

        if (!IsSuccess(statusCode))
            throw new ApiException(statusCode, ParseError(responseText));

        var response = Deserialize<RefreshResponse>(responseText);
        mTokenStorage.SaveTokens(response.AccessToken, response.RefreshToken);
    }

    private UnityWebRequest BuildRequest(string method, string endpoint, string body)
    {
        var url = mConfig.BaseUrl + endpoint;
        var request = new UnityWebRequest(url, method)
        {
            timeout = mConfig.TimeoutSeconds,
            downloadHandler = new DownloadHandlerBuffer()
        };

        if (!string.IsNullOrEmpty(body))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", "application/json");
        }

#if UNITY_EDITOR || DEV_BUILD
        request.certificateHandler = new AcceptAllCertificates();
#endif

        return request;
    }

    private void FireSessionExpired() => SessionExpired?.Invoke();

    private static bool IsSuccess(int code) => code >= 200 && code < 300;

    private static bool IsAuthEndpoint(string endpoint) =>
        endpoint.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase);

    private static string ParseError(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "Неизвестная ошибка";
        try { return JsonConvert.DeserializeObject<ApiError>(json)?.Error ?? "Неизвестная ошибка"; }
        catch { return "Неизвестная ошибка"; }
    }

    private static string Serialize(object obj) => JsonConvert.SerializeObject(obj);
    private static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        return JsonConvert.DeserializeObject<T>(json);
    }

    // ─── Внутренние типы ─────────────────────────────────────────────────────

    [Serializable]
    private class RefreshResponse
    {
        [JsonProperty("accessToken")] public string AccessToken { get; set; }
        [JsonProperty("refreshToken")] public string RefreshToken { get; set; }
    }

#if UNITY_EDITOR || DEV_BUILD
    private sealed class AcceptAllCertificates : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }
#endif
}
