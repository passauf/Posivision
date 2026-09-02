/// <summary>
/// Hareket protokolüne göre hangi kapıların/uyarıların aktif olduğu.
/// PhysioAnalyzer hareket adına özel dallanmaz — profil ExerciseCatalog'dan gelir.
/// </summary>
public struct MovementProtocolProfile
{
    public bool enableYawGate;
    public bool enableSideProfileGate;
    public bool foreshortenInvalidatesRep;
    public bool foreshortenWarnFeedback;
    public bool yawAffectsPeakRom;

    public static MovementProtocolProfile ForMovement(MovementId id)
    {
        if (ExerciseCatalog.TryGet(id, out ExerciseDefinition def))
        {
            switch (def.AnalysisFamily)
            {
                case MovementAnalysisFamily.ShoulderElevation:
                    return def.UsesSideProfile
                        ? SideProfileShoulderElevation
                        : FrontalShoulderElevation;
                case MovementAnalysisFamily.ElbowHinge:
                    return ElbowHinge;
                case MovementAnalysisFamily.Neck:
                    return Neck;
                case MovementAnalysisFamily.LowerLimb:
                    return LowerLimb;
            }
        }

        return SharedTorsoOnly;
    }

    /// <summary>Yan profil omuz elevasyonu (fleksiyon).</summary>
    public static readonly MovementProtocolProfile SideProfileShoulderElevation = new MovementProtocolProfile
    {
        enableYawGate = false,
        enableSideProfileGate = true,
        foreshortenInvalidatesRep = false,
        foreshortenWarnFeedback = false,
        yawAffectsPeakRom = false
    };

    /// <summary>Ön kamera omuz elevasyonu (abdüksiyon).</summary>
    public static readonly MovementProtocolProfile FrontalShoulderElevation = new MovementProtocolProfile
    {
        enableYawGate = true,
        enableSideProfileGate = false,
        foreshortenInvalidatesRep = true,
        foreshortenWarnFeedback = true,
        yawAffectsPeakRom = true
    };

    public static readonly MovementProtocolProfile ElbowHinge = new MovementProtocolProfile
    {
        enableYawGate = false,
        enableSideProfileGate = false,
        foreshortenInvalidatesRep = false,
        foreshortenWarnFeedback = false,
        yawAffectsPeakRom = false
    };

    public static readonly MovementProtocolProfile Neck = new MovementProtocolProfile
    {
        enableYawGate = false,
        enableSideProfileGate = false,
        foreshortenInvalidatesRep = false,
        foreshortenWarnFeedback = false,
        yawAffectsPeakRom = false
    };

    public static readonly MovementProtocolProfile LowerLimb = new MovementProtocolProfile
    {
        enableYawGate = false,
        enableSideProfileGate = true,
        foreshortenInvalidatesRep = false,
        foreshortenWarnFeedback = false,
        yawAffectsPeakRom = false
    };

    public static readonly MovementProtocolProfile SharedTorsoOnly = new MovementProtocolProfile
    {
        enableYawGate = false,
        enableSideProfileGate = false,
        foreshortenInvalidatesRep = false,
        foreshortenWarnFeedback = false,
        yawAffectsPeakRom = false
    };
}
