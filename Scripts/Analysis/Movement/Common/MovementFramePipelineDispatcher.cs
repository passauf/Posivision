/// <summary>
/// Hareket ailesine göre Burst açı pipeline seçimi. PhysioAnalyzer yalnızca burayı çağırır.
/// </summary>
public static class MovementFramePipelineDispatcher
{
    public static bool TryScheduleAngles(
        MovementAnalysisFamily family,
        in ShoulderElevationAnglePipeline.ScheduleInput input,
        out ShoulderElevationAnglePipeline.ScheduleOutput output)
    {
        switch (family)
        {
            case MovementAnalysisFamily.ShoulderElevation:
                ShoulderElevationAnglePipeline.ScheduleAndComplete(in input, out output);
                return true;
            default:
                output = default;
                return false;
        }
    }
}
