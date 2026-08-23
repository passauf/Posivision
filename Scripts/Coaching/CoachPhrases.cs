using UnityEngine;

/// <summary>
/// Sesli koç ifadeleri — dil Loc üzerinden. KVKK: kimlik yok.
/// </summary>
public static class CoachPhrases
{
    public static string SessionStart => Loc.T("coach.sessionStart");
    public static string StandStraight => Loc.T("coach.stand");
    public static string HighStrain => Loc.T("coach.strain");
    public static string RepInvalid => Loc.T("coach.repInvalid");
    public static string SlowDown => Loc.T("coach.slow");
    public static string GoodPace => Loc.T("coach.pace");
    public static string AlmostDone => Loc.T("coach.almost");
    public static string DepthCollapse => Loc.T("coach.depthCollapse");
    public static string FaceFront => Loc.T("coach.faceFront");
    public static string TurnFront => Loc.T("coach.turnFront");

    public static string TargetsApplied(float angle, int reps)
    {
        return Loc.Format("coach.targets", Mathf.RoundToInt(angle), reps);
    }
}

public enum CoachCue
{
    SessionStart = 0,
    StandStraight = 1,
    HighStrain = 2,
    RepInvalid = 3,
    SlowDown = 4,
    GoodPace = 5,
    AlmostDone = 6,
    TargetsApplied = 7,
    DepthCollapse = 8,
    FaceFront = 9,
    TurnFront = 10
}
