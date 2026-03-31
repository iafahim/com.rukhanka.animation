using Rukhanka.Toolbox;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using Hash128 = Unity.Entities.Hash128;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public partial class GPUAnimationSystem
{
[BurstCompile]
struct CopyNewAnimationsToGPUJob: IJobFor
{
	[ReadOnly]
	public NativeList<GPUAnimationClipPlacementData> newAnimationClips;
	public ThreadedSparseUploader gpuAnimationClips;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(int index)
	{
		var animationClip = newAnimationClips[index];
		ref var ac = ref animationClip.animationClipBlob.Value;
		
		var gpuAnimClipData = new GPUStructures.AnimationClip()
		{
			flags = ac.flags,
			hash = ac.hash.Value,
			length = ac.length,
			looped = ac.looped,
			cycleOffset = ac.cycleOffset,
		};
		
		SetPerfectHashTableData(ref animationClip.animationClipBlob.Value.clipTracks, animationClip.clipTracks, ref gpuAnimClipData.clipTracks);
		SetPerfectHashTableData(ref animationClip.animationClipBlob.Value.additiveReferencePoseFrame, animationClip.additiveRefPoseTracks, ref gpuAnimClipData.additiveReferencePoseTracks);
		
		var absOffset = animationClip.animationClipDataOffset;
		gpuAnimationClips.AddUpload(gpuAnimClipData, absOffset);
		
		UploadTrackSet(absOffset, animationClip.clipTracks, ref animationClip.animationClipBlob.Value.clipTracks);
		UploadTrackSet(absOffset, animationClip.additiveRefPoseTracks, ref animationClip.animationClipBlob.Value.additiveReferencePoseFrame);
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void SetPerfectHashTableData(ref TrackSet ts, in GPUTrackSetPlacementData tspd, ref GPUStructures.TrackSet gpuTrackSet)
	{
		gpuTrackSet = tspd.ToGPUTrackSet();
		gpuTrackSet.trackGroupPHTSeed = ts.trackGroupPHT.seed;
		gpuTrackSet.trackGroupPHTSizeMask = (uint)ts.trackGroupPHT.pht.Length - 1;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	unsafe void UploadTrackSet(int baseOffset, in GPUTrackSetPlacementData tspd, ref TrackSet ts)
	{
		if (ts.keyframes.Length == 0)
			return;
		
		gpuAnimationClips.AddUpload(ts.keyframes.GetUnsafePtr(), ts.keyframes.Length * UnsafeUtility.SizeOf<GPUStructures.KeyFrame>(), baseOffset + tspd.keyFramesOffset);	
		gpuAnimationClips.AddUpload(ts.trackGroups.GetUnsafePtr(), ts.trackGroups.Length * UnsafeUtility.SizeOf<int>(), baseOffset + tspd.trackGroupsOffset);
		gpuAnimationClips.AddUpload(ts.trackGroupPHT.pht.GetUnsafePtr(), ts.trackGroupPHT.pht.Length * UnsafeUtility.SizeOf<int2>(), baseOffset + tspd.hashTableOffset);
		
	#if RUKHANKA_DEBUG_INFO
		//	Must copy because debug tracks contain name field
		var tmpArr = new NativeArray<GPUStructures.Track>(ts.tracks.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
		for (var i = 0; i < tmpArr.Length; ++i)
		{
			var it = ts.tracks[i];
			var t = new GPUStructures.Track()
			{
				props = it.props,
				keyFrameRange = it.keyFrameRange
			};
			tmpArr[i] = t;
		}
		gpuAnimationClips.AddUpload(tmpArr, baseOffset + tspd.tracksOffset);	
	#else
		gpuAnimationClips.AddUpload(ts.tracks.GetUnsafePtr(), ts.tracks.Length * UnsafeUtility.SizeOf<GPUStructures.Track>(), baseOffset + tspd.tracksOffset);	
	#endif
		
	}
}
	
//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
struct CopyNewAvatarMasksToGPUJob: IJobFor
{
	[ReadOnly]
	public NativeList<GPUAvatarMaskPlacementData> newAvatarMasks;
	public ThreadedSparseUploader gpuAvatarMasks;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public unsafe void Execute(int index)
	{
		var am = newAvatarMasks[index];
		
		var dstOffset = am.dataOffset * UnsafeUtility.SizeOf<uint>();
		gpuAvatarMasks.AddUpload(am.avatarMaskBlob.Value.humanBodyPartsAvatarMask, dstOffset);
		
		ref var bm = ref am.avatarMaskBlob.Value.includedBoneMask;
		var srcPtr = bm.GetUnsafePtr();
		var srcSize = bm.Length * UnsafeUtility.SizeOf<uint>();
		gpuAvatarMasks.AddUpload(srcPtr, srcSize, dstOffset + 4);
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
struct CopyNewRigsToGPUJob: IJobFor
{
	[ReadOnly]
	public NativeList<GPURigDefinitionPlacementData> newRigs;
	public ThreadedSparseUploader gpuRigDefs;
	public ThreadedSparseUploader gpuRigBones;
	public ThreadedSparseUploader gpuHumanRotationData;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public unsafe void Execute(int index)
	{
		var rig = newRigs[index];
		ref var rdb = ref rig.rigBlob.Value;
		
	#if RUKHANKA_DEBUG_INFO
		for (var i = 0; i < rdb.bones.Length; ++i)
		{
			ref var b = ref rdb.bones[i];
			var grp = new GPUStructures.BoneTransform()
			{
				pos	= b.refPose.pos,
				rot = b.refPose.rot.value,
				scale = b.refPose.scale
			};
			
			var rb = new GPUStructures.RigBone()
			{
				hash = b.hash,
				refPose = grp,
				humanBodyPart = (int)b.humanBodyPart,
				parentBoneIndex = b.parentBoneIndex
			};
			var rigBoneIndex = rig.rigBonesOffset + i;
			var dstRigBoneOffsetInBytes = rigBoneIndex * UnsafeUtility.SizeOf<GPUStructures.RigBone>();
			gpuRigBones.AddUpload(rb, dstRigBoneOffsetInBytes);
		}
	#else
		var srcPtr = rdb.bones.GetUnsafePtr();
		var srcOffset = rig.rigBonesOffset * UnsafeUtility.SizeOf<GPUStructures.RigBone>();
		var srcSize = rdb.bones.Length * UnsafeUtility.SizeOf<GPUStructures.RigBone>();
		gpuRigBones.AddUpload(srcPtr, srcSize, srcOffset);
	#endif
		
		if (rig.humanRotationDataOffset >= 0)
		{
			BurstAssert.IsTrue(UnsafeUtility.SizeOf<GPUStructures.HumanRotationData>() == UnsafeUtility.SizeOf<HumanRotationData>(), "Size must match");
			BurstAssert.IsTrue(rig.rigBlob.Value.bones.Length == rig.rigBlob.Value.humanData.Value.humanRotData.Length, "Count must match");
			var srcHRDPtr = rdb.humanData.Value.humanRotData.GetUnsafePtr();
			var srcHRDOffset = rig.humanRotationDataOffset * UnsafeUtility.SizeOf<GPUStructures.HumanRotationData>();
			var srcHRDSize = rdb.humanData.Value.humanRotData.Length * UnsafeUtility.SizeOf<GPUStructures.HumanRotationData>();
			gpuHumanRotationData.AddUpload(srcHRDPtr, srcHRDSize, srcHRDOffset);
		}
		
		var gpuRig = new GPUStructures.RigDefinition()
		{
			hash = rdb.hash.Value,
			rigBonesRange = new int2(rig.rigBonesOffset, rdb.bones.Length),
			rootBoneIndex = rdb.rootBoneIndex,
			humanRotationDataOffset = rig.humanRotationDataOffset
		};
		var dstRigDefOffsetInBytes = rig.rigDefinitionIndex * UnsafeUtility.SizeOf<GPUStructures.RigDefinition>();
		gpuRigDefs.AddUpload(gpuRig, dstRigDefOffsetInBytes);
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
struct CopyNewSkinnedMeshesDataJob: IJobFor
{
	[ReadOnly]
	public NativeList<GPUSkinnedMeshPlacementData> newSkinnedMeshData;
	public ThreadedSparseUploader gpuSkinnedMeshBonesData;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(int index)
	{
		var brt = newSkinnedMeshData[index];
		BurstAssert.IsTrue
		(
			brt.skinnedMeshInfo.Value.bones.Length == brt.boneRemapTableBlob.Value.remapIndices.Length,
			"Skinned mesh bones count should be equal as remap indices count for this mesh"
		);
		
		for (var i = 0; i < brt.skinnedMeshInfo.Value.bones.Length; ++i)
		{
			ref var bi = ref brt.skinnedMeshInfo.Value.bones[i];
			var smbd = new GPUStructures.SkinnedMeshBoneData()
			{
				boneRemapIndex = brt.boneRemapTableBlob.Value.remapIndices[i]
			};
			//	Last column of bind pose is (0, 0, 0, 1). Do not store it
			smbd.bindPose.c0 = bi.bindPose.c0.xyz;
			smbd.bindPose.c1 = bi.bindPose.c1.xyz;
			smbd.bindPose.c2 = bi.bindPose.c2.xyz;
			smbd.bindPose.c3 = bi.bindPose.c3.xyz;
			
			var dstOffsetInBytes = (brt.dataOffset + i) * UnsafeUtility.SizeOf<GPUStructures.SkinnedMeshBoneData>();
			gpuSkinnedMeshBonesData.AddUpload(smbd, dstOffsetInBytes);
		}
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
struct FillFrameAnimatedRigWorkloadBuffersJob: IJobChunk
{
	[NativeDisableParallelForRestriction]
	public NativeArray<GPUStructures.AnimatedBoneWorkload> animatedBonesWorkloadBuf;
	[NativeDisableParallelForRestriction]
	public NativeArray<GPUStructures.AnimationJob> frameRigAnimationJobs;
	[NativeDisableParallelForRestriction]
	public NativeArray<GPUStructures.AnimationToProcess> animationToProcessBuf;
	
	[ReadOnly]
	public NativeParallelHashMap<Hash128, int> animationClipsOffsets;
	[ReadOnly]
	public NativeParallelHashMap<Hash128, int> avatarMasksOffsets;
	[ReadOnly]
	public NativeParallelHashMap<Hash128, int> rigDefinitionOffsets;
	
	[ReadOnly]
	public ComponentTypeHandle<RigDefinitionComponent> rigDefComponentTypeHandle;
	[ReadOnly]
	public BufferTypeHandle<AnimationToProcessComponent> atpBufTypeHandle;
	[ReadOnly]
	public EntityTypeHandle entityTypeHandle;
	[ReadOnly]
	public ComponentTypeHandle<GPURigFrameOffsetsComponent> frameOffsetsTypeHandle;
		
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
	{
		var rigDefs = chunk.GetNativeArray(ref rigDefComponentTypeHandle);
		var atpBufs = chunk.GetBufferAccessorRO(ref atpBufTypeHandle);
		var frameOffsets = chunk.GetNativeArray(ref frameOffsetsTypeHandle);
		var entities = chunk.GetNativeArray(entityTypeHandle);
		var frameOffsetsInChunk = chunk.GetChunkComponentData(ref frameOffsetsTypeHandle);
		
		var cee = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
		while (cee.NextEntityIndex(out var i))
		{
			var rdc = rigDefs[i];
			var atps = atpBufs[i];
			var e = entities[i];
			var frameOffset = frameOffsets[i];
			frameOffset.AddOffsets(frameOffsetsInChunk);
			Execute(e, rdc, atps, frameOffset);
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(Entity e, in RigDefinitionComponent rdc, in DynamicBuffer<AnimationToProcessComponent> atps, in GPURigFrameOffsetsComponent frameOffsets)
	{
		var abCount = rdc.rigBlob.Value.bones.Length;
		var atpCount = atps.Length;
		
		for (var i = 0; i < atpCount; ++i)
		{
			var atp = atps[i];
			if (!atp.animation.IsCreated)
				continue;
			
			var gpuAtp = new GPUStructures.AnimationToProcess()
			{
				time = atp.time,
				weight = atp.weight,
				animationClipAddress = animationClipsOffsets[atp.animation.Value.hash],
				blendMode = (int)atp.blendMode,
				layerIndex = atp.layerIndex,
				layerWeight = atp.layerWeight,
				avatarMaskDataOffset = atp.avatarMask.IsCreated ? avatarMasksOffsets[atp.avatarMask.Value.hash] : -1
			};
			
			var idx = i + frameOffsets.animationToProcessIndex;
			animationToProcessBuf[idx] = gpuAtp;
		}
		
		if (!rigDefinitionOffsets.TryGetValue(rdc.rigBlob.Value.hash, out var rigDefIndex))
		{
			return;
		}
		
		var faj = new GPUStructures.AnimationJob()
		{
			rigDefinitionIndex = rigDefIndex,
			animatedBoneIndexOffset = frameOffsets.boneIndex,
			animationsToProcessRange = new int2(frameOffsets.animationToProcessIndex, atpCount)
		};
		frameRigAnimationJobs[frameOffsets.rigIndex] = faj;
		
		var abw = new GPUStructures.AnimatedBoneWorkload()
		{
			animationJobIndex = frameOffsets.rigIndex,
		};
		
		for (var i = 0; i < abCount; ++i)
		{
			abw.boneIndexInRig = i;
			animatedBonesWorkloadBuf[frameOffsets.boneIndex + i] = abw;
		}
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
struct FillFrameSkinMatrixWorkloadBuffersJob: IJobChunk
{
	[ReadOnly]
	public ComponentLookup<GPUAnimationEngineTag> gpuAnimationEngineTagLookup;
	[ReadOnly]
	public ComponentLookup<RigDefinitionComponent> rigDefLookup;
	[ReadOnly]
	public ComponentLookup<GPURigFrameOffsetsComponent> frameOffsetsLookup;
	[ReadOnly]
	public ComponentLookup<LocalToWorld> localToWorldLookup;
	[ReadOnly]
	public NativeParallelHashMap<Hash128, int> skinnedMeshDataMap;
	[ReadOnly]
	public NativeParallelHashMap<Entity, SkinnedMeshRendererFrameDeformationData> entityToSMRFrameDataMap;
	
	[NativeDisableParallelForRestriction]
	public NativeArray<GPUStructures.SkinnedMeshWorkload> frameSkinMatrixWorkloadBuf;
	[NativeDisableUnsafePtrRestriction]
	public UnsafeAtomicCounter32 frameSkinnedMeshesAtomicCounter;
		
	[ReadOnly]
	public ComponentTypeHandle<SkinnedMeshRendererComponent> smrTypeHandle;
	[ReadOnly]
	public ComponentTypeHandle<LocalToWorld> l2wTypeHandle;
	[ReadOnly]
	public EntityTypeHandle entityTypeHandle;
	[ReadOnly]
	public ComponentTypeHandle<GPURigFrameOffsetsComponent> gpuRigChunkDataTypeHandle;
		
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
	{
		var smrs = chunk.GetNativeArray(ref smrTypeHandle);
		var l2ws = chunk.GetNativeArray(ref l2wTypeHandle);
		var entities = chunk.GetNativeArray(entityTypeHandle);
		
		var cee = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
		while (cee.NextEntityIndex(out var i))
		{
			var smr = smrs[i];
			var l2w = l2ws[i];
			var e = entities[i];
			var rigChunkIndex = chunk.m_EntityComponentStore->GetChunk(smr.animatedRigEntity);
			var rigChunk = new ArchetypeChunk(rigChunkIndex, chunk.m_EntityComponentStore);
			Execute(e, smr, l2w, rigChunk);
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(Entity e, in SkinnedMeshRendererComponent smr, in LocalToWorld l2w, in ArchetypeChunk rigChunk)
	{
		if (!smr.IsGPUAnimator(gpuAnimationEngineTagLookup))
			return;
		
		if (!entityToSMRFrameDataMap.TryGetValue(e, out var skinMatrixData))
			return;
		
		if (!rigDefLookup.TryGetComponent(smr.animatedRigEntity, out var rigDef))
			return;
		
		var hash = AnimationUtils.CalculateBoneRemapTableHash(smr.smrInfoBlob, rigDef.rigBlob);
		
		if (!skinnedMeshDataMap.TryGetValue(hash, out var remapTableOffset))
		{
			BurstAssert.IsTrue(false, "Skinned mesh to rig remap table is not found");
			return;
		}
		
		var gpuRigFrameOffsets = rigChunk.GetChunkComponentData(ref gpuRigChunkDataTypeHandle);
		frameOffsetsLookup.TryGetComponent(smr.animatedRigEntity, out var frameOffsets);
		frameOffsets.AddOffsets(gpuRigFrameOffsets);
		
		var animatedEntityL2W = localToWorldLookup[smr.animatedRigEntity];
		var invAnimatedEntityPose = math.inverse(animatedEntityL2W.Value);
		var smrPose = l2w.Value;
		var rootBoneToEntityTransform = math.mul(invAnimatedEntityPose, smrPose);
		
		var smw = new GPUStructures.SkinnedMeshWorkload()
		{
			skinMatricesCount = smr.smrInfoBlob.Value.bones.Length,
			boneRemapTableIndex = remapTableOffset,
			skinMatrixBaseOutIndex = skinMatrixData.skinMatrixIndex,
			rootBoneIndex = smr.rootBoneIndexInRig,
			animatedBoneIndexOffset = frameOffsets.boneIndex,
			skinnedMeshInverseTransform = math.inverse(rootBoneToEntityTransform)
		};
		var index = frameSkinnedMeshesAtomicCounter.Add(1);
		frameSkinMatrixWorkloadBuf[index] = smw;
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
struct ComputeFrameRigWorkloadSizesPerChunkJob: IJobChunk
{
	[ReadOnly]
	public ComponentTypeHandle<RigDefinitionComponent> rigDefComponentTypeHandle;
	[ReadOnly]
	public BufferTypeHandle<AnimationToProcessComponent> atpBufTypeHandle;
	
	public ComponentTypeHandle<GPURigFrameOffsetsComponent> frameOffsetsTypeHandle;
		
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
	{
		var rigDefs = chunk.GetNativeArray(ref rigDefComponentTypeHandle);
		var atpBufs = chunk.GetBufferAccessorRO(ref atpBufTypeHandle);
		var frameOffsets = chunk.GetNativeArray(ref frameOffsetsTypeHandle);
	
		var gpuRigFrameOffsets = new GPURigFrameOffsetsComponent();
		
		var cee = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
		while (cee.NextEntityIndex(out var i))
		{
			var rdc = rigDefs[i];
			var atps = atpBufs[i];
			frameOffsets[i] = gpuRigFrameOffsets;
			
			gpuRigFrameOffsets.boneIndex += rdc.rigBlob.Value.bones.Length;
			gpuRigFrameOffsets.rigIndex += 1;
			gpuRigFrameOffsets.animationToProcessIndex += atps.Length;
		}
		chunk.SetChunkComponentData(ref frameOffsetsTypeHandle, gpuRigFrameOffsets);
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
unsafe struct ComputeFrameRigWorkloadSizesAbsChunkOffsetsJob: IJobChunk
{
	[NativeDisableUnsafePtrRestriction]
	public uint* frameAnimatedBonesCounter;
	[NativeDisableUnsafePtrRestriction]
	public uint* frameAnimationToProcessCounter;
	[NativeDisableUnsafePtrRestriction]
	public uint* frameAnimatedRigsCounter;
	
	public ComponentTypeHandle<GPURigFrameOffsetsComponent> gpuRigChunkDataTypeHandle;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
	{
		var gpuRigChunkData = chunk.GetChunkComponentData(ref gpuRigChunkDataTypeHandle);
		var gcd = gpuRigChunkData;
		
		gpuRigChunkData.boneIndex = (int)*frameAnimatedBonesCounter;
		gpuRigChunkData.animationToProcessIndex = (int)*frameAnimationToProcessCounter;
		gpuRigChunkData.rigIndex = (int)*frameAnimatedRigsCounter;
		
		*frameAnimatedBonesCounter += (uint)gcd.boneIndex;
		*frameAnimationToProcessCounter += (uint)gcd.animationToProcessIndex;;
		*frameAnimatedRigsCounter += (uint)gcd.rigIndex;
		
		chunk.SetChunkComponentData(ref gpuRigChunkDataTypeHandle, gpuRigChunkData);
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
partial struct ComputeFrameSkinnedMeshWorkloadSizesJob: IJobEntity
{
	[ReadOnly]
	public ComponentLookup<GPUAnimationEngineTag> gpuAnimationEngineTagLookup;
	
	[NativeDisableParallelForRestriction]
	public NativeList<uint> frameSkinnedMeshesPerThreadCounters;
		
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(in SkinnedMeshRendererComponent asmc)
	{
		if (!asmc.IsGPUAnimator(gpuAnimationEngineTagLookup))
			return;
		
		frameSkinnedMeshesPerThreadCounters[JobsUtility.ThreadIndex] += 1;
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
unsafe struct ComputeFrameSkinnedMeshWorkloadSizesTotalJob: IJob
{
	[ReadOnly]
	public NativeList<uint> frameSkinnedMeshesPerThreadCounters;
	
	[NativeDisableUnsafePtrRestriction]
	public uint* frameSkinnedMeshesCounter;
		
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute()
	{
		foreach (var c in frameSkinnedMeshesPerThreadCounters)
		{
			*frameSkinnedMeshesCounter += c;
		}
	}
}

//-----------------------------------------------------------------------------------------------------------------//

[BurstCompile]
struct ResetFrameDataJob: IJob
{
	[NativeDisableUnsafePtrRestriction]
	public UnsafeAtomicCounter32 frameAnimatedBonesCounter;
	[NativeDisableUnsafePtrRestriction]
	public UnsafeAtomicCounter32 frameAnimatedRigsCounter;
	[NativeDisableUnsafePtrRestriction]
	public UnsafeAtomicCounter32 frameAnimationToProcessCounter;
	[NativeDisableUnsafePtrRestriction]
	public UnsafeAtomicCounter32 frameSkinnedMeshesCounter;
	public NativeList<uint> frameSkinnedMeshesPerThreadCounters;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute()
	{
		frameAnimatedBonesCounter.Reset(0);
		frameAnimatedRigsCounter.Reset(0);
		frameSkinnedMeshesCounter.Reset(0);
		frameAnimationToProcessCounter.Reset(0);
		frameSkinnedMeshesPerThreadCounters.Clear();
		frameSkinnedMeshesPerThreadCounters.Resize(JobsUtility.ThreadIndexCount, NativeArrayOptions.ClearMemory);
	}
}
}
}
