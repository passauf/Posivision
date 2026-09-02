using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Tasks.Components.Containers;

public class ElbowFlexionAnalyzer : MonoBehaviour
{
    public Image fillBar;
    private float _currentAngle;
    private bool _hasData;

    public void ProcessData(NormalizedLandmarks pose)
    {
        var shoulder = pose.landmarks[12];
        var elbow = pose.landmarks[14];
        var wrist = pose.landmarks[16];

        _currentAngle = CalculateAngle(shoulder, elbow, wrist);
        _hasData = true;
    }

    void Update()
    {
        if (_hasData && fillBar != null)
        {
            // Dirsekte büküldükçe barın dolması için (180'den çıkarma)
            fillBar.fillAmount = 1f - Mathf.Clamp01(_currentAngle / 180f);
            _hasData = false;
        }
    }

    private float CalculateAngle(NormalizedLandmark a, NormalizedLandmark b, NormalizedLandmark c)
    {
        Vector3 v1 = new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        Vector3 v2 = new Vector3(c.x - b.x, c.y - b.y, c.z - b.z);
        return Vector3.Angle(v1, v2);
    }
}