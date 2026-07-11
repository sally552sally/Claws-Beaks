using UnityEngine;

/// <summary>
/// Конфиг realtime-соединений (SignalR). Создать: ПКМ в Project → Create → MMORPG →
/// RealtimeConfig. Хранить в Assets/Configs/RealtimeConfig.asset, назначить в ProjectInstaller.
/// </summary>
[CreateAssetMenu(fileName = "RealtimeConfig", menuName = "MMORPG/RealtimeConfig")]
public class RealtimeConfig : ScriptableObject
{
    [Header("Переподключение")]
    [SerializeField, Tooltip("Первая пауза перед повтором ПЕРВОГО подключения, сек. " +
        "После первого успешного коннекта дальнейшие разрывы обрабатывает встроенный " +
        "механизм SignalR (WithAutomaticReconnect) с той же прогрессией.")]
    private float mInitialRetryDelaySeconds = 2f;

    [SerializeField, Tooltip("Потолок паузы между попытками — экспоненциальный рост " +
        "капается здесь и для первого подключения, и для авто-реконнекта.")]
    private float mMaxRetryDelaySeconds = 30f;

    public float InitialRetryDelaySeconds => mInitialRetryDelaySeconds;
    public float MaxRetryDelaySeconds => mMaxRetryDelaySeconds;
}
