/// <summary>
/// Canlı hareket ROM analizi sözleşmesi (strateji). Host pipeline/kuyruk sahibi kalır.
/// SaMD Class B karar-destek; teşhis değildir.
/// </summary>
public interface IMovementAnalyzer
{
    MovementId Id { get; }
    MovementAnalysisFamily Family { get; }
    PoseRegionMask RequiredMask { get; }
    void ResetSession();
    void ProcessFrame(in MovementFrameContext ctx, ref MovementFrameResult result);
}
