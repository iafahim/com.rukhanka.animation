using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Assertions;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public struct AnimationStream: IDisposable
{
    public DynamicFrameData rigFrameData;
    public RuntimeAnimationData runtimeData;
    public BlobAssetReference<RigDefinitionBlob> rigBlob;
    public NativeBitArray worldPoseDirtyFlags;
    
/////////////////////////////////////////////////////////////////////////////////

    public static AnimationStream Create(RuntimeAnimationData rd, in RigDefinitionComponent rdc)
    {
        var rv = new AnimationStream()
        {
            rigFrameData = rdc.dynamicFrameData,
            runtimeData = rd,
            rigBlob = rdc.rigBlob,
            worldPoseDirtyFlags = new NativeBitArray(rdc.dynamicFrameData.rigBoneCount, Allocator.Temp)
        };
        
        return rv;
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        RebuildOutdatedBonePoses(-1);
    }

/////////////////////////////////////////////////////////////////////////////////

    public BoneTransform GetLocalPose(int boneIndex)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return BoneTransform.Identity();
        
        return runtimeData.animatedBonesBuffer[rigFrameData.bonePoseOffset + boneIndex];
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public float3 GetLocalPosition(int boneIndex) => GetLocalPose(boneIndex).pos;
    public quaternion GetLocalRotation(int boneIndex) => GetLocalPose(boneIndex).rot;

/////////////////////////////////////////////////////////////////////////////////

    public BoneTransform GetWorldPose(int boneIndex)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return BoneTransform.Identity();
        
        var isWorldPoseDirty = worldPoseDirtyFlags.IsSet(boneIndex);
        if (isWorldPoseDirty)
            RebuildOutdatedBonePoses(boneIndex);
        
        return runtimeData.worldSpaceBonesBuffer[rigFrameData.bonePoseOffset + boneIndex];   
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public float3 GetWorldPosition(int boneIndex) => GetWorldPose(boneIndex).pos;
    public quaternion GetWorldRotation(int boneIndex) => GetWorldPose(boneIndex).rot;
    
/////////////////////////////////////////////////////////////////////////////////

    BoneTransform GetParentBoneWorldPose(int boneIndex)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return BoneTransform.Identity();
        
        var parentBoneIndex = rigBlob.Value.bones[boneIndex].parentBoneIndex;
        var parentWorldPose = BoneTransform.Identity();
        if (parentBoneIndex >= 0)
        {
            if (worldPoseDirtyFlags.IsSet(parentBoneIndex))
                RebuildOutdatedBonePoses(parentBoneIndex);
            parentWorldPose = runtimeData.worldSpaceBonesBuffer[parentBoneIndex + rigFrameData.bonePoseOffset];
        }

        return parentWorldPose;
    }

/////////////////////////////////////////////////////////////////////////////////

    public void SetWorldPose(int boneIndex, in BoneTransform bt)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return;
        
        var absBoneIndex = rigFrameData.bonePoseOffset + boneIndex;
        runtimeData.worldSpaceBonesBuffer[absBoneIndex] = bt;
        
        var parentWorldPose = GetParentBoneWorldPose(boneIndex);
        
        ref var boneLocalPose = ref runtimeData.animatedBonesBuffer.ElementAt(absBoneIndex);
        boneLocalPose = BoneTransform.Multiply(BoneTransform.Inverse(parentWorldPose), bt);
        
        MarkChildrenWorldPosesAsDirty(boneIndex);
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public void SetWorldPosition(int boneIndex, float3 pos)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return;
        
        var curPose = GetWorldPose(boneIndex);
        curPose.pos = pos;
        SetWorldPose(boneIndex, curPose);
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public void SetWorldRotation(int boneIndex, quaternion rot)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return;
        
        var absBoneIndex = rigFrameData.bonePoseOffset + boneIndex;
        ref var boneWorldPose = ref runtimeData.worldSpaceBonesBuffer.ElementAt(absBoneIndex);
        boneWorldPose.rot = rot;
        
        var parentWorldRot = GetParentBoneWorldPose(boneIndex).rot;

        ref var boneLocalPose = ref runtimeData.animatedBonesBuffer.ElementAt(absBoneIndex);
        boneLocalPose.rot = math.mul(math.inverse(parentWorldRot), boneWorldPose.rot);
        
        MarkChildrenWorldPosesAsDirty(boneIndex);
    }

