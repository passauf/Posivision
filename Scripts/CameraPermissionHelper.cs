using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Android runtime kamera izni. Editor / masaüstünde her zaman granted.
/// KVKK: izin diyalogu kimlik loglamaz.
/// </summary>
public static class CameraPermissionHelper
{
    private const float WaitTimeoutSeconds = 20f;

    public static IEnumerator EnsureGranted(Action<bool> onDone)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            onDone?.Invoke(true);
            yield break;
        }

        Permission.RequestUserPermission(Permission.Camera);
        float elapsed = 0f;
        while (elapsed < WaitTimeoutSeconds)
        {
            if (Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                onDone?.Invoke(true);
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        onDone?.Invoke(Permission.HasUserAuthorizedPermission(Permission.Camera));
#else
        onDone?.Invoke(true);
        yield break;
#endif
    }

    public static bool HasAnyCameraDevice()
    {
        try
        {
            var devices = WebCamTexture.devices;
            return devices != null && devices.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
