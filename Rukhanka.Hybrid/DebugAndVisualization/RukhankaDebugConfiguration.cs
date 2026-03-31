using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Hybrid
{
public class RukhankaDebugConfiguration: MonoBehaviour
{
	[Header("Bone Visualization")]
	public bool visualizeAllRigs;
	public Color boneColorCPURig = new Color(0, 1, 1, 1);
	public Color boneColorGPURig = new Color(0, 1, 0, 1);
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public class DebugConfigurationBaker: Baker<RukhankaDebugConfiguration>
{
	public override void Bake(RukhankaDebugConfiguration a)
	{
		var dcc = DebugConfigurationComponent.Default();
		
		dcc.visualizeAllRigs = a.visualizeAllRigs;
		dcc.cpuRigColor = new float4(a.boneColorCPURig.r, a.boneColorCPURig.g, a.boneColorCPURig.b, a.boneColorCPURig.a);
		dcc.gpuRigColor = new float4(a.boneColorGPURig.r, a.boneColorGPURig.g, a.boneColorGPURig.b, a.boneColorGPURig.a);

		var e = GetEntity(TransformUsageFlags.None);
		AddComponent(e, dcc);
		
	#if (RUKHANKA_NO_DEBUG_DRAWER && RUKHANKA_DEBUG_INFO)
		if (a.visualizeAllRigs)
			Debug.LogWarning("All rigs visualization was requested, but DebugDrawer is compiled out via RUKHANKA_NO_DEBUG_DRAWER script symbol. No visualization is available.");
	#endif
	}
}
}

