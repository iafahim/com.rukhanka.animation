using Rukhanka.Toolbox;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using TransformHelpers = Unity.Transforms.TransformHelpers;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
partial class GPUAttachmentsUpdateSystem
{
[BurstCompile]
struct UpdateGPUAttachmentBoneIndexJob: IJobChunk
{
    struct ParentBoneInfo
    {
        public Entity boneEntity;
        public Entity rigEntity;
        public int boneIndexInRig;
        public float4x4 attachmentToBoneTransform;
    }
    
    [ReadOnly]
    public ComponentLookup<GPURigFrameOffsetsComponent> frameOffsetsLookup;
    [ReadOnly]
    public ComponentLookup<LocalTransform> localTransformLookup;
    [ReadOnly]
    public ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;
    [ReadOnly]
    public ComponentLookup<AnimatorEntityRefComponent> rigBoneRefLookup;
    [ReadOnly]
    public ComponentLookup<Parent> parentLookup;
    [ReadOnly]
    public ComponentTypeHandle<GPUAttachmentComponent> gpuAttachmentTypeHandle;
    [ReadOnly]
    public ComponentTypeHandle<GPURigFrameOffsetsComponent> gpuRigFrameOffsetsTypeHandle;
    [ReadOnly]
    public ComponentLookup<GPUAnimationEngineTag> gpuEngineTagLookup;
    [ReadOnly]
    public EntityTypeHandle entityTypeHandle;
    
    public ComponentTypeHandle<GPUAttachmentBoneIndexMPComponent> gpuAttachmentBoneIndexTypeHandle;
    public ComponentTypeHandle<GPUAttachmentToBoneTransformMPComponent> gpuAttachmentToBoneTransformTypeHandle;
    public ComponentTypeHandle<GPURigEntityLocalToWorldMPComponent> gpuRigEntityLocalToWorldTypeHandle;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
	{
		var gpuAttachments = chunk.GetNativeArray(ref gpuAttachmentTypeHandle);
		var gpuAttachmentBoneIndices = chunk.GetNativeArray(ref gpuAttachmentBoneIndexTypeHandle);
		var gpuAttachmentToBoneTransforms = chunk.GetNativeArray(ref gpuAttachmentToBoneTransformTypeHandle);
		var gpuRigEntityLocalToWorlds = chunk.GetNativeArray(ref gpuRigEntityLocalToWorldTypeHandle);
		var entities = chunk.GetNativeArray(entityTypeHandle);
        
		var cee = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
		while (cee.NextEntityIndex(out var i))
		{
			var gpuAttachment = gpuAttachments[i];
            var gpuAttachmentBoneIndex = gpuAttachmentBoneIndices[i];
            var gpuAttachmentToBoneTransform = gpuAttachmentToBoneTransforms[i];
            var gpuRigEntityToL2W = gpuRigEntityLocalToWorlds[i];
			var e = entities[i];
            
			Execute(e, ref gpuAttachmentBoneIndex, ref gpuAttachmentToBoneTransform, ref gpuRigEntityToL2W, gpuAttachment, chunk);
            
            gpuAttachmentBoneIndices[i] = gpuAttachmentBoneIndex;
            gpuAttachmentToBoneTransforms[i] = gpuAttachmentToBoneTransform;
            gpuRigEntityLocalToWorlds[i] = gpuRigEntityToL2W;
		}
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void Execute
    (
        Entity e,
        ref GPUAttachmentBoneIndexMPComponent abi,
        ref GPUAttachmentToBoneTransformMPComponent atbt,
        ref GPURigEntityLocalToWorldMPComponent rltw,
        in GPUAttachmentComponent ac,
        in ArchetypeChunk chunk
    )
    {
        abi.boneIndex = -1;
            
        var pbi = GetParentRigBoneEntity(e, ac);
        if (pbi.boneEntity == Entity.Null)
            return;
        
        //  Act as ordinary attachment in case of CPU animation engine
        if (!AnimationUtils.IsGPUAnimator(pbi.rigEntity, gpuEngineTagLookup))
            return;
        
        atbt.value = pbi.attachmentToBoneTransform;
        TransformHelpers.ComputeWorldTransformMatrix(pbi.rigEntity, out rltw.value, ref localTransformLookup, ref parentLookup, ref postTransformMatrixLookup);
        
        if (frameOffsetsLookup.TryGetComponent(pbi.rigEntity, out var boneOffset) &&
            EntityTools.TryGetChunkComponentData(chunk, pbi.rigEntity, ref gpuRigFrameOffsetsTypeHandle, out var chunkFrameOffsets))
        {
            boneOffset.AddOffsets(chunkFrameOffsets);
            abi.boneIndex = boneOffset.boneIndex + pbi.boneIndexInRig;
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    ParentBoneInfo GetParentRigBoneEntity(Entity e, GPUAttachmentComponent ac)
    {
        AnimatorEntityRefComponent rbr = default;
        float4x4 entityToBoneTransform = float4x4.identity;
        var parent = new Parent() { Value = e};
        
        do
        {
            if (!localTransformLookup.TryGetComponent(parent.Value, out var plt))
                break;
            if (rigBoneRefLookup.TryGetComponent(parent.Value, out rbr))
                break;
            
            var pltMat = plt.ToMatrix();
            if (Hint.Unlikely(postTransformMatrixLookup.TryGetComponent(parent.Value, out var postTransformMatrixComponent)))
            {
                pltMat = math.mul(pltMat, postTransformMatrixComponent.Value);
            }
            
            entityToBoneTransform = math.mul(pltMat, entityToBoneTransform);
        }
        while (parentLookup.TryGetComponent(parent.Value, out parent));
        
        var rv = new ParentBoneInfo();
        if (rbr.animatorEntity != Entity.Null)
        {
            rv.boneEntity = parent.Value;
            rv.rigEntity = rbr.animatorEntity;
            rv.boneIndexInRig = math.select(rbr.boneIndexInAnimationRig, ac.attachedBoneIndex, ac.attachedBoneIndex >= 0);
            rv.attachmentToBoneTransform = entityToBoneTransform;
        }
        
        return rv;
    }
}
}
}
