using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Çözünürlük / en-boy oranı: Canvas Scaler + overlay kart boyut tavanı.
/// Referans 1920×1080 landscape. SaMD Class B UI; hasta verisi yok.
/// </summary>
public static class UiSafeLayout
{
    public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    public const float MatchWidthOrHeight = 0.5f;
    public const float DefaultMargin = 32f;
    public const float MinCardWidth = 280f;
    public const float MinCardHeight = 200f;
    /// <summary>Uygulama kilitli landscape — overlay kartları dikey telefon oranı kullanmaz.</summary>
    public const float LandscapeOverlayWidth = 1560f;
    public const float LandscapeOverlayHeight = 880f;
    public const float LandscapeDialogWidth = 900f;
    public const float LandscapeDialogHeight = 420f;

    /// <summary>
    /// Kök canvas'a Scale With Screen Size uygular. İç içe overlay canvas'a dokunmaz
    /// (üst canvas ölçeğini miras alır).
    /// </summary>
    public static void ApplyScaler(Canvas canvas)
    {
        if (canvas == null) return;
        if (canvas.transform.parent != null)
        {
            Canvas parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas != canvas)
                return;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = MatchWidthOrHeight;
        scaler.referencePixelsPerUnit = 100f;
    }

    public static Vector2 ParentSize(RectTransform rt)
    {
        if (rt == null) return ReferenceResolution;

        RectTransform parent = rt.parent as RectTransform;
        if (parent != null)
        {
            Rect r = parent.rect;
            if (r.width > 8f && r.height > 8f)
                return new Vector2(r.width, r.height);
        }

        Canvas c = rt.GetComponentInParent<Canvas>();
        if (c != null)
        {
            Rect pr = c.pixelRect;
            float s = c.scaleFactor > 0.01f ? c.scaleFactor : 1f;
            return new Vector2(pr.width / s, pr.height / s);
        }

        return new Vector2(Screen.width, Screen.height);
    }

    public static void FitCenteredCard(RectTransform cardRt, float maxWidth, float maxHeight, float margin = DefaultMargin)
    {
        if (cardRt == null) return;
        Vector2 parent = ParentSize(cardRt);
        float w = Mathf.Min(maxWidth, Mathf.Max(MinCardWidth, parent.x - margin * 2f));
        float h = Mathf.Min(maxHeight, Mathf.Max(MinCardHeight, parent.y - margin * 2f));
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = new Vector2(w, h);
    }

    public static void FitTallCard(
        RectTransform cardRt, float maxWidth, float topNorm, float bottomNorm, float marginX = DefaultMargin)
    {
        if (cardRt == null) return;
        Vector2 parent = ParentSize(cardRt);
        float w = Mathf.Min(maxWidth, Mathf.Max(MinCardWidth, parent.x - marginX * 2f));
        cardRt.anchorMin = new Vector2(0.5f, Mathf.Clamp01(bottomNorm));
        cardRt.anchorMax = new Vector2(0.5f, Mathf.Clamp01(topNorm));
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = new Vector2(w, 0f);
    }

    public static void BindCenteredCard(
        RectTransform overlayRoot, RectTransform cardRt, float maxWidth, float maxHeight, float margin = DefaultMargin)
    {
        UiSafeCard.AttachCentered(overlayRoot, cardRt, maxWidth, maxHeight, margin);
    }

    public static void BindTallCard(
        RectTransform overlayRoot, RectTransform cardRt, float maxWidth, float topNorm, float bottomNorm,
        float marginX = DefaultMargin)
    {
        UiSafeCard.AttachTall(overlayRoot, cardRt, maxWidth, topNorm, bottomNorm, marginX);
    }

    public static void BindLandscapeCard(
        RectTransform overlayRoot, RectTransform cardRt,
        float maxWidth = LandscapeOverlayWidth, float maxHeight = LandscapeOverlayHeight,
        float margin = DefaultMargin)
    {
        UiSafeCard.AttachCentered(overlayRoot, cardRt, maxWidth, maxHeight, margin);
    }
}
