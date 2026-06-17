using Rukhanka.Toolbox;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
    //	Per-foot horizontal plant lock. While a foot is grounded its world XZ is frozen so the planted
    //	foot does not slide as the body moves over it; the lock releases the moment the foot lifts.
    //	Auto-added (below) to every humanoid rig that requests foot IK.
    public struct FootIKPlantState : IComponentData
    {
        public float2 leftLockXZ;
        public float2 rightLockXZ;
        public bool leftPlanted;
        public bool rightPlanted;
    }

    //	Foot IK (BovineLabs Timeline). Consumes AnimationToProcessComponent.applyFootIK (plumbed from the
    //	Timeline animation clips). For each grounded foot it FREEZES the foot's world XZ (captured the frame
    //	it lands) and solves the leg (UpperLeg -> LowerLeg -> Foot) with a two-bone IK so the foot holds that
    //	spot while the body moves — killing retarget/locomotion slide. The reference foot pose is Rukhanka's
    //	OWN sampled foot (NOT a baked goal), so there is no bake-space-vs-rig-space mismatch. Vertical motion
    //	and ankle orientation stay 100% animation-driven. No-op for non-humanoid rigs / clips that don't
    //	request foot IK. Runs in the IK injection group, after the base pose is sampled.
    [UpdateInGroup(typeof(RukhankaAnimationInjectionSystemGroup))]
    [UpdateAfter(typeof(TwoBoneIKSystem))]
    public partial struct FootIKSystem : ISystem
    {
        //	ponytail: a foot counts as "grounded" when within this height band of the LOWER of the two feet.
        //	Self-calibrating (relative to the rig's own stance), so it needs no world floor height. Widen for
        //	wide-stance / uneven idles, narrow if a swinging foot locks too early.
        const float PlantBand = 0.08f;

        [BurstCompile]
        partial struct FootIKJob : IJobEntity
        {
            [NativeDisableContainerSafetyRestriction]
            public RuntimeAnimationData runtimeData;

            //	Character world transform, to lock the foot in WORLD space (so body travel doesn't drag it).
            [ReadOnly]
            public ComponentLookup<LocalToWorld> l2wLookup;

            //	Global multiplier on the foot-IK weight (1 = full). Hook for a future authoring knob.
            public float globalWeight;
            public float plantBand;

            void Execute(Entity entity, in RigDefinitionComponent rigDef,
                in DynamicBuffer<AnimationToProcessComponent> atps, ref FootIKPlantState plant)
            {
                if (atps.IsEmpty)
                {
                    plant.leftPlanted = false;
                    plant.rightPlanted = false;
                    return;
                }

                ref var rigBlobValue = ref rigDef.rigBlob.Value;
                //	humanData is allocated for humanoid rigs only.
                if (!rigBlobValue.humanData.IsValid)
                    return;

                ref var h2s = ref rigBlobValue.humanData.Value.humanBoneToSkeletonBoneIndices;

                //	Strongest clip that requested foot IK drives the weight (a blend fades the lock in/out).
                var bestWeight = 0f;
                for (var i = 0; i < atps.Length; ++i)
                {
                    var atp = atps[i];
                    if (!atp.applyFootIK)
                        continue;
                    var w = atp.weight * atp.layerWeight;
                    if (w > bestWeight)
                        bestWeight = w;
                }

                var weight = math.saturate(bestWeight) * globalWeight;
                if (weight <= 1e-4f)
                {
                    plant.leftPlanted = false;
                    plant.rightPlanted = false;
                    return;
                }

                var lfIdx = HumanBone(ref h2s, (int)UnityEngine.HumanBodyBones.LeftFoot);
                var rfIdx = HumanBone(ref h2s, (int)UnityEngine.HumanBodyBones.RightFoot);
                if (lfIdx < 0 || rfIdx < 0)
                    return;

                var l2w = l2wLookup.HasComponent(entity) ? l2wLookup[entity].Value : float4x4.identity;
                var invL2w = math.inverse(l2w);

                //	AnimationStream's working buffers are reference-semantic native containers, so the by-value
                //	copies passed into the solver mutate the same shared bone data. `using` finalizes (rebuilds
                //	outdated world poses) on scope exit — that IS how the IK result is written back.
                using var stream = AnimationStream.Create(runtimeData, rigDef);

                //	Ground reference = the lower foot, in WORLD space. The other foot is "grounded" if it sits
                //	within plantBand of it. Relative test => works at any floor height, on slopes, on platforms.
                var lFootWorldY = math.transform(l2w, stream.GetWorldPose(lfIdx).pos).y;
                var rFootWorldY = math.transform(l2w, stream.GetWorldPose(rfIdx).pos).y;
                var groundRef = math.min(lFootWorldY, rFootWorldY);

                SolveFoot(stream, ref h2s, l2w, invL2w, groundRef, ref plant.leftLockXZ, ref plant.leftPlanted, weight,
                    (int)UnityEngine.HumanBodyBones.LeftUpperLeg,
                    (int)UnityEngine.HumanBodyBones.LeftLowerLeg,
                    (int)UnityEngine.HumanBodyBones.LeftFoot);

                SolveFoot(stream, ref h2s, l2w, invL2w, groundRef, ref plant.rightLockXZ, ref plant.rightPlanted, weight,
                    (int)UnityEngine.HumanBodyBones.RightUpperLeg,
                    (int)UnityEngine.HumanBodyBones.RightLowerLeg,
                    (int)UnityEngine.HumanBodyBones.RightFoot);
            }

            void SolveFoot(AnimationStream stream, ref BlobArray<int> h2s, in float4x4 l2w, in float4x4 invL2w,
                float groundRef, ref float2 lockXZ, ref bool planted, float weight, int upperBone, int lowerBone, int footBone)
            {
                var rootIdx = HumanBone(ref h2s, upperBone);
                var midIdx = HumanBone(ref h2s, lowerBone);
                var tipIdx = HumanBone(ref h2s, footBone);
                if (rootIdx < 0 || midIdx < 0 || tipIdx < 0)
                {
                    planted = false;
                    return;
                }

                var footWorld = math.transform(l2w, stream.GetWorldPose(tipIdx).pos);

                //	Foot lifted above the stance band -> release; it follows the animation freely.
                if (footWorld.y > groundRef + plantBand)
                {
                    planted = false;
                    return;
                }

                //	Just landed -> capture the world XZ to hold. Already planted -> keep the captured spot.
                if (!planted)
                {
                    lockXZ = footWorld.xz;
                    planted = true;
                }

                //	Hold XZ, let the animation own Y (heel roll, micro-lift). Back to rig-root space for the solve.
                var targetWorld = new float3(lockXZ.x, footWorld.y, lockXZ.y);
                var targetRootRel = math.transform(invL2w, targetWorld);
                SolveTwoBone(stream, rootIdx, midIdx, tipIdx, targetRootRel, weight);
            }

            //	Two-bone analytic IK in rig-relative space, mirroring TwoBoneIKSystem's proven solve (natural
            //	bend axis from the current pose preserves knee direction). Position-only: the ankle keeps the
            //	animation's orientation, so the foot never twists from the leg adjustment.
            static void SolveTwoBone(AnimationStream stream, int rootIdx, int midIdx, int tipIdx,
                float3 goalPos, float weight)
            {
                var root = stream.GetWorldPose(rootIdx);
                var mid = stream.GetWorldPose(midIdx);
                var tip = stream.GetWorldPose(tipIdx);

                var initialTipRot = tip.rot;
                var targetPos = math.lerp(tip.pos, goalPos, weight);

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

                //	Re-assert the animation's foot orientation (the leg rotations above dragged it along).
                stream.SetWorldRotation(tipIdx, initialTipRot);
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

        //	Not [BurstCompile]: it plays back a structural-change ECB (managed). The hot path is the job.
        public void OnUpdate(ref SystemState ss)
        {
            //	Lazily give every humanoid rig that wants foot IK its persistent plant state. The WithNone
            //	query is empty once seeded, so this is a near no-op after the first frame each rig appears.
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rigDef, e) in SystemAPI
                         .Query<RefRO<RigDefinitionComponent>>()
                         .WithNone<FootIKPlantState>()
                         .WithEntityAccess())
            {
                if (rigDef.ValueRO.rigBlob.IsCreated && rigDef.ValueRO.rigBlob.Value.humanData.IsValid)
                    ecb.AddComponent(e, new FootIKPlantState());
            }
            ecb.Playback(ss.EntityManager);
            ecb.Dispose();

            ref var runtimeData = ref SystemAPI.GetSingletonRW<RuntimeAnimationData>().ValueRW;

            var job = new FootIKJob
            {
                runtimeData = runtimeData,
                l2wLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                globalWeight = 1f,
                plantBand = PlantBand,
            };

            job.ScheduleParallel();
        }
    }
}
