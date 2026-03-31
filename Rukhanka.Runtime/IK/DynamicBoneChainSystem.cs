using System;
using Rukhanka.Toolbox;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
    
[UpdateInGroup(typeof(RukhankaAnimationInjectionSystemGroup), OrderFirst = true)]
public partial struct DynamicBoneChainSystem: ISystem
{
[BurstCompile]
partial struct DynamicBoneChainVerletSolverJob : IJobEntity
{
    [ReadOnly]
    public ComponentLookup<RigDefinitionComponent> rigDefLookup;
    [ReadOnly]
    public ComponentLookup<AnimatorEntityRefComponent> boneEntityRefLookup;
    [ReadOnly]
    public ComponentLookup<LocalTransform> localTransformLookup;
    [ReadOnly]
    public ComponentLookup<Parent> parentLookup;
    
    [NativeDisableContainerSafetyRestriction]
    public RuntimeAnimationData runtimeData;
    
    public float deltaTime;
    static readonly float fixedUpdateRate = 1 / 60.0f;

/////////////////////////////////////////////////////////////////////////////////

    void Execute(Entity e, ref DynamicBoneChainComponent dbcc, in AnimatorEntityRefComponent aer, ref DynamicBuffer<DynamicBoneChainNode> dynamicChain)
    {
        var rigDef = rigDefLookup[aer.animatorEntity];
        using var animStream = AnimationStream.Create(runtimeData, rigDef);
        
        Span<int> boneIndicesInRig = stackalloc int[dynamicChain.Length];
        Span<float3> initialPositions = stackalloc float3[dynamicChain.Length];
        var dca = dynamicChain.AsNativeArray();
        
        InitBoneFrameData(dca, boneIndicesInRig, initialPositions, animStream);
        var inertia = ComputeEntityInertia(e, ref dbcc, runtimeData);

        dbcc.timeAccumulator += deltaTime;
        var simulationCount = 0;
        while (dbcc.timeAccumulator >= fixedUpdateRate)
        {
            var modInertia = math.select(0, inertia * dbcc.inertia, simulationCount == 0);
            Integrate(dbcc, dca, modInertia, boneIndicesInRig, animStream);
            Elasticity(dbcc, dca, boneIndicesInRig, animStream);
            ConstrainDistance(dca, boneIndicesInRig, animStream);
            dbcc.timeAccumulator -= fixedUpdateRate;
            simulationCount++;
        }
        
        if (simulationCount == 0)
        {
            ApplyMovementOffset(inertia, dca);
            Elasticity(dbcc, dca, boneIndicesInRig, animStream);
            ConstrainDistance(dca, boneIndicesInRig, animStream);
        }
        
        MakeChainRotations(dca, boneIndicesInRig, animStream);
        for (var i = 0; i < dynamicChain.Length; ++i)
        {
            animStream.SetWorldPosition(boneIndicesInRig[i], dynamicChain[i].position);
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////

    void ApplyMovementOffset(float3 inertia, NativeArray<DynamicBoneChainNode> dynamicChain)
    {
        for (var i = 1; i < dynamicChain.Length; ++i)
        {
            var dc = dynamicChain[i];
            dc.position += inertia;
            dc.prevPosition += inertia;
            dynamicChain[i] = dc;
        }
    }

/////////////////////////////////////////////////////////////////////////////////

    void InitBoneFrameData(NativeArray<DynamicBoneChainNode> dynamicChain, Span<int> boneIndicesInRig, Span<float3> initialPositions, AnimationStream animStream)
    {
        for (var i = 0; i < dynamicChain.Length; ++i)
        {
            var dbe = dynamicChain[i];
            if (!boneEntityRefLookup.TryGetComponent(dbe.boneEntity, out var boneEntityRef))
                return;
            
            animStream.SetLocalPose(boneEntityRef.boneIndexInAnimationRig, dbe.referenceLocalPose);
            if (i == 0)
            {
                var rootPose = animStream.GetWorldPose(boneEntityRef.boneIndexInAnimationRig);
                dbe.position = dbe.prevPosition = rootPose.pos;
                dynamicChain[0] = dbe;
            }
            
            boneIndicesInRig[i] = boneEntityRef.boneIndexInAnimationRig;
        }
        
        for (var i = 0; i < dynamicChain.Length; ++i)
        {
            initialPositions[i] = animStream.GetWorldPosition(boneIndicesInRig[i]);
        }
    }

/////////////////////////////////////////////////////////////////////////////////

    float3 ComputeEntityInertia(Entity e, ref DynamicBoneChainComponent dbcc, RuntimeAnimationData runtimeAnimationData)
    {
        BoneTransform bt = BoneTransform.Identity();
        IKCommon.GetEntityWorldTransform
        (
            e,
            ref bt,
            runtimeAnimationData,
            localTransformLookup,
            parentLookup,
            boneEntityRefLookup,
            rigDefLookup
        );
        var rv = bt.pos - dbcc.prevPosition;
        dbcc.prevPosition = bt.pos;
        return rv;
    }

/////////////////////////////////////////////////////////////////////////////////

    void Integrate(DynamicBoneChainComponent dbcc, NativeArray<DynamicBoneChainNode> dynamicChain, float3 inertia, Span<int> boneIndicesInRig, AnimationStream animStream)
    {
        //  Root bone is stationary
        var dc0 = dynamicChain[0];
        dc0.prevPosition = dc0.position;
        dc0.position = animStream.GetWorldPosition(boneIndicesInRig[0]);
        
        for (var i = 1; i < dynamicChain.Length; ++i)
        {
            var dc = dynamicChain[i];
            var dv = dc.position - dc.prevPosition;
            dc.prevPosition = dc.position + inertia;
            dc.position += inertia + dv * (1 - dbcc.damping);
            dynamicChain[i] = dc;
        }
    }

/////////////////////////////////////////////////////////////////////////////////
    
    void ConstrainDistance(NativeArray<DynamicBoneChainNode> dynamicChain, Span<int> boneIndicesInRig, AnimationStream animStream)
    {
        //  Distance constraints
        for (var i = 1; i < dynamicChain.Length; ++i)
        {
            var dc = dynamicChain[i];
            var bonePose = animStream.GetWorldPose(boneIndicesInRig[i]);
            var parentBonePose = animStream.GetWorldPose(boneIndicesInRig[dc.parentIndex]);
            var refDeltaPos = parentBonePose.pos - bonePose.pos;
            var refDeltaPosLen = math.length(refDeltaPos);
            
            var curDeltaPos = dynamicChain[dc.parentIndex].position - dc.position;
            var curDeltaPosLenSq = math.lengthsq(curDeltaPos);
            
            if (curDeltaPosLenSq > 0)
            {
                var curDeltaPosLen = math.sqrt(curDeltaPosLenSq);
                var slm = math.rcp(curDeltaPosLen);
                dc.position += curDeltaPos * slm * (curDeltaPosLen - refDeltaPosLen);
                dynamicChain[i] = dc;
            }
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////

    void Elasticity(DynamicBoneChainComponent dbcc, NativeArray<DynamicBoneChainNode> dynamicChain, Span<int> boneIndicesInRig, AnimationStream animStream)
    {
        for (var i = 1; i < dynamicChain.Length; ++i)
        {
            var dc = dynamicChain[i];
            var targetPos = ComputeRigidPos(i, dynamicChain, boneIndicesInRig, animStream);
            var delta = targetPos - dc.position;
            dc.position += delta * dbcc.elasticity;
            dc.position += Stiffness(targetPos, dc.position, dbcc.stiffness);
            dynamicChain[i] = dc;
        }
    }

/////////////////////////////////////////////////////////////////////////////////

    float3 Stiffness(float3 rigidPos, float3 simPos, float stiffnessFactor)
    {
        if (stiffnessFactor <= 0)
            return float3.zero;
        
        var delta = rigidPos - simPos;
        var deltaLengthSq = math.lengthsq(delta);
        var deltaLength = math.sqrt(deltaLengthSq);
        
        if (deltaLengthSq < math.EPSILON)
            return float3.zero;
        
        var rcpDeltaLength = math.rcp(deltaLength);
        var stiffness = (2 - 2 * stiffnessFactor) * deltaLength;
        var dl = math.max(deltaLength - stiffness, 0);
        var rv = dl * delta * rcpDeltaLength;
        return rv;
    }

/////////////////////////////////////////////////////////////////////////////////

    float3 ComputeRigidPos(int boneIndex, NativeArray<DynamicBoneChainNode> dynamicChain, Span<int> boneIndicesInRig, AnimationStream animStream)
    {
        var dc1 = dynamicChain[boneIndex];
        var dc0 = dynamicChain[dc1.parentIndex];
        var wpp0 = animStream.GetWorldPose(boneIndicesInRig[dc1.parentIndex]);
        var lpp1 = animStream.GetLocalPosition(boneIndicesInRig[boneIndex]);
        wpp0.pos = dc0.position;
        
        var rv = BoneTransform.TransformPoint(wpp0, lpp1);
        return rv;
    }

/////////////////////////////////////////////////////////////////////////////////
    
    void MakeChainRotations(NativeArray<DynamicBoneChainNode> dynamicChain, Span<int> boneIndicesInRig, AnimationStream animStream)
    {
        for (var i = 2; i < dynamicChain.Length; ++i)
        {
            var dc1 = dynamicChain[i];
            var dc0 = dynamicChain[dc1.parentIndex];
            
            var dv = dc1.position - dc0.position;
            var ndv = math.normalizesafe(dv);
            var wp0 = animStream.GetWorldPose(boneIndicesInRig[dc1.parentIndex]);
            var lp0 = animStream.GetLocalPose(boneIndicesInRig[dc1.parentIndex]);
            var wf0 = math.rotate(wp0.rot, lp0.pos);
            var nwf0 = math.normalizesafe(wf0);
            var q = MathUtils.FromToRotationForNormalizedVectors(nwf0, ndv);
            var newRot = math.mul(q, wp0.rot);
            animStream.SetWorldRotation(boneIndicesInRig[dc1.parentIndex], newRot);
        }
    }
}

/////////////////////////////////////////////////////////////////////////////////

[BurstCompile]
public void OnCreate(ref SystemState ss)
{
    var q = SystemAPI.QueryBuilder()
        .WithAll<DynamicBoneChainComponent, AnimatorEntityRefComponent, DynamicBoneChainNode>()
        .Build();
    
    ss.RequireForUpdate(q);
}

/////////////////////////////////////////////////////////////////////////////////

[BurstCompile]
public void OnUpdate(ref SystemState ss)
{
    var rigDefLookup = SystemAPI.GetComponentLookup<RigDefinitionComponent>(true);
    var aerLookup = SystemAPI.GetComponentLookup<AnimatorEntityRefComponent>(true);
    var ltLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
    var parentLookup = SystemAPI.GetComponentLookup<Parent>(true);
    ref var runtimeData = ref SystemAPI.GetSingletonRW<RuntimeAnimationData>().ValueRW;
    
    var ikJob = new DynamicBoneChainVerletSolverJob()
    {
        runtimeData = runtimeData,
        rigDefLookup = rigDefLookup,
        boneEntityRefLookup = aerLookup,
        parentLookup = parentLookup,
        localTransformLookup = ltLookup,
        deltaTime = SystemAPI.Time.DeltaTime
    };

    ikJob.ScheduleParallel();
}
}
}
