using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Hedef açı / tekrar ayarları. Seans başlatmaz — KVKK rıza akışını atlamamak için.
/// Seans ExerciseHud üzerinden (kalibrasyon → profil → BeginSession) başlar.
/// </summary>
public class ExerciseSettingsManager : MonoBehaviour
{
    [Header("Referanslar")]
    public PhysioAnalyzer analyzer;

    [Header("Input Alanları")]
    public TMP_InputField angleInputField;
    public TMP_InputField repInputField;

    [Header("UI Geri Bildirim")]
    public TextMeshProUGUI statusText;

    public void ApplyNewSettings()
    {
        if (analyzer == null) return;

        if (angleInputField != null && float.TryParse(angleInputField.text, out float newAngle))
            analyzer.targetAngleDegrees = newAngle;

        if (repInputField != null && int.TryParse(repInputField.text, out int newRepGoal))
            analyzer.SetTargetReps(newRepGoal);

        // cmd: BeginSession burada YOK — rıza/profil panelini atlamasın
        if (statusText != null)
        {
            statusText.text = "Ayarlar kaydedildi. Seansı HUD'dan başlatın.";
            Invoke(nameof(ClearStatus), 2.5f);
        }
    }

    private void ClearStatus()
    {
        if (statusText != null) statusText.text = "";
    }
}
