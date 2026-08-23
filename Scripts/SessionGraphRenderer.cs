using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Seans ROM zaman serisini RawImage üzerine çizer.
/// Sol/alt kenarda derece ve saniye etiketleri (piksel font) gösterilir.
/// </summary>
public class SessionGraphRenderer : MonoBehaviour
{
    private const int PadLeft = 36;
    private const int PadRight = 10;
    private const int PadTop = 10;
    private const int PadBottom = 28;

    [Header("Grafik UI")]
    [SerializeField] private RawImage graphImage;
    [SerializeField] private int textureWidth = 1280;
    [SerializeField] private int textureHeight = 420;
    [Tooltip("Çizgi kalınlığı (px). Detay panelinde daha yumuşak görünüm.")]
    [SerializeField] private int lineThickness = 2;

    [Header("Renkler")]
    [SerializeField] private Color backgroundColor = new Color(0.070f, 0.110f, 0.160f, 1f);
    [SerializeField] private Color gridColor = new Color(0.200f, 0.280f, 0.380f, 1f);
    [SerializeField] private Color rightArmColor = new Color(0.350f, 0.700f, 0.980f, 1f);
    [SerializeField] private Color leftArmColor = new Color(0.239f, 0.863f, 0.592f, 1f);
    [Tooltip("Yardımlı ölçüm aralığında ilgili kol çizgisi (ilk algı → yardım bitene).")]
    [SerializeField] private Color assistArmColor = new Color(0.910f, 0.180f, 0.180f, 1f);
    [SerializeField] private Color targetLineColor = new Color(1f, 0.55f, 0.2f, 0.85f);
    [SerializeField] private Color compensationMarkColor = new Color(0.75f, 0.22f, 0.17f, 1f);
    [SerializeField] private Color strainColor = new Color(0.557f, 0.267f, 0.678f, 1f);
    [SerializeField] private Color labelColor = new Color(0.75f, 0.82f, 0.90f, 1f);

    private Texture2D _texture;
    private Color32[] _pixels;

    public bool ShowRight = true;
    public bool ShowLeft = true;
    public bool ShowStrain = true;

    // 3x5 bitmap font — her satır 3 bit (bit2=sol, bit0=sağ). Digits 0-9
    private static readonly int[] DigitGlyphs =
    {
        0x7, 0x5, 0x5, 0x5, 0x7, // 0
        0x2, 0x6, 0x2, 0x2, 0x7, // 1
        0x7, 0x1, 0x7, 0x4, 0x7, // 2
        0x7, 0x1, 0x3, 0x1, 0x7, // 3
        0x5, 0x5, 0x7, 0x1, 0x1, // 4
        0x7, 0x4, 0x7, 0x1, 0x7, // 5
        0x7, 0x4, 0x7, 0x5, 0x7, // 6
        0x7, 0x1, 0x2, 0x2, 0x2, // 7
        0x7, 0x5, 0x7, 0x5, 0x7, // 8
        0x7, 0x5, 0x7, 0x1, 0x7, // 9
    };

    public void SetGraphImage(RawImage image)
    {
        graphImage = image;
        if (_texture != null && graphImage != null)
            graphImage.texture = _texture;
    }

    /// <summary>Seans detay paneli: yüksek çözünürlük + bilinear + kalın çizgi.</summary>
    public void ConfigureDetailQuality()
    {
        textureWidth = 1280;
        textureHeight = 420;
        lineThickness = 3;
        RecreateTexture();
    }

    private void Awake()
    {
        ApplyThemeColors();
        EnsureTexture();
    }

    /// <summary>Klinik paleti — Inspector eski değerlerini runtime’da override eder.</summary>
    private void ApplyThemeColors()
    {
        backgroundColor = UiTheme.GraphBg;
        gridColor = UiTheme.GraphGrid;
        rightArmColor = UiTheme.SeriesRight;
        leftArmColor = UiTheme.SeriesLeft;
        assistArmColor = UiTheme.Warning;
        targetLineColor = new Color(UiTheme.Cta.r, UiTheme.Cta.g, UiTheme.Cta.b, 0.85f);
        compensationMarkColor = UiTheme.Danger;
        strainColor = UiTheme.SeriesStrain;
        labelColor = UiTheme.TextMuted;
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
        RecreateTexture();
    }

