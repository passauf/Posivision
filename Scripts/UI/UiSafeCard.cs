using UnityEngine;

/// <summary>
/// Overlay kökünde çözünürlük değişince kartı yeniden sığdırır.
/// Update kullanmaz (GC). SaMD Class B UI.
/// </summary>
public class UiSafeCard : MonoBehaviour
{
    private enum Mode
    {
        Centered,
        Tall
    }

    private RectTransform _card;
    private Mode _mode;
    private float _maxWidth;
    private float _maxHeight;
    private float _margin;
    private float _topNorm;
    private float _bottomNorm;
    private bool _applying;

    public static void AttachCentered(
        RectTransform overlayRoot, RectTransform cardRt, float maxWidth, float maxHeight, float margin)
    {
        if (overlayRoot == null || cardRt == null) return;
        UiSafeCard hook = overlayRoot.GetComponent<UiSafeCard>();
        if (hook == null) hook = overlayRoot.gameObject.AddComponent<UiSafeCard>();
        hook._card = cardRt;
        hook._mode = Mode.Centered;
        hook._maxWidth = maxWidth;
        hook._maxHeight = maxHeight;
        hook._margin = margin;
        hook.Apply();
    }

    public static void AttachTall(
        RectTransform overlayRoot, RectTransform cardRt, float maxWidth,
        float topNorm, float bottomNorm, float marginX)
    {
        if (overlayRoot == null || cardRt == null) return;
        UiSafeCard hook = overlayRoot.GetComponent<UiSafeCard>();
        if (hook == null) hook = overlayRoot.gameObject.AddComponent<UiSafeCard>();
        hook._card = cardRt;
        hook._mode = Mode.Tall;
        hook._maxWidth = maxWidth;
        hook._margin = marginX;
        hook._topNorm = topNorm;
        hook._bottomNorm = bottomNorm;
        hook.Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled || _applying) return;
        Apply();
    }

    private void Apply()
    {
        if (_card == null) return;
        _applying = true;
        if (_mode == Mode.Tall)
            UiSafeLayout.FitTallCard(_card, _maxWidth, _topNorm, _bottomNorm, _margin);
        else
            UiSafeLayout.FitCenteredCard(_card, _maxWidth, _maxHeight, _margin);
        _applying = false;
    }
}
