using UnityEngine;

/// <summary>
/// Omuz abdüksiyonu (ön kamera): elevasyon + foreshortening + yaw düzeltmesi + önkol artefakt koruması.
/// SaMD Class B; teşhis değildir.
/// </summary>
public sealed class ShoulderAbductionAnalyzer : IShoulderElevationAnalyzer, IMovementConfigurable, IMovementAvatarHooks
{
    public MovementId Id => MovementId.ShoulderAbduction;
    public MovementAnalysisFamily Family => MovementAnalysisFamily.ShoulderElevation;
    public PoseRegionMask RequiredMask => PoseRegionMask.ShoulderFlexion();

    private ShoulderAbductionAnalyzerConfig _config;
    private TheoreticalRomCorrectionConfig _romCorrection = TheoreticalRomCorrectionConfig.TheoreticalDefaults();
    private float _physicRight;
    private float _physicLeft;
    private float _refUpperArmLenR;
    private float _refUpperArmLenL;
    private float _prevElbowFlexR = -1000f;
    private float _prevElbowFlexL = -1000f;
    private float _prevGuardedFlexR;
    private float _prevGuardedFlexL;

    public void Configure(in ShoulderAbductionAnalyzerConfig config)
    {
        _config = config;
    }

    public void ApplyHostSettings(in MovementHostSettings settings)
    {
        _config = settings.abduction;
        _config.reference = settings.reference;
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
        _prevElbowFlexR = -1000f;
        _prevElbowFlexL = -1000f;
        _prevGuardedFlexR = 0f;
        _prevGuardedFlexL = 0f;
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

        bool foreshortenMpRight = ctx.mpRightOk && ctx.mpRightWristOk
            && ShoulderAbductionForegroundGuards.IsCameraAxisArmCollapse(
                ctx.mpRightShoulder, ctx.mpRightElbow, ctx.mpRightWrist,
                _refUpperArmLenR, in _config);
        bool foreshortenMpLeft = ctx.mpLeftOk && ctx.mpLeftWristOk
            && ShoulderAbductionForegroundGuards.IsCameraAxisArmCollapse(
                ctx.mpLeftShoulder, ctx.mpLeftElbow, ctx.mpLeftWrist,
                _refUpperArmLenL, in _config);

        if (foreshortenMpRight) mpRightAngle = float.NaN;
        if (foreshortenMpLeft) mpLeftAngle = float.NaN;

        result.notifyForeshorten = foreshortenMpRight || foreshortenMpLeft;
        result.foreshortenMpRight = foreshortenMpRight;
        result.foreshortenMpLeft = foreshortenMpLeft;

        if (ctx.mpRightOk && ctx.mpRightWristOk && !float.IsNaN(mpRightAngle))
        {
            mpRightAngle = ShoulderAbductionForegroundGuards.GuardForearmRotation(
                mpRightAngle, ctx.mpRightShoulder, ctx.mpRightElbow, ctx.mpRightWrist,
                in _config, ref _prevElbowFlexR, ref _prevGuardedFlexR);
        }
        if (ctx.mpLeftOk && ctx.mpLeftWristOk && !float.IsNaN(mpLeftAngle))
        {
            mpLeftAngle = ShoulderAbductionForegroundGuards.GuardForearmRotation(
                mpLeftAngle, ctx.mpLeftShoulder, ctx.mpLeftElbow, ctx.mpLeftWrist,
                in _config, ref _prevElbowFlexL, ref _prevGuardedFlexL);
        }

        if (!float.IsNaN(mpRightAngle))
        {
            mpRightAngle = ShoulderElevationCore.CorrectAngle(
                mpRightAngle, in ctx, ctx.mpRightShoulder, ctx.mpRightElbow,
                _refUpperArmLenR, _config.foreshorteningMinArmRatio,
                in _romCorrection, applyYaw: true);
        }
        if (!float.IsNaN(mpLeftAngle))
        {
            mpLeftAngle = ShoulderElevationCore.CorrectAngle(
                mpLeftAngle, in ctx, ctx.mpLeftShoulder, ctx.mpLeftElbow,
                _refUpperArmLenL, _config.foreshorteningMinArmRatio,
                in _romCorrection, applyYaw: true);
        }

        ShoulderElevationCore.FinishFrame(
            in ctx, ref _physicRight, ref _physicLeft,
            mpRightAngle, mpLeftAngle,
            foreshortenMpRight, foreshortenMpLeft,
            ref result);
    }
}
