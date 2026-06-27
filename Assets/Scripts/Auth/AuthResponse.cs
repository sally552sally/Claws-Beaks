using Newtonsoft.Json;

/// <summary>
/// Ответ сервера на /api/auth/login, /api/auth/register, /api/auth/refresh.
/// </summary>
public class AuthResponse
{
    [JsonProperty("accessToken")]  public string AccessToken  { get; set; }
    [JsonProperty("refreshToken")] public string RefreshToken { get; set; }
    [JsonProperty("username")]     public string Username     { get; set; }
    [JsonProperty("role")]         public string Role         { get; set; }
}
