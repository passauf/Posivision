using UnityEngine;

/// <summary>
/// Omuz fleksiyonu (yan profil): kalça–omuz–dirsek ROM; bilek/foreshortening/yaw yok.
/// SaMD Class B; teşhis değildir.
/// </summary>
public sealed class ShoulderFlexionAnalyzer : IShoulderElevationAnalyzer, IMovementConfigurable, IMovementAvatarHooks
{
    public MovementId Id => MovementId.ShoulderFlexion;
    public MovementAnalysisFamily Family => MovementAnalysisFamily.ShoulderElevation;
    public PoseRegionMask RequiredMask => PoseRegionMask.ShoulderFlexion();

    private ShoulderFlexionAnalyzerConfig _config;
    private TheoreticalRomCorrectionConfig _romCorrection = TheoreticalRomCorrectionConfig.TheoreticalDefaults();
    private float _physicRight;
    private float _physicLeft;
    private float _refUpperArmLenR;
    private float _refUpperArmLenL;

    public void Configure(in ShoulderFlexionAnalyzerConfig config)
    {
        _config = config;
    }

    public void ApplyHostSettings(in MovementHostSettings settings)
    {
        _config = new ShoulderFlexionAnalyzerConfig { reference = settings.reference };
        ConfigureRomCorrection(settings.romCorrection);
    }

    public void SyncAvatarTargets(AvatarBodyDriver driver, float targetRightDegrees, float targetLeftDegrees)
    {
        if (driver == null) return;
        driver.SetFlexionTargets(targetRightDegrees, targetLeftDegrees);
    }

    public void ConfigureRomCorrection(in TheoreticalRomCorrectionConfig config)
    {
        _romCorrection = config;
    }

    public void ResetSession()
    {
        _physicRight = 0f;
        _physicLeft = 0f;
        _refUpperArmLenR = 0f;
        _refUpperArmLenL = 0f;
    }

    public float GetReferenceArmLength(int mpArmIndex)
    {
        return mpArmIndex == 0 ? _refUpperArmLenR : _refUpperArmLenL;
    }

    public void UpdateReferenceArmLength(int mpArmIndex, Vector2 shoulder, Vector2 elbow)
    {
        ShoulderElevationCore.UpdateReferenceArmLength(
            ref _refUpperArmLenR, ref _refUpperArmLenL,
            mpArmIndex, shoulder, elbow, _config.reference);
    }

    public void ProcessFrame(in MovementFrameContext ctx, ref MovementFrameResult result)
    {
        result = default;

        float mpRightAngle = ctx.mpRightOk ? ctx.jobAngleMpRight : float.NaN;
        float mpLeftAngle = ctx.mpLeftOk ? ctx.jobAngleMpLeft : float.NaN;

        // Yan profil: yaw düzeltmesi kapalı; foreshortening/bilek kullanılmaz.
        if (!float.IsNaN(mpRightAngle))
        {
            mpRightAngle = ShoulderElevationCore.CorrectAngle(
                mpRightAngle, in ctx, ctx.mpRightShoulder, ctx.mpRightElbow,
                _refUpperArmLenR, 0f, in _romCorrection, applyYaw: false);
        }
        if (!float.IsNaN(mpLeftAngle))
        {
            mpLeftAngle = ShoulderElevationCore.CorrectAngle(
                mpLeftAngle, in ctx, ctx.mpLeftShoulder, ctx.mpLeftElbow,
                _refUpperArmLenL, 0f, in _romCorrection, applyYaw: false);
        }

        ShoulderElevationCore.FinishFrame(
            in ctx, ref _physicRight, ref _physicLeft,
            mpRightAngle, mpLeftAngle,
            foreshortenMpRight: false, foreshortenMpLeft: false,
            ref result);
    }
}

/// <summary>Omuz fleksiyonu (yan profil) eşikleri.</summary>
public struct ShoulderFlexionAnalyzerConfig
{
    public ShoulderElevationReferenceConfig reference;
}
