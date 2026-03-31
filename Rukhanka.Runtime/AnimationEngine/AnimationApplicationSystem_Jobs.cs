using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using Hash128 = Unity.Entities.Hash128;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
partial struct AnimationApplicationSystem
{

//=================================================================================================================//

[BurstCompile]
partial struct ApplyAnimationToSkinnedMeshJob: IJobEntity
{
	[ReadOnly]
	public ComponentLookup<RigDefinitionComponent> rigDefinitionLookup;
	[ReadOnly]
	public ComponentLookup<CullAnimationsTag> cullAnimationsTagLookup;
	[ReadOnly]
	public ComponentLookup<Parent> parentLookup;
	[ReadOnly]
	public ComponentLookup<LocalTransform> localTransformLookup;
	[ReadOnly]
	public ComponentLookup<AnimatorEntityRefComponent> rigBoneLookup;
	[ReadOnly]
	public ComponentLookup<GPUAnimationEngineTag> gpuAnimationEngineTagLookup;
	[ReadOnly]
	public NativeList<BoneTransform> boneTransforms;
	[ReadOnly]
	public NativeParallelHashMap<Hash128, BlobAssetReference<BoneRemapTableBlob>> rigToSkinnedMeshRemapTables;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(Entity e, in SkinnedMeshRendererComponent smc, in LocalTransform lt, ref DynamicBuffer<SkinMatrix> outSkinMatricesBuf)
	{
		var rigEntity = smc.animatedRigEntity;
		
		if (cullAnimationsTagLookup.HasComponent(rigEntity) && cullAnimationsTagLookup.IsComponentEnabled(rigEntity))
			return;

		if (!rigDefinitionLookup.TryGetComponent(rigEntity, out var rigDef))
			return;
		
		if (smc.IsGPUAnimator(gpuAnimationEngineTagLookup))
			return;
		
		var boneDataOffset = rigDef.dynamicFrameData;
		ref var boneRemapTable = ref GetBoneRemapTable(smc.smrInfoBlob, rigDef.rigBlob);

		var absoluteBoneTransforms = RuntimeAnimationData.GetAnimationDataForRigRO(boneTransforms, boneDataOffset.bonePoseOffset, boneDataOffset.rigBoneCount);
		var skinMeshBonesInfo = smc.smrInfoBlob;
		var meshRenderToRigRootTransform = SkinnedMeshToRigRootTransform(e, smc.animatedRigEntity, lt, absoluteBoneTransforms);

		// Iterate over all skinned mesh bones and set skin matrix from corresponding animation pose
		for (int skinnedMeshBoneIndex = 0; skinnedMeshBoneIndex < skinMeshBonesInfo.Value.bones.Length; ++skinnedMeshBoneIndex)
		{
			var animationBoneIndex = boneRemapTable.remapIndices[skinnedMeshBoneIndex];

			//	Skip bone if it is not present in animation
			if (animationBoneIndex < 0)
				continue;

			var absBonePose = absoluteBoneTransforms[animationBoneIndex];
			absBonePose = BoneTransform.Multiply(meshRenderToRigRootTransform, absBonePose);
			var boneXForm = absBonePose.ToFloat4x4();

			ref var boneInfo = ref skinMeshBonesInfo.Value.bones[skinnedMeshBoneIndex];
			var skinMatrix = MakeSkinMatrixForBone(ref boneInfo, boneXForm);
			outSkinMatricesBuf[skinnedMeshBoneIndex] = skinMatrix;
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	BoneTransform SkinnedMeshToRigRootTransform(Entity e, Entity selfAnimatedEntity, in LocalTransform lt, ReadOnlySpan<BoneTransform> bonePoses)
	{
		var rv = BoneTransform.Identity();
		var parent = new Parent() { Value = e };
		do
		{
			e = parent.Value;
			if (rigBoneLookup.TryGetComponent(e, out var rb) && rb.animatorEntity == selfAnimatedEntity)
			{
				var absBonePose = bonePoses[rb.boneIndexInAnimationRig];
				rv = BoneTransform.Multiply(absBonePose, rv);
				break;
			}
			var entityLocalPose = new BoneTransform(localTransformLookup[e]);
			rv = BoneTransform.Multiply(entityLocalPose, rv);
		}
		while (parentLookup.TryGetComponent(e, out parent));
		return BoneTransform.Inverse(rv);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public static Hash128 CalculateBoneRemapTableHash(in BlobAssetReference<SkinnedMeshInfoBlob> skinnedMesh, in BlobAssetReference<RigDefinitionBlob> rigDef)
	{
		var rv = new Hash128(skinnedMesh.Value.hash.Value.x, skinnedMesh.Value.hash.Value.y, rigDef.Value.hash.Value.x, rigDef.Value.hash.Value.y);
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	ref BoneRemapTableBlob GetBoneRemapTable(in BlobAssetReference<SkinnedMeshInfoBlob> skinnedMesh, in BlobAssetReference<RigDefinitionBlob> rigDef)
	{
		var h = CalculateBoneRemapTableHash(skinnedMesh, rigDef);
		return ref rigToSkinnedMeshRemapTables[h].Value;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	SkinMatrix MakeSkinMatrixForBone(ref SkinnedMeshBoneInfo boneInfo, in float4x4 boneXForm)
	{
		var boneTransformMatrix = math.mul(boneXForm, boneInfo.bindPose);

		var skinMatrix = new SkinMatrix() { Value = new float3x4(boneTransformMatrix.c0.xyz, boneTransformMatrix.c1.xyz, boneTransformMatrix.c2.xyz, boneTransformMatrix.c3.xyz) };
		return skinMatrix;
	}

}

//=================================================================================================================//

[BurstCompile]
partial struct PropagateBoneTransformToEntityTRSJob: IJobEntity
{
	[ReadOnly]
	public NativeList<BoneTransform> boneTransforms;

	[NativeDisableParallelForRestriction]
	public ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;
	[ReadOnly]
	public ComponentLookup<RigDefinitionComponent> rigDefLookup;
	[ReadOnly]
	public ComponentLookup<GPUAnimationEngineTag> gpuEngineTagLookup;
		
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(Entity e, in AnimatorEntityRefComponent animatorRef, ref LocalTransform lt)
	{
		if (gpuEngineTagLookup.IsComponentEnabled(animatorRef.animatorEntity))
			return;
		
		var rigDefinition = rigDefLookup[animatorRef.animatorEntity];
		var boneData = RuntimeAnimationData.GetAnimationDataForRigRO(boneTransforms, rigDefinition);
		if (boneData.IsEmpty)
			return;
		
		if (animatorRef.boneIndexInAnimationRig >= boneData.Length)
			return;

		var boneTransform = boneData[animatorRef.boneIndexInAnimationRig];
		lt = boneTransform.ToLocalTransformComponent();
		if (postTransformMatrixLookup.HasComponent(e))
		{
			lt.Scale = 1;
			var ptm = float4x4.Scale(boneTransform.scale);
			postTransformMatrixLookup[e] = new PostTransformMatrix() { Value = ptm };
		}
	}
}

//=================================================================================================================//

[BurstCompile]
partial struct CountNumberOfNewRemapTablesJob: IJobEntity
{
	[ReadOnly]
	public ComponentLookup<RigDefinitionComponent> rigDefinitionArr;
	[ReadOnly]
	public NativeParallelHashMap<Hash128, BlobAssetReference<BoneRemapTableBlob>> rigToSkinnedMeshRemapTables;
	
	[NativeDisableUnsafePtrRestriction]
	public UnsafeAtomicCounter32 numberOfNewRemapTables;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(in SkinnedMeshRendererComponent asmc)
	{
		if (!rigDefinitionArr.TryGetComponent(asmc.animatedRigEntity, out var rigDef))
			return;
		
		var h = ApplyAnimationToSkinnedMeshJob.CalculateBoneRemapTableHash(asmc.smrInfoBlob, rigDef.rigBlob);
		if (!rigToSkinnedMeshRemapTables.ContainsKey(h))
			numberOfNewRemapTables.Add(1);
	}
}

//=================================================================================================================//

[BurstCompile]
unsafe struct IncreaseRigRemapTableCapacityJob: IJob
{
	public NativeParallelHashMap<Hash128, BlobAssetReference<BoneRemapTableBlob>> rigToSkinnedMeshRemapTables;
	[ReadOnly, NativeDisableUnsafePtrRestriction]
	public int *numberOfNewRemapTables;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute()
	{
		rigToSkinnedMeshRemapTables.Capacity += *numberOfNewRemapTables;
	}
}

//=================================================================================================================//

[BurstCompile]
unsafe partial struct FillRigToSkinBonesRemapTableCacheJob: IJobEntity
{
	[ReadOnly]
	public ComponentLookup<RigDefinitionComponent> rigDefinitionArr;
	public NativeParallelHashMap<Hash128, BlobAssetReference<BoneRemapTableBlob>>.ParallelWriter rigToSkinnedMeshRemapTables;
	[ReadOnly, NativeDisableUnsafePtrRestriction]
	public int *newRemapTablesCounter;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(in SkinnedMeshRendererComponent asmc)
	{
		if (*newRemapTablesCounter == 0 || !rigDefinitionArr.TryGetComponent(asmc.animatedRigEntity, out var rigDef))
			return;
		
		MakeRigToSkinnedMeshRemapTable(asmc, rigDef);
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void MakeRigToSkinnedMeshRemapTable(in SkinnedMeshRendererComponent sm, in RigDefinitionComponent rigDef)
	{
		//	Try cache first
		var h = ApplyAnimationToSkinnedMeshJob.CalculateBoneRemapTableHash(sm.smrInfoBlob, rigDef.rigBlob);
		if (UnsafeParallelHashMapBase<Hash128, BlobAssetReference<BoneRemapTableBlob>>
		    .TryGetFirstValueAtomic(rigToSkinnedMeshRemapTables.m_Writer.m_Buffer, h, out _, out _))
			return;

		//	Make new remap table
		var rv = AnimationUtils.MakeSkinnedMeshToRigRemapTable(sm, rigDef, Allocator.Persistent);
		if (!rigToSkinnedMeshRemapTables.TryAdd(h, rv))
			rv.Dispose();
	}
}

//=================================================================================================================//

[BurstCompile]
[WithAll(typeof(ShouldUpdateBoundingBoxTag))]
partial struct UpdateSkinnedMeshBoundsJob: IJobEntity
{
	[ReadOnly]
	public ComponentLookup<RigDefinitionComponent> rigDefLookup;
	[ReadOnly]
	public NativeList<BoneTransform> worldBonePoses;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(in SkinnedMeshRendererComponent asm, ref RenderBounds rbb)
	{
		var rigDefinition = rigDefLookup[asm.animatedRigEntity];
		var animationData = RuntimeAnimationData.GetAnimationDataForRigRO(worldBonePoses, rigDefinition);
		
		if (animationData.Length <= asm.rootBoneIndexInRig || asm.rootBoneIndexInRig < 0)
			return;
		
		//	Skinned mesh root bone world pose
		var rootPose = animationData[0];
		var invRootPose = BoneTransform.Inverse(rootPose);
		
		//	Loop over all bones and calculate extents in root bone space
		float3 minPt = float.MaxValue;
		float3 maxPt = float.MinValue;
		for (var i = asm.rootBoneIndexInRig + 1; i < animationData.Length; ++i)
		{
			var boneWorldPose = animationData[i];
			var rootBoneSpaceBonePose = BoneTransform.Multiply(invRootPose, boneWorldPose);
			minPt = math.min(minPt, rootBoneSpaceBonePose.pos);
			maxPt = math.max(maxPt, rootBoneSpaceBonePose.pos);
		}
		
		var aabb = new AABB()
		{
			Center = (minPt + maxPt) * 0.5f,
			Extents = (maxPt - minPt) * 0.5f,
		};
		
		//	Slightly extend result aabb to 'emulate' skin. Without proper per-vertex skin hull calculation this is reasonable approximation
		aabb.Extents *= 1.1f;
		rbb.Value = aabb;
	}
}
}
}
