using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Basit onay diyaloğu (KVKK silme vb.). Runtime üretilir.
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    public static ConfirmDialog Show(Transform canvasRoot, string title, string body,
        string confirmLabel, string cancelLabel, Action<bool> onComplete)
    {
        if (canvasRoot == null)
        {
            onComplete?.Invoke(false);
            return null;
        }

        var existing = canvasRoot.GetComponentInChildren<ConfirmDialog>(true);
        if (existing != null) Destroy(existing.gameObject);

        GameObject go = new GameObject("ConfirmDialog", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ConfirmDialog));
        go.transform.SetParent(canvasRoot, false);
        var dialog = go.GetComponent<ConfirmDialog>();
        dialog.Build(title, body, confirmLabel, cancelLabel, onComplete);
        return dialog;
    }

    private void Build(string title, string body, string confirmLabel, string cancelLabel, Action<bool> onComplete)
    {
        var rt = GetComponent<RectTransform>();
        Stretch(rt);
        GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
        GetComponent<Image>().raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        UiSafeLayout.BindLandscapeCard(rt, cardRt, UiSafeLayout.LandscapeDialogWidth, UiSafeLayout.LandscapeDialogHeight);
        card.GetComponent<Image>().color = UiTheme.Panel;

        CreateLabel(card.transform, "Title", title, 20f, FontStyles.Bold,
            new Vector2(0f, -24f), new Vector2(420f, 32f));

        CreateLabel(card.transform, "Body", body, 15f, FontStyles.Normal,
            new Vector2(0f, -70f), new Vector2(420f, 110f)).color = UiTheme.TextMuted;

        CreateButton(card.transform, "Confirm", confirmLabel, UiTheme.Danger,
            new Vector2(-100f, 28f), new Vector2(180f, 44f), () =>
            {
                onComplete?.Invoke(true);
                Destroy(gameObject);
            });

        CreateButton(card.transform, "Cancel", cancelLabel, UiTheme.ButtonNormal,
            new Vector2(100f, 28f), new Vector2(180f, 44f), () =>
            {
                onComplete?.Invoke(false);
                Destroy(gameObject);
            });
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

    private static void CreateButton(Transform parent, string name, string label, Color color,
        Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        GameObject lbl = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl.GetComponent<RectTransform>());
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.ContrastOn(color);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
