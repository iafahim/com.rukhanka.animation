#if RUKHANKA_SHADER_DEBUG

using System;
using Unity.Entities;
using UnityEngine;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{

[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
[UpdateInGroup(typeof(RukhankaDeformationSystemGroup), OrderLast = true)]
public partial class ShaderDebugAndValidationSystem: SystemBase
{
	
/////////////////////////////////////////////////////////////////////////////////
	
	protected override void OnUpdate()
	{
		for (var i = 0; i <  ShaderDebugAndValidationInitSystem.debugLoggerReadbackData.Length; ++i)
		{
			var errorsCount = ShaderDebugAndValidationInitSystem.debugLoggerReadbackData[i];
			if (errorsCount == 0)
				continue;
			
			var dm = (RukhankaDebugMarkers)i;
			Debug.LogException(new Exception($"Shader Validation: '{dm.ToString()}' marker error count '{errorsCount}'"));
		}
	}
}

//-------------------------------------------------------------------------------//

[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
internal partial class ShaderDebugAndValidationInitSystem: SystemBase
{
	public static GraphicsBuffer debugLoggerCB;
	public static int[] debugLoggerReadbackData;
	public static int[] debugLoggerZeroData;
	static readonly int ShaderID_debugLoggerCB = Shader.PropertyToID("debugLoggerCB");
	
/////////////////////////////////////////////////////////////////////////////////

	protected override void OnCreate()
	{
		var totalMarkers = (int)RukhankaDebugMarkers.Total;
		debugLoggerReadbackData = new int[totalMarkers];
		debugLoggerZeroData = new int[totalMarkers];
		debugLoggerCB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None, totalMarkers, sizeof(int));
		debugLoggerCB.SetData(debugLoggerZeroData);
		Shader.SetGlobalBuffer(ShaderID_debugLoggerCB, debugLoggerCB);
	}
	
/////////////////////////////////////////////////////////////////////////////////

	protected override void OnUpdate()
	{
		Shader.SetGlobalBuffer(ShaderID_debugLoggerCB, debugLoggerCB);
		debugLoggerCB.GetData(debugLoggerReadbackData);
		debugLoggerCB.SetData(debugLoggerZeroData);
	}
	
/////////////////////////////////////////////////////////////////////////////////
	
	protected override void OnDestroy()
	{
		if (debugLoggerCB != null)
			debugLoggerCB.Dispose();
	}
}
}

#endif