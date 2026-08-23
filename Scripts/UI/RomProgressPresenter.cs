using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans canlı ROM: mevcut açı, hedef ve kalan (hedefe yakınlık).
/// View katmanı — klinik mantık PhysioAnalyzer’da. Zero-allocation hot path.
/// SaMD Class B motivasyonel geri bildirim; teşhis değildir.
/// </summary>
public sealed class RomProgressPresenter
{
    private GameObject _root;
    private Image _card;
    private Image _fill;
    private TextMeshProUGUI _currentText;
    private TextMeshProUGUI _detailText;
    private TextMeshProUGUI _armText;

    private int _lastCurrent = int.MinValue;
    private int _lastTarget = int.MinValue;
    private int _lastRemain = int.MinValue;
    private int _lastProgressPct = int.MinValue;
    private int _lastArmKey = int.MinValue;
    private float _lastFillWidth = -1f;
    private bool _visible;

    private const float CardWidth = 320f;
    private const float FillMaxWidth = 280f;

    public bool IsBuilt => _root != null;

    public void Ensure(Transform canvasRoot)
    {
        if (_root != null || canvasRoot == null) return;

        Transform existing = canvasRoot.Find("RomProgressHud");
        if (existing != null)
        {
            _root = existing.gameObject;
            CacheRefs(existing);
            return;
        }

        _root = new GameObject("RomProgressHud", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.transform.SetParent(canvasRoot, false);
        var rt = _root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -72f);
        rt.sizeDelta = new Vector2(CardWidth, 92f);

        _card = _root.GetComponent<Image>();
        _card.color = new Color(UiTheme.Card.r, UiTheme.Card.g, UiTheme.Card.b, 0.88f);
        _card.raycastTarget = false;

        _armText = CreateLabel(_root.transform, "Arm", new Vector2(0f, -8f), new Vector2(CardWidth - 24f, 20f), 13f, FontStyles.Bold);
        _currentText = CreateLabel(_root.transform, "Current", new Vector2(0f, -30f), new Vector2(CardWidth - 24f, 36f), 28f, FontStyles.Bold);
        _detailText = CreateLabel(_root.transform, "Detail", new Vector2(0f, -58f), new Vector2(CardWidth - 24f, 18f), 14f, FontStyles.Normal);

        GameObject trackGo = new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trackGo.transform.SetParent(_root.transform, false);
        var trackRt = trackGo.GetComponent<RectTransform>();
        trackRt.anchorMin = new Vector2(0.5f, 0f);
        trackRt.anchorMax = new Vector2(0.5f, 0f);
        trackRt.pivot = new Vector2(0.5f, 0f);
        trackRt.anchoredPosition = new Vector2(0f, 10f);
        trackRt.sizeDelta = new Vector2(FillMaxWidth, 8f);
        var trackImg = trackGo.GetComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.18f);
        trackImg.raycastTarget = false;

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(trackGo.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = new Vector2(0f, 0f);
        _fill = fillGo.GetComponent<Image>();
        _fill.color = UiTheme.Success;
        _fill.raycastTarget = false;

        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
        if (!visible)
        {
            _lastCurrent = int.MinValue;
            _lastTarget = int.MinValue;
            _lastRemain = int.MinValue;
            _lastProgressPct = int.MinValue;
            _lastArmKey = int.MinValue;
            _lastFillWidth = -1f;
        }
    }

    /// <summary>Seans açıkken ölçülen kolun canlı açısı + hedefe kalan.</summary>
    public void Tick(PhysioAnalyzer analyzer)
    {
        if (_root == null || analyzer == null) return;
        if (!analyzer.IsSessionRunning)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        bool showRight = analyzer.IsMeasuringRightArm;
        bool showLeft = analyzer.IsMeasuringLeftArm;
        bool useRight;
        if (showRight && showLeft)
        {
            // İkisi birden: daha yüksek açıyı vurgula (hasta “ne kadar kaldırdı” görsün)
            useRight = analyzer.CurrentRightAngleDegrees >= analyzer.CurrentLeftAngleDegrees;
        }
        else
            useRight = showRight;

        if (!showRight && !showLeft)
        {
            SetVisible(false);
            return;
        }

        float current = useRight ? analyzer.CurrentRightAngleDegrees : analyzer.CurrentLeftAngleDegrees;
        float target = Mathf.Max(1f, analyzer.targetAngleDegrees);
        int curInt = Mathf.Clamp(Mathf.RoundToInt(current), 0, 180);
        int tgtInt = Mathf.Clamp(Mathf.RoundToInt(target), 1, 180);
        int remainInt = Mathf.Max(0, tgtInt - curInt);
        int progressPct = Mathf.Clamp(Mathf.RoundToInt(100f * Mathf.Clamp01(current / target)), 0, 100);
        int armKey = useRight ? 1 : 2;

        if (armKey != _lastArmKey && _armText != null)
        {
            _lastArmKey = armKey;
            _armText.text = useRight ? Loc.T("hud.rom.arm.right") : Loc.T("hud.rom.arm.left");
        }

        if (curInt != _lastCurrent && _currentText != null)
        {
            _lastCurrent = curInt;
            _currentText.text = curInt.ToString() + "°";
            float t = Mathf.Clamp01(curInt / (float)tgtInt);
            _currentText.color = Color.Lerp(UiTheme.Warning, UiTheme.Success, t);
        }

        if ((tgtInt != _lastTarget || remainInt != _lastRemain || progressPct != _lastProgressPct)
            && _detailText != null)
        {
            _lastTarget = tgtInt;
            _lastRemain = remainInt;
            _lastProgressPct = progressPct;
            _detailText.text = Loc.Format("hud.rom.detail", tgtInt, remainInt, progressPct);
        }

        if (_fill != null)
        {
            float width = FillMaxWidth * (progressPct * 0.01f);
            if (Mathf.Abs(width - _lastFillWidth) > 0.5f)
            {
                _lastFillWidth = width;
                _fill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                float t = progressPct * 0.01f;
                if (t < 0.45f) _fill.color = UiTheme.Warning;
                else if (t < 0.85f) _fill.color = UiTheme.Warning;
                else _fill.color = UiTheme.Success;
            }
        }
    }

    public void RefreshLanguage()
    {
        _lastArmKey = int.MinValue;
        _lastTarget = int.MinValue;
        _lastRemain = int.MinValue;
        _lastProgressPct = int.MinValue;
        _lastCurrent = int.MinValue;
    }

    private void CacheRefs(Transform root)
    {
        _card = root.GetComponent<Image>();
        Transform arm = root.Find("Arm");
        Transform cur = root.Find("Current");
        Transform detail = root.Find("Detail");
        Transform track = root.Find("Track");
        _armText = arm != null ? arm.GetComponent<TextMeshProUGUI>() : null;
        _currentText = cur != null ? cur.GetComponent<TextMeshProUGUI>() : null;
        _detailText = detail != null ? detail.GetComponent<TextMeshProUGUI>() : null;
        if (track != null)
        {
            Transform fill = track.Find("Fill");
            _fill = fill != null ? fill.GetComponent<Image>() : null;
        }
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent, string name, Vector2 anchoredPos, Vector2 size, float fontSize, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UiTheme.TextPrimary;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        tmp.text = "";
        return tmp;
    }
}
