using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <inheritdoc cref="IViewPool{TView}" />
public sealed class ViewPool<TView> : IViewPool<TView> where TView : Component
{
    private readonly TView mPrefab;
    private readonly Transform mParent;
    private readonly Stack<TView> mFree = new();
    private readonly List<TView> mActive = new();

    public ViewPool(TView prefab, Transform parent)
    {
        mPrefab = prefab;
        mParent = parent;
    }

    public TView Get()
    {
        var view = mFree.Count > 0 ? mFree.Pop() : Object.Instantiate(mPrefab, mParent);
        view.transform.SetAsLastSibling(); // сохраняем хронологический порядок отображения
        view.gameObject.SetActive(true);
        mActive.Add(view);
        return view;
    }

    public void Return(TView view)
    {
        if (view == null) return;
        if (!mActive.Remove(view)) return;

        view.gameObject.SetActive(false);
        mFree.Push(view);
    }

    public void ReturnAll()
    {
        // Копия — Return мутирует mActive во время обхода.
        var toReturn = new List<TView>(mActive);
        foreach (var view in toReturn)
            Return(view);
    }
}