    private void RecreateTexture()
    {
        if (_texture != null)
        {
            Destroy(_texture);
            _texture = null;
        }

        _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        _texture.filterMode = FilterMode.Bilinear;
        _texture.wrapMode = TextureWrapMode.Clamp;
        _pixels = new Color32[textureWidth * textureHeight];

        if (graphImage != null)
            graphImage.texture = _texture;
    }

    public void Draw(
        float[] sampleTimes,
        float[] rightAngles,
        float[] leftAngles,
        int sampleCount,
        float[] compensationTimes,
        int compensationCount,
        float targetAngle,
        float maxAngleScale)
    {
        Draw(sampleTimes, rightAngles, leftAngles, null, sampleCount,
            compensationTimes, compensationCount, targetAngle, maxAngleScale, null, null);
    }

    public void Draw(
        float[] sampleTimes,
        float[] rightAngles,
        float[] leftAngles,
        float[] strainSamples,
        int sampleCount,
        float[] compensationTimes,
        int compensationCount,
        float targetAngle,
        float maxAngleScale)
    {
        Draw(sampleTimes, rightAngles, leftAngles, strainSamples, sampleCount,
            compensationTimes, compensationCount, targetAngle, maxAngleScale, null, null);
    }

    public void Draw(
        float[] sampleTimes,
        float[] rightAngles,
        float[] leftAngles,
        float[] strainSamples,
        int sampleCount,
        float[] compensationTimes,
        int compensationCount,
        float targetAngle,
        float maxAngleScale,
        bool[] assistRight,
        bool[] assistLeft)
    {
        Draw(sampleTimes, rightAngles, leftAngles, strainSamples, sampleCount,
            compensationTimes, compensationCount, targetAngle, maxAngleScale,
            assistRight, assistLeft, 0f, -1f);
    }

    /// <param name="viewEnd">
    /// &lt; 0 ise tüm seri; aksi halde [viewStart, viewEnd] penceresi (canlı seans son N dk).
    /// </param>
    public void Draw(
        float[] sampleTimes,
        float[] rightAngles,
        float[] leftAngles,
        float[] strainSamples,
        int sampleCount,
        float[] compensationTimes,
        int compensationCount,
        float targetAngle,
        float maxAngleScale,
        bool[] assistRight,
        bool[] assistLeft,
        float viewStart,
        float viewEnd)
    {
        EnsureTexture();
        Clear(backgroundColor);

        if (sampleCount < 2)
        {
            DrawAxisLabels(0f, Mathf.Max(maxAngleScale, 180f));
            Apply();
            return;
        }

        float tLast = sampleTimes[sampleCount - 1];
        float t0 = Mathf.Max(0f, viewStart);
        float t1 = viewEnd < 0f ? tLast : Mathf.Min(tLast, viewEnd);
        if (t1 <= t0) t1 = tLast;
        float tMax = Mathf.Max(t1 - t0, 0.001f);
        float yMax = Mathf.Max(maxAngleScale, targetAngle, 1f);

        DrawPlotAreaBorder();
        DrawGrid(tMax, yMax);

        int targetY = AngleToY(targetAngle, yMax);
        DrawHorizontalLineClamped(targetY, targetLineColor);

        int i0 = LowerBoundTime(sampleTimes, sampleCount, t0);
        if (i0 > 0) i0--;
        int i1 = LowerBoundTime(sampleTimes, sampleCount, t1);
        if (i1 < sampleCount - 1) i1++;
        i0 = Mathf.Clamp(i0, 0, sampleCount - 1);
        i1 = Mathf.Clamp(i1, i0 + 1, sampleCount - 1);

        // Texture genişliğine göre stride — daha sık örnek = daha düzgün eğri
        int span = Mathf.Max(1, i1 - i0);
        int plotW = Mathf.Max(1, textureWidth - PadLeft - PadRight);
        int stride = Mathf.Max(1, span / (plotW * 4));

        if (ShowRight)
            DrawSeriesWindow(sampleTimes, rightAngles, i0, i1, t0, tMax, yMax, rightArmColor, assistRight, stride);
        if (ShowLeft)
            DrawSeriesWindow(sampleTimes, leftAngles, i0, i1, t0, tMax, yMax, leftArmColor, assistLeft, stride);

        if (ShowStrain && strainSamples != null)
            DrawStrainSeriesWindow(sampleTimes, strainSamples, i0, i1, t0, tMax, stride);

        for (int i = 0; i < compensationCount; i++)
        {
            float t = compensationTimes[i];
            if (t < t0 || t > t0 + tMax) continue;
            int x = TimeToX(t - t0, tMax);
            float ang = SampleAngleAtTime(sampleTimes, rightAngles, leftAngles, sampleCount, t);
            int y = AngleToY(ang, yMax);
            PlotDot(x, y, compensationMarkColor, 4);
        }

        DrawAxisLabels(tMax, yMax, t0);
        Apply();
    }

