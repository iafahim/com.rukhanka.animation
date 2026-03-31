
using Unity.Collections;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.WaybackMachine
{
public struct AnimationEventHistoryData
{
	public FixedString32Bytes name;
	public FixedString32Bytes stringParam;
	public int frameIndex;
	public uint nameHash;
	public float floatParam;
	public int intParam;
	public uint stringParamHash;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	
	public AnimationEventHistoryData(in AnimationEventComponent aec, int frameIndex)
	{
	#if RUKHANKA_DEBUG_INFO
		name = aec.name;
		stringParam = aec.stringParam;
	#else
		name = default;
		stringParam = default;
	#endif
		nameHash = aec.nameHash;
		floatParam = aec.floatParam;
		intParam = aec.intParam;
		stringParamHash = aec.stringParamHash;
		this.frameIndex = frameIndex;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public string GetName()
	{
	#if RUKHANKA_DEBUG_INFO
		return name.ToString();
	#else
		return $"{nameHash}";
	#endif
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public string GetStringParam()
	{
	#if RUKHANKA_DEBUG_INFO
		return stringParam.ToString();
	#else
		return $"{stringParamHash}";
	#endif
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	
}
}
