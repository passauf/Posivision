/// <summary>
/// Host'tan hareket stratejisine aktarılan ayarlar. Her analyzer yalnızca ihtiyaç duyduğu alanları okur.
/// SaMD Class B; teşhis değildir.
/// </summary>
public struct MovementHostSettings
{
    public RepPolicyHostConfig rep;
    public ShoulderElevationReferenceConfig reference;
    public ShoulderAbductionAnalyzerConfig abduction;
    public TheoreticalRomCorrectionConfig romCorrection;
    public float foreshorteningWarningCooldownSeconds;
}
