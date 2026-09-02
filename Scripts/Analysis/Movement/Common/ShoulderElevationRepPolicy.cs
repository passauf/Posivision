using UnityEngine;

/// <summary>
/// Omuz elevasyon tekrar politikası (fleksiyon + abdüksiyon ortak).
/// SaMD Class B; teşhis değildir.
/// </summary>
public class ShoulderElevationRepPolicy : IRepPolicy
{
    private RepPolicyHostConfig _config;
    private float _targetDegrees;
    private float _lowerLimitDegrees;

    public float TargetDegrees => _targetDegrees;
    public float LowerLimitDegrees => _lowerLimitDegrees;

    public void Configure(in RepPolicyHostConfig config)
    {
        _config = config;
        if (_targetDegrees > 1f)
            _lowerLimitDegrees = ComputeRepLowerLimit(_targetDegrees);
    }

    public void Reset()
    {
    }

    public void SetTargetDegrees(float targetDegrees)
    {
        _targetDegrees = targetDegrees;
        _lowerLimitDegrees = ComputeRepLowerLimit(targetDegrees);
    }

    public void Tick(in RepTickContext ctx, ref ArmRepState state, ref RepTickResult result)
    {
        result = default;
        if (!ctx.gateValid || ctx.deltaTime <= 0f) return;

        float target = ctx.targetDegrees > 1f ? ctx.targetDegrees : _targetDegrees;
        float lower = ctx.lowerLimitDegrees > 0f ? ctx.lowerLimitDegrees : _lowerLimitDegrees;
        float holdSeconds = ctx.holdSeconds > 0f ? ctx.holdSeconds : _config.holdSeconds;
        float enterSlack = ctx.enterSlackDegrees > 0f ? ctx.enterSlackDegrees : _config.enterSlackDegrees;
        float minTravel = ctx.minTravelDegrees > 0f ? ctx.minTravelDegrees : _config.minTravelDegrees;

        float gateAngle = ctx.gateAngle;
        bool repInProgress = gateAngle > lower;
        if (repInProgress && ctx.invalidatePose)
            state.repInvalid = true;

        float targetEnter = Mathf.Max(lower + 1f, target - enterSlack);
        float cycleResetBelow = lower;
        float holdRequired = Mathf.Max(0.2f, holdSeconds);
        float targetExit = Mathf.Max(cycleResetBelow + 1f, targetEnter - Mathf.Max(4f, minTravel * 0.5f));

        if (gateAngle <= cycleResetBelow)
        {
            state.repCountedAtPeak = false;
            state.inTargetZone = false;
            state.targetHoldStreak = 0f;
            state.isUp = false;
            if (!repInProgress) state.repInvalid = false;
        }
        else if (!state.inTargetZone && gateAngle >= targetEnter)
        {
            state.inTargetZone = true;
        }
        else if (state.inTargetZone && gateAngle < targetExit)
        {
            state.inTargetZone = false;
            state.targetHoldStreak = 0f;
        }

        if (state.inTargetZone && repInProgress && !state.repCountedAtPeak)
        {
            state.targetHoldStreak += ctx.deltaTime;
            if (state.targetHoldStreak >= holdRequired)
            {
                state.repCountedAtPeak = true;
                state.isUp = true;
                state.targetHoldStreak = 0f;
                result.gateAngleAtCount = gateAngle;
                if (state.repInvalid)
                {
                    state.invalidCount++;
                    result.countedInvalid = true;
                }
                else
                {
                    state.count++;
                    result.countedValid = true;
                }
                state.repInvalid = false;
            }
        }
    }

    public float ComputeRepLowerLimit(float targetDegrees)
    {
        float target = Mathf.Max(targetDegrees, PersonalizedTargetAdvisor.MinAngleDegrees);
        float ratio = Mathf.Clamp01(_config.returnRatio);
        float minFloor = Mathf.Max(0f, _config.lowerMinDegrees);
        float maxFloor = Mathf.Max(minFloor, _config.lowerMaxDegrees);
        float minTravel = Mathf.Max(2f, _config.minTravelDegrees);

        float fromRatio = target * ratio;
        float lower = Mathf.Clamp(fromRatio, minFloor, maxFloor);

        float maxAllowedLower = target - minTravel;
        if (maxAllowedLower < minFloor)
            lower = Mathf.Clamp(maxAllowedLower, 0f, minFloor);
        else
            lower = Mathf.Min(lower, maxAllowedLower);

        return lower;
    }
}
