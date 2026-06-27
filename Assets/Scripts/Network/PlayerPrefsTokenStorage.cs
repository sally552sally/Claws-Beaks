using UnityEngine;

/// <summary>
/// Хранение токенов через PlayerPrefs. Достаточно для MVP.
/// TD-01: заменить на Keychain/EncryptedSharedPreferences перед продом.
/// </summary>
public class PlayerPrefsTokenStorage : ITokenStorage
{
    private const string ACCESS_TOKEN_KEY = "auth.access_token";
    private const string REFRESH_TOKEN_KEY = "auth.refresh_token";

    public bool HasTokens =>
        !string.IsNullOrEmpty(GetAccessToken()) &&
        !string.IsNullOrEmpty(GetRefreshToken());

    public void SaveTokens(string accessToken, string refreshToken)
    {
        PlayerPrefs.SetString(ACCESS_TOKEN_KEY, accessToken);
        PlayerPrefs.SetString(REFRESH_TOKEN_KEY, refreshToken);
        PlayerPrefs.Save();
    }

    public string GetAccessToken() =>
        PlayerPrefs.GetString(ACCESS_TOKEN_KEY, string.Empty);

    public string GetRefreshToken() =>
        PlayerPrefs.GetString(REFRESH_TOKEN_KEY, string.Empty);

    public void Clear()
    {
        PlayerPrefs.DeleteKey(ACCESS_TOKEN_KEY);
        PlayerPrefs.DeleteKey(REFRESH_TOKEN_KEY);
        PlayerPrefs.Save();
    }
}
