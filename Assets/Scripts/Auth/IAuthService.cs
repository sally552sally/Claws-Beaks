using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Сервис авторизации. Вызывает API и сохраняет/очищает токены.
/// </summary>
public interface IAuthService
{
    /// <summary>Вход по email/паролю. Сохраняет токены при успехе.</summary>
    /// <exception cref="ApiException">401 — неверный пароль, 403 — бан.</exception>
    UniTask<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Регистрация. Сохраняет токены при успехе.</summary>
    /// <exception cref="ApiException">409 — email занят.</exception>
    UniTask<AuthResponse> RegisterAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Выход. Очищает токены и уведомляет сервер.</summary>
    UniTask LogoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Восстановление сессии при старте приложения.
    /// Делает refresh сохранённого токена.
    /// </summary>
    /// <returns>true — сессия восстановлена, false — нужен логин.</returns>
    UniTask<bool> TryRestoreSessionAsync(CancellationToken ct = default);
}
