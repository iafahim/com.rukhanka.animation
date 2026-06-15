using Rukhanka.Toolbox;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
    //	Foot IK (BovineLabs Timeline, Option A2). Consumes AnimationToProcessComponent.applyFootIK
    //	(plumbed from the Timeline animation clips) + the per-clip hips-relative foot goals baked
    //	into AnimationClipBlob.footIKGoals, and solves each leg (UpperLeg -> LowerLeg -> Foot) with a
    //	two-bone IK so the foot reaches the baked goal — correcting Rukhanka's runtime humanoid
    //	sampling back to the foot placement the clip authored. No-op for non-humanoid rigs / clips
    //	without baked goals. Runs in the IK injection group, after the base pose is sampled.
    [UpdateInGroup(typeof(RukhankaAnimationInjectionSystemGroup))]
    [UpdateAfter(typeof(TwoBoneIKSystem))]
    public partial struct FootIKSystem : ISystem
    {
        [BurstCompile]
        partial struct FootIKJob : IJobEntity
        {
            [NativeDisableContainerSafetyRestriction]
            public RuntimeAnimationData runtimeData;

            //	Global multiplier on the per-clip foot-IK weight (1 = full). Hook for a future authoring knob.
            public float globalWeight;

            void Execute(in RigDefinitionComponent rigDef, in DynamicBuffer<AnimationToProcessComponent> atps)
            {
                if (atps.IsEmpty)
                    return;

                ref var rigBlobValue = ref rigDef.rigBlob.Value;
                //	humanData is allocated for humanoid rigs only.
                if (!rigBlobValue.humanData.IsValid)
                    return;

                ref var h2s = ref rigBlobValue.humanData.Value.humanBoneToSkeletonBoneIndices;

                var hipsIdx = HumanBone(ref h2s, (int)UnityEngine.HumanBodyBones.Hips);
                if (hipsIdx < 0)
                    return;

                //	Pick the single dominant clip that requested foot IK and carries valid goals.
                var bestWeight = 0f;
                var goalTime = 0f;
                var hasGoal = false;
                BlobAssetReference<AnimationClipBlob> goalClip = default;
                for (var i = 0; i < atps.Length; ++i)
                {
                    var atp = atps[i];
                    if (!atp.applyFootIK || !atp.animation.IsCreated)
                        continue;
                    if (!atp.animation.Value.footIKGoals.isValid)
                        continue;

                    var w = atp.weight * atp.layerWeight;
                    if (w <= bestWeight)
                        continue;

                    bestWeight = w;
                    goalTime = atp.time;
                    goalClip = atp.animation;
                    hasGoal = true;
                }

                var weight = math.saturate(bestWeight) * globalWeight;
                if (!hasGoal || weight <= 1e-4f)
                    return;

                var goal = goalClip.Value.footIKGoals.Sample(goalTime);

                //	AnimationStream's working buffers are reference-semantic native containers, so the
                //	by-value copies passed into the solver mutate the same shared bone data. `using`
                //	finalizes (rebuilds outdated world poses) on scope exit.
                using var stream = AnimationStream.Create(runtimeData, rigDef);
                var hipsWorld = stream.GetWorldPose(hipsIdx);

                SolveLeg(stream, ref h2s, hipsWorld, goal.leftFoot, weight,
                    (int)UnityEngine.HumanBodyBones.LeftUpperLeg,
                    (int)UnityEngine.HumanBodyBones.LeftLowerLeg,
                    (int)UnityEngine.HumanBodyBones.LeftFoot);

                SolveLeg(stream, ref h2s, hipsWorld, goal.rightFoot, weight,
                    (int)UnityEngine.HumanBodyBones.RightUpperLeg,
                    (int)UnityEngine.HumanBodyBones.RightLowerLeg,
                    (int)UnityEngine.HumanBodyBones.RightFoot);
            }

            void SolveLeg(AnimationStream stream, ref BlobArray<int> h2s, in BoneTransform hipsWorld,
                in BoneTransform goalHipsRelative, float weight, int upperBone, int lowerBone, int footBone)
            {
                var rootIdx = HumanBone(ref h2s, upperBone);
                var midIdx = HumanBone(ref h2s, lowerBone);
                var tipIdx = HumanBone(ref h2s, footBone);
                if (rootIdx < 0 || midIdx < 0 || tipIdx < 0)
                    return;

                //	Re-anchor the hips-relative goal to the live animated hips pose so the foot tracks the body.
                var goalWorld = BoneTransform.Multiply(hipsWorld, goalHipsRelative);
                SolveTwoBone(stream, rootIdx, midIdx, tipIdx, goalWorld.pos, goalWorld.rot, weight);
            }

            //	Two-bone analytic IK in rig-relative world space, mirroring TwoBoneIKSystem's proven solve
            //	(no explicit bend hint: the natural bend axis from the current pose preserves knee direction).
            static void SolveTwoBone(AnimationStream stream, int rootIdx, int midIdx, int tipIdx,
                float3 goalPos, quaternion goalRot, float weight)
            {
                var root = stream.GetWorldPose(rootIdx);
                var mid = stream.GetWorldPose(midIdx);
                var tip = stream.GetWorldPose(tipIdx);

                var targetPos = math.lerp(tip.pos, goalPos, weight);
                var initialTipRot = tip.rot;

                var rootToMid = mid.pos - root.pos;
                var rootToMidLen = math.length(rootToMid);
                var midToTip = tip.pos - mid.pos;
                var midToTipLen = math.length(midToTip);
                var rootToTip = tip.pos - root.pos;
                var rootToTipLen = math.length(rootToTip);
                var rootToTarget = targetPos - root.pos;
                var rootToTargetLen = math.length(rootToTarget);

                var curBendAngle = CosineLawAngle(rootToMidLen, midToTipLen, rootToTipLen);
                var targetBendAngle = CosineLawAngle(rootToMidLen, midToTipLen, rootToTargetLen);

                var bendAxis = math.cross(rootToMid, midToTip);
                if (math.lengthsq(bendAxis) < math.EPSILON)
                {
                    bendAxis = math.cross(rootToTarget, midToTip);
                    if (math.lengthsq(bendAxis) <= math.EPSILON)
                        bendAxis = math.up();
                }

                bendAxis = math.normalize(bendAxis);

                var midRotDelta = quaternion.AxisAngle(bendAxis, curBendAngle - targetBendAngle);
                var midRot = math.normalize(math.mul(midRotDelta, mid.rot));
                stream.SetWorldRotation(midIdx, midRot);

                tip = stream.GetWorldPose(tipIdx);
                var updatedRootToTip = tip.pos - root.pos;
                var rootRotDelta = MathUtils.FromToRotation(updatedRootToTip, rootToTarget);
                var rootRot = math.mul(rootRotDelta, root.rot);
                stream.SetWorldRotation(rootIdx, rootRot);

                var finalTipRot = math.slerp(initialTipRot, goalRot, weight);
                stream.SetWorldRotation(tipIdx, finalTipRot);
            }

            static float CosineLawAngle(float aLen, float bLen, float cLen)
            {
                var denom = aLen * bLen;
                if (denom < math.EPSILON)
                    return 0f;
                var cosC = (aLen * aLen + bLen * bLen - cLen * cLen) / denom * 0.5f;
                return math.acos(math.clamp(cosC, -1f, 1f));
            }

            static int HumanBone(ref BlobArray<int> h2s, int humanBoneIndex)
            {
                return humanBoneIndex >= 0 && humanBoneIndex < h2s.Length ? h2s[humanBoneIndex] : -1;
            }
        }

//==============================================================================//

        [BurstCompile]
        public void OnCreate(ref SystemState ss)
        {
            ss.RequireForUpdate<RuntimeAnimationData>();
        }

/////////////////////////////////////////////////////////////////////////////////

        [BurstCompile]
        public void OnUpdate(ref SystemState ss)
        {
            ref var runtimeData = ref SystemAPI.GetSingletonRW<RuntimeAnimationData>().ValueRW;

            var job = new FootIKJob
            {
                runtimeData = runtimeData,
                globalWeight = 1f,
            };

            job.ScheduleParallel();
        }
    }
}
