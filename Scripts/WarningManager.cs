using UnityEngine;
using TMPro;
using System.Collections;

public class WarningManager : MonoBehaviour
{
    [Header("UI Referansları")]
    public GameObject warningObject;
    public TextMeshProUGUI warningText;

    [Header("Ayarlar")]
    [SerializeField] private float warningDisplaySeconds = 3f;

    private Coroutine _activeCoroutine;

    private void Awake()
    {
        if (warningObject == null)
        {
            Debug.LogError("WarningObject atanmamış. Inspector'dan bağlayın.");
            return;
        }

        if (warningObject == gameObject)
        {
            Debug.LogWarning("warningObject bu scriptin GameObject'i — kapatmak bileşeni de kapatır.");
        }

        warningObject.SetActive(false);
        if (warningText != null) warningText.text = "";
    }

    public void TriggerWarning(string message)
    {
        if (warningObject == null || !gameObject.activeInHierarchy) return;

        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(WaitAndHideRoutine(message));
    }

    private IEnumerator WaitAndHideRoutine(string message)
    {
        if (warningText != null) warningText.text = message;
        warningObject.SetActive(true);

        yield return new WaitForSeconds(warningDisplaySeconds);

        warningObject.SetActive(false);
        _activeCoroutine = null;
    }
}