/////////////////////////////////////////////////////////////////////////////////

    public void SetLocalPose(int boneIndex, in BoneTransform bt)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return;
        
        var absBoneIndex = rigFrameData.bonePoseOffset + boneIndex;
        runtimeData.animatedBonesBuffer[absBoneIndex] = bt;
        MarkChildrenWorldPosesAsDirty(boneIndex);
        worldPoseDirtyFlags.Set(boneIndex, true);
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public void SetLocalPosition(int boneIndex, float3 pos)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return;
        
        var absBoneIndex = rigFrameData.bonePoseOffset + boneIndex;
        ref var curPose = ref runtimeData.animatedBonesBuffer.ElementAt(absBoneIndex);
        curPose.pos = pos;
        MarkChildrenWorldPosesAsDirty(boneIndex);
        worldPoseDirtyFlags.Set(boneIndex, true);
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public void SetLocalRotation(int boneIndex, quaternion rot)
    {
        if (boneIndex >= rigFrameData.rigBoneCount)
            return;
        
        var absBoneIndex = rigFrameData.bonePoseOffset + boneIndex;
        ref var curPose = ref runtimeData.animatedBonesBuffer.ElementAt(absBoneIndex);
        curPose.rot = rot;
        MarkChildrenWorldPosesAsDirty(boneIndex);
        worldPoseDirtyFlags.Set(boneIndex, true);
    }
    
/////////////////////////////////////////////////////////////////////////////////

    void MarkChildrenWorldPosesAsDirty(int rootBoneIndex)
    {
        for (var i = rootBoneIndex + 1; i < rigFrameData.rigBoneCount; ++i)
        {
            ref var bone = ref rigBlob.Value.bones[i];
            if (bone.parentBoneIndex == rootBoneIndex)
            {
                worldPoseDirtyFlags.Set(i, true);
                MarkChildrenWorldPosesAsDirty(i);
            }
        }
    }

/////////////////////////////////////////////////////////////////////////////////

    void RebuildOutdatedBonePoses(int interestedBoneIndex)
    {
        if (rigFrameData.rigBoneCount < 0)
            return;
        
        var endBoneIndex = math.select(rigFrameData.rigBoneCount - 1, interestedBoneIndex, interestedBoneIndex >= 0);
        endBoneIndex = math.min(endBoneIndex, rigFrameData.rigBoneCount);
        for (var i = 0; i <= endBoneIndex; ++i)
        {
            var isWorldPoseDirty = worldPoseDirtyFlags.IsSet(i);
            if (!isWorldPoseDirty)
                continue;
            
            var absBoneIndex = rigFrameData.bonePoseOffset + i;
            ref var rigBone = ref rigBlob.Value.bones[i];
            var boneLocalPose = runtimeData.animatedBonesBuffer[absBoneIndex];

            var parentBoneWorldPose = BoneTransform.Identity();
            if (rigBone.parentBoneIndex >= 0)
            {
                parentBoneWorldPose = runtimeData.worldSpaceBonesBuffer[rigFrameData.bonePoseOffset + rigBone.parentBoneIndex];
            }

            var worldPose = BoneTransform.Multiply(parentBoneWorldPose, boneLocalPose);
            runtimeData.worldSpaceBonesBuffer[absBoneIndex] = worldPose;
        }
        worldPoseDirtyFlags.SetBits(0, false, endBoneIndex + 1);
    }

/////////////////////////////////////////////////////////////////////////////////

    public AnimationTransformFlags GetAnimationTransformFlagsRO()
    {
        return AnimationTransformFlags.CreateFromBufferRO(runtimeData.boneTransformFlagsHolderArr, rigFrameData.boneFlagsOffset, rigFrameData.rigBoneCount);
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public AnimationTransformFlags GetAnimationTransformFlagsRW()
    {
        return AnimationTransformFlags.CreateFromBufferRW(runtimeData.boneTransformFlagsHolderArr, rigFrameData.boneFlagsOffset, rigFrameData.rigBoneCount);
    }
}
}
