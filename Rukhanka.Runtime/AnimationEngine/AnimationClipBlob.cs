
using System.Runtime.CompilerServices;
using Rukhanka.Toolbox;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using FixedStringName = Unity.Collections.FixedString512Bytes;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public enum BindingType
{
	Unknown,
	Translation,
	Quaternion,
	EulerAngles,
	HumanMuscle,
	Scale
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct KeyFrame
{
	public float v;
	public float inTan, outTan;
	public float time;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct AnimationEventBlob
{
#if RUKHANKA_DEBUG_INFO
	public BlobString name;
#endif
	public uint nameHash;
	public float time;
	public float floatParam;
	public int intParam;
	public uint stringParamHash;
#if RUKHANKA_DEBUG_INFO
	public BlobString stringParam;
#endif
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct PerfectHashTableBlob
{
	public BlobArray<uint2> pht;
	public uint seed;
	
	public unsafe int Query(uint v)
	{
		return Perfect2HashTable.Query(v, seed, (uint2*)pht.GetUnsafePtr(), pht.Length);
	}
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public enum TrackFrame
{
	First,
	Last,
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct Track
{
#if RUKHANKA_DEBUG_INFO
	public FixedString64Bytes name;
#endif
	public uint props;
	public int2 keyFrameRange;
	
	public Track(BindingType bt, uint channelIndex)
	{
		keyFrameRange = 0;
		props = 0;
	#if RUKHANKA_DEBUG_INFO
		name = default;
	#endif
		
		bindingType = bt;
		this.channelIndex = channelIndex;
	}
	
    public BindingType bindingType
    {
        get => (BindingType)(props & 0xf);
        set => props = (uint)value | props & 0xfffffff0;
    }
    
    public uint channelIndex
    {
        get => props >> 4 & 3;
        set => props = value << 4 | props & 0xffffffcf;
    }
	
	//	Zero out last 4 bits, to force Unknown binding type for such tracks
	public static uint CalculateHash(uint h) => h & 0xfffffff0;
	public static uint CalculateHash(in FixedStringName h) => CalculateHash(h.CalculateHash32());
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#if RUKHANKA_DEBUG_INFO
public struct TrackGroupInfo
{
	public FixedString128Bytes name;
	public uint hash;
}
#endif

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct TrackSet
{
	public BlobArray<KeyFrame> keyframes;
	public BlobArray<Track> tracks;
	public BlobArray<int> trackGroups;
	public PerfectHashTableBlob trackGroupPHT;
#if RUKHANKA_DEBUG_INFO
	public BlobArray<TrackGroupInfo> trackGroupDebugInfo;
#endif
	
	public int GetTrackGroupIndex(uint boneHash) => trackGroupPHT.Query(boneHash);
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

//	Foot IK goal (BovineLabs Timeline foot-IK, Option A2): the left/right foot bone pose
//	sampled at bake time in HIPS-RELATIVE space. At runtime, when AnimationToProcessComponent
//	.applyFootIK is set, FootIKSystem solves each leg (TwoBoneIK) so the foot reaches this goal,
//	keeping the foot where the source animation placed it (no retarget slide/float). This is a
//	coordinate space WE control, sidestepping Unity's opaque normalized goal-curve space.
public struct FootIKGoalSample
{
	public BoneTransform leftFoot;
	public BoneTransform rightFoot;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct FootIKGoalSet
{
	//	Evenly spaced over the clip: sample i covers normalized time i / (count - 1).
	public BlobArray<FootIKGoalSample> samples;
	//	True only for humanoid clips where both feet (and hips) resolved at bake time.
	public bool isValid;

	//	normalizedTime in [0,1]. Caller is responsible for fractioning looped clips.
	public FootIKGoalSample Sample(float normalizedTime)
	{
		var n = samples.Length;
		if (n == 0)
			return default;
		if (n == 1)
			return samples[0];

		var t = math.saturate(normalizedTime) * (n - 1);
		var i0 = (int)math.floor(t);
		var i1 = math.min(i0 + 1, n - 1);
		var f = t - i0;

		var a = samples[i0];
		var b = samples[i1];
		return new FootIKGoalSample
		{
			leftFoot = LerpBoneTransform(a.leftFoot, b.leftFoot, f),
			rightFoot = LerpBoneTransform(a.rightFoot, b.rightFoot, f),
		};
	}

	static BoneTransform LerpBoneTransform(in BoneTransform a, in BoneTransform b, float f)
	{
		return new BoneTransform
		{
			pos = math.lerp(a.pos, b.pos, f),
			rot = math.slerp(a.rot, b.rot, f),
			scale = math.lerp(a.scale, b.scale, f),
		};
	}
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct AnimationClipBlob: GenericAssetBlob
{
#if RUKHANKA_DEBUG_INFO
	public BlobString name;
	public string Name() => name.ToString();
	public float bakingTime;
	public float BakingTime() => bakingTime;
#endif
	public Hash128 hash;
	public Hash128 Hash() => hash;
	
	public TrackSet clipTracks;
	public TrackSet additiveReferencePoseFrame;
	public BlobArray<AnimationEventBlob> events;
	//	Foot IK goals sampled in hips-relative space (Option A2). isValid only for humanoid clips.
	public FootIKGoalSet footIKGoals;
	
	public uint flags;
	public float cycleOffset;
	public float length;
	public bool looped { get => GetFlag(1); set => SetFlag(1, value); }
	public bool loopPoseBlend { get => GetFlag(2); set => SetFlag(2, value); }
	public uint maxTrackKeyframeLength;
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void SetFlag(int index, bool value)
	{
		var v = 1u << index;
		var mask = ~v;
		var valueBits = math.select(0, v, value);
		flags = flags & mask | valueBits;
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	bool GetFlag(int index)
	{
		var v = 1u << index;
		return (flags & v) != 0;
	}
}

}
