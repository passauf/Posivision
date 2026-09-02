/// <summary>
/// Hareket ailesine özel avatar hedef senkronu (ör. omuz yay hedefi).
/// </summary>
public interface IMovementAvatarHooks
{
    void SyncAvatarTargets(AvatarBodyDriver driver, float targetRightDegrees, float targetLeftDegrees);
}
