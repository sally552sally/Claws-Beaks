/// <summary>
/// Хранилище JWT-токенов. MVP-реализация — PlayerPrefs.
/// Перед продом заменить на нативный Keychain (iOS) / EncryptedSharedPreferences (Android).
/// Абстракция позволяет сменить реализацию одной строкой в ProjectInstaller.
/// </summary>
public interface ITokenStorage
{
    /// <summary>true — есть оба токена в хранилище.</summary>
    bool HasTokens { get; }

    /// <summary>Сохранить пару токенов после успешного логина или refresh.</summary>
    void SaveTokens(string accessToken, string refreshToken);

    /// <summary>Получить access-токен для заголовка Authorization.</summary>
    string GetAccessToken();

    /// <summary>Получить refresh-токен для обновления сессии.</summary>
    string GetRefreshToken();

    /// <summary>Очистить токены при логауте или невалидной сессии.</summary>
    void Clear();
}
