/// <summary>
/// KVKK aydınlatma metinleri — dil Loc üzerinden.
/// Metin değişince PatientProfile.ConsentTextVersion artırılmalı.
/// </summary>
public static class PrivacyNotice
{
    public static string ShortHint => Loc.T("privacy.short");
    public static string ConsentLabel => Loc.T("privacy.consent");
    public static string DeleteConfirmTitle => Loc.T("privacy.delete.title");
    public static string DeleteConfirmBody => Loc.T("privacy.delete.body");
    public static string CameraDeniedTitle => Loc.T("cam.denied.title");
    public static string CameraDeniedBody => Loc.T("cam.denied.body");
    public static string CameraMissingTitle => Loc.T("cam.missing.title");
    public static string CameraMissingBody => Loc.T("cam.missing.body");
    public static string CameraStartFailedTitle => Loc.T("cam.fail.title");
    public static string CameraStartFailedBody => Loc.T("cam.fail.body");
    public static string ModelFailedTitle => Loc.T("model.fail.title");
    public static string ModelFailedBody => Loc.T("model.fail.body");
}
