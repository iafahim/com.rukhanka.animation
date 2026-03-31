
using System;
using Rukhanka.Toolbox;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.WaybackMachine
{
public struct HistoryValue
{
	public float value;
	public int frameIndex;
}
	
//---------------------------------------------------------------------------------------//
	
[BurstCompile]
public struct AnimationHistoryData: IDisposable
{
	public FixedString128Bytes animationName;
	public int2 frameSpan;
	public Hash128 animationHash;
	public Hash128 avatarMaskHash;
	public AnimationBlendingMode blendMode;
	public float layerWeight;
	public int layerIndex;
	public uint motionId;
	public UnsafeList<HistoryValue> historyWeights;
	public UnsafeList<HistoryValue> historyAnimTime;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public AnimationHistoryData(in AnimationToProcessComponent atp, int frameIndex)
	{
		frameSpan = new int2(frameIndex, frameIndex);
		animationHash = atp.animation.IsCreated ? atp.animation.Value.hash : new Hash128();
		avatarMaskHash = atp.avatarMask.IsCreated ? atp.avatarMask.Value.hash : new Hash128();
		blendMode = atp.blendMode;
		layerWeight = atp.layerWeight;
		layerIndex = atp.layerIndex;
		motionId = atp.motionId;
		historyWeights = default;
		historyAnimTime = default;
		animationName = default;
	#if RUKHANKA_DEBUG_INFO
		if (atp.animation.IsCreated)
		{
			atp.animation.Value.name.CopyToWithTruncate(ref animationName);
		}
	#endif
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public Hash128 ComputeHash()
	{
		var hasher = new xxHash3.StreamingState(false, 0xaabbccdd);
		hasher.Update(animationHash);
		hasher.Update(avatarMaskHash);
		hasher.Update(blendMode);
		hasher.Update(layerIndex);
		hasher.Update(layerWeight);
		hasher.Update(motionId);
		return new Hash128(hasher.DigestHash128());
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	
	public void Dispose()
	{
		historyWeights.Dispose();
		historyAnimTime.Dispose();
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	
	[BurstCompile]
	public static float GetHistoryValueForFrame(in UnsafeList<HistoryValue> hvs, int frameIndex)
	{
		if (hvs.Length == 0) 
			return 0;
		
		var rv = hvs[0].value;
		for (int i = 0; i < hvs.Length; ++i)
		{
			var hv = hvs[i];
			rv = hv.value;
			if (hv.frameIndex >= frameIndex)
				break;
		}
		return rv;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public string GetName()
	{
	#if RUKHANKA_DEBUG_INFO
		return animationName.ToString();
	#else
		return $"{animationHash}";
	#endif
	}
	
}
}
