
using Rukhanka.Toolbox;
using Rukhanka.WaybackMachine;
using Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

//=================================================================================================================//

namespace Rukhanka
{
public partial struct AnimationEventEmitSystem
{
	
[BurstCompile]
partial struct EmitAnimationEventsJob : IJobEntity
{
	public float deltaTime;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(Entity e, in DynamicBuffer<AnimationToProcessComponent> atp, in DynamicBuffer<PreviousProcessedAnimationComponent> ppa, ref DynamicBuffer<AnimationEventComponent> aec)
	{
		aec.Clear();

		var ppaArr = ppa.AsNativeArray();
		var atpArr = atp.AsNativeArray();
		ulong prevAnimationsProcessedIndices = 0;
		
		var maxBitsCount = UnsafeUtility.SizeOf<ulong>() * 8;
		BurstAssert.IsTrue(ppaArr.Length <= maxBitsCount, "Too many simultaneous animations! Change bits holder to the NativeBitArray if this error occurs.");
		
		for (var i = 0; i < atpArr.Length; ++i)
		{
			var a = atpArr[i];
			if (a.animation == BlobAssetReference<AnimationClipBlob>.Null)
				continue;
			
			var curTime = a.time;
			var prevBufferId = GetPreviousBufferAnimationIndex(a.motionId, i, ppaArr);
			var prevTime = 0.0f;
			if (prevBufferId < 0)
			{
				//	There is no such animation in "previous buffer". Assume that this animation advances by dt already
				var animationAdjustedDeltaTime = deltaTime / a.animation.Value.length;
				prevTime = curTime - animationAdjustedDeltaTime;	
			}
			else
			{
				prevAnimationsProcessedIndices |= 1ul << prevBufferId;
				prevTime = ppaArr[prevBufferId].animationTime;
			}
			
			if (prevTime == curTime)
				continue;
			
			var negativeAnimationDT = prevTime > curTime;
			if (negativeAnimationDT)
				(prevTime, curTime) = (curTime, prevTime);
			
			ref var aes = ref a.animation.Value.events;
			if (a.animation.Value.looped)
			{
				ProcessEventsForLoopedAnimation(e, ref aec, ref aes, prevTime, curTime, negativeAnimationDT);
			}
			else
			{
                if (prevTime < 1 && curTime > 0)
					ProcessEventsForAnimation(e, ref aec, ref aes, prevTime, curTime, negativeAnimationDT);
			}
		}
		
		//	Now we need check for animations that missed now, but were there at previous frame
		//	Animation can ending playing for at least one frame (dt)
		for (var i = 0; i < ppaArr.Length; ++i)
		{
			var bitMask = 1ul << i;
			if ((prevAnimationsProcessedIndices & bitMask) != 0)
				continue;
			
			var p = ppaArr[i];
			ref var aes = ref p.animation.Value.events;
			ProcessEventsForAnimation(e, ref aec, ref aes, p.animationTime, p.animationTime + deltaTime, false);
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void ProcessEventsForLoopedAnimation(Entity e, ref DynamicBuffer<AnimationEventComponent> aec, ref BlobArray<AnimationEventBlob> aes, float prevTime, float curTime, bool negativeAnimationDT)
	{
		var t0 = prevTime;
		var dt = curTime - prevTime;
		if (t0 < 0)
			t0 = t0 - math.floor(t0);
		
		var t1 = t0 + dt;
		var it0 = t0;
		
		//	Divide whole range to sections that fit in [0..1] range, and execute events calculation for them individually
		do
		{
			it0 = math.floor(it0);
			var tStart = math.max(it0, t0);
			var tEnd = math.min(it0 + 1, t1);
			tEnd -= tStart;
			tStart = math.frac(tStart);
			tEnd += tStart;
			ProcessEventsForAnimation(e, ref aec, ref aes, tStart, tEnd, negativeAnimationDT);
			it0 += 1;
		}
		while (it0 < t1);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void ProcessEventsForAnimation(Entity e, ref DynamicBuffer<AnimationEventComponent> outEvents, ref BlobArray<AnimationEventBlob> events, float fStart, float fEnd, bool reverseEventIteration)
	{
		for (var i = 0; i < events.Length; ++i)
		{
			//	Reverse events iteration order to preserve events order in output buffer
			var idx = reverseEventIteration ? events.Length - i - 1 : i;
			ref var ae = ref events[idx];
			var eventTime = ae.time;
			
			var emitEvent = eventTime >= fStart && eventTime <= fEnd;
		
			if (emitEvent)
			{	
				var evt = new AnimationEventComponent(ref ae);
				outEvents.Add(evt);
			}
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	int GetPreviousBufferAnimationIndex(uint motionId, int animationBufferIndex, NativeArray<PreviousProcessedAnimationComponent> ppa)
	{
		//	Fast path
		if (animationBufferIndex < ppa.Length && ppa[animationBufferIndex].motionId == motionId)
			return animationBufferIndex;

		//	Full search
		for (var i = 0; i < ppa.Length; ++i)
		{
			if (motionId == ppa[i].motionId)
				return i;
		}

		return -1;
	}
}

//=================================================================================================================//

[BurstCompile]
partial struct MakeProcessedAnimationsSnapshotJob: IJobEntity
{
	void Execute(in DynamicBuffer<AnimationToProcessComponent> atp, ref DynamicBuffer<PreviousProcessedAnimationComponent> ppa)
	{
		ppa.Resize(atp.Length, NativeArrayOptions.UninitializedMemory);
		for (var i = 0; i < atp.Length; ++i)
		{
			var a = atp[i];
			var p = new PreviousProcessedAnimationComponent()
			{
				animationTime = a.time,
				motionId = a.motionId,
				animation = a.animation
			};
			ppa[i] = p;
		}
	}
}

//=================================================================================================================//

[BurstCompile]
[WithAll(typeof(RecordComponent))]
partial struct CopyAnimationEventsToWaybackMachineRecordingJob: IJobEntity
{
	public NativeList<AnimationEventComponent> outEvents;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void Execute(in DynamicBuffer<AnimationEventComponent> aec)
	{
		outEvents.AddRange(aec.AsNativeArray());
	}
}
}
}
