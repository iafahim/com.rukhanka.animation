
using Rukhanka.Toolbox;
using Unity.Collections;
using Unity.Mathematics;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.WaybackMachine
{
public struct AnimatorControllerStateHistoryData
{
	public FixedString32Bytes name;
	public int2 frameSpan;
	public int layerIndex;
	public int stateId;
	public uint motionId;
	public float weight;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public AnimatorControllerStateHistoryData(in AnimatorControllerLayerComponent aclc, int stateId, uint motionId, int frameIndex)
	{
		frameSpan = new (frameIndex, frameIndex);
		weight = 1;
		this.stateId = stateId;
		this.layerIndex = aclc.layerIndex;
		this.motionId = motionId;
		ref var sb = ref aclc.controller.Value.layers[aclc.layerIndex].states[stateId];
		name = default;
	#if RUKHANKA_DEBUG_INFO
		if (sb.name.Length > 0)
		{
			sb.name.CopyToWithTruncate(ref name);
		}
	#endif
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public string GetName()
	{
	#if RUKHANKA_DEBUG_INFO
		return name.ToString();
	#else
		return $"{stateId}";
	#endif
	}
}

//----------------------------------------------------------------------------------------------------------------//

public struct AnimatorControllerTransitionHistoryData
{
	public FixedString64Bytes name;
	public int2 frameSpan;
	public int srcStateId;
	public int dstStateId;
	public int transitionId;
	public int layerIndex;
	public float2 weightRange;
	public int srcStateDataIndex;
	public int dstStateDataIndex;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public string GetName()
	{
	#if RUKHANKA_DEBUG_INFO
		return name.ToString();
	#else
		return $"{srcStateId}->{dstStateId}";
	#endif
	}
}
}
