
using Rukhanka.WaybackMachine;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{

[DisableAutoCreation]
[UpdateBefore(typeof(AnimationProcessSystem))]
public partial struct AnimationEventEmitSystem: ISystem
{
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle EmitAnimationEvents(ref SystemState ss, JobHandle dependsOn)
	{
		var dt = SystemAPI.Time.DeltaTime;
		
		var emitAnimationEventsJob = new EmitAnimationEventsJob()
		{
			deltaTime = dt
		};
		var jh = emitAnimationEventsJob.ScheduleParallel(dependsOn);
		return jh;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle MakeProcessedAnimationsSnapshot(ref SystemState ss, JobHandle dependsOn)
	{
		var makeProcessedAnimationsSnapshotJob = new MakeProcessedAnimationsSnapshotJob() { };
		var jh = makeProcessedAnimationsSnapshotJob.ScheduleParallel(dependsOn);
		return jh;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	
	JobHandle CopyEventsForWaybackMachineDuringRecording(ref SystemState ss, JobHandle dependsOn)
	{
		if (!SystemAPI.TryGetSingletonRW<RecordComponent>(out var rcd))
			return dependsOn;
		
		var copyEventsToWaybackMachineJob = new CopyAnimationEventsToWaybackMachineRecordingJob()
		{
			outEvents = rcd.ValueRW.wbData.Value.emittedAnimationEvents
		};
		var jh = copyEventsToWaybackMachineJob.Schedule(dependsOn);
		return jh;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public void OnUpdate(ref SystemState ss)
	{
		//	Emit animation events based on current and previously processed animations
		var emitAnimationEventsJH = EmitAnimationEvents(ref ss, ss.Dependency);
		
		//	Make a snapshot of current frame animations, to use it in next frame as previously processed jobs
		var makeProcessedAnimationsSnapshotJH = MakeProcessedAnimationsSnapshot(ref ss, emitAnimationEventsJH);
		
		//	Copy animation events into shadow buffer for wayback machine if recording requested
		//	Why to copy? Wayback machine recordings are performed in fixed-step update loop. If game frame time is more
		//	then recording fixed time, then some events will be missed from recording because each simulation (game)
		//	frame event buffer is cleared in AnimationEventEmitSystem
		var copyEventsToWaybackMachineBufJH = CopyEventsForWaybackMachineDuringRecording(ref ss, emitAnimationEventsJH);

		var combinedJH = JobHandle.CombineDependencies(copyEventsToWaybackMachineBufJH, makeProcessedAnimationsSnapshotJH);
		ss.Dependency = combinedJH;
	}
}
}
