using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Seanslar arası R/L maks + pik zorlanma çizgi grafiği.
/// Menü ilerleme ve seans detay (HTML seans geçmişi ile aynı yapı).
/// SaMD Class B karar-destek.
/// </summary>
public class ProgressGraphRenderer : MonoBehaviour
{
    private const int PadL = 28;
    private const int PadR = 12;
    private const int PadT = 10;
    private const int PadB = 14;

    [SerializeField] private RawImage graphImage;
    [SerializeField] private int textureWidth = 640;
    [SerializeField] private int textureHeight = 220;
    [SerializeField] private float maxAngleScale = 180f;

    private Texture2D _texture;
    private Color32[] _pixels;

    public bool ShowRightMax = true;
    public bool ShowLeftMax = true;
    public bool ShowAvg = false;
    public bool ShowStrain = true;
    public bool ShowTargetLine = true;

    /// <summary>Vurgulanacak seans indeksi (−1 = yok). Detayda son seans.</summary>
    public int HighlightIndex = -1;

    public void SetGraphImage(RawImage image)
    {
        graphImage = image;
        if (_texture != null && graphImage != null)
            graphImage.texture = _texture;
    }

    private void Awake()
    {
        EnsureTexture();
    }

    private void OnDestroy()
    {
        if (_texture != null)
        {
            Destroy(_texture);
            _texture = null;
        }
    }

    private void EnsureTexture()
    {
        if (_texture != null) return;
        _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        _texture.filterMode = FilterMode.Bilinear;
        _texture.wrapMode = TextureWrapMode.Clamp;
        _pixels = new Color32[textureWidth * textureHeight];
        if (graphImage != null) graphImage.texture = _texture;
    }

    public void Draw(PatientHistory history)
    {
        if (history == null || history.sessions == null)
        {
            Draw(new List<SessionEntry>());
            return;
        }
        Draw(history.sessions);
    }

    public void Draw(List<SessionEntry> sessions)
    {
        EnsureTexture();
        Clear(UiTheme.GraphBg);

        if (sessions == null || sessions.Count < 1)
        {
            Apply();
            return;
        }

        DrawGrid();

        int n = sessions.Count;
        float denom = Mathf.Max(1, n - 1);

        float target = 0f;
        if (ShowTargetLine)
        {
            for (int i = 0; i < n; i++)
            {
                if (sessions[i] != null && sessions[i].targetAngle > 1f)
                    target = sessions[i].targetAngle;
            }
            if (target > 1f)
                DrawHorizontalLine(AngleToY(target), new Color(UiTheme.Cta.r, UiTheme.Cta.g, UiTheme.Cta.b, 0.75f));
        }

        if (n == 1)
        {
            int x = textureWidth / 2;
            SessionEntry s = sessions[0];
            int r = (HighlightIndex == 0 || HighlightIndex < 0) ? 5 : 3;
            if (ShowRightMax) PlotDot(x, AngleToY(SessionHistoryFilter.EffectiveRightMax(s)), UiTheme.SeriesRight, r);
            if (ShowLeftMax) PlotDot(x, AngleToY(SessionHistoryFilter.EffectiveLeftMax(s)), UiTheme.SeriesLeft, r);
            if (ShowAvg) PlotDot(x, AngleToY(AvgOf(s)), UiTheme.SeriesAvg, r);
            if (ShowStrain) PlotDot(x, NormToY(s.peakStrain), UiTheme.SeriesStrain, Mathf.Max(1, r - 1));
            Apply();
            return;
        }

        if (ShowRightMax) DrawAngleSeries(sessions, n, denom, right: true, UiTheme.SeriesRight);
        if (ShowLeftMax) DrawAngleSeries(sessions, n, denom, right: false, UiTheme.SeriesLeft);
        if (ShowAvg)
        {
            for (int i = 1; i < n; i++)
            {
                DrawLine(
                    Mathf.RoundToInt(PadX(i - 1, denom)), AngleToY(AvgOf(sessions[i - 1])),
                    Mathf.RoundToInt(PadX(i, denom)), AngleToY(AvgOf(sessions[i])),
                    UiTheme.SeriesAvg);
            }
            for (int i = 0; i < n; i++)
                PlotDot(Mathf.RoundToInt(PadX(i, denom)), AngleToY(AvgOf(sessions[i])), UiTheme.SeriesAvg, DotR(i));
        }
        if (ShowStrain)
        {
            for (int i = 1; i < n; i++)
            {
                DrawLine(
                    Mathf.RoundToInt(PadX(i - 1, denom)), NormToY(sessions[i - 1].peakStrain),
                    Mathf.RoundToInt(PadX(i, denom)), NormToY(sessions[i].peakStrain),
                    UiTheme.SeriesStrain);
            }
            for (int i = 0; i < n; i++)
                PlotDot(Mathf.RoundToInt(PadX(i, denom)), NormToY(sessions[i].peakStrain), UiTheme.SeriesStrain, DotR(i) - 1);
        }

        Apply();
    }

