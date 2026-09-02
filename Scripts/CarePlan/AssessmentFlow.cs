using UnityEngine;

/// <summary>
/// Seans bitişi → anket → analiz kancası.
/// SaMD Class B / KVKK: yerel; klinisyen notları hasta UI'de yok.
/// </summary>
public static class AssessmentFlow
{
    /// <summary>
    /// Normal bitiş: anket UI göster (tanımada zorunlu).
    /// onUiComplete: anket + analiz bittikten sonra (rapor gösterme vb.).
    /// </summary>
    public static void OnSessionFinished(
        DataManager dataManager,
        Transform canvasRoot,
        bool showUi,
        System.Action onUiComplete)
    {
        if (dataManager == null)
        {
            onUiComplete?.Invoke();
            return;
        }

        PatientProfile profile = dataManager.LoadProfile();
        PatientHistory history = dataManager.LoadHistoryForPatient(profile);
        PatientCareState state = dataManager.LoadCareState(history, profile);
        SessionEntry last = null;
        if (history != null && history.sessions != null && history.sessions.Count > 0)
            last = history.sessions[history.sessions.Count - 1];

        bool assessmentSurvey = state.phase == CarePhase.Assessment
                                && state.assessmentSessionCount < PatientCareState.AssessmentSessionTarget;

        if (!showUi || canvasRoot == null)
        {
            AssessmentAnalyzer.ProcessEmergencySession(state, history);
            dataManager.SaveCareState(state, profile);
            onUiComplete?.Invoke();
            return;
        }

        // Tanıma: zorunlu anket. Program: kısa check-in (zorunlu — tutarlı adaptasyon için).
        PostSessionSurveyPanel.Show(canvasRoot, state, history, last, survey =>
        {
            AssessmentAnalyzer.ProcessAfterSurvey(state, history, last, survey, assessmentSurvey);
            if (survey != null)
                dataManager.AttachSurveyToLatestSession(profile, survey);
            dataManager.SaveCareState(state, profile);
            onUiComplete?.Invoke();
        });
    }
}
