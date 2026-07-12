using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Экран охоты — Panel_Hunting.
/// Видимость управляется извне через View_Location (который слушает LocationPresenter.IsHuntingOpen).
/// Кнопка «Назад» вызывает LocationPresenter.CloseHunting().
///
/// Обновление данных происходит при открытии охоты (View_Location.OnHuntingStateChanged).
///
/// GameObject: Panel_Hunting
/// </summary>
public class View_Hunting : DisposableBehaviour
{
    // ─── Мобы ─────────────────────────────────────────────────────────────────

    [Header("Мобы")]
    [SerializeField] private RectTransform mMobsArea;
    [SerializeField] private MobView mMobViewPrefab;

    // ─── Игроки ───────────────────────────────────────────────────────────────

    [Header("Игроки")]
    [SerializeField] private Transform mPlayersContainer;
    [SerializeField] private PlayerListItem mPlayerListItemPrefab;

    // ─── Попап действий ───────────────────────────────────────────────────────

    [Header("Попап игрока")]
    [SerializeField] private ContextMenuPopup mContextMenuPopup;

    // ─── Конфиг ───────────────────────────────────────────────────────────────

    [Header("Конфиг")]
    [SerializeField] private HuntingConfig mHuntingConfig;

    // ─── Навигация ────────────────────────────────────────────────────────────

    [Header("Навигация")]
    /// <summary>Кнопка «← Назад» — возвращает в Panel_LocationMain.</summary>
    [SerializeField] private Button mButtonBack;
    /// <summary>Кнопка «Инвентарь» — открывает Panel_Inventory поверх охоты.</summary>
    [SerializeField] private Button mButtonInventory;
    /// <summary>Кнопка «Чат» — открывает Panel_Chat поверх охоты.</summary>
    [SerializeField] private Button mButtonChat;

    // ─── Внутренний стейт ─────────────────────────────────────────────────────

    private LocationPresenter mPresenter;
    private CombatPresenter mCombatPresenter;
    private InventoryPresenter mInventoryPresenter;
    private ChatPresenter mChatPresenter;

    private readonly List<MobView> mMobViews = new();
    private readonly List<PlayerListItem> mPlayerItems = new();

    // ─── Zenject Inject ───────────────────────────────────────────────────────

    [Inject]
    public void Construct(LocationPresenter presenter, CombatPresenter combatPresenter,
        InventoryPresenter inventoryPresenter, ChatPresenter chatPresenter)
    {
        mPresenter = presenter;
        mCombatPresenter = combatPresenter;
        mInventoryPresenter = inventoryPresenter;
        mChatPresenter = chatPresenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        // Мобы и игроки
        mPresenter.Mobs
            .SubscribeOnValueChanged(RebuildMobs)
            .DisposeWhenLifeEnded(this);

        mPresenter.Players
            .SubscribeOnValueChanged(RebuildPlayers)
            .DisposeWhenLifeEnded(this);

        // Кнопка «Назад» → закрыть охоту
        if (mButtonBack != null)
            mButtonBack.SubscribeOnClick(() => mPresenter.CloseHunting())
                .DisposeWhenLifeEnded(this);

        // Кнопка «Инвентарь» → открыть Panel_Inventory поверх охоты
        if (mButtonInventory != null)
            mButtonInventory.SubscribeOnClick(() => mInventoryPresenter.Open())
                .DisposeWhenLifeEnded(this);

        // Кнопка «Чат» → открыть Panel_Chat поверх охоты
        if (mButtonChat != null)
            mButtonChat.SubscribeOnClick(() => mChatPresenter.Open())
                .DisposeWhenLifeEnded(this);

        // «Написать» из контекстного меню игрока → выбрать адресата лички и открыть чат.
        // «Атаковать» → запросить PvP-бой (с подтверждением внутри CombatPresenter).
        // Плейн C#-событие (ContextMenuPopup — не Zenject-объект) — отписка в OnDispose.
        if (mContextMenuPopup != null)
        {
            mContextMenuPopup.MessageClicked += OnPlayerMessageClicked;
            mContextMenuPopup.AttackClicked += OnPlayerAttackClicked;
        }
    }

    protected override void OnDispose()
    {
        if (mContextMenuPopup != null)
        {
            mContextMenuPopup.MessageClicked -= OnPlayerMessageClicked;
            mContextMenuPopup.AttackClicked -= OnPlayerAttackClicked;
        }

        base.OnDispose();
    }

    // ─── Мобы ─────────────────────────────────────────────────────────────────

    private void RebuildMobs(List<MobSpawnDto> mobs)
    {
        foreach (var view in mMobViews)
            Destroy(view.gameObject);
        mMobViews.Clear();

        if (mobs == null) return;

        foreach (var mob in mobs)
        {
            if (mob.State == "dead") continue;

            var view = Instantiate(mMobViewPrefab, mMobsArea);
            view.Setup(mob, mMobsArea, mHuntingConfig, OnMobAttackClicked);
            mMobViews.Add(view);
        }
    }

    // ─── Игроки ───────────────────────────────────────────────────────────────

    private void RebuildPlayers(List<PlayerInLocationDto> players)
    {
        foreach (var item in mPlayerItems)
            Destroy(item.gameObject);
        mPlayerItems.Clear();

        if (players == null) return;

        foreach (var player in players)
        {
            var item = Instantiate(mPlayerListItemPrefab, mPlayersContainer);
            item.Setup(player, OnPlayerInfoClicked);
            mPlayerItems.Add(item);
        }
    }

    // ─── Обработчики ──────────────────────────────────────────────────────────

    private void OnMobAttackClicked(long spawnId)
    {
        mCombatPresenter.EngageMobAsync(spawnId, destroyCancellationToken).Forget();
    }

    private void OnPlayerInfoClicked(PlayerInLocationDto player, Vector2 screenPosition)
    {
        mContextMenuPopup.Show(player, screenPosition);
    }

    private void OnPlayerMessageClicked(PlayerInLocationDto player)
    {
        mChatPresenter.SetPrivateTarget(player.CharacterId, player.Nickname);
        mChatPresenter.Open();
    }

    private void OnPlayerAttackClicked(PlayerInLocationDto player)
    {
        mCombatPresenter.RequestAttackPlayer(player.CharacterId, player.Nickname);
    }
}