    private static int LowerBoundTime(float[] times, int count, float t)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            int m = (lo + hi) >> 1;
            if (times[m] < t) lo = m + 1;
            else hi = m;
        }
        return lo;
    }

    private float SampleAngleAtTime(float[] times, float[] right, float[] left, int count, float t)
    {
        int best = 0;
        float bestDt = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            float dt = Mathf.Abs(times[i] - t);
            if (dt < bestDt)
            {
                bestDt = dt;
                best = i;
            }
        }
        float r = right != null ? right[best] : 0f;
        float l = left != null ? left[best] : 0f;
        if (float.IsNaN(r)) r = 0f;
        if (float.IsNaN(l)) l = 0f;
        if (ShowRight && ShowLeft) return (r + l) * 0.5f;
        if (ShowLeft) return l;
        return r;
    }

    private void DrawStrainSeries(float[] times, float[] strain, int count, float tMax)
    {
        DrawStrainSeriesWindow(times, strain, 0, count - 1, 0f, tMax, 1);
    }

    private void DrawStrainSeriesWindow(
        float[] times, float[] strain, int i0, int i1, float t0, float tMax, int stride)
    {
        for (int i = i0 + stride; i <= i1; i += stride)
        {
            int prev = i - stride;
            if (float.IsNaN(strain[prev]) || float.IsNaN(strain[i])) continue;
            int x0 = TimeToX(times[prev] - t0, tMax);
            int y0 = NormToY(strain[prev]);
            int x1 = TimeToX(times[i] - t0, tMax);
            int y1 = NormToY(strain[i]);
            DrawLine(x0, y0, x1, y1, strainColor);
        }
    }

    private int NormToY(float norm01)
    {
        float plotH = textureHeight - PadTop - PadBottom - 1;
        return PadBottom + Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(norm01) * plotH), 0, (int)plotH);
    }

    private void PlotDot(int x, int y, Color color, int radius)
    {
        Color32 c = color;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                SetPixel(x + dx, y + dy, c);
            }
        }
    }

    private void DrawPlotAreaBorder()
    {
        Color32 c = gridColor;
        // Sol ve alt eksen
        for (int y = PadBottom; y < textureHeight - PadTop; y++)
            SetPixel(PadLeft, y, c);
        for (int x = PadLeft; x < textureWidth - PadRight; x++)
            SetPixel(x, PadBottom, c);
    }

    private void DrawGrid(float tMax, float yMax)
    {
        for (int deg = 0; deg <= 180; deg += 45)
        {
            if (deg > yMax) break;
            DrawHorizontalLineClamped(AngleToY(deg, yMax), gridColor);
        }

        // X: 5 eşit zaman dilimi
        for (int i = 1; i < 5; i++)
        {
            float t = tMax * (i / 5f);
            DrawVerticalLineClamped(TimeToX(t, tMax), gridColor);
        }
    }

    private void DrawAxisLabels(float tMax, float yMax)
    {
        DrawAxisLabels(tMax, yMax, 0f);
    }

    private void DrawAxisLabels(float tMax, float yMax, float tOffset)
    {
        Color32 c = labelColor;

        for (int deg = 0; deg <= 180; deg += 45)
        {
            if (deg > yMax) break;
            int y = AngleToY(deg, yMax);
            DrawNumber(2, y - 3, deg, c, scale: 1);
        }

        int tickCount = 5;
        bool useMinSec = (tOffset + tMax) >= 120f;
        for (int i = 0; i <= tickCount; i++)
        {
            float tRel = tMax * (i / (float)tickCount);
            float tAbs = tOffset + tRel;
            int x = TimeToX(tRel, tMax);
            if (useMinSec)
            {
                int total = Mathf.Max(0, Mathf.RoundToInt(tAbs));
                int m = total / 60;
                int s = total % 60;
                // m:ss — iki alan
                int textW = (m >= 10 ? 2 : 1) * 4 + 2 + 2 * 4;
                int cursor = x - textW / 2;
                DrawNumber(cursor, 4, m, c, scale: 1);
                cursor += (m >= 10 ? 2 : 1) * 4;
                SetPixel(cursor, 5, c);
                SetPixel(cursor, 7, c);
                cursor += 2;
                DrawNumber(cursor, 4, s < 10 ? 0 : s / 10, c, scale: 1);
                DrawNumber(cursor + 4, 4, s % 10, c, scale: 1);
            }
            else
            {
                int seconds = Mathf.RoundToInt(tAbs);
                int digitCount = seconds >= 100 ? 3 : (seconds >= 10 ? 2 : 1);
                int textW = digitCount * 4;
                DrawNumber(x - textW / 2, 4, seconds, c, scale: 1);
            }
        }

        DrawCharS(textureWidth - PadRight - 14, 4, c);
        DrawCharN(textureWidth - PadRight - 8, 4, c);
    }

    private void DrawSeries(float[] times, float[] angles, int count, float tMax, float yMax, Color color)
    {
        DrawSeries(times, angles, count, tMax, yMax, color, null);
    }

    private void DrawSeries(
        float[] times, float[] angles, int count, float tMax, float yMax, Color color, bool[] assistFlags)
    {
        DrawSeriesWindow(times, angles, 0, count - 1, 0f, tMax, yMax, color, assistFlags, 1);
    }

    private void DrawSeriesWindow(
        float[] times, float[] angles, int i0, int i1, float t0, float tMax, float yMax,
        Color color, bool[] assistFlags, int stride)
    {
        for (int i = i0 + stride; i <= i1; i += stride)
        {
            int prev = i - stride;
            if (float.IsNaN(angles[prev]) || float.IsNaN(angles[i])) continue;
            int x0 = TimeToX(times[prev] - t0, tMax);
            int y0 = AngleToY(angles[prev], yMax);
            int x1 = TimeToX(times[i] - t0, tMax);
            int y1 = AngleToY(angles[i], yMax);
            bool assisted = assistFlags != null
                && i < assistFlags.Length
                && (assistFlags[i] || assistFlags[prev]);
            DrawLine(x0, y0, x1, y1, assisted ? assistArmColor : color);
        }
    }

    private int TimeToX(float t, float tMax)
    {
        float plotW = textureWidth - PadLeft - PadRight - 1;
        return PadLeft + Mathf.Clamp(Mathf.RoundToInt((t / tMax) * plotW), 0, (int)plotW);
    }

    private int AngleToY(float angle, float yMax)
    {
        float plotH = textureHeight - PadTop - PadBottom - 1;
        return PadBottom + Mathf.Clamp(Mathf.RoundToInt((angle / yMax) * plotH), 0, (int)plotH);
    }

    private void DrawHorizontalLineClamped(int y, Color color)
    {
        y = Mathf.Clamp(y, PadBottom, textureHeight - PadTop - 1);
        Color32 c = color;
        for (int x = PadLeft; x < textureWidth - PadRight; x++)
            SetPixel(x, y, c);
    }

    private void DrawVerticalLineClamped(int x, Color color)
    {
        x = Mathf.Clamp(x, PadLeft, textureWidth - PadRight - 1);
        Color32 c = color;
        for (int y = PadBottom; y < textureHeight - PadTop; y++)
            SetPixel(x, y, c);
    }

    private void DrawNumber(int x, int y, int value, Color32 color, int scale)
    {
        if (value < 0) value = 0;
        // Basamakları sağdan sola
        int v = value;
        int digits = value == 0 ? 1 : 0;
        int tmp = value;
        while (tmp > 0) { digits++; tmp /= 10; }

        int cursor = x + (digits - 1) * (4 * scale);
        if (v == 0)
        {
            DrawDigit(cursor, y, 0, color, scale);
            return;
        }
        while (v > 0)
        {
            DrawDigit(cursor, y, v % 10, color, scale);
            v /= 10;
            cursor -= 4 * scale;
        }
    }

    private void DrawDigit(int x, int y, int digit, Color32 color, int scale)
    {
        digit = Mathf.Clamp(digit, 0, 9);
        int baseIdx = digit * 5;
        for (int row = 0; row < 5; row++)
        {
            int bits = DigitGlyphs[baseIdx + row];
            for (int col = 0; col < 3; col++)
            {
                if ((bits & (1 << (2 - col))) == 0) continue;
                for (int sy = 0; sy < scale; sy++)
                for (int sx = 0; sx < scale; sx++)
                    SetPixel(x + col * scale + sx, y + (4 - row) * scale + sy, color);
            }
        }
    }

    // Basit 's' ve 'n' (3x5)
    private void DrawCharS(int x, int y, Color32 color)
    {
        int[] rows = { 0x0F, 0x10, 0x0E, 0x01, 0x1E };
        DrawGlyphRows(x, y, rows, color);
    }

    private void DrawCharN(int x, int y, Color32 color)
    {
        int[] rows = { 0x11, 0x19, 0x15, 0x13, 0x11 };
        DrawGlyphRows(x, y, rows, color);
    }

    private void DrawGlyphRows(int x, int y, int[] rows, Color32 color)
    {
        for (int row = 0; row < 5; row++)
        {
            int bits = rows[row];
            for (int col = 0; col < 5; col++)
            {
                if ((bits & (1 << (4 - col))) == 0) continue;
                SetPixel(x + col, y + (4 - row), color);
            }
        }
    }

    private void Clear(Color color)
    {
        Color32 c = color;
        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = c;
    }

    private void SetPixel(int x, int y, Color32 c)
    {
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;
        _pixels[y * textureWidth + x] = c;
    }

    private void BlendPixel(int x, int y, Color32 c, float alpha)
    {
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;
        if (alpha <= 0.02f) return;
        if (alpha >= 0.98f)
        {
            _pixels[y * textureWidth + x] = c;
            return;
        }

        int i = y * textureWidth + x;
        Color32 d = _pixels[i];
        float a = Mathf.Clamp01(alpha);
        float ia = 1f - a;
        _pixels[i] = new Color32(
            (byte)(c.r * a + d.r * ia),
            (byte)(c.g * a + d.g * ia),
            (byte)(c.b * a + d.b * ia),
            255);
    }

    private void DrawLine(int x0, int y0, int x1, int y1, Color color)
    {
        Color32 c = color;
        int thickness = Mathf.Max(1, lineThickness);
        float half = thickness * 0.5f;

        float dx = x1 - x0;
        float dy = y1 - y0;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
        {
            PlotSoftDot(x0, y0, c, half + 0.5f);
            return;
        }

        // Adım ≈ 0.5 px — pürüzsüz eğri
        int steps = Mathf.Max(1, Mathf.CeilToInt(len * 2f));
        float inv = 1f / steps;
        float nx = -dy / len;
        float ny = dx / len;

        for (int s = 0; s <= steps; s++)
        {
            float t = s * inv;
            float px = x0 + dx * t;
            float py = y0 + dy * t;
            // Kalınlık + hafif AA halkası
            for (float r = -half - 0.75f; r <= half + 0.75f; r += 0.5f)
            {
                float dist = Mathf.Abs(r) - half;
                float a = dist <= 0f ? 1f : Mathf.Clamp01(1f - dist);
                if (a <= 0.05f) continue;
                int ix = Mathf.RoundToInt(px + nx * r);
                int iy = Mathf.RoundToInt(py + ny * r);
                BlendPixel(ix, iy, c, a);
            }
        }
    }

    private void PlotSoftDot(int x, int y, Color32 c, float radius)
    {
        int r = Mathf.CeilToInt(radius + 1f);
        float r2 = radius * radius;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                float d2 = dx * dx + dy * dy;
                if (d2 > (radius + 1f) * (radius + 1f)) continue;
                float a = d2 <= r2 ? 1f : Mathf.Clamp01(1f - (Mathf.Sqrt(d2) - radius));
                BlendPixel(x + dx, y + dy, c, a);
            }
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
