using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Omuz elevasyon ailesi Burst açı job'ı + MovementFrameContext üretimi.
/// PhysioAnalyzer yalnızca orkestrasyon; hip–omuz–dirsek mantığı burada.
/// </summary>
public static class ShoulderElevationAnglePipeline
{
    public const int ArmJobCount = 2;
    public const int LandmarkTripletCount = 6;

    public struct ScheduleInput
    {
        public bool mpRightOk;
        public bool mpLeftOk;
        public bool mpRightWristOk;
        public bool mpLeftWristOk;
        public bool torsoOk;
        public bool swap;
        public bool clinicalRightOk;
        public bool clinicalLeftOk;
        public float bodyYawDegrees;
        public bool patientSideView;
        public float rawShoulderWidth01;
        public Vector2 rightHip;
        public Vector2 rightShoulder;
        public Vector2 rightElbow;
        public Vector2 rightWrist;
        public Vector2 leftHip;
        public Vector2 leftShoulder;
        public Vector2 leftElbow;
        public Vector2 leftWrist;
        public IShoulderElevationAnalyzer elevationAnalyzer;
        public IMovementAnalyzer movementAnalyzer;
        public NativeArray<float2> jobLandmarks;
        public NativeArray<float> jobAngles;
        public NativeArray<bool> jobEnabled;
        public NativeArray<float> jobRefArmLengths;
        public NativeArray<float> jobLeanOut;
        public Vector2 leanLeftShoulder;
        public Vector2 leanRightShoulder;
        public Vector2 leanLeftHip;
        public Vector2 leanRightHip;
    }

    public struct ScheduleOutput
    {
        public float spineLeanDegrees;
        public MovementFrameResult frameResult;
    }

    public static void ScheduleAndComplete(in ScheduleInput input, out ScheduleOutput output)
    {
        output = default;

        input.jobEnabled[0] = input.mpRightOk;
        if (input.mpRightOk)
        {
            input.jobLandmarks[0] = ToFloat2(input.rightHip);
            input.jobLandmarks[1] = ToFloat2(input.rightShoulder);
            input.jobLandmarks[2] = ToFloat2(input.rightElbow);
            if (input.elevationAnalyzer != null)
            {
                input.elevationAnalyzer.UpdateReferenceArmLength(0, input.rightShoulder, input.rightElbow);
                input.jobRefArmLengths[0] = input.elevationAnalyzer.GetReferenceArmLength(0);
            }
        }

        input.jobEnabled[1] = input.mpLeftOk;
        if (input.mpLeftOk)
        {
            input.jobLandmarks[3] = ToFloat2(input.leftHip);
            input.jobLandmarks[4] = ToFloat2(input.leftShoulder);
            input.jobLandmarks[5] = ToFloat2(input.leftElbow);
            if (input.elevationAnalyzer != null)
            {
                input.elevationAnalyzer.UpdateReferenceArmLength(1, input.leftShoulder, input.leftElbow);
                input.jobRefArmLengths[1] = input.elevationAnalyzer.GetReferenceArmLength(1);
            }
        }

        var angleJob = new JointAngleJob
        {
            landmarks = input.jobLandmarks,
            referenceArmLengths = input.jobRefArmLengths,
            anglesOut = input.jobAngles,
            enabled = input.jobEnabled
        };

        JobHandle angleHandle = angleJob.Schedule(ArmJobCount, 1);
        float spineLean = 0f;

        if (input.torsoOk)
        {
            var leanJob = new SpineLeanJob
            {
                leftShoulder = ToFloat2(input.leanLeftShoulder),
                rightShoulder = ToFloat2(input.leanRightShoulder),
                leftHip = ToFloat2(input.leanLeftHip),
                rightHip = ToFloat2(input.leanRightHip),
                leanDegreesOut = input.jobLeanOut
            };
            JobHandle leanHandle = leanJob.Schedule();
            JobHandle.CombineDependencies(angleHandle, leanHandle).Complete();
            spineLean = input.jobLeanOut[0];
        }
        else
        {
            angleHandle.Complete();
        }

        output.spineLeanDegrees = spineLean;

        var frameCtx = new MovementFrameContext
        {
            deltaTime = Time.unscaledDeltaTime,
            swapArms = input.swap,
            mpRightOk = input.mpRightOk,
            mpLeftOk = input.mpLeftOk,
            mpRightWristOk = input.mpRightWristOk,
            mpLeftWristOk = input.mpLeftWristOk,
            clinicalRightOk = input.clinicalRightOk,
            clinicalLeftOk = input.clinicalLeftOk,
            jobAngleMpRight = input.mpRightOk ? input.jobAngles[0] : float.NaN,
            jobAngleMpLeft = input.mpLeftOk ? input.jobAngles[1] : float.NaN,
            mpRightShoulder = input.rightShoulder,
            mpRightElbow = input.rightElbow,
            mpRightWrist = input.rightWrist,
            mpLeftShoulder = input.leftShoulder,
            mpLeftElbow = input.leftElbow,
            mpLeftWrist = input.leftWrist,
            bodyYawDegrees = input.bodyYawDegrees,
            patientSideView = input.patientSideView,
            rawShoulderWidth01 = input.rawShoulderWidth01
        };

        MovementFrameResult frameResult = default;
        if (input.movementAnalyzer != null)
            input.movementAnalyzer.ProcessFrame(in frameCtx, ref frameResult);

        output.frameResult = frameResult;
    }

    private static float2 ToFloat2(Vector2 v) => new float2(v.x, v.y);
}
