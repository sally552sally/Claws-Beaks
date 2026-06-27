using UnityEngine;

/// <summary>
/// Конфигурация HTTP-клиента. Два ассета: ApiConfig_Dev и ApiConfig_Prod.
/// Переключать вручную в ProjectInstaller — никогда не хардкодить URL в коде.
/// </summary>
[CreateAssetMenu(menuName = "MMORPG/ApiConfig", fileName = "ApiConfig")]
public class ApiConfig : ScriptableObject
{
    [SerializeField] private string mBaseUrl = "https://localhost:7052";
    [SerializeField] private int mTimeoutSeconds = 30;

    public string BaseUrl => mBaseUrl;
    public int TimeoutSeconds => mTimeoutSeconds;
}
