using System.Runtime.CompilerServices;
using Rukhanka.Toolbox;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

[assembly: InternalsVisibleTo("Rukhanka.Tests")]

/////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{ 
public struct AnimatorControllerSystemJobs
{

[BurstCompile]
public struct StateMachineProcessJob: IJobChunk
{
	public float dt;
	public BufferTypeHandle<AnimatorControllerLayerComponent> controllerLayersBufferHandle;
	public BufferTypeHandle<AnimatorControllerParameterComponent> controllerParametersBufferHandle;
	public EntityTypeHandle entityTypeHandle;
	[NativeDisableParallelForRestriction]
	public BufferLookup<AnimatorControllerEventComponent> controllerEventsBufferLookup;
	[ReadOnly]
	public NativeHashMap<Hash128, BlobAssetReference<AnimationClipBlob>> animationDatabase;
	[ReadOnly]
	public ComponentLookup<AnimatorOverrideAnimations> animatorOverrideAnimationLookup;
	
	public NativeParallelHashMap<int, BlobAssetReference<ControllerAnimationsBlob>>.ParallelWriter animatorOverrideAnimationsMap;

	BlobAssetReference<ControllerAnimationsBlob> controllerAnimationsBlob;

/////////////////////////////////////////////////////////////////////////////////////////////////////

