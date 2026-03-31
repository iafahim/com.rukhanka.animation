
using System;
using System.Runtime.CompilerServices;
using Rukhanka.Toolbox;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public struct RuntimeAnimationData: IComponentData
{
    internal NativeList<BoneTransform> animatedBonesBuffer;
    internal NativeList<BoneTransform> worldSpaceBonesBuffer;
    internal NativeList<int3> boneToEntityArr;
	internal NativeList<ulong> boneTransformFlagsHolderArr;

/////////////////////////////////////////////////////////////////////////////////

	public static RuntimeAnimationData MakeDefault()
	{
		var rv = new RuntimeAnimationData()
		{
			animatedBonesBuffer = new (Allocator.Persistent),
			worldSpaceBonesBuffer = new (Allocator.Persistent),
			boneToEntityArr = new (Allocator.Persistent),
			boneTransformFlagsHolderArr = new (Allocator.Persistent),
		};
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////

	public void Dispose()
	{
		animatedBonesBuffer.Dispose();
		worldSpaceBonesBuffer.Dispose();
		boneToEntityArr.Dispose();
		boneTransformFlagsHolderArr.Dispose();
	}

/////////////////////////////////////////////////////////////////////////////////

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<BoneTransform> GetAnimationDataForRigRO(in NativeList<BoneTransform> animatedBonesBuffer, int offset, int length)
	{
		var rv = animatedBonesBuffer.GetReadOnlySpan(offset, length);
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Span<BoneTransform> GetAnimationDataForRigRW(in NativeList<BoneTransform> animatedBonesBuffer, int offset, int length)
	{
		var rv = animatedBonesBuffer.GetSpan(offset, length);
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<BoneTransform> GetAnimationDataForRigRO
	(
		in NativeList<BoneTransform> animatedBonesBuffer,
		in RigDefinitionComponent rigDefinition
	)
	{
		return GetAnimationDataForRigRO(animatedBonesBuffer, rigDefinition.dynamicFrameData.bonePoseOffset, rigDefinition.dynamicFrameData.rigBoneCount);
	}

///////////////////////////////////////////////////////////////////////////////////////////

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Span<BoneTransform> GetAnimationDataForRigRW
	(
		in NativeList<BoneTransform> animatedBonesBuffer,
		in RigDefinitionComponent rigDefinition
	)
	{
		return GetAnimationDataForRigRW(animatedBonesBuffer, rigDefinition.dynamicFrameData.bonePoseOffset, rigDefinition.dynamicFrameData.rigBoneCount);
	}

///////////////////////////////////////////////////////////////////////////////////////////

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static AnimationTransformFlags GetAnimationTransformFlagsRO(in NativeList<int3> boneToEntityArr, in NativeList<ulong> boneTransformFlagsArr, int globalBoneIndex, int boneCount)
	{
		var boneInfo = boneToEntityArr[globalBoneIndex];
		var rv = AnimationTransformFlags.CreateFromBufferRO(boneTransformFlagsArr, boneInfo.z, boneCount);
		return rv;
	}

///////////////////////////////////////////////////////////////////////////////////////////

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static AnimationTransformFlags GetAnimationTransformFlagsRW(in NativeList<int3> boneToEntityArr, in NativeList<ulong> boneTransformFlagsArr, int globalBoneIndex, int boneCount)
	{
		var boneInfo = boneToEntityArr[globalBoneIndex];
		var rv = AnimationTransformFlags.CreateFromBufferRW(boneTransformFlagsArr, boneInfo.z, boneCount);
		return rv;
	}
}
}
