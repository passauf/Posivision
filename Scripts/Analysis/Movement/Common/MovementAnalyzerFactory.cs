/// <summary>
/// Hareket stratejisi fabrikası — MovementId / aileye göre analyzer + rep policy.
/// Yeni hareket: klasör + IMovementAnalyzer + IRepPolicy + buraya case ekle.
/// PhysioAnalyzer somut tip bilmez.
/// SaMD Class B; teşhis değildir.
/// </summary>
public static class MovementAnalyzerFactory
{
    public static IMovementAnalyzer CreateAnalyzer(MovementId movementId)
    {
        if (!ExerciseCatalog.IsLiveReady(movementId))
            movementId = ExerciseCatalog.DefaultMovementId;

        switch (movementId)
        {
            case MovementId.ShoulderFlexion:
                return new ShoulderFlexionAnalyzer();
            case MovementId.ShoulderAbduction:
                return new ShoulderAbductionAnalyzer();
        }

        ExerciseDefinition def = ExerciseCatalog.GetOrDefault(movementId);
        switch (def.AnalysisFamily)
        {
            case MovementAnalysisFamily.ShoulderElevation:
                return new ShoulderFlexionAnalyzer();
            case MovementAnalysisFamily.ElbowHinge:
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[Movement] ElbowHinge family live but analyzer missing — fallback flexion");
#endif
                return new ShoulderFlexionAnalyzer();
            default:
                return new ShoulderFlexionAnalyzer();
        }
    }

    public static IRepPolicy CreateRepPolicy(MovementId movementId)
    {
        if (!ExerciseCatalog.IsLiveReady(movementId))
            movementId = ExerciseCatalog.DefaultMovementId;

        switch (movementId)
        {
            case MovementId.ShoulderFlexion:
                return new ShoulderFlexionRepPolicy();
            case MovementId.ShoulderAbduction:
                return new ShoulderAbductionRepPolicy();
        }

        ExerciseDefinition def = ExerciseCatalog.GetOrDefault(movementId);
        switch (def.AnalysisFamily)
        {
            case MovementAnalysisFamily.ShoulderElevation:
                return new ShoulderElevationRepPolicy();
            case MovementAnalysisFamily.ElbowHinge:
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[Movement] ElbowHinge rep policy missing — fallback elevation policy");
#endif
                return new ShoulderElevationRepPolicy();
            default:
                return new ShoulderElevationRepPolicy();
        }
    }
}
