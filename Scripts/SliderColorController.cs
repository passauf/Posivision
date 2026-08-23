using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class SliderColorController : MonoBehaviour
{
    [Header("Görsel Ayarlar")]
    [Tooltip("Renk geçişlerini ton ton buradan ayarlayabilirsin.")]
    public Gradient sliderGradient;

    private Image _fillImage;
    private Slider _slider;
    private float _lastNormalized = float.NaN;
    private const float ColorDirtyEpsilon = 0.01f;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
        if (_slider != null && _slider.fillRect != null)
        {
            _fillImage = _slider.fillRect.GetComponent<Image>();
        }
    }

    // Single Responsibility: Sadece rengi günceller
    public void UpdateColor(float normalizedValue)
    {
        if (_fillImage == null) return;

        // cmd: her kare Gradient.Evaluate + material dirty pahalı — küçük farkı atla
        if (!float.IsNaN(_lastNormalized)
            && Mathf.Abs(normalizedValue - _lastNormalized) < ColorDirtyEpsilon)
            return;

        _lastNormalized = normalizedValue;
        _fillImage.color = sliderGradient.Evaluate(normalizedValue);
    }
}