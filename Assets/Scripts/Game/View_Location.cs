using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Главный экран Game-сцены — текущая локация.
/// Показывает название, уровень, таймер и кнопки соседних локаций.
///
/// Навигация между панелями управляется через LocationPresenter.IsHuntingOpen:
///   — «Охота» → OpenHunting()  → скрывает mPanelLocationMain, показывает mPanelHunting
///   — «Назад» в View_Hunting → CloseHunting() → обратно
///
/// БЕЗОПАСНОСТЬ:
///   locationId для перехода берётся строго из NeighborDto (серверный ответ).
///
/// GameObject: View_Location
/// </summary>
public class View_Location : DisposableBehaviour
{
    // ─── Информация о локации ─────────────────────────────────────────────────

    [Header("Информация о локации")]
    [SerializeField] private TMP_Text mLocationNameLabel;
    [SerializeField] private TMP_Text mLocationLevelLabel;
    [SerializeField] private TMP_Text mTimerLabel;
    [SerializeField] private GameObject mLoadingSpinner;

    // ─── Соседние локации ─────────────────────────────────────────────────────

    [Header("Соседние локации")]
    [SerializeField] private Transform mNeighborsContainer;
    [SerializeField] private NeighborButtonView mNeighborButtonPrefab;

    // ─── Навигация ────────────────────────────────────────────────────────────

    [Header("Навигация")]
    [SerializeField] private Button mHuntButton;
    [SerializeField] private Button mMapButton;
    /// <summary>Кнопка «Инвентарь» — открывает Panel_Inventory поверх локации.</summary>
    [SerializeField] private Button mInventoryButton;
    /// <summary>Кнопка «Чат» — открывает Panel_Chat поверх локации.</summary>
    [SerializeField] private Button mChatButton;

    /// <summary>
    /// Кнопка «Кузнец» — открывает Panel_Blacksmith (ремонт). Видна ТОЛЬКО в локациях, где
    /// кузнец есть: сервер отдаёт флаг в blacksmithEnabled. Прятать, а не гасить — так само
    /// расположение кнопки объясняет игроку, что чинят в городе.
    /// </summary>
    [SerializeField] private Button mBlacksmithButton;
    /// <summary>Кнопка «Выйти» — необязательна, добавляешь сам в редакторе и просто
    /// перетаскиваешь сюда. Код клика уже готов (BindButtons ниже).</summary>
    [SerializeField] private Button mLogoutButton;

    /// <summary>
    /// Основной контент локации (название, соседи, кнопки).
    /// Скрывается когда открыта охота.
    /// </summary>
    [SerializeField] private GameObject mPanelLocationMain;

    /// <summary>
    /// Panel_Hunting — показывается когда IsHuntingOpen=true.
    /// Управляется через LocationPresenter.IsHuntingOpen, не напрямую.
    /// </summary>
    [SerializeField] private GameObject mPanelHunting;

    // ─── Внутренний стейт ─────────────────────────────────────────────────────

    private LocationPresenter mPresenter;
    private InventoryPresenter mInventoryPresenter;
    private ChatPresenter mChatPresenter;
    private BlacksmithPresenter mBlacksmithPresenter;
    private readonly List<NeighborButtonView> mNeighborButtons = new();

    // ─── Zenject Inject ───────────────────────────────────────────────────────

    [Inject]
    public void Construct(LocationPresenter presenter, InventoryPresenter inventoryPresenter,
        ChatPresenter chatPresenter, BlacksmithPresenter blacksmithPresenter)
    {
        mPresenter = presenter;
        mInventoryPresenter = inventoryPresenter;
        mChatPresenter = chatPresenter;
        mBlacksmithPresenter = blacksmithPresenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        BindLabels();
        BindNeighbors();
        BindButtons();
        BindHuntingSwitch();
    }

    // ─── Привязки ─────────────────────────────────────────────────────────────

    private void BindLabels()
    {
        mLocationNameLabel
            .SetTextSource(mPresenter.LocationName)
            .DisposeWhenLifeEnded(this);

        mPresenter.LocationLevel
            .SubscribeOnValueChanged(level => mLocationLevelLabel.text = $"Уровень: {level}")
            .DisposeWhenLifeEnded(this);

        mPresenter.TimerText
            .SubscribeOnValueChanged(text =>
            {
                mTimerLabel.text = text;
                mTimerLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
            })
            .DisposeWhenLifeEnded(this);

        // Ошибки локации теперь идут тостами через INotificationService (Фаза 5),
        // не строкой в этом экране.

        mPresenter.IsLoading
            .SubscribeOnValueChanged(loading => mLoadingSpinner.SetActive(loading))
            .DisposeWhenLifeEnded(this);
    }

