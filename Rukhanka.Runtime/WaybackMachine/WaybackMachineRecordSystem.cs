
using Rukhanka.Toolbox;
using Rukhanka.WaybackMachine;
using Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
[DisableAutoCreation]
public partial struct WaybackMachineRecordSystem: ISystem, ISystemStartStop
{
    int frameCounter;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////

	public void OnStartRunning(ref SystemState ss)
	{
		frameCounter = 0;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	public void OnStopRunning(ref SystemState ss)
	{
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public void OnCreate(ref SystemState ss)
	{
		ss.RequireForUpdate<RecordComponent>();
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void RecordAnimation(AnimationToProcessComponent atp, ref RecordComponent rc, int frameIndex)
	{
		var ahs = rc.wbData.Value.animHistory;
		var curHistory = new AnimationHistoryData(atp, frameIndex);
		
		//	Search back for given animation in history
		var foundIndex = -1;
		for (var k = ahs.Length - 1; k >= 0; --k)
		{
			var ah = ahs[k];
			if (ah.frameSpan.y + 1 != frameIndex)
				continue;
			
			if (ah.ComputeHash() == curHistory.ComputeHash())
			{
				foundIndex = k;
				break;
			}
		}
		
		if (foundIndex < 0)
		{
			foundIndex = ahs.Length;
			curHistory.historyWeights = new (0xff, Allocator.Persistent);
			curHistory.historyAnimTime = new (0xff, Allocator.Persistent);
			ahs.Add(curHistory);
		}
		ref var historyElement = ref ahs.ElementAt(foundIndex);
		historyElement.frameSpan.y = frameIndex;
		
		var whv = new HistoryValue() { value = atp.weight, frameIndex = frameIndex };
		historyElement.historyWeights.Add(whv);
		var thv = new HistoryValue() { value = atp.time, frameIndex = frameIndex };
		historyElement.historyAnimTime.Add(thv);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void RecordAnimationEvents(ref SystemState ss, Entity rce, ref RecordComponent rc)
	{
		if (!SystemAPI.HasBuffer<AnimationEventComponent>(rce))
			return;
		
		var animEvents = rc.wbData.Value.emittedAnimationEvents;
		
		//	Record animation events
		for (var i = 0; i < animEvents.Length; ++i)
		{
			var aec = animEvents[i];
			var ehd = new AnimationEventHistoryData(aec, frameCounter);
			rc.wbData.Value.animationEventHistory.Add(ehd);
		}
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	void RecordAnimations(ref SystemState ss, Entity rce, ref RecordComponent rc)
	{
		if (!SystemAPI.HasBuffer<AnimationToProcessComponent>(rce))
			return;
		
		var atps = SystemAPI.GetBuffer<AnimationToProcessComponent>(rce);
		for (var i = 0; i < atps.Length; ++i)
		{
			var atp = atps[i];
			RecordAnimation(atp, ref rc, frameCounter);
		}
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	void RecordAnimatorTransition(in AnimatorControllerLayerComponent aclc, ref RecordComponent rc, int si, int di)
	{
		if (aclc.rtd.activeTransition.id < 0)
			return;
		
		ref var tb = ref aclc.controller.Value.layers[aclc.layerIndex].states[aclc.rtd.srcState.id].transitions[aclc.rtd.activeTransition.id];
		var std = new AnimatorControllerTransitionHistoryData
		{
			frameSpan = new (frameCounter, frameCounter),
			transitionId = aclc.rtd.activeTransition.id,
			layerIndex = aclc.layerIndex,
			dstStateId = aclc.rtd.dstState.id,
			srcStateId = aclc.rtd.srcState.id,
			weightRange = new float2(1) * aclc.rtd.activeTransition.normalizedDuration
		};
		
		var cth = rc.wbData.Value.controllerTransitionHistory;
		
		//	Search for existing entry in history
		var i = cth.Length - 1;
		for (; i >= 0; --i)
		{
			var ct = cth[i];
			if (ct.srcStateId == std.srcStateId && ct.dstStateId == std.dstStateId && ct.frameSpan.y + 1 == frameCounter && std.layerIndex == ct.layerIndex)
				break;
		}
		
		//	If not found create new entry
		if (i < 0)
		{
			i = cth.Length;
		#if RUKHANKA_DEBUG_INFO
			tb.name.CopyToWithTruncate(ref std.name);
		#endif
			cth.Add(std);
		}
		ref var csv = ref cth.ElementAt(i);
		//	If found update existing entry
		csv.frameSpan.y = frameCounter;
		csv.weightRange.y = aclc.rtd.activeTransition.normalizedDuration;
		csv.srcStateDataIndex = si;
		csv.dstStateDataIndex = di;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	int RecordAnimatorState(in AnimatorControllerLayerComponent aclc, int stateId, uint motionId, ref RecordComponent rc)
	{
		if (stateId < 0)
			return -1;
		
		var shd = new AnimatorControllerStateHistoryData(aclc, stateId, motionId, frameCounter);
		var csh = rc.wbData.Value.controllerStateHistory;
		
		//	Search for existing entry in history
		var i = csh.Length - 1;
		for (; i >= 0; --i)
		{
			var cs = csh[i];
			if (cs.stateId == shd.stateId &&
			    cs.frameSpan.y + 1 == frameCounter &&
			    cs.layerIndex == shd.layerIndex &&
			    cs.motionId == shd.motionId)
				break;
		}
		
		//	If not found create new entry
		if (i < 0)
		{
			i = csh.Length;
			csh.Add(shd);
		}
		ref var csv = ref csh.ElementAt(i);
		//	If found update existing entry
		csv.frameSpan.y = frameCounter;
		return i;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void RecordAnimatorStates(ref SystemState ss, Entity rce, ref RecordComponent rc)
	{
		if (!SystemAPI.HasBuffer<AnimatorControllerLayerComponent>(rce))
			return;
		
		var aclcs = SystemAPI.GetBuffer<AnimatorControllerLayerComponent>(rce);
		for (var i = 0; i < aclcs.Length; ++i)
		{
			var aclc = aclcs[i];
			var srcStateDataIndex = RecordAnimatorState(aclc, aclc.rtd.srcState.id, aclc.rtd.srcState.motionId, ref rc);
			var dstStateDataIndex = RecordAnimatorState(aclc, aclc.rtd.dstState.id, aclc.rtd.dstState.motionId, ref rc);
			RecordAnimatorTransition(aclc, ref rc, srcStateDataIndex, dstStateDataIndex);
		}
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	void RecordAnimatorEvents(ref SystemState ss, Entity rce, ref RecordComponent rc)
	{
		if (!SystemAPI.HasBuffer<AnimatorControllerEventComponent>(rce))
			return;
		
		var acecs = rc.wbData.Value.emittedAnimatorEvents;
		for (var i = 0; i < acecs.Length; ++i)
		{
			var acec = acecs[i];
			var aehd = new AnimatorEventHistoryData(acec, frameCounter);
			var k = -1;
			//	For every frame update events append it to the previous to make continuous span
			if (aehd.eventType == AnimatorControllerEventComponent.EventType.StateUpdate)
			{
				k = rc.wbData.Value.animatorEventHistory.Length - 1;
				for (; k >= 0; --k)
				{
					ref var hd = ref rc.wbData.Value.animatorEventHistory.ElementAt(k);
					if (hd.stateId == aehd.stateId && hd.layerId == aehd.layerId && hd.eventType == AnimatorControllerEventComponent.EventType.StateUpdate)
					{
						hd.frameRange.y = frameCounter;
						break;
					}
				}
			}
			
			//	Find begin event for this event
			if (aehd.eventType != AnimatorControllerEventComponent.EventType.StateEnter)
			{
				var l = rc.wbData.Value.animatorEventHistory.Length - 1;
				for (; l >= 0; --l)
				{
					ref var hd = ref rc.wbData.Value.animatorEventHistory.ElementAt(l);
					if (hd.stateId == aehd.stateId && hd.layerId == aehd.layerId && hd.eventType == AnimatorControllerEventComponent.EventType.StateEnter)
						break;
				}
				
				if (l >= 0)
					aehd.beginHistoryIndex = l;
			}
			
			//	If previous update event was not found, or we are dealing with other event type - add new entry
			if (k < 0)
			{
				rc.wbData.Value.animatorEventHistory.Add(aehd);
			}
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public unsafe void OnUpdate(ref SystemState ss)
	{
		var rcd = SystemAPI.GetSingletonRW<RecordComponent>();
		var rce = SystemAPI.GetSingletonEntity<RecordComponent>();

		ref var rc = ref rcd.ValueRW;
		var ptr = rc.wbData.GetUnsafePtr();
		ptr->lastRecordedFrame = frameCounter;
		
		RecordAnimatorStates(ref ss, rce, ref rc);
		RecordAnimationEvents(ref ss, rce, ref rc);
		RecordAnimatorEvents(ref ss, rce, ref rc);
		RecordAnimations(ref ss, rce, ref rc);
		
		frameCounter++;
		rcd.ValueRW.wbData.Value.EndFrame();
	}
}
}
