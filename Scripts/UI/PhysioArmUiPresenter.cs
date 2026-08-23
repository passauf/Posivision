using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Kol HUD görünürlüğü (slider/TMP/kart). Referanslar host public alanlarında kalır.
/// View katmanı — klinik mantık yok. Zero-allocation hot path.
/// </summary>
public sealed class PhysioArmUiPresenter
{
    public void ApplyArmUiVisibility(
        bool sessionLive,
        bool measureLeftArm,
        bool measureRightArm,
        Slider leftSlider,
        Slider rightSlider,
        TextMeshProUGUI leftAngleText,
        TextMeshProUGUI rightAngleText,
        TextMeshProUGUI leftRepText,
        TextMeshProUGUI rightRepText,
        SliderColorController leftColorCtrl,
        SliderColorController rightColorCtrl)
    {
        // Slider / üst açı kartları kaldırıldı — ROM radyal yay + yay tepesi etiketi.
        HideSliderStack(leftSlider, leftAngleText, leftColorCtrl);
        HideSliderStack(rightSlider, rightAngleText, rightColorCtrl);
        SetHudCardActive(leftAngleText, "LeftHudCard", false);
        SetHudCardActive(rightAngleText, "RightHudCard", false);
        if (leftAngleText != null) leftAngleText.gameObject.SetActive(false);
        if (rightAngleText != null) rightAngleText.gameObject.SetActive(false);
        if (leftRepText != null) leftRepText.gameObject.SetActive(sessionLive && measureLeftArm);
        if (rightRepText != null) rightRepText.gameObject.SetActive(sessionLive && measureRightArm);
    }

    public static void HideSliderStack(Slider slider, TextMeshProUGUI angleText, SliderColorController color)
    {
        if (slider != null) slider.gameObject.SetActive(false);
        if (color != null) color.gameObject.SetActive(false);
        if (angleText != null) angleText.gameObject.SetActive(false);
    }

    public static void SetArmUiActive(
        Slider slider,
        TextMeshProUGUI angleText,
        TextMeshProUGUI repText,
        SliderColorController color,
        bool active)
    {
        if (slider != null) slider.gameObject.SetActive(false);
        if (angleText != null) angleText.gameObject.SetActive(false);
        if (color != null) color.gameObject.SetActive(false);
        if (repText != null) repText.gameObject.SetActive(active);
    }

    public static void SetHudCardActive(TextMeshProUGUI angleText, string cardName, bool active)
    {
        if (angleText == null || angleText.transform.parent == null) return;
        Transform card = angleText.transform.parent.Find(cardName);
        if (card != null) card.gameObject.SetActive(active);
    }

    /// <summary>
    /// Soft-follow görsel açı + (eski) slider/metin. Slider/üst metin kaldırıldı; no-op UI.
    /// </summary>
    public void UpdateArmVisual(
        ref float visualAngle,
        float rawAngle,
        float lerpSpeed,
        float deltaTime,
        Slider slider,
        SliderColorController color,
        TextMeshProUGUI angleText,
        ref string cachedAngle,
        ref int lastShownAngle)
    {
        visualAngle = Mathf.Lerp(visualAngle, rawAngle, deltaTime * lerpSpeed);
        UpdateSliderAndAngleText(slider, color, angleText, ref lastShownAngle, ref cachedAngle, visualAngle);
    }

    public static void UpdateSliderAndAngleText(
        Slider slider,
        SliderColorController color,
        TextMeshProUGUI aText,
        ref int lastShownAngle,
        ref string cachedAngle,
        float currentV)
    {
        // Slider / üst açı metni kaldırıldı — ROM radyal yay + yay tepesi etiketi.
        _ = slider;
        _ = color;
        _ = aText;
        _ = lastShownAngle;
        _ = cachedAngle;
        _ = currentV;
    }
}
