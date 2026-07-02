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

    // ─── DEV_BUILD ────────────────────────────────────────────────────────────

#if DEV_BUILD
    [Header("DEV — убрать в Фазе 5 (SignalR)")]
    [SerializeField] private Button mRefreshButton;
#endif

    // ─── Внутренний стейт ─────────────────────────────────────────────────────

    private LocationPresenter mPresenter;
    private InventoryPresenter mInventoryPresenter;
    private readonly List<NeighborButtonView> mNeighborButtons = new();

    // ─── Zenject Inject ───────────────────────────────────────────────────────

    [Inject]
    public void Construct(LocationPresenter presenter, InventoryPresenter inventoryPresenter)
    {
        mPresenter = presenter;
        mInventoryPresenter = inventoryPresenter;
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

#if DEV_BUILD
        if (mRefreshButton != null)
            mRefreshButton.SubscribeOnClick(OnRefreshClicked).DisposeWhenLifeEnded(this);
#endif
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

        // При открытии охоты — обновляем данные (мобы/игроки могли измениться)
        // TD-11: заменить на SignalR push в Фазе 5
        if (isHuntingOpen)
            mPresenter.RefreshAsync(destroyCancellationToken).Forget();
    }

    private void OnMapClicked()
    {
        // TODO Фаза 2в: открыть View_Map
        Debug.Log("[View_Location] Карта — Фаза 2в");
    }

#if DEV_BUILD
    private void OnRefreshClicked()
    {
        mPresenter.RefreshAsync(destroyCancellationToken).Forget();
    }
#endif
}
