using Rukhanka.Toolbox;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
#if RUKHANKA_WITH_NETCODE
using Unity.NetCode;
#endif
using FixedStringName = Unity.Collections.FixedString512Bytes;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
[InternalBufferCapacity(4)]
[ChunkSerializable]
public struct AnimationToProcessComponent: IBufferElementData
{
	public float weight;
	public float time;
	public BlobAssetReference<AnimationClipBlob> animation;
	public BlobAssetReference<AvatarMaskBlob> avatarMask;
	public AnimationBlendingMode blendMode;
	public float layerWeight;
	public int layerIndex;
	public uint motionId;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct AnimatorEntityRefComponent: IComponentData
{
	public int boneIndexInAnimationRig;
	public Entity animatorEntity;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#if RUKHANKA_WITH_NETCODE
[GhostComponent(PrefabType = GhostPrefabType.Client)]
#endif
public struct SkinnedMeshRendererComponent: IComponentData
{
	public uint nameHash;
	public Entity animatedRigEntity;
	public int rootBoneIndexInRig;
	public BlobAssetReference<SkinnedMeshInfoBlob> smrInfoBlob;
	
	public bool IsGPUAnimator(in ComponentLookup<GPUAnimationEngineTag> gpuAnimationEngineTagLookup)
		=> AnimationUtils.IsGPUAnimator(animatedRigEntity, gpuAnimationEngineTagLookup);
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#if RUKHANKA_WITH_NETCODE
[GhostComponent(PrefabType = GhostPrefabType.Client)]
#endif
public struct ShouldUpdateBoundingBoxTag: IComponentData, IEnableableComponent { }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct RootMotionAnimationStateComponent: IBufferElementData, IEnableableComponent
{
	public uint uniqueMotionId;
	public BoneTransform animationState;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct RootMotionVelocityComponent: IComponentData
{
	public bool removeBuiltinEntityMovement;
	public float3 worldVelocity;
	public float3 deltaPos;
	public quaternion deltaRot;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[InternalBufferCapacity(4)]
public struct AnimationEventComponent: IBufferElementData, IEnableableComponent
{
	public AnimationEventComponent(ref AnimationEventBlob aeb)
	{
		nameHash = aeb.nameHash;
		floatParam = aeb.floatParam;
		intParam = aeb.intParam;
		stringParamHash = aeb.stringParamHash;
	#if RUKHANKA_DEBUG_INFO
		name = "";
		if (aeb.name.Length > 0)
			aeb.name.CopyToWithTruncate(ref name);
		stringParam = "";
		if (aeb.stringParam.Length > 0)
			aeb.stringParam.CopyToWithTruncate(ref stringParam);
	#endif
	}
	
#if RUKHANKA_DEBUG_INFO
	public FixedString32Bytes name;
	public FixedString32Bytes stringParam;
#endif
	public uint nameHash;
	public float floatParam;
	public int intParam;
	public uint stringParamHash;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[ChunkSerializable]
public struct PreviousProcessedAnimationComponent: IBufferElementData
{
	public uint motionId;
	public float animationTime;
	public BlobAssetReference<AnimationClipBlob> animation;
}


/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

//	Define some special bone names
public static class SpecialBones
{
	//	Special bone hashes need to be recalculated in case of hashing function change!
	//	I cannot add hash computation code here, because burst will fail to compile
	public static readonly string UnnamedRootBoneName = "RUKHANKA_UnnamedRootBone";
	public static readonly string AnimatorTypeName = "RUKHANKA_Animator";
	public static readonly uint AnimatorTypeNameHash = 0xde00343e;
}
}

