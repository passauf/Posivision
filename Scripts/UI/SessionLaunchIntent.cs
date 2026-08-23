/// <summary>
/// Menüde onaylanan seans planı sahneye taşınır. Hasta kimliği tutulmaz (KVKK).
/// </summary>
public static class SessionLaunchIntent
{
    public static bool PreparedThisVisit { get; private set; }

    public static void MarkPrepared()
    {
        PreparedThisVisit = true;
    }

    public static void Consume()
    {
        PreparedThisVisit = false;
    }
}
