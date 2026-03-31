using Rukhanka.WaybackMachine;
using Unity.Burst;
using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
[DisableAutoCreation]
[UpdateAfter(typeof(AnimationCullingSystem))]
[UpdateBefore(typeof(AnimationEventEmitSystem))]
public partial struct WaybackMachinePlaybackSystem: ISystem
{
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public void OnCreate(ref SystemState ss)
	{
		ss.RequireForUpdate<PlaybackComponent>();
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public void OnUpdate(ref SystemState ss)
	{
		var pc = SystemAPI.GetSingleton<PlaybackComponent>();
		var pce = SystemAPI.GetSingletonEntity<PlaybackComponent>();

		if (!SystemAPI.HasBuffer<AnimationToProcessComponent>(pce))
			return;
		
		var atps = SystemAPI.GetBuffer<AnimationToProcessComponent>(pce);
		atps.Clear();
		atps.AddRange(pc.playbackData.AsArray());
	}
}
}
