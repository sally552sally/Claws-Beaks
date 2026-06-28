using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Кнопка одной соседней локации на экране текущей локации.
/// Создаётся динамически из префаба в View_Location.
///
/// БЕЗОПАСНОСТЬ:
///   mLocationId хранит ID строго из NeighborDto.LocationId (серверный ответ).
///   Клиент никогда не вычисляет и не модифицирует этот ID.
///   EntryEnabled — только UX (скрытие иконки + блокировка кнопки).
///   Сервер проверяет флаги независимо при MoveAsync.
///
/// Prefab: Item_NeighborButton
/// </summary>
public class NeighborButtonView : MonoBehaviour
{
    [SerializeField] private Button     mButton;
    [SerializeField] private TMP_Text   mNameLabel;
    [SerializeField] private TMP_Text   mLevelLabel;

    /// <summary>Иконка закрытой локации (необязательный элемент префаба).</summary>
    [SerializeField] private GameObject mClosedIcon;

    /// <summary>ID строго из NeighborDto — никакой клиентской логики.</summary>
    private long mLocationId;

    /// <summary>EntryEnabled от сервера — нужен при пересчёте интерактивности.</summary>
    private bool mEntryEnabled;

    private Action<long> mOnClick;

    private void Awake()
    {
        mButton.onClick.AddListener(OnButtonClicked);
    }

    private void OnDestroy()
    {
        mButton.onClick.RemoveListener(OnButtonClicked);
    }

    /// <summary>
    /// Инициализация кнопки данными из NeighborDto (серверный ответ).
    /// Вызывается из View_Location при перестройке списка соседей.
    /// </summary>
    /// <param name="neighbor">Данные с сервера. LocationId не трогаем.</param>
    /// <param name="canMove">Текущее значение CanMove от Presenter (с сервера).</param>
    /// <param name="onClick">Колбэк с locationId для передачи в Presenter.MoveAsync.</param>
    public void Setup(NeighborDto neighbor, bool canMove, Action<long> onClick)
    {
        // ID сохраняем как есть — только сервер знает реальный ID
        mLocationId    = neighbor.LocationId;
        mEntryEnabled  = neighbor.EntryEnabled;
        mOnClick       = onClick;

        mNameLabel.text  = neighbor.Name;
        mLevelLabel.text = $"[{neighbor.Level}]";

        // Иконка закрытой локации — UX, не блокировка (сервер блокирует на своей стороне)
        if (mClosedIcon != null)
            mClosedIcon.SetActive(!neighbor.EntryEnabled);

        UpdateInteractable(canMove);
    }

    /// <summary>
    /// Обновить интерактивность при изменении CanMove у Presenter.
    /// Учитывает EntryEnabled соседа — нет смысла жать на закрытую локацию.
    /// </summary>
    public void SetCanMove(bool canMove) => UpdateInteractable(canMove);

    private void UpdateInteractable(bool canMove)
    {
        // Кнопка активна только если: таймер истёк (canMove) И вход открыт (entryEnabled)
        // EntryEnabled — UX-подсказка, финальная проверка на сервере
        mButton.interactable = canMove && mEntryEnabled;
    }

    private void OnButtonClicked() => mOnClick?.Invoke(mLocationId);
}
