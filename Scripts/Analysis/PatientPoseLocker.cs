using UnityEngine;

/// <summary>
/// Katman 1 — Person ID Locking: 2 pose varken hastayı centroid sürekliliği ile sabitler.
/// MediaPipe indeks sırası değişse bile klinik kişi kaybolmaz. SaMD Class B; teşhis değildir.
/// Zero-allocation; callback thread'de Unity API yok.
/// </summary>
public sealed class PatientPoseLocker
{
    private Vector2 _patientCentroid;
    private bool _hasLock;
    private float _centroidBlend = 0.35f;

    public bool HasLock => _hasLock;
    public Vector2 LockedPatientCentroid => _patientCentroid;

    public void Reset()
    {
        _hasLock = false;
        _patientCentroid = default;
    }

    public void Configure(float centroidBlend)
    {
        _centroidBlend = Mathf.Clamp(centroidBlend, 0.05f, 1f);
    }

    /// <summary>
    /// patientIndex / helperIndex: poseLandmarks dizisi indeksleri. helperIndex=-1 → tek kişi.
    /// </summary>
    public void Resolve(
        int poseCount,
        Vector2 centroid0,
        float shoulderWidth0,
        bool valid0,
        Vector2 centroid1,
        float shoulderWidth1,
        bool valid1,
        out int patientIndex,
        out int helperIndex)
    {
        patientIndex = 0;
        helperIndex = -1;

        if (poseCount < 1)
            return;

        if (poseCount == 1 || !valid1)
        {
            if (valid0)
            {
                patientIndex = 0;
                UpdateLock(centroid0);
            }
            else if (valid1)
            {
                patientIndex = 1;
                UpdateLock(centroid1);
            }
            return;
        }

        if (!valid0 && valid1)
        {
            patientIndex = 1;
            helperIndex = -1;
            UpdateLock(centroid1);
            return;
        }

        if (valid0 && !valid1)
        {
            patientIndex = 0;
            UpdateLock(centroid0);
            return;
        }

        // İki geçerli pose
        if (!_hasLock)
        {
            // İlk kilit: merkeze yakın + daha büyük omuz (genelde hasta)
            float score0 = CenterScore(centroid0) + 0.4f * ScaleScore(shoulderWidth0);
            float score1 = CenterScore(centroid1) + 0.4f * ScaleScore(shoulderWidth1);
            patientIndex = score0 >= score1 ? 0 : 1;
            helperIndex = 1 - patientIndex;
            _patientCentroid = patientIndex == 0 ? centroid0 : centroid1;
            _hasLock = true;
            return;
        }

        float d0 = DistSq(centroid0, _patientCentroid);
        float d1 = DistSq(centroid1, _patientCentroid);
        patientIndex = d0 <= d1 ? 0 : 1;
        helperIndex = 1 - patientIndex;
        UpdateLock(patientIndex == 0 ? centroid0 : centroid1);
    }

    private void UpdateLock(Vector2 centroid)
    {
        if (!_hasLock)
        {
            _patientCentroid = centroid;
            _hasLock = true;
            return;
        }

        _patientCentroid = Vector2.Lerp(_patientCentroid, centroid, _centroidBlend);
    }

    private static float CenterScore(Vector2 c)
    {
        float dx = c.x - 0.5f;
        float dy = c.y - 0.5f;
        return 1f / (1f + dx * dx + dy * dy);
    }

    private static float ScaleScore(float shoulderWidth)
    {
        // Tipik 0.08–0.35; büyüdükçe skor artar (kameraya yakın / büyük silüet)
        return Mathf.Clamp01(shoulderWidth / 0.30f);
    }

    private static float DistSq(Vector2 a, Vector2 b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return dx * dx + dy * dy;
    }

    /// <summary>Gövde centroid + omuz genişliği (görüntü 0–1). Confidence düşükse valid=false.</summary>
    public static bool TryMeasurePose(
        Mediapipe.Tasks.Components.Containers.NormalizedLandmarks landmarks,
        float visibilityThreshold,
        out Vector2 centroid,
        out float shoulderWidth)
    {
        centroid = default;
        shoulderWidth = 0f;
        if (landmarks.landmarks == null || landmarks.landmarks.Count <= 24)
            return false;

        var ls = landmarks.landmarks[11];
        var rs = landmarks.landmarks[12];
        var lh = landmarks.landmarks[23];
        var rh = landmarks.landmarks[24];

        if (!IsConfident(ls, visibilityThreshold)
            || !IsConfident(rs, visibilityThreshold)
            || !IsConfident(lh, visibilityThreshold)
            || !IsConfident(rh, visibilityThreshold))
            return false;

        float midSx = (ls.x + rs.x) * 0.5f;
        float midSy = (ls.y + rs.y) * 0.5f;
        float midHx = (lh.x + rh.x) * 0.5f;
        float midHy = (lh.y + rh.y) * 0.5f;
        centroid = new Vector2((midSx + midHx) * 0.5f, (midSy + midHy) * 0.5f);

        float dx = ls.x - rs.x;
        float dy = ls.y - rs.y;
        shoulderWidth = Mathf.Sqrt(dx * dx + dy * dy);
        return shoulderWidth > 1e-4f;
    }

    private static bool IsConfident(
        Mediapipe.Tasks.Components.Containers.NormalizedLandmark lm,
        float threshold)
    {
        float v = lm.visibility.HasValue ? lm.visibility.Value : 1f;
        float p = lm.presence.HasValue ? lm.presence.Value : 1f;
        return v >= threshold && p >= threshold;
    }
}
