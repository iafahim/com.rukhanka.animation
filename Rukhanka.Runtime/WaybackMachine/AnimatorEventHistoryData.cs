
using Unity.Collections;
using Unity.Mathematics;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.WaybackMachine
{
public struct AnimatorEventHistoryData
{
	public FixedString32Bytes name;
	public int layerId;
	public int stateId;
	//	History index of begin event for this event. -1 for state enter events
	public int beginHistoryIndex;
	public int2 frameRange;
	public AnimatorControllerEventComponent.EventType eventType;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	
	public AnimatorEventHistoryData(in AnimatorControllerEventComponent acec, int frameIndex)
	{
		name = default;
	#if RUKHANKA_DEBUG_INFO
		name = acec.stateName;
	#endif
		eventType = acec.eventType;
		frameRange = new (frameIndex, frameIndex);
		layerId = acec.layerId;
		stateId = acec.stateId;
		beginHistoryIndex = -1;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public string GetName()
	{
	#if RUKHANKA_DEBUG_INFO
		return name.ToString();
	#else
		var prefix = "";
		if (eventType == AnimatorControllerEventComponent.EventType.StateEnter)
			prefix = "Enter ";
		if (eventType == AnimatorControllerEventComponent.EventType.StateExit)
			prefix = "Exit ";
		return $"{prefix}{layerId}:{stateId}";
	#endif
	}
	
}
}
