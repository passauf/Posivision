using UnityEngine;

/// <summary>
/// Uygulama yalnızca yatay (landscape). Dar ortamda kamera çekimi için.
/// SaMD Class B UI kısıtı; hasta verisi içermez.
/// </summary>
public static class AppOrientation
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
    }
}
