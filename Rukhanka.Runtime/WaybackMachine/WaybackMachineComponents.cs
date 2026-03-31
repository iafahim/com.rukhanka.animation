
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.WaybackMachine
{
[BurstCompile]
public struct WaybackMachineData: IDisposable
{
    public enum FPSMode
    {
        FPS120,
        FPS60,
        FPS30
    }
    
	public FPSMode fpsMode;
	public int lastRecordedFrame;
    public NativeList<AnimationHistoryData> animHistory;
    public NativeList<AnimatorControllerStateHistoryData> controllerStateHistory;
    public NativeList<AnimatorControllerTransitionHistoryData> controllerTransitionHistory;
    public NativeList<AnimationEventHistoryData> animationEventHistory;
    public NativeList<AnimatorEventHistoryData> animatorEventHistory;
    
    public NativeList<AnimationEventComponent> emittedAnimationEvents;
    public NativeList<AnimatorControllerEventComponent> emittedAnimatorEvents;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Clear()
	{
		foreach (var ah in animHistory)
		{
			ah.Dispose();
		}
		lastRecordedFrame = 0;
		animHistory.Clear();
		animationEventHistory.Clear();
		animatorEventHistory.Clear();
		controllerStateHistory.Clear();
		controllerTransitionHistory.Clear();
		
		EndFrame();
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void EndFrame()
	{
		emittedAnimationEvents.Clear();
		emittedAnimatorEvents.Clear();
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Construct()
	{
		fpsMode = FPSMode.FPS60;
		animHistory = new (0xff, Allocator.Persistent);
		animationEventHistory = new (0xff, Allocator.Persistent);
		animatorEventHistory = new (0xff, Allocator.Persistent);
		controllerStateHistory = new (0xff, Allocator.Persistent);
		controllerTransitionHistory = new (0xff, Allocator.Persistent);
		emittedAnimationEvents = new (0xff, Allocator.Persistent);
		emittedAnimatorEvents = new (0xff, Allocator.Persistent);
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public float GetFrameDuration() => fpsMode switch
    {
        FPSMode.FPS30 => 1 / 30.0f,
        FPSMode.FPS60 => 1 / 60.0f,
        FPSMode.FPS120 => 1 / 120.0f,
        _ => 1 / 60.0f
    };

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	public long GetDataSize()
	{
		var rv = 0L;
		rv += animHistory.Length * UnsafeUtility.SizeOf<AnimationHistoryData>();
		foreach (var ah in animHistory)
		{
			rv += ah.historyWeights.Length * UnsafeUtility.SizeOf<HistoryValue>();
			rv += ah.historyAnimTime.Length * UnsafeUtility.SizeOf<HistoryValue>();
		}
		rv += controllerStateHistory.Length * UnsafeUtility.SizeOf<AnimatorControllerStateHistoryData>();
		rv += controllerTransitionHistory.Length * UnsafeUtility.SizeOf<AnimatorControllerTransitionHistoryData>();
		rv += animationEventHistory.Length * UnsafeUtility.SizeOf<AnimationEventHistoryData>();
		rv += animatorEventHistory.Length * UnsafeUtility.SizeOf<AnimatorEventHistoryData>();
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	
	public void Dispose()
	{
		foreach (var ah in animHistory)
		{
			ah.Dispose();
		}
		animHistory.Dispose();
		animationEventHistory.Dispose();
		animatorEventHistory.Dispose();
		controllerStateHistory.Dispose();
		controllerTransitionHistory.Dispose();
		emittedAnimationEvents.Dispose();
		emittedAnimatorEvents.Dispose();
	}
}

//---------------------------------------------------------------------------------------//
 
public struct RecordComponent: IComponentData
{
	public NativeReference<WaybackMachineData> wbData;
}

//---------------------------------------------------------------------------------------//
 
public struct PlaybackComponent: IComponentData
{
	public NativeList<AnimationToProcessComponent> playbackData;
}
}
