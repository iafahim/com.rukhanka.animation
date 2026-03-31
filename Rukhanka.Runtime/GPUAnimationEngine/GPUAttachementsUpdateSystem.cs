using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{

[WorldSystemFilter(WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.Default)]
[UpdateInGroup(typeof(RukhankaDeformationSystemGroup))]
[UpdateAfter(typeof(MeshDeformationSystem))]
partial class GPUAttachmentsUpdateSystem: SystemBase
{
    GraphicsBuffer dummyGB;
    EntityQuery gpuAttachmentsQuery;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override void OnCreate()
    {
        gpuAttachmentsQuery = SystemAPI.QueryBuilder()
            .WithAll<GPUAttachmentBoneIndexMPComponent, GPUAttachmentComponent>()
            .Build();
        RequireForUpdate(gpuAttachmentsQuery);
        
        //  Make small dummy bone transform buffer to prevent "attempted to draw with missing bindings" warnings and missed meshes for GPU attachments in edit mode
		dummyGB = new (GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None, 1, 4);
		Shader.SetGlobalBuffer(GPUAnimationSystem.ShaderID_rigSpaceBoneTransformsBuf, dummyGB);
		Shader.SetGlobalBuffer(GPUAnimationSystem.ShaderID_boneLocalTransforms, dummyGB);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override void OnDestroy()
    {
        dummyGB.Dispose();
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override void OnUpdate()
    {
        var attachmentBoneIndexUpdateJob = new UpdateGPUAttachmentBoneIndexJob()
        {
            frameOffsetsLookup = SystemAPI.GetComponentLookup<GPURigFrameOffsetsComponent>(true),
            localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            parentLookup = SystemAPI.GetComponentLookup<Parent>(true),
            postTransformMatrixLookup = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
            rigBoneRefLookup = SystemAPI.GetComponentLookup<AnimatorEntityRefComponent>(true),
            gpuAttachmentTypeHandle = SystemAPI.GetComponentTypeHandle<GPUAttachmentComponent>(true),
            gpuAttachmentBoneIndexTypeHandle = SystemAPI.GetComponentTypeHandle<GPUAttachmentBoneIndexMPComponent>(),
            gpuAttachmentToBoneTransformTypeHandle = SystemAPI.GetComponentTypeHandle<GPUAttachmentToBoneTransformMPComponent>(),
            gpuRigEntityLocalToWorldTypeHandle = SystemAPI.GetComponentTypeHandle<GPURigEntityLocalToWorldMPComponent>(),
            gpuRigFrameOffsetsTypeHandle = SystemAPI.GetComponentTypeHandle<GPURigFrameOffsetsComponent>(true),
            gpuEngineTagLookup = SystemAPI.GetComponentLookup<GPUAnimationEngineTag>(true),
            entityTypeHandle = SystemAPI.GetEntityTypeHandle()
        };
        var attachmentBoneIndexUpdateJH = attachmentBoneIndexUpdateJob.ScheduleParallel(gpuAttachmentsQuery, Dependency);
        Dependency = attachmentBoneIndexUpdateJH;
        Dependency.Complete();
    }
}
}
