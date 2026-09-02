/// <summary>
/// Tekrar tanıma politikası. Host UI/rapor yan etkilerini uygular.
/// Configure: aile bağımsız host eşikleri (<see cref="RepPolicyHostConfig"/>).
/// SaMD Class B; teşhis değildir.
/// </summary>
public interface IRepPolicy
{
    void Reset();
    void Configure(in RepPolicyHostConfig config);
    void SetTargetDegrees(float targetDegrees);
    float TargetDegrees { get; }
    float LowerLimitDegrees { get; }
    void Tick(in RepTickContext ctx, ref ArmRepState state, ref RepTickResult result);
}

/// <summary>
/// Host tekrar eşikleri — elevasyon ve gelecekteki hinge politikaları paylaşabilir.
/// Eski ad: ShoulderFlexionRepPolicyConfig (alias aşağıda).
/// </summary>
public struct RepPolicyHostConfig
{
    public float holdSeconds;
    public float enterSlackDegrees;
    public float returnRatio;
    public float lowerMinDegrees;
    public float lowerMaxDegrees;
    public float minTravelDegrees;
}

/// <summary>Geriye uyumluluk alias — yeni kod <see cref="RepPolicyHostConfig"/> kullansın.</summary>
public struct ShoulderFlexionRepPolicyConfig
{
    public float holdSeconds;
    public float enterSlackDegrees;
    public float returnRatio;
    public float lowerMinDegrees;
    public float lowerMaxDegrees;
    public float minTravelDegrees;

    public static implicit operator RepPolicyHostConfig(ShoulderFlexionRepPolicyConfig c)
    {
        return new RepPolicyHostConfig
        {
            holdSeconds = c.holdSeconds,
            enterSlackDegrees = c.enterSlackDegrees,
            returnRatio = c.returnRatio,
            lowerMinDegrees = c.lowerMinDegrees,
            lowerMaxDegrees = c.lowerMaxDegrees,
            minTravelDegrees = c.minTravelDegrees
        };
    }
}
