/// <summary>
/// Hareket stratejisi fabrikası — <see cref="MovementAnalysisFamily"/> ile yönlendirir.
/// Yeni aile: case + analyzer/rep ekle. Aynı ailedeki yeni MovementId yalnızca kataloğa yazılır.
/// SaMD Class B; teşhis değildir.
/// </summary>
public static class MovementAnalyzerFactory
{
    public static IMovementAnalyzer CreateAnalyzer(MovementId movementId)
    {
        if (!ExerciseCatalog.IsLiveReady(movementId))
            movementId = ExerciseCatalog.DefaultMovementId;

        ExerciseDefinition def = ExerciseCatalog.GetOrDefault(movementId);
        switch (def.AnalysisFamily)
        {
            case MovementAnalysisFamily.ShoulderElevation:
                return new ShoulderFlexionAnalyzer(movementId);

            case MovementAnalysisFamily.ElbowHinge:
                // Canlı değilken IsLiveReady false → DefaultMovementId’e düşülür.
                // Implemented=true olunca buraya ElbowHingeAnalyzer dönülmeli.
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[Movement] ElbowHinge family live but analyzer missing — fallback elevation");
#endif
                return new ShoulderFlexionAnalyzer(ExerciseCatalog.DefaultMovementId);

            default:
                return new ShoulderFlexionAnalyzer(ExerciseCatalog.DefaultMovementId);
        }
    }

    public static IRepPolicy CreateRepPolicy(MovementId movementId)
    {
        if (!ExerciseCatalog.IsLiveReady(movementId))
            movementId = ExerciseCatalog.DefaultMovementId;

        ExerciseDefinition def = ExerciseCatalog.GetOrDefault(movementId);
        switch (def.AnalysisFamily)
        {
            case MovementAnalysisFamily.ShoulderElevation:
                return new ShoulderFlexionRepPolicy();

            case MovementAnalysisFamily.ElbowHinge:
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[Movement] ElbowHinge rep policy missing — fallback elevation policy");
#endif
                return new ShoulderFlexionRepPolicy();

            default:
                return new ShoulderFlexionRepPolicy();
        }
    }
}
