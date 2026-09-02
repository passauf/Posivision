/// <summary>
/// Protokol profiline göre tekrar/ROM geçersiz kılma — host hareket adına dallanmaz.
/// </summary>
public static class MovementInvalidationAssembler
{
    public struct Input
    {
        public in MovementProtocolProfile protocol;
        public bool patientSideView;
        public bool torsoActive;
        public bool invalidateLean;
        public bool invalidateFacing;
        public bool invalidateStrain;
        public bool sideMeasurementValid;
        public bool foreshortenClinicalRight;
        public bool foreshortenClinicalLeft;
        public bool measureRightArm;
        public bool measureLeftArm;
    }

    public struct Output
    {
        public bool invalidatePoseEarly;
        public bool invalidateRightRep;
        public bool invalidateLeftRep;
        public bool blockPeakRomRight;
        public bool blockPeakRomLeft;
    }

    public static void Evaluate(in Input input, out Output output)
    {
        output = default;
        bool invalidateSide = input.protocol.enableSideProfileGate
            && input.patientSideView
            && !input.sideMeasurementValid;

        output.invalidatePoseEarly = input.invalidateLean
            || (input.protocol.enableYawGate && input.invalidateFacing)
            || input.invalidateStrain
            || invalidateSide;

        bool fsRight = input.protocol.foreshortenInvalidatesRep && input.foreshortenClinicalRight;
        bool fsLeft = input.protocol.foreshortenInvalidatesRep && input.foreshortenClinicalLeft;

        output.invalidateRightRep = output.invalidatePoseEarly || fsRight;
        output.invalidateLeftRep = output.invalidatePoseEarly || fsLeft;

        output.blockPeakRomRight = input.measureRightArm && fsRight;
        output.blockPeakRomLeft = input.measureLeftArm && fsLeft;
    }
}
