/// <summary>
/// MediaPipe pose örnekleri — callback thread → ana thread kuyruk taşıması.
/// Heap allocation yok (struct). SaMD Class B veri taşıyıcısı; teşhis değildir.
/// </summary>
public static class PoseLandmarkIndices
{
    public const int Count = 33;
    public const int Nose = 0;
    public const int LeftShoulder = 11;
    public const int RightShoulder = 12;
    public const int LeftElbow = 13;
    public const int RightElbow = 14;
    public const int LeftWrist = 15;
    public const int RightWrist = 16;
    public const int LeftIndex = 19;
    public const int RightIndex = 20;
    public const int LeftHip = 23;
    public const int RightHip = 24;
}

public struct LandmarkPoint
{
    public float x;
    public float y;
    public float visibility;
    public float presence;
    public bool hasVisibility;
    public bool hasPresence;
}

public struct PoseLandmarkSample
{
    public float timestampSeconds;
    public LandmarkPoint leftShoulder;
    public LandmarkPoint rightShoulder;
    public LandmarkPoint leftElbow;
    public LandmarkPoint rightElbow;
    public LandmarkPoint leftWrist;
    public LandmarkPoint rightWrist;
    public LandmarkPoint leftHip;
    public LandmarkPoint rightHip;
    public LandmarkPoint nose;
    /// <summary>MediaPipe bu karede kaç pose üretti (NumPoses üst sınırı).</summary>
    public int detectedPoseCount;
    /// <summary>2. kişi (yardımcı) kol/el/gövde noktaları kopyalandı mı.</summary>
    public bool hasHelperPose;
    public LandmarkPoint helperLeftShoulder;
    public LandmarkPoint helperRightShoulder;
    public LandmarkPoint helperLeftElbow;
    public LandmarkPoint helperRightElbow;
    public LandmarkPoint helperLeftWrist;
    public LandmarkPoint helperRightWrist;
    public LandmarkPoint helperLeftIndex;
    public LandmarkPoint helperRightIndex;
    public LandmarkPoint helperLeftHip;
    public LandmarkPoint helperRightHip;
}