	public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
	{
		var layerBuffers = chunk.GetBufferAccessor(ref controllerLayersBufferHandle);
		var parameterBuffers = chunk.GetBufferAccessor(ref controllerParametersBufferHandle);
		var entities = chunk.GetNativeArray(entityTypeHandle);

		var cee = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

		while (cee.NextEntityIndex(out var i))
		{
			var layers = layerBuffers[i];
			var parameters = parameterBuffers.Length > 0 ? parameterBuffers[i].AsNativeArray() : default;
			var e = entities[i];
			
			DynamicBuffer<AnimatorControllerEventComponent> controllerEventsBuffer = default;
			if (controllerEventsBufferLookup.HasBuffer(e) && controllerEventsBufferLookup.IsBufferEnabled(e))
				controllerEventsBuffer = controllerEventsBufferLookup[e];

			ExecuteSingle(layers, parameters, ref controllerEventsBuffer, e);
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	unsafe void ExecuteSingle
	(
		in DynamicBuffer<AnimatorControllerLayerComponent> aclc,
		in NativeArray<AnimatorControllerParameterComponent> acpc,
		ref DynamicBuffer<AnimatorControllerEventComponent> events,
		Entity entity
	)
	{
		if (events.IsCreated)
			events.Clear();

		var numIntsForBitMemory = BitFieldN.CalculateUIntsCountForGivenBitCount(acpc.Length);
		var triggersToResetMem = stackalloc uint[numIntsForBitMemory];
		var triggersToReset = new BitFieldN(triggersToResetMem, numIntsForBitMemory);
			
		var startIndex = 0;
		for (int i = 0; i < aclc.Length; ++i)
		{
			ref var acc = ref aclc.ElementAt(i);
			
			//	Save controller animations blob asset reference in class variable, because passing it inside almost all functions will bloat signatures significantly
			controllerAnimationsBlob = FillAnimationsFromControllerSystem.GetControllerAnimationsBlob
				(entity, animatorOverrideAnimationLookup, acc.animations, animatorOverrideAnimationsMap);

			ProcessLayer(ref acc.controller.Value, acc.layerIndex, acpc, aclc, ref events, triggersToReset);
			if (events.IsCreated)
			{
				EmitStateUpdateEvents(ref events, acc, startIndex);
				startIndex = events.Length;
			}
		}
		
		//	Reset affected triggers
		ResetTriggers(acpc, triggersToReset);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void ResetTriggers(NativeArray<AnimatorControllerParameterComponent> acpc, BitFieldN triggersToReset)
	{
		if (!triggersToReset.TestAny())
			return;
		
		for (var i = 0; i < acpc.Length; ++i)
		{
			var p = acpc[i];
			if (p.type == ControllerParameterType.Trigger && triggersToReset.IsSet(i))
			{
				p.BoolValue = false;
				acpc[i] = p;
			}
		}
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////

	RuntimeAnimatorData.StateData InitControllerStateData(ref RuntimeAnimatorData rtd, int stateID)
	{
		var rv = rtd.MakeDefaultState();
		rv.id = stateID;
		rv.normalizedDuration = 0;
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void TryExitTransition(ref AnimatorControllerLayerComponent acc, ref DynamicBuffer<AnimatorControllerEventComponent> events)
	{
		if (acc.rtd.activeTransition.id < 0)
			return;
		
		if (CheckTransitionExitConditions(acc.rtd.activeTransition))
		{
			//	Add state exit event
			EmitEvent(ref events, AnimatorControllerEventComponent.EventType.StateExit, acc, acc.rtd.srcState.id, acc.rtd.srcState.normalizedDuration);
				
			acc.rtd.srcState = acc.rtd.dstState;
			acc.rtd.ClearStateSnapshots();
			acc.rtd.dstState = acc.rtd.MakeDefaultState();
			acc.rtd.activeTransition = acc.rtd.MakeDefaultTransition();
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	int GetValidTransitionForCurrentFrame
	(
		ref BlobArray<TransitionBlob> transitions,
		float normalizedStateDuration,
		float srcStateDurationFrameDelta,
		NativeArray<AnimatorControllerParameterComponent> runtimeParams,
		BitFieldN triggersToReset
	)
	{
		var isTransitionFits = false;
		int i = 0;
		for (; i < transitions.Length && !isTransitionFits; ++i)
		{
			ref var t = ref transitions[i];
			isTransitionFits =
				CheckTransitionEnterExitTimeCondition(ref t, normalizedStateDuration, srcStateDurationFrameDelta) &&
				CheckTransitionEnterConditions(ref t, runtimeParams, triggersToReset);
		}
		return isTransitionFits ? i - 1 : -1;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void TryEnterTransition
	(
		in DynamicBuffer<AnimatorControllerLayerComponent> aclc,
		ref ControllerBlob controllerBlob,
		int layerIndex,
		NativeArray<AnimatorControllerParameterComponent> runtimeParams,
		float srcStateDurationFrameDelta,
		float curStateDuration,
		ref DynamicBuffer<AnimatorControllerEventComponent> events,
		BitFieldN triggersToReset
	)
	{
		ref var acc = ref aclc.ElementAt(layerIndex);
		ref var layer = ref controllerBlob.layers[layerIndex];
		ref var currentState = ref layer.states[acc.rtd.srcState.id];
			
		if (acc.rtd.activeTransition.id >= 0)
		{
			TryTransitionInterruption(ref layer, ref acc.rtd, srcStateDurationFrameDelta, runtimeParams, triggersToReset);
			return;
		}

		var newTransitionIndex = GetValidTransitionForCurrentFrame(ref currentState.transitions, acc.rtd.srcState.normalizedDuration, srcStateDurationFrameDelta, runtimeParams, triggersToReset);
		if (newTransitionIndex < 0)
			return;
		
		ref var t = ref currentState.transitions[newTransitionIndex];
		var timeShouldBeInTransition = GetTimeInSecondsShouldBeInTransition(ref t, acc.rtd.srcState.normalizedDuration, curStateDuration, srcStateDurationFrameDelta);
		acc.rtd.activeTransition.id	= newTransitionIndex;
		acc.rtd.activeTransition.length = GetTransitionLength(ref t);
		acc.rtd.activeTransition.normalizedDuration = timeShouldBeInTransition / CalculateTransitionDuration(acc.rtd.activeTransition, curStateDuration);
		var dstStateDur = CalculateStateDuration(layerIndex, t.targetStateId, ref controllerBlob, aclc.AsNativeArray(), runtimeParams);
		acc.rtd.dstState = InitControllerStateData(ref acc.rtd, t.targetStateId);
		acc.rtd.dstState.normalizedDuration += timeShouldBeInTransition / dstStateDur + t.offset;
		
		//	Add state enter event
		EmitEvent(ref events, AnimatorControllerEventComponent.EventType.StateEnter, acc, acc.rtd.dstState.id, acc.rtd.dstState.normalizedDuration);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void EmitEvent
	(
		ref DynamicBuffer<AnimatorControllerEventComponent> events,
		AnimatorControllerEventComponent.EventType eventType,
		AnimatorControllerLayerComponent aclc,
		int stateId,
		float stateDuration
	)
	{
		if (!events.IsCreated)
			return;
		
		var evt = new AnimatorControllerEventComponent()
		{
			eventType = eventType,
			stateId = stateId,
			layerId = aclc.layerIndex,
			timeInState = stateDuration,
		};
		
	#if RUKHANKA_DEBUG_INFO
		aclc.controller.Value.layers[aclc.layerIndex].states[stateId].name.CopyToWithTruncate(ref evt.stateName);
	#endif

		events.Add(evt);
		
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void EmitStateUpdateEvents(ref DynamicBuffer<AnimatorControllerEventComponent> events, in AnimatorControllerLayerComponent acc, int startIndex)
	{
		if (!events.IsCreated)
			return;
		
		var srcStateEnterExit = false;
		var dstStateEnterExit = false;
		for (var i = startIndex; i < events.Length; ++i)
		{
			var e = events[i];
			if (acc.rtd.srcState.id >= 0 && e.stateId == acc.rtd.srcState.id)
				srcStateEnterExit = true;
			if (acc.rtd.dstState.id >= 0 && e.stateId == acc.rtd.dstState.id)
				dstStateEnterExit = true;
		}
		
		if (acc.rtd.srcState.id >= 0 && !srcStateEnterExit)
			EmitEvent(ref events, AnimatorControllerEventComponent.EventType.StateUpdate, acc, acc.rtd.srcState.id, acc.rtd.srcState.normalizedDuration);
		if (acc.rtd.dstState.id >= 0 && !dstStateEnterExit)
			EmitEvent(ref events, AnimatorControllerEventComponent.EventType.StateUpdate, acc, acc.rtd.dstState.id, acc.rtd.dstState.normalizedDuration);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void ProcessLayer
	(
		ref ControllerBlob c,
		int layerIndex,
		in NativeArray<AnimatorControllerParameterComponent> runtimeParams,
		DynamicBuffer<AnimatorControllerLayerComponent> aclc,
		ref DynamicBuffer<AnimatorControllerEventComponent> events,
		BitFieldN triggersToReset
	)
	{
		ref var layer = ref c.layers[layerIndex];
		ref var acc = ref aclc.ElementAt(layerIndex);
			
		var currentStateID = acc.rtd.srcState.id;
		if (currentStateID < 0)
			currentStateID = layer.defaultStateIndex;
		
		//	Adjust delta time according to the layer animator speed
		var layerDeltaTime = dt * acc.speed;

		var curStateDuration = CalculateStateDuration(layerIndex, currentStateID, ref c, aclc.AsNativeArray(), runtimeParams);
		
		if (Hint.Unlikely(acc.rtd.srcState.id < 0))
		{
			acc.rtd.srcState = InitControllerStateData(ref acc.rtd, layer.defaultStateIndex);
			EmitEvent(ref events, AnimatorControllerEventComponent.EventType.StateEnter, acc, acc.rtd.srcState.id, acc.rtd.srcState.normalizedDuration);
		}

		var srcStateDurationFrameDelta = CalculateStateFrameDeltaSafe(layerDeltaTime, curStateDuration);
		acc.rtd.srcState.normalizedDuration += srcStateDurationFrameDelta;

		if (acc.rtd.dstState.id >= 0)
		{
			var dstStateDuration = CalculateStateDuration(layerIndex, acc.rtd.dstState.id, ref c, aclc.AsNativeArray(), runtimeParams);
			acc.rtd.dstState.normalizedDuration += CalculateStateFrameDeltaSafe(layerDeltaTime,  dstStateDuration);
		}

		if (acc.rtd.activeTransition.id >= 0)
		{
			var transitionDuration = CalculateTransitionDuration(acc.rtd.activeTransition, curStateDuration);
			acc.rtd.activeTransition.normalizedDuration += layerDeltaTime / transitionDuration;
		}

		TryExitTransition(ref acc, ref events);
		TryEnterTransition(aclc, ref c, layerIndex, runtimeParams, srcStateDurationFrameDelta, curStateDuration, ref events, triggersToReset);
		//	Check transition exit conditions one more time in case of Enter->Exit sequence appeared in single frame
		TryExitTransition(ref acc, ref events);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	float CalculateStateFrameDeltaSafe(float dt, float stateDuration)
	{
		var rv = dt / stateDuration;
		rv = math.select(0, rv, math.isfinite(rv));
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	float CalculateMotionDuration
	(
		ref MotionBlob mb,
		in NativeArray<AnimatorControllerParameterComponent> runtimeParams,
		float weight
	)
	{
		if (weight == 0) return 0;

		switch (mb.type)
		{
		case MotionBlob.Type.None:
			return 1;
		case MotionBlob.Type.AnimationClip:
			var animationHash = controllerAnimationsBlob.Value.animations[mb.animationIndex];
			var animBlob = BlobDatabaseSingleton.GetBlobAsset(animationHash, animationDatabase);
			if (animBlob != BlobAssetReference<AnimationClipBlob>.Null)
				return animBlob.Value.length * weight;
			return 1;
		}
		
		var childMotions = ScriptedAnimator.GetChildMotionsList(ref mb, runtimeParams);
		var rv = CalculateBlendTreeMotionDuration(childMotions, ref mb.blendTree.motions, runtimeParams, weight);
		
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	float CalculateBlendTreeMotionDuration
	(
		NativeList<ScriptedAnimator.MotionIndexAndWeight> miwArr,
		ref BlobArray<ChildMotionBlob> motions,
		in NativeArray<AnimatorControllerParameterComponent> runtimeParams,
		float weight
	)
	{
		if (!miwArr.IsCreated || miwArr.IsEmpty)
			return 1;

		var weightSum = 0.0f;
		for (int i = 0; i < miwArr.Length; ++i)
			weightSum += miwArr[i].weight;

		//	If total weight less then 1, normalize weights
		if (Hint.Unlikely(weightSum < 1))
		{
			for (int i = 0; i < miwArr.Length; ++i)
			{
				var miw = miwArr[i];
				miw.weight = miw.weight / weightSum;
				miwArr[i] = miw;
			}
		}

		var rv = 0.0f;
		for (int i = 0; i < miwArr.Length; ++i)
		{
			var miw = miwArr[i];
			ref var m = ref motions[miw.motionIndex];
			rv += CalculateMotionDuration(ref m.motion, runtimeParams, weight * miw.weight) / m.timeScale;
		}

		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	//	Negative value means that length is a normalized value from source state length
	float GetTransitionLength(ref TransitionBlob tb)
	{
		return math.select(-tb.duration, tb.duration, tb.hasFixedDuration);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	float CalculateTransitionDuration(in RuntimeAnimatorData.TransitionData trd, float curStateDuration)
	{
		var rv = math.abs(trd.length);
		if (trd.length < 0)
		{
			rv *= curStateDuration;
		}
		return math.max(rv, 0.0001f);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	float CalculateStateDuration
	(
		int layerIndex,
		int stateId,
		ref ControllerBlob controllerBlob,
		in NativeArray<AnimatorControllerLayerComponent> aclc,
		in NativeArray<AnimatorControllerParameterComponent> runtimeParams
	)
	{
		ref var layer = ref controllerBlob.layers[layerIndex];
		ref var sb = ref layer.states[stateId];
		var motionDuration = CalculateMotionDuration(ref sb.motion, runtimeParams, 1);
		
		//	In case of layer sync option is enabled, adjust state duration with respect to "timing" property
		if (Hint.Unlikely(layer.syncedLayerIndex >= 0))
		{
			//	Override controller must be exact copy of current
			ref var baseState = ref controllerBlob.layers[layer.syncedLayerIndex].states[stateId];
			var baseMotionDuration = CalculateMotionDuration(ref baseState.motion, runtimeParams, 1);
			var weightedMotionDuration = math.lerp(baseMotionDuration, motionDuration, aclc[layerIndex].weight);
			motionDuration = math.select(baseMotionDuration, weightedMotionDuration, layer.syncedTiming > 0);
		}
		else if (Hint.Unlikely(layer.syncedTiming >= 0))
		{
			ref var syncedState = ref controllerBlob.layers[layer.syncedTiming].states[stateId];
			var syncedStateDuration = CalculateMotionDuration(ref syncedState.motion, runtimeParams, 1);
			motionDuration = math.lerp(motionDuration, syncedStateDuration, aclc[layer.syncedTiming].weight);
		}
		
		var speedMultiplier = 1.0f;
		if (sb.speedMultiplierParameterIndex >= 0)
		{
			speedMultiplier = runtimeParams[sb.speedMultiplierParameterIndex].FloatValue;
		}
		var rv = motionDuration / (sb.speed * speedMultiplier);
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	internal static float GetLoopAwareTransitionExitTime(float exitTime, float normalizedDuration, float speedSign)
	{
		var rv = exitTime;
		if (exitTime <= 1.0f)
		{
			//	Unity animator logic and documentation mismatch. Documentation says that exit time loop condition should be when transition exitTime less then 1, but in practice it will loop when exitTime is less or equal(!) to 1.
			exitTime = math.min(exitTime, 0.9999f);
			var snd = normalizedDuration * speedSign;

			var f = math.frac(snd);
			rv += (int)snd;
			if (f > exitTime)
				rv += 1;
		}
		return rv * speedSign;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	float GetTimeInSecondsShouldBeInTransition(ref TransitionBlob tb, float normalizedStateDuration, float curStateDuration, float frameDT)
	{
		if (!tb.hasExitTime) return 0;

		//	This should be always less then curStateRTD.normalizedDuration
		var loopAwareExitTime = GetLoopAwareTransitionExitTime(tb.exitTime, normalizedStateDuration - frameDT, math.sign(frameDT));
		var loopDelta = normalizedStateDuration - loopAwareExitTime;
		var rv = loopDelta * curStateDuration;
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	bool CheckTransitionEnterExitTimeCondition(ref TransitionBlob tb, float normalizedStateDuration, float srcStateDurationFrameDelta)
	{
		var noNormalConditions = tb.conditions.Length == 0;
		if (!tb.hasExitTime) return !noNormalConditions;

		var l0 = normalizedStateDuration - srcStateDurationFrameDelta;
		var l1 = normalizedStateDuration;
		var speedSign = math.select(-1, 1, l0 < l1);

		var loopAwareExitTime = GetLoopAwareTransitionExitTime(tb.exitTime, l0, speedSign);

		if (speedSign < 0)
			(l0, l1) = (l1, l0);

		var rv = loopAwareExitTime > l0 && loopAwareExitTime <= l1;
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	bool CheckIntCondition(in AnimatorControllerParameterComponent param, ref ConditionBlob c)
	{
		var rv = true;
		switch (c.conditionMode)
		{
		case AnimatorConditionMode.Equals:
			if (param.IntValue != c.threshold.intValue) rv = false;
			break;
		case AnimatorConditionMode.Greater:
			if (param.IntValue <= c.threshold.intValue) rv = false;
			break;
		case AnimatorConditionMode.Less:
			if (param.IntValue >= c.threshold.intValue) rv = false;
			break;
		case AnimatorConditionMode.NotEqual:
			if (param.IntValue == c.threshold.intValue) rv = false;
			break;
		default:
			Debug.LogError($"Unsupported condition type for int parameter value!");
			break;
		}
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	bool CheckFloatCondition(in AnimatorControllerParameterComponent param, ref ConditionBlob c)
	{
		var rv = true;
		switch (c.conditionMode)
		{
		case AnimatorConditionMode.Greater:
			if (param.FloatValue <= c.threshold.floatValue) rv = false;
			break;
		case AnimatorConditionMode.Less:
			if (param.FloatValue >= c.threshold.floatValue) rv = false;
			break;
		default:
			Debug.LogError($"Unsupported condition type for int parameter value!");
			break;
		}
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	bool CheckBoolCondition(in AnimatorControllerParameterComponent param, ref ConditionBlob c)
	{
		var rv = true;
		switch (c.conditionMode)
		{
		case AnimatorConditionMode.If:
			rv = param.BoolValue;
			break;
		case AnimatorConditionMode.IfNot:
			rv = !param.BoolValue;
			break;
		default:
			Debug.LogError($"Unsupported condition type for int parameter value!");
			break;
		}
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	void MarkTriggersToReset(ref TransitionBlob tb, BitFieldN triggersToReset)
	{
		for (int i = 0; i < tb.conditions.Length; ++i)
		{
			ref var c = ref tb.conditions[i];
			//	Mark all transition parameters as "need to be reset". We will check actual parameter type later, after all layers processing
			triggersToReset.Set(c.paramIdx, true);
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	bool CheckTransitionEnterConditions(ref TransitionBlob tb, NativeArray<AnimatorControllerParameterComponent> runtimeParams, BitFieldN triggersToReset)
	{
		if (tb.conditions.Length == 0)
			return true;

		var rv = true;
		var hasTriggers = false;
		for (int i = 0; i < tb.conditions.Length && rv; ++i)
		{
			ref var c = ref tb.conditions[i];
			var param = runtimeParams[c.paramIdx];

			switch (param.type)
			{
			case ControllerParameterType.Float:
				rv = CheckFloatCondition(param, ref c);
				break;
			case ControllerParameterType.Int:
				rv = CheckIntCondition(param, ref c);
				break;
			case ControllerParameterType.Bool:
				rv = CheckBoolCondition(param, ref c);
				break;
			case ControllerParameterType.Trigger:
				rv = CheckBoolCondition(param, ref c);
				hasTriggers = true;
				break;
			}
		}

		if (hasTriggers && rv)
			MarkTriggersToReset(ref tb, triggersToReset);

		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	bool CheckTransitionExitConditions(RuntimeAnimatorData.TransitionData transitionRuntimeData)
	{
		return transitionRuntimeData.normalizedDuration >= 1;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////

	unsafe void TryTransitionInterruption
	(
		ref LayerBlob layerBlob,
		ref RuntimeAnimatorData rtd,
		float srcStateDurationFrameDelta,
		NativeArray<AnimatorControllerParameterComponent> runtimeParams,
		BitFieldN triggersToReset
	)
	{
		ref var srcStateTransitions = ref layerBlob.states[rtd.srcState.id].transitions;
		var transitionIndex = rtd.activeTransition.id;
		//	Transition index could be greater than number of actual transitions when 'ScriptedAnimator.CrossFade' is used
		if (transitionIndex >= srcStateTransitions.Length)
			return;
		
		//	We are in transition right now. Perform transition interruption logic
		ref var activeTransition = ref srcStateTransitions[transitionIndex];
		//	Can't interrupt, return
		if (Hint.Likely(activeTransition.interruptionSource == TransitionBlob.InterruptionSource.None))
			return;
		
		//	Make copy of triggers to reset flags because I don't know if there are valid interruption transitions yet
		var triggersToResetMem = stackalloc uint[triggersToReset.SizeInInts()];
		var triggersClone = triggersToReset.Clone(triggersToResetMem);
		
		var transitionIndexCandidateSrc = -1;
		var transitionIndexCandidateDst = -1;
		var canBeInterrupted = false;
		
		if
		(
			activeTransition.interruptionSource == TransitionBlob.InterruptionSource.Source ||
			activeTransition.interruptionSource == TransitionBlob.InterruptionSource.SourceThenDestination ||
			activeTransition.interruptionSource == TransitionBlob.InterruptionSource.DestinationThenSource
		)
		{
			transitionIndexCandidateSrc = GetValidTransitionForCurrentFrame(ref srcStateTransitions, rtd.srcState.normalizedDuration, srcStateDurationFrameDelta, runtimeParams, triggersClone);
			canBeInterrupted |=
				transitionIndexCandidateSrc >= 0 &&
				(activeTransition.orderedInterruption ? transitionIndexCandidateSrc < transitionIndex : transitionIndexCandidateSrc != transitionIndex);
		}
		
		ref var dstStateTransitions = ref layerBlob.states[rtd.dstState.id].transitions;
		if
		(
			activeTransition.interruptionSource == TransitionBlob.InterruptionSource.Destination ||
			activeTransition.interruptionSource == TransitionBlob.InterruptionSource.SourceThenDestination ||
			activeTransition.interruptionSource == TransitionBlob.InterruptionSource.DestinationThenSource
		)
		{
			transitionIndexCandidateDst = GetValidTransitionForCurrentFrame(ref dstStateTransitions, rtd.dstState.normalizedDuration, srcStateDurationFrameDelta, runtimeParams, triggersClone);
			canBeInterrupted |= transitionIndexCandidateDst >= 0;
		}
		
		if (!canBeInterrupted)
			return;
		
		//	There is valid interruption
		//	Select interrupting transition
		var interruptingTransitionID = -1;
		TransitionBlob* transitionBlobsPtr = null;
		switch (activeTransition.interruptionSource)
		{
		case TransitionBlob.InterruptionSource.Source:
			interruptingTransitionID = transitionIndexCandidateSrc;
			transitionBlobsPtr = (TransitionBlob*)srcStateTransitions.GetUnsafePtr();
			break;
		case TransitionBlob.InterruptionSource.Destination:
			interruptingTransitionID = transitionIndexCandidateDst;
			transitionBlobsPtr = (TransitionBlob*)dstStateTransitions.GetUnsafePtr();
			break;
		case TransitionBlob.InterruptionSource.SourceThenDestination:
			interruptingTransitionID = math.select(transitionIndexCandidateDst, transitionIndexCandidateSrc, transitionIndexCandidateSrc >= 0);
			transitionBlobsPtr = transitionIndexCandidateSrc >= 0 ? (TransitionBlob*)srcStateTransitions.GetUnsafePtr() : (TransitionBlob*)dstStateTransitions.GetUnsafePtr();
			break;
		case TransitionBlob.InterruptionSource.DestinationThenSource:
			interruptingTransitionID = math.select(transitionIndexCandidateSrc, transitionIndexCandidateDst, transitionIndexCandidateDst >= 0);
			transitionBlobsPtr = transitionIndexCandidateDst >= 0 ? (TransitionBlob*)dstStateTransitions.GetUnsafePtr() : (TransitionBlob*)srcStateTransitions.GetUnsafePtr();
			break;
		default:
			//	This should not happen
			BurstAssert.IsTrue(false, "Transition interruption invalid code path");
			return;
		}
		ref var newTransition = ref transitionBlobsPtr[interruptingTransitionID];
		
		//	Copy triggers back to original bitset
		triggersToReset.CopyFrom(triggersClone);
		
		//	If state snapshot array is empty, then push both destination state and source state
		//	If there are some state snapshots in array, the these snapshots are our "source state" already, so we need to add only destination state
		if (rtd.srcStateSnapshots.IsEmpty)
			rtd.PushStateSnapshot(rtd.srcState.id, 1, rtd.srcState.normalizedDuration, rtd.srcState.motionId);
		rtd.PushStateSnapshot(rtd.dstState.id, rtd.activeTransition.normalizedDuration, rtd.dstState.normalizedDuration, rtd.dstState.motionId);	
		
		rtd.srcState.normalizedDuration = 0;
		//	If interrupting transition belongs to next state, configure srcState accordingly
		if (transitionBlobsPtr == dstStateTransitions.GetUnsafePtr())
			rtd.srcState.id = rtd.dstState.id;
				
		rtd.activeTransition.id = interruptingTransitionID;
		rtd.activeTransition.length = GetTransitionLength(ref newTransition);
		rtd.activeTransition.normalizedDuration = 0;
		
		rtd.dstState = InitControllerStateData(ref rtd, newTransition.targetStateId);
		rtd.dstState.normalizedDuration = newTransition.offset;
	}
}
}
}
