using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;

/// <summary>
/// Реализация сервиса авторизации.
/// Вызывает эндпоинты /api/auth/* через IApiClient и управляет токенами через ITokenStorage.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IApiClient mApiClient;
    private readonly ITokenStorage mTokenStorage;

    [Inject]
    public AuthService(IApiClient apiClient, ITokenStorage tokenStorage)
    {
        mApiClient = apiClient;
        mTokenStorage = tokenStorage;
    }

    public async UniTask<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var response = await mApiClient.PostAsync<AuthResponse>(
            "/api/auth/login",
            new { email, password },
            ct);

        mTokenStorage.SaveTokens(response.AccessToken, response.RefreshToken);
        return response;
    }

    public async UniTask<AuthResponse> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        // raceId = null → сервер использует дефолтную расу (1)
        var response = await mApiClient.PostAsync<AuthResponse>(
            "/api/auth/register",
            new { email, password, raceId = (int?)null },
            ct);

        mTokenStorage.SaveTokens(response.AccessToken, response.RefreshToken);
        return response;
    }

    public async UniTask LogoutAsync(CancellationToken ct = default)
    {
        var refreshToken = mTokenStorage.GetRefreshToken();

        // Сначала чистим локально — сессия завершена даже если сервер недоступен
        mTokenStorage.Clear();

        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                await mApiClient.PostAsync("/api/auth/logout", new { refreshToken }, ct);
            }
            catch
            {
                // Игнорируем — токены уже очищены локально
            }
        }
    }

    public async UniTask<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        if (!mTokenStorage.HasTokens)
            return false;

        try
        {
            var refreshToken = mTokenStorage.GetRefreshToken();
            var response = await mApiClient.PostAsync<AuthResponse>(
                "/api/auth/refresh",
                new { refreshToken },
                ct);

            mTokenStorage.SaveTokens(response.AccessToken, response.RefreshToken);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
            mTokenStorage.Clear();
            return false;
        }
    }
}
