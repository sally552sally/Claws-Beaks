using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Строка одного игрока в списке локации.
/// Создаётся динамически из префаба в View_Hunting.
///
/// Кнопка [i] слева → View_Hunting показывает ContextMenuPopup рядом с кнопкой.
/// Действия в меню: Профиль / Написать / Пригласить в группу (заглушки).
///
/// Prefab: Item_PlayerRow
/// </summary>
public class PlayerListItem : MonoBehaviour
{
    [SerializeField] private TMP_Text mNicknameLabel;
    [SerializeField] private Button   mInfoButton;

    private PlayerInLocationDto mPlayer;

    /// <summary>Колбэк с данными игрока и позицией кнопки [i] для размещения попапа.</summary>
    private Action<PlayerInLocationDto, Vector2> mOnInfoClicked;

    private void Awake()
    {
        mInfoButton.onClick.AddListener(OnInfoClicked);
    }

    private void OnDestroy()
    {
        mInfoButton.onClick.RemoveListener(OnInfoClicked);
    }

    /// <summary>
    /// Инициализация строки данными с сервера.
    /// onInfoClicked получает: данные игрока + screen-позицию кнопки [i].
    /// </summary>
    public void Setup(PlayerInLocationDto player, Action<PlayerInLocationDto, Vector2> onInfoClicked)
    {
        mPlayer        = player;
        mOnInfoClicked = onInfoClicked;

        // Ник + уровень в скобках
        mNicknameLabel.text = $"{player.Nickname} ({player.Level})";
    }

    private void OnInfoClicked()
    {
        // Передаём screen-позицию кнопки для корректного размещения попапа
        mOnInfoClicked?.Invoke(mPlayer, mInfoButton.transform.position);
    }
}
