using UnityEngine;

/// <summary>
/// Конфиг панели уведомлений. Все числа/цвета — здесь, не в коде (правило проекта — без хардкода).
/// Создать: ПКМ в Project → Create → MMORPG → NotificationConfig.
/// Хранить в Assets/Configs/NotificationConfig.asset, назначить в ProjectInstaller.
/// </summary>
[CreateAssetMenu(fileName = "NotificationConfig", menuName = "MMORPG/NotificationConfig")]
public class NotificationConfig : ScriptableObject
{
    [Header("Тосты")]
    [SerializeField, Tooltip("Если выключено — тосты полностью не показываются (ShowToast/ShowError/" +
        "ShowWarning/ShowInfo молча ничего не делают). Диалоги (ShowDialog/ShowConfirm/ShowMessage) " +
        "не затрагиваются — это отдельный, всегда включённый канал.")]
    private bool mToastsEnabled = true;

    [SerializeField, Tooltip("Сколько секунд тост висит до авто-скрытия")]
    private float mToastDurationSeconds = 2.5f;

    [SerializeField, Tooltip("Пауза между тостами в очереди (сек), чтобы не мигало")]
    private float mToastGapSeconds = 0.15f;

    [Header("Диалоги")]
    [SerializeField, Range(0f, 1f), Tooltip("Прозрачность затемнения фона под модальным диалогом (0 — без затемнения)")]
    private float mDialogDimAlpha = 0.1f;

    [Header("Цвета по типу уведомления")]
    [SerializeField] private Color mColorInfo = new(0.35f, 0.55f, 0.85f);
    [SerializeField] private Color mColorMessage = new(0.6f, 0.6f, 0.6f);
    [SerializeField] private Color mColorWarning = new(0.9f, 0.7f, 0.2f);
    [SerializeField] private Color mColorError = new(0.85f, 0.25f, 0.25f);

    public float ToastDurationSeconds => mToastDurationSeconds;
    public float ToastGapSeconds => mToastGapSeconds;
    public bool ToastsEnabled => mToastsEnabled;
    public float DialogDimAlpha => mDialogDimAlpha;

    /// <summary>Акцентный цвет для типа уведомления.</summary>
    public Color ColorFor(NotificationType type) => type switch
    {
        NotificationType.Error => mColorError,
        NotificationType.Warning => mColorWarning,
        NotificationType.Message => mColorMessage,
        _ => mColorInfo
    };
}
