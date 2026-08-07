using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// Экран кузнеца — Panel_Blacksmith (Game-сцена, поверх Location, под Combat по Sort Order).
///
/// Пока показывает только ремонт: список надетых повреждённых вещей с ценой у каждой,
/// «Починить всё» с общей суммой, ремонт поштучно. Купля-продажа станут вкладками этого же
/// экрана позже.
///
/// View только отображает и зовёт команды Presenter. Логика — в BlacksmithPresenter.
/// Иерархию собирает Editor/BlacksmithSetup.cs.
///
/// GameObject: Panel_Blacksmith
/// </summary>
public sealed class View_Blacksmith : DisposableBehaviour
{
    [Header("Шапка")]
    [SerializeField] private Button mButtonClose;
    [SerializeField] private TMP_Text mLabelGold;        // «Золото: 1240»
    [SerializeField] private GameObject mSpinner;

    [Header("Список ремонта")]
    [SerializeField] private Transform mRowsContainer;
    [SerializeField] private RepairRowView mRowPrefab;

    /// <summary>«Всё целое — чинить нечего». Показывается вместо пустого списка.</summary>
    [SerializeField] private GameObject mEmptyHint;

    /// <summary>«N вещей изношены до предела — ремонт их уничтожит». Только если такие есть.</summary>
    [SerializeField] private GameObject mWornOutHint;
    [SerializeField] private TMP_Text mLabelWornOut;

    [Header("Починить всё")]
    [SerializeField] private Button mButtonRepairAll;
    [SerializeField] private TMP_Text mLabelRepairAll;   // «Починить всё — 340 з»

    // ─── Инъекции ─────────────────────────────────────────────────────────────

    private BlacksmithPresenter mPresenter;

    private IViewPool<RepairRowView> mRowPool;
    private readonly List<RepairRowView> mRows = new();

    [Inject]
    public void Construct(BlacksmithPresenter presenter)
    {
        mPresenter = presenter;
    }

    // ─── DisposableBehaviour ──────────────────────────────────────────────────

    protected override void SafeAwake()
    {
        mRowPool = new ViewPool<RepairRowView>(mRowPrefab, mRowsContainer);

        BindReactive();
        BindButtons();

        gameObject.SetActive(mPresenter.IsOpen.Value);
    }

    private void BindReactive()
    {
        mPresenter.IsOpen
            .SubscribeOnValueChanged(open => gameObject.SetActive(open))
            .DisposeWhenLifeEnded(this);

        mPresenter.IsLoading
            .SubscribeOnValueChanged(loading => SetActive(mSpinner, loading))
            .DisposeWhenLifeEnded(this);

        mPresenter.Items
            .SubscribeOnValueChanged(_ => RebuildRows())
            .DisposeWhenLifeEnded(this);

        mPresenter.Gold
            .SubscribeOnValueChanged(gold =>
            {
                if (mLabelGold != null) mLabelGold.text = $"Золото: {gold}";
            })
            .DisposeWhenLifeEnded(this);

        mPresenter.TotalCost
            .SubscribeOnValueChanged(_ => UpdateRepairAllButton())
            .DisposeWhenLifeEnded(this);

        mPresenter.CanAffordAll
            .SubscribeOnValueChanged(_ => UpdateRepairAllButton())
            .DisposeWhenLifeEnded(this);

        mPresenter.SkippedWornOut
            .SubscribeOnValueChanged(UpdateWornOutHint)
            .DisposeWhenLifeEnded(this);
    }

    private void BindButtons()
    {
        if (mButtonClose != null)
            mButtonClose.SubscribeOnClick(mPresenter.Close).DisposeWhenLifeEnded(this);

        if (mButtonRepairAll != null)
            mButtonRepairAll.SubscribeOnClick(mPresenter.RequestRepairAll).DisposeWhenLifeEnded(this);
    }

    // ─── Список ───────────────────────────────────────────────────────────────

    private void RebuildRows()
    {
        // Полная пересборка через пул — тот же приём, что в чате: список короткий (надетых вещей
        // максимум десяток), а инкрементальное обновление тут дороже в поддержке, чем в работе.
        mRowPool.ReturnAll();
        mRows.Clear();

        var items = mPresenter.Items.Value;
        bool hasItems = items != null && items.Count > 0;

        SetActive(mEmptyHint, !hasItems);

        if (hasItems)
        {
            long gold = mPresenter.Gold.Value;
            foreach (var item in items)
            {
                var row = mRowPool.Get();
                // Кнопка поштучного ремонта гаснет, если не хватает золота именно на эту вещь.
                row.Setup(item, gold >= item.Cost, mPresenter.RequestRepairOne);
                mRows.Add(row);
            }
        }

        UpdateRepairAllButton();
    }

    private void UpdateRepairAllButton()
    {
        var items = mPresenter.Items.Value;
        bool hasItems = items != null && items.Count > 0;

        if (mLabelRepairAll != null)
            mLabelRepairAll.text = hasItems
                ? $"Починить всё — {mPresenter.TotalCost.Value} з"
                : "Чинить нечего";

        if (mButtonRepairAll != null)
            mButtonRepairAll.interactable = hasItems && mPresenter.CanAffordAll.Value;
    }

    private void UpdateWornOutHint(int skipped)
    {
        SetActive(mWornOutHint, skipped > 0);

        if (mLabelWornOut != null && skipped > 0)
            mLabelWornOut.text =
                $"Изношено до предела: {skipped}. Такие вещи ремонт уничтожит — они пропущены.";
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
