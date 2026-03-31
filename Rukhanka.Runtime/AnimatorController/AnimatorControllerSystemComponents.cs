using Unity.Collections;
using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{

public struct AnimatorControllerLayerComponent: IBufferElementData, IEnableableComponent
{
	public BlobAssetReference<ControllerBlob> controller;
	public BlobAssetReference<ControllerAnimationsBlob> animations;
	public int layerIndex;
	public float weight;
	public float speed;
	public RuntimeAnimatorData rtd;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////

public struct AnimatorControllerParameterComponent: IBufferElementData
{
#if RUKHANKA_DEBUG_INFO
	public FixedString64Bytes name;
#endif
	public uint hash;
	public ControllerParameterType type;
	public ParameterValue value;

	public float FloatValue
	{
		get => value.floatValue;
		set => this.value.floatValue = value;
	}

	public int IntValue
	{
		get => value.intValue;
		set => this.value.intValue= value;
	}

	public bool BoolValue
	{
		get => value.boolValue;
		set => this.value.boolValue = value;
	}

	public void SetTrigger()
	{
		value.boolValue = true;
	}
}

/////////////////////////////////////////////////////////////////////////////////////////////////////

public struct AnimatorControllerParameterIndexTableComponent: IComponentData
{
	public BlobAssetReference<PerfectHashTableBlob> value;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////

public struct AnimatorControllerEventComponent : IBufferElementData, IEnableableComponent
{
	public enum EventType
	{
		StateEnter,
		StateExit,
		StateUpdate
	}

	public EventType eventType;
	public int layerId;
	public int stateId;
	public float timeInState;
	
#if RUKHANKA_DEBUG_INFO
	public FixedString32Bytes stateName;
#endif
}

/////////////////////////////////////////////////////////////////////////////////////////////////////

public struct AnimatorOverrideAnimations: IComponentData, IEnableableComponent
{
	public BlobAssetReference<ControllerAnimationsBlob> value;
}

}