    private void DrawAngleSeries(List<SessionEntry> sessions, int n, float denom, bool right, Color color)
    {
        for (int i = 1; i < n; i++)
        {
            float v0 = right
                ? SessionHistoryFilter.EffectiveRightMax(sessions[i - 1])
                : SessionHistoryFilter.EffectiveLeftMax(sessions[i - 1]);
            float v1 = right
                ? SessionHistoryFilter.EffectiveRightMax(sessions[i])
                : SessionHistoryFilter.EffectiveLeftMax(sessions[i]);
            DrawLine(
                Mathf.RoundToInt(PadX(i - 1, denom)), AngleToY(v0),
                Mathf.RoundToInt(PadX(i, denom)), AngleToY(v1),
                color);
        }
        for (int i = 0; i < n; i++)
        {
            float v = right
                ? SessionHistoryFilter.EffectiveRightMax(sessions[i])
                : SessionHistoryFilter.EffectiveLeftMax(sessions[i]);
            PlotDot(Mathf.RoundToInt(PadX(i, denom)), AngleToY(v), color, DotR(i));
        }
    }

    private int DotR(int index)
    {
        if (HighlightIndex < 0) return 3;
        return index == HighlightIndex ? 5 : 2;
    }

    private static float AvgOf(SessionEntry s)
    {
        if (s == null) return 0f;
        if (s.averageROM > 1f) return s.averageROM;
        return SessionHistoryFilter.EffectiveMax(s);
    }

    private float PadX(int index, float denom)
    {
        float plotW = textureWidth - PadL - PadR - 1;
        return PadL + plotW * (index / denom);
    }

    private void DrawGrid()
    {
        for (int deg = 0; deg <= 180; deg += 45)
            DrawHorizontalLine(AngleToY(deg), UiTheme.GraphGrid);
        for (int i = 1; i < 4; i++)
        {
            int x = PadL + ((textureWidth - PadL - PadR) * i) / 4;
            DrawVerticalLine(x, UiTheme.GraphGrid);
        }
    }

    private int AngleToY(float angle)
    {
        float plotH = textureHeight - PadT - PadB - 1;
        float n = Mathf.Clamp01(angle / Mathf.Max(1f, maxAngleScale));
        return PadB + Mathf.Clamp(Mathf.RoundToInt(n * plotH), 0, (int)plotH);
    }

    private int NormToY(float norm01)
    {
        float plotH = textureHeight - PadT - PadB - 1;
        float n = Mathf.Clamp01(norm01);
        return PadB + Mathf.Clamp(Mathf.RoundToInt(n * plotH), 0, (int)plotH);
    }

    private void Clear(Color color)
    {
        Color32 c = color;
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = c;
    }

    private void DrawHorizontalLine(int y, Color color)
    {
        y = Mathf.Clamp(y, 0, textureHeight - 1);
        Color32 c = color;
        for (int x = PadL; x < textureWidth - PadR; x++)
            _pixels[y * textureWidth + x] = c;
    }

    private void DrawVerticalLine(int x, Color color)
    {
        x = Mathf.Clamp(x, 0, textureWidth - 1);
        Color32 c = color;
        for (int y = PadB; y < textureHeight - PadT; y++)
            _pixels[y * textureWidth + x] = c;
    }

    private void PlotDot(int x, int y, Color color)
    {
        PlotDot(x, y, color, 3);
    }

    private void PlotDot(int x, int y, Color color, int radius)
    {
        if (radius < 1) radius = 1;
        Color32 c = color;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                int px = x + dx;
                int py = y + dy;
                if (px < 0 || px >= textureWidth || py < 0 || py >= textureHeight) continue;
                _pixels[py * textureWidth + px] = c;
            }
        }
    }

    private void DrawLine(int x0, int y0, int x1, int y1, Color color)
    {
        Color32 c = color;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int x = x0;
        int y = y0;
        while (true)
        {
            if (x >= 0 && x < textureWidth && y >= 0 && y < textureHeight)
                _pixels[y * textureWidth + x] = c;
            if (x == x1 && y == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    private void Apply()
    {
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
        if (graphImage != null && graphImage.texture != _texture)
            graphImage.texture = _texture;
    }
}
