using UnityEngine;
using TMPro;

/// <summary>
/// Klinik UI renk ve tipografi sabitleri (magic number yasağı).
/// Koyu arduvaz + teal primary + mercan CTA — rehabilitasyon paleti.
/// SaMD Class B: Warning = form uyarısı; Danger = stop/ağrı/sil.
/// </summary>
public static class UiTheme
{
    // Hex → 0–1 (dark :root)
    public static readonly Color Background = Hex(0x090D16);           // --bg-app
    public static readonly Color Panel = Hex(0x0F172A, 0.96f);         // --bg-surface
    public static readonly Color Card = Hex(0x1E293B);                 // --bg-surface-elevated
    public static readonly Color Border = Hex(0x1E293B);               // --border-subtle

    public static readonly Color Primary = Hex(0x14B8A6);              // --color-primary-600 (dark)
    public static readonly Color PrimaryStrong = Hex(0x0D9488);        // --color-primary-600 light / dim
    public static readonly Color PrimaryBright = Hex(0x2DD4BF);        // --color-primary-700 dark

    /// <summary>Geriye uyum: seçili / aktif vurgu = Primary (teal).</summary>
    public static readonly Color Accent = Primary;
    public static readonly Color AccentDim = PrimaryStrong;

    public static readonly Color Secondary = Hex(0x0284C7);            // --color-secondary-600
    public static readonly Color SecondaryBright = Hex(0x0EA5E9);      // --color-secondary-500

    public static readonly Color Cta = Hex(0xFB923C);                  // --color-accent-500 dark (mercan)
    public static readonly Color CtaStrong = Hex(0xF97316);            // --color-accent-500 light

    public static readonly Color Success = Hex(0x10B981);              // doğru form / tamam
    public static readonly Color Warning = Hex(0xF59E0B);              // form bozulması (kırmızı değil)
    public static readonly Color Danger = Hex(0xE11D48);               // bitir / ağrı / sil / hata

    public static readonly Color TextPrimary = Hex(0xF8FAFC);          // --text-main
    public static readonly Color TextMuted = Hex(0x94A3B8);             // --text-muted

    public static readonly Color ButtonNormal = Hex(0x1E293B);
    public static readonly Color ButtonHighlight = Hex(0x334155);
    public static readonly Color ButtonPressed = Hex(0x0F172A);

    public static readonly Color GraphBg = Hex(0x0F172A);
    public static readonly Color GraphGrid = Hex(0x334155);
    public static readonly Color SeriesMax = Primary;
    public static readonly Color SeriesAvg = SecondaryBright;
    public static readonly Color SeriesRight = Primary;
    public static readonly Color SeriesLeft = Secondary;
    public static readonly Color SeriesStrain = Warning;

    /// <summary>Yazı imleci (caret) — koyu zeminde net görünsün.</summary>
    public static readonly Color InputCaret = Hex(0xFBBF24);
    public static readonly Color InputSelection = new Color(Primary.r, Primary.g, Primary.b, 0.40f);
    public const float InputCaretWidth = 2.5f;
    public const float InputCaretBlinkRate = 0.85f;

    public const float TitleFontSize = 36f;
    public const float SubtitleFontSize = 18f;
    public const float BodyFontSize = 16f;
    public const float CardValueFontSize = 28f;
    public const float CardLabelFontSize = 13f;
    public const float ButtonFontSize = 18f;

    public const string ExerciseSceneName = "Pose Landmark Detection";
    public const string MenuSceneName = "MenuScene";

    private static Color Hex(int rgb, float a = 1f)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f;
        float g = ((rgb >> 8) & 0xFF) / 255f;
        float b = (rgb & 0xFF) / 255f;
        return new Color(r, g, b, a);
    }

    /// <summary>
    /// CTA / Danger / Primary buton etiketinde koyu metin; diğerlerinde TextPrimary.
    /// </summary>
    public static Color ContrastOn(Color fill)
    {
        if (IsSameRgb(fill, Cta) || IsSameRgb(fill, CtaStrong)
            || IsSameRgb(fill, Danger) || IsSameRgb(fill, Accent) || IsSameRgb(fill, Primary)
            || IsSameRgb(fill, Success) || IsSameRgb(fill, Warning))
            return Background;
        return TextPrimary;
    }

    private static bool IsSameRgb(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.02f
            && Mathf.Abs(a.g - b.g) < 0.02f
            && Mathf.Abs(a.b - b.b) < 0.02f;
    }

    /// <summary>
    /// Yazı alanında net, yanıp sönen imleç. TMP dahili caret koyu zeminde kaybolduğu için
    /// VisibleInputCaret çubuğu eklenir.
    /// </summary>
    public static void ApplyVisibleCaret(TMP_InputField field)
    {
        if (field == null) return;

        field.customCaretColor = true;
        field.caretColor = new Color(0f, 0f, 0f, 0f);
        field.selectionColor = InputSelection;
        field.caretWidth = 0;
        field.caretBlinkRate = 0f;
        field.shouldHideMobileInput = true;

        VisibleInputCaret overlay = field.GetComponent<VisibleInputCaret>();
        if (overlay == null)
            overlay = field.gameObject.AddComponent<VisibleInputCaret>();
        overlay.Bind(field);
    }
}