    private void BindNeighbors()
    {
        mPresenter.Neighbors
            .SubscribeOnValueChanged(RebuildNeighborButtons)
            .DisposeWhenLifeEnded(this);

        mPresenter.CanMove
            .SubscribeOnValueChanged(_ => RebuildNeighborButtons(mPresenter.Neighbors.Value))
            .DisposeWhenLifeEnded(this);
    }

    private void BindButtons()
    {
        mHuntButton.SubscribeOnClick(OnHuntClicked).DisposeWhenLifeEnded(this);
        mMapButton.SubscribeOnClick(OnMapClicked).DisposeWhenLifeEnded(this);

        if (mInventoryButton != null)
            mInventoryButton.SubscribeOnClick(() => mInventoryPresenter.Open())
                .DisposeWhenLifeEnded(this);

        if (mChatButton != null)
            mChatButton.SubscribeOnClick(() => mChatPresenter.Open())
                .DisposeWhenLifeEnded(this);

        if (mBlacksmithButton != null)
        {
            mBlacksmithButton.SubscribeOnClick(() => mBlacksmithPresenter.Open())
                .DisposeWhenLifeEnded(this);

            // Видимость — по флагу локации. callOnSubscribe: true, чтобы кнопка приняла верное
            // состояние сразу, не дожидаясь следующего обновления локации.
            mPresenter.BlacksmithHere
                .SubscribeOnValueChanged(here => mBlacksmithButton.gameObject.SetActive(here),
                    callOnSubscribe: true)
                .DisposeWhenLifeEnded(this);
        }

        if (mLogoutButton != null)
            mLogoutButton.SubscribeOnClick(() => mPresenter.LogoutAsync(destroyCancellationToken).Forget())
                .DisposeWhenLifeEnded(this);
    }

    /// <summary>
    /// Подписывается на IsHuntingOpen и переключает между панелями.
    /// Panel_LocationMain ↔ Panel_Hunting — всегда ровно одна активна.
    /// </summary>
    private void BindHuntingSwitch()
    {
        // Применяем начальное состояние сразу
        OnHuntingStateChanged(mPresenter.IsHuntingOpen.Value);

        mPresenter.IsHuntingOpen
            .SubscribeOnValueChanged(OnHuntingStateChanged)
            .DisposeWhenLifeEnded(this);
    }

    // ─── Соседи ───────────────────────────────────────────────────────────────

    private void RebuildNeighborButtons(List<NeighborDto> neighbors)
    {
        foreach (var btn in mNeighborButtons)
            Destroy(btn.gameObject);
        mNeighborButtons.Clear();

        if (neighbors == null || neighbors.Count == 0) return;

        var canMove = mPresenter.CanMove.Value;

        foreach (var neighbor in neighbors)
        {
            var btn = Instantiate(mNeighborButtonPrefab, mNeighborsContainer);
            btn.Setup(neighbor, canMove, OnNeighborClicked);
            mNeighborButtons.Add(btn);
        }
    }

    // ─── Обработчики ──────────────────────────────────────────────────────────

    private void OnNeighborClicked(long locationId)
    {
        mPresenter.MoveAsync(locationId, destroyCancellationToken).Forget();
    }

    private void OnHuntClicked()
    {
        // Открытие охоты через презентер — он же сообщит View_Hunting через IsHuntingOpen
        mPresenter.OpenHunting();
    }

    private void OnHuntingStateChanged(bool isHuntingOpen)
    {
        if (mPanelLocationMain != null)
            mPanelLocationMain.SetActive(!isHuntingOpen);

        if (mPanelHunting != null)
            mPanelHunting.SetActive(isHuntingOpen);

        // При открытии охоты — обновляем данные явным REST-запросом (подстраховка:
        // основной источник живых обновлений мобов/игроков теперь SignalR, см.
        // ILocationRealtimeService; этот вызов просто гарантирует свежий снимок
        // на момент открытия панели, а не ждёт следующего пуша).
        if (isHuntingOpen)
            mPresenter.RefreshAsync(destroyCancellationToken).Forget();
    }

    private void OnMapClicked()
    {
        // TODO Фаза 2в: открыть View_Map
        Debug.Log("[View_Location] Карта — Фаза 2в");
    }
}
