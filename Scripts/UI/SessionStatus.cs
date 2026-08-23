/// <summary>
/// Sahneler arası seans durumu (menü bildirimi + egzersiz HUD).
/// Event-driven: durum değişince Changed tetiklenir; HUD her frame poll etmez.
/// Hasta kimliği tutulmaz (KVKK).
/// </summary>
public static class SessionStatus
{
    public enum Phase
    {
        Idle,
        Active,
        Completed
    }

    public static Phase Current { get; private set; } = Phase.Idle;
    public static string Message { get; private set; } = "Seans bekleniyor";

    public static bool IsActive => Current == Phase.Active;

    /// <summary>Durum her değiştiğinde tetiklenir (abone HUD/menü UI'ı günceller).</summary>
    public static event System.Action Changed;

    public static void MarkIdle()
    {
        Set(Phase.Idle, "Seans bekleniyor — başlatmak için butona basın");
    }

    public static void MarkActive()
    {
        Set(Phase.Active, "SEANS AKTİF");
    }

    public static void MarkCompleted()
    {
        Set(Phase.Completed, "Seans tamamlandı — rapor hazır");
    }

    private static void Set(Phase phase, string message)
    {
        Current = phase;
        Message = message;
        Changed?.Invoke();
    }
}
