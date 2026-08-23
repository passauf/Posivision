using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Kamera / AI init hataları için tam ekran uyarı + yeniden dene.
/// Hasta kimliği göstermez (KVKK).
/// </summary>
public class RuntimeErrorOverlay : MonoBehaviour
{
    public static RuntimeErrorOverlay Show(string title, string body, string retryLabel, Action onRetry)
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var canvasGo = new GameObject("ErrorOverlayCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            UiSafeLayout.ApplyScaler(canvas);
        }

        var existing = canvas.GetComponentInChildren<RuntimeErrorOverlay>(true);
        if (existing != null) Destroy(existing.gameObject);

        GameObject go = new GameObject("RuntimeErrorOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RuntimeErrorOverlay));
        go.transform.SetParent(canvas.transform, false);
        var overlay = go.GetComponent<RuntimeErrorOverlay>();
        overlay.Build(title, body, retryLabel, onRetry);
        return overlay;
    }

    private void Build(string title, string body, string retryLabel, Action onRetry)
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        GetComponent<Image>().color = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.92f);
        GetComponent<Image>().raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindLandscapeCard(rt, cardRt, UiSafeLayout.LandscapeDialogWidth, UiSafeLayout.LandscapeDialogHeight);
        card.GetComponent<Image>().color = UiTheme.Panel;

        CreateLabel(card.transform, "Title", title, 22f, FontStyles.Bold, new Vector2(0f, -28f), new Vector2(440f, 36f))
            .color = UiTheme.Danger;

        CreateLabel(card.transform, "Body", body, 15f, FontStyles.Normal, new Vector2(0f, -80f), new Vector2(440f, 120f))
            .color = UiTheme.TextMuted;

        GameObject btnGo = new GameObject("Retry", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(card.transform, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0f);
        btnRt.anchorMax = new Vector2(0.5f, 0f);
        btnRt.pivot = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = new Vector2(0f, 28f);
        btnRt.sizeDelta = new Vector2(220f, 48f);
        btnGo.GetComponent<Image>().color = UiTheme.Cta;
        btnGo.GetComponent<Button>().onClick.AddListener(() =>
        {
            Destroy(gameObject);
            onRetry?.Invoke();
        });

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(btnGo.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = retryLabel;
        tmp.fontSize = 16f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.ContrastOn(UiTheme.Cta);
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
