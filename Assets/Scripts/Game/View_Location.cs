using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Главный экран Game-сцены — текущая локация.
/// Показывает название, уровень, таймер перехода и кнопки соседних локаций.
/// Panel_Hunting и View_Map — дочерние панели, открываются по кнопкам.
///
/// Архитектура:
///   View_Location (этот скрипт, всегда активен)
///     ├── Panel_LocationMain  — основной вид (фон, название, соседи)
///     └── Panel_Hunting       — экран охоты (мобы, игроки) [Фаза 2б]
///
/// View_Map — отдельный Canvas поверх (Sort Order выше).
///
/// БЕЗОПАСНОСТЬ:
///   locationId для перехода берётся строго из NeighborDto (серверный ответ).
///   CanMove, CombatEnabled, PvpEnabled — только от сервера, только для UX.
///
/// GameObject: View_Location
/// </summary>
public class View_Location : DisposableBehaviour
{
    // ─── Информация о локации ─────────────────────────────────────────────────

    [Header("Информация о локации")]
    [SerializeField] private TMP_Text   mLocationNameLabel;
    [SerializeField] private TMP_Text   mLocationLevelLabel;
    [SerializeField] private TMP_Text   mTimerLabel;
    [SerializeField] private TMP_Text   mErrorLabel;
    [SerializeField] private GameObject mLoadingSpinner;

    // ─── Соседние локации ─────────────────────────────────────────────────────

    [Header("Соседние локации")]
    [SerializeField] private Transform         mNeighborsContainer;
    [SerializeField] private NeighborButtonView mNeighborButtonPrefab;

    // ─── Навигация ────────────────────────────────────────────────────────────

    [Header("Навигация")]
    [SerializeField] private Button     mHuntButton;
    [SerializeField] private Button     mMapButton;
    [SerializeField] private GameObject mPanelHunting;

    // ─── DEV_BUILD ────────────────────────────────────────────────────────────

#if DEV_BUILD
    [Header("DEV — убрать в Фазе 5 (SignalR)")]
    [SerializeField] private Button mRefreshButton;
#endif

    // ─── Внутренний стейт ─────────────────────────────────────────────────────

    private LocationPresenter mPresenter;
    private readonly List<NeighborButtonView> mNeighborButtons = new();

    // ─── Zenject Inject ───────────────────────────────────────────────────────

    [Inject]
    public void Construct(LocationPresenter presenter)
    {
        mPresenter = presenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        BindLabels();
        BindNeighbors();
        BindButtons();
    }

    // ─── Привязки ─────────────────────────────────────────────────────────────

    private void BindLabels()
    {
        // Название локации
        mLocationNameLabel
            .SetTextSource(mPresenter.LocationName)
            .DisposeWhenLifeEnded(this);

        // Уровень локации
        mPresenter.LocationLevel
            .SubscribeOnValueChanged(level =>
                mLocationLevelLabel.text = $"Уровень: {level}")
            .DisposeWhenLifeEnded(this);

        // Таймер обратного отсчёта
        mPresenter.TimerText
            .SubscribeOnValueChanged(text =>
            {
                mTimerLabel.text = text;
                mTimerLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
            })
            .DisposeWhenLifeEnded(this);

        // Ошибки
        mPresenter.ErrorMessage
            .SubscribeOnValueChanged(msg =>
            {
                mErrorLabel.text = msg;
                mErrorLabel.gameObject.SetActive(!string.IsNullOrEmpty(msg));
            })
            .DisposeWhenLifeEnded(this);

        // Спиннер загрузки
        mPresenter.IsLoading
            .SubscribeOnValueChanged(loading => mLoadingSpinner.SetActive(loading))
            .DisposeWhenLifeEnded(this);
    }

    private void BindNeighbors()
    {
        // Пересоздаём кнопки при каждом обновлении списка соседей
        mPresenter.Neighbors
            .SubscribeOnValueChanged(RebuildNeighborButtons)
            .DisposeWhenLifeEnded(this);

        // Обновляем интерактивность кнопок при изменении CanMove
        mPresenter.CanMove
            .SubscribeOnValueChanged(canMove =>
            {
                foreach (var btn in mNeighborButtons)
                    btn.SetCanMove(canMove);
            })
            .DisposeWhenLifeEnded(this);
    }

    private void BindButtons()
    {
        mHuntButton.SubscribeOnClick(OnHuntClicked).DisposeWhenLifeEnded(this);
        mMapButton.SubscribeOnClick(OnMapClicked).DisposeWhenLifeEnded(this);

#if DEV_BUILD
        // TD: убрать кнопку в Фазе 5 — заменить на SignalR push из LocationHub
        if (mRefreshButton != null)
            mRefreshButton.SubscribeOnClick(OnRefreshClicked).DisposeWhenLifeEnded(this);
#endif
    }

    // ─── Соседи ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Пересоздаёт кнопки соседних локаций по актуальным данным с сервера.
    /// Старые кнопки уничтожаются, новые инстанциируются из префаба.
    /// locationId в каждую кнопку приходит строго из NeighborDto.
    /// </summary>
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

    /// <summary>
    /// Тап по кнопке соседней локации.
    /// locationId строго из NeighborButtonView.Setup (серверный ответ).
    /// </summary>
    private void OnNeighborClicked(long locationId)
    {
        mPresenter.MoveAsync(locationId, destroyCancellationToken).Forget();
    }

    private void OnHuntClicked()
    {
        if (mPanelHunting == null) return;

        var isOpen = !mPanelHunting.activeSelf;
        mPanelHunting.SetActive(isOpen);

        // При открытии охоты — обновляем данные (мобы/игроки могли измениться)
        // TD Фаза 5: заменить на SignalR push — убрать этот вызов
        if (isOpen)
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
        // TD: убрать в Фазе 5, заменить на SignalR push
        mPresenter.RefreshAsync(destroyCancellationToken).Forget();
    }
#endif
}
