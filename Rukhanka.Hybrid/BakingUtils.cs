#if UNITY_EDITOR

using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Hybrid
{
public static class BakingUtils
{
    public static Hash128 ComputeAnimationHash(uint2 animationAssetID, uint2 avatarAssetID)
    {
        var rv = new Hash128(avatarAssetID.x, avatarAssetID.y, animationAssetID.x, animationAssetID.y);
        return rv;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static uint2 GetAssetID(Object obj)
    {
        if (obj == null)
            return 0;
        
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var guidString, out long fileID))
        {
            //  In case of no backed file, use InstanceID
        #if UNITY_6000_4_OR_NEWER
            ulong entityID = obj.GetEntityId().GetRawData();
        #else
            ulong entityID = (ulong)obj.GetInstanceID();
        #endif
            return new uint2((uint)entityID, (uint)(entityID >> 32));
        }
        
        var guid = new GUID(guidString);
        
        var hashBuilder = new xxHash3.StreamingState(true, 121212);
        hashBuilder.Update(guid);
        hashBuilder.Update(fileID);
        var rv = hashBuilder.DigestHash64();
        return rv;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static Hash128 ComputeAnimationHash(AnimationClip animation, Avatar avatar)
    {
        var animationAssetID = GetAssetID(animation);
        var avatarAssetID = GetAssetID(avatar);
        var rv = ComputeAnimationHash(animationAssetID, avatarAssetID);
        return rv;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    //  Foot-IK variant of the animation hash. applyFootIK == true keeps the original (unsalted) hash so the
    //  default foot-IK'd blob, its disk cache and the AnimatorController path are byte-for-byte unchanged.
    //  applyFootIK == false is a genuinely different baked pose (raw leg-muscle curves, no foot planting), so
    //  it must hash differently or it would alias the foot-IK'd blob of the same clip and silently win the cache.
    public static Hash128 ComputeAnimationHash(AnimationClip animation, Avatar avatar, bool applyFootIK)
    {
        var rv = ComputeAnimationHash(animation, avatar);
        if (!applyFootIK)
            rv = new Hash128(rv.Value.x, rv.Value.y, rv.Value.z, rv.Value.w ^ 0xF007140Fu);
        return rv;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static Hash128 ComputeControllerHash(AnimatorController controller)
    {
        var assetID = GetAssetID(controller);
        var rv = new Hash128(assetID.x, assetID.y, 0, 0);
        return rv;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static Hash128 ComputeAvatarMaskHash(AvatarMask avatarMask, RigDefinitionAuthoring rigDefinition)
    {
		var assetID = GetAssetID(avatarMask);
        var rigHash = rigDefinition.CalculateRigHash();
        var rv = new Hash128(assetID.x, assetID.y, rigHash.Value.x, rigHash.Value.y);
        return rv;
    }
}
}

#endif
