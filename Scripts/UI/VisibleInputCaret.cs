using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// TMP dahili imleç koyu UI'da kaybolduğu için Text üzerinde yanıp sönen çubuk çizer.
/// Konum doğrudan TMP karakter lokal koordinatından alınır (viewport dönüşümü yok).
/// </summary>
[DisallowMultipleComponent]
public class VisibleInputCaret : MonoBehaviour
{
    private const float CaretWidthPx = 3f;
    private const float BlinkHz = 1.1f;
    private const float MinHeightPx = 14f;
    private const float EmptyPadX = 4f;
    private const float EmptyPadY = -2f;

    private TMP_InputField _field;
    private RectTransform _caretRt;
    private Image _caretImg;
    private float _blinkClock;

    public void Bind(TMP_InputField field)
    {
        _field = field;
        EnsureCaretGraphic();
    }

    private void EnsureCaretGraphic()
    {
        if (_field == null || _field.textComponent == null) return;

        RectTransform textRt = _field.textComponent.rectTransform;
        if (_caretRt != null)
        {
            if (_caretRt.parent != textRt)
                _caretRt.SetParent(textRt, false);
            return;
        }

        GameObject go = new GameObject("VisibleCaret", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(textRt, false);
        go.transform.SetAsLastSibling();

        _caretRt = go.GetComponent<RectTransform>();
        _caretRt.anchorMin = new Vector2(0.5f, 0.5f);
        _caretRt.anchorMax = new Vector2(0.5f, 0.5f);
        _caretRt.pivot = new Vector2(0f, 1f);
        _caretRt.sizeDelta = new Vector2(CaretWidthPx, MinHeightPx);
        _caretRt.localScale = Vector3.one;
        _caretRt.localRotation = Quaternion.identity;

        _caretImg = go.GetComponent<Image>();
        _caretImg.color = UiTheme.InputCaret;
        _caretImg.raycastTarget = false;
        _caretImg.enabled = false;
    }

    private void LateUpdate()
    {
        if (_field == null) return;
        EnsureCaretGraphic();
        if (_caretRt == null || _caretImg == null) return;

        bool focused = _field.isFocused;
        if (!focused && EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == _field.gameObject)
            focused = true;

        if (!focused || !isActiveAndEnabled)
        {
            _caretImg.enabled = false;
            return;
        }

        float height;
        Vector3 localPos;
        if (!TryGetCaretInTextLocal(out localPos, out height))
        {
            Rect r = _field.textComponent.rectTransform.rect;
            localPos = new Vector3(r.xMin + EmptyPadX, r.yMax + EmptyPadY, 0f);
            height = Mathf.Max(MinHeightPx, _field.textComponent.fontSize * 1.15f);
        }

        _caretRt.localPosition = localPos;
        _caretRt.sizeDelta = new Vector2(CaretWidthPx, height);

        _blinkClock += Time.unscaledDeltaTime * BlinkHz;
        bool on = (_blinkClock % 1f) < 0.55f;
        _caretImg.enabled = on;
        if (on)
            _caretImg.color = UiTheme.InputCaret;
    }

    private bool TryGetCaretInTextLocal(out Vector3 localPos, out float height)
    {
        localPos = Vector3.zero;
        height = MinHeightPx;

        TMP_Text text = _field.textComponent;
        if (text == null) return false;

        text.ForceMeshUpdate(ignoreActiveState: false);
        TMP_TextInfo info = text.textInfo;
        if (info == null || info.characterCount <= 0) return false;

        string content = _field.text ?? "";
        int caret = Mathf.Clamp(_field.stringPosition, 0, content.Length);
        height = Mathf.Max(MinHeightPx, text.fontSize * 1.15f);

        // Boş / yalnız görünmez karakter
        if (content.Length == 0)
        {
            Rect r = text.rectTransform.rect;
            localPos = new Vector3(r.xMin + EmptyPadX, r.yMax + EmptyPadY, 0f);
            return true;
        }

        // characterInfo[i] ≈ string index i (BMP). Sonda TMP bazen \0 ekler.
        int maxIdx = info.characterCount - 1;
        while (maxIdx > 0 && info.characterInfo[maxIdx].character == 0)
            maxIdx--;

        TMP_CharacterInfo ch;
        if (caret <= 0)
        {
            ch = info.characterInfo[0];
            localPos = new Vector3(ch.origin, ch.ascender, 0f);
        }
        else if (caret >= content.Length)
        {
            // Metin sonu → son gerçek karakterin xAdvance
            int i = Mathf.Min(content.Length - 1, maxIdx);
            i = Mathf.Clamp(i, 0, maxIdx);
            ch = info.characterInfo[i];
            localPos = new Vector3(ch.xAdvance, ch.ascender, 0f);
        }
        else
        {
            // Ortada → bu indeksteki karakterin sol kenarı (origin)
            int i = Mathf.Clamp(caret, 0, maxIdx);
            ch = info.characterInfo[i];
            localPos = new Vector3(ch.origin, ch.ascender, 0f);
        }

        height = Mathf.Max(MinHeightPx, Mathf.Abs(ch.ascender - ch.descender));
        return true;
    }
}
