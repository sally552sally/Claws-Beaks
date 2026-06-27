using UnityEngine;

/// <summary>
/// Подгоняет RectTransform под SafeArea устройства.
/// Вешается на объект-обёртку внутри Canvas — не на сам Canvas.
/// Без этого UI залезает под нотч на iPhone и закругления экрана.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaAdapter : MonoBehaviour
{
    private RectTransform mRectTransform;

    private void Awake()
    {
        mRectTransform = GetComponent<RectTransform>();
        Apply();
    }

    private void Apply()
    {
        var safeArea = Screen.safeArea;
        var screenSize = new Vector2(Screen.width, Screen.height);

        var anchorMin = safeArea.position;
        var anchorMax = safeArea.position + safeArea.size;

        anchorMin.x /= screenSize.x;
        anchorMin.y /= screenSize.y;
        anchorMax.x /= screenSize.x;
        anchorMax.y /= screenSize.y;

        mRectTransform.anchorMin = anchorMin;
        mRectTransform.anchorMax = anchorMax;
    }
}
