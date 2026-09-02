/// <summary>
/// Hareket stratejisi host ayarlarını alır — PhysioAnalyzer somut tip bilmez.
/// </summary>
public interface IMovementConfigurable
{
    void ApplyHostSettings(in MovementHostSettings settings);
}
