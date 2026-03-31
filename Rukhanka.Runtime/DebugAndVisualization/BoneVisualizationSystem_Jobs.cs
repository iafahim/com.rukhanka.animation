#if RUKHANKA_DEBUG_INFO

using Rukhanka.DebugDrawer;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
partial class BoneVisualizationSystem
{
[BurstCompile]
partial struct RenderBonesCPUAnimatorsJob: IJobEntity
{
	[ReadOnly]
	public NativeList<BoneTransform> bonePoses;

    public uint colorTriangles;
    public uint colorLines;
    
    public Drawer drawer;
    
/////////////////////////////////////////////////////////////////////////////////

    public void Execute(Entity e, in RigDefinitionComponent rd, in BoneVisualizationComponent bvc)
    {
        var bt = RuntimeAnimationData.GetAnimationDataForRigRO(bonePoses, rd);

        var len = bt.Length;
        
        for (int l = rd.rigBlob.Value.rootBoneIndex; l < len; ++l)
        {
            ref var rb = ref rd.rigBlob.Value.bones[l];

            if (rb.parentBoneIndex < 0)
                continue;

            var bonePose0 = bt[l];
            var bonePose1 = bt[rb.parentBoneIndex];

            if (math.any(math.abs(bonePose0.pos - bonePose1.pos)))
            {
                drawer.DrawBoneMesh(bonePose0.pos, bonePose1.pos, colorTriangles, colorLines);
                if (bvc.tripodSize > 0)
                {
                    var fwd = math.rotate(bonePose0.rot, math.forward()) * bvc.tripodSize;
                    var left = math.rotate(bonePose0.rot, math.up()) * bvc.tripodSize;
                    var up = math.rotate(bonePose0.rot, math.left()) * bvc.tripodSize;
                    drawer.DrawLine(bonePose0.pos, bonePose0.pos + fwd, 0x0000ffff);
                    drawer.DrawLine(bonePose0.pos, bonePose0.pos + up, 0x00ff00ff);
                    drawer.DrawLine(bonePose0.pos, bonePose0.pos + left, 0xff0000ff);
                }
            }
        }
    }
}

//------------------------------------------------------------------------------//

[BurstCompile]
struct PrepareGPURigsJob: IJobChunk
{
    [ReadOnly]
    public ComponentLookup<LocalTransform> localTransformLookup;
    [ReadOnly]
    public ComponentLookup<BoneVisualizationComponent> boneVisualizationLookup;
    [ReadOnly]
    public ComponentTypeHandle<GPURigFrameOffsetsComponent> gpuFrameOffsetsTypeHandle;
    [ReadOnly]
    public EntityTypeHandle entityTypeHandle;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<GPUBoneRendererRigInfo> frameRigData;
    
/////////////////////////////////////////////////////////////////////////////////

	public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
	{
		var entityFrameOffsets = chunk.GetNativeArray(ref gpuFrameOffsetsTypeHandle);
		var chunkFrameOffsets = chunk.GetChunkComponentData(ref gpuFrameOffsetsTypeHandle);
		var entities = chunk.GetNativeArray(entityTypeHandle);
        
		var cee = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
		while (cee.NextEntityIndex(out var i))
        {
            var e = entities[i];
            var gpuFrameOffsets = entityFrameOffsets[i];
            gpuFrameOffsets.AddOffsets(chunkFrameOffsets);
            
            var rigInfo = new GPUBoneRendererRigInfo();
            if (boneVisualizationLookup.HasComponent(e))
            {
                localTransformLookup.TryGetComponent(e, out rigInfo.rigWorldPose);
            }
            frameRigData[gpuFrameOffsets.rigIndex] = rigInfo;
        }
    }
}
}
}
#endif
