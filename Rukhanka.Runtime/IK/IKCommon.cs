using Unity.Entities;
using Unity.Transforms;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public static class IKCommon
{
    public static void GetEntityWorldTransform
    (
        Entity e,
        ref BoneTransform t,
		in RuntimeAnimationData runtimeAnimationData,
        ComponentLookup<LocalTransform> ltl,
        ComponentLookup<Parent> pl,
        ComponentLookup<AnimatorEntityRefComponent> aerc,
        ComponentLookup<RigDefinitionComponent> rd
    )
    {
        if (!ltl.TryGetComponent(e, out var lt)) return;
        
        //  If current entity is a part of the rig (bone entity) use its animated pose
        if (aerc.TryGetComponent(e, out var aer))
        {
            var boneWorldPoses = RuntimeAnimationData.GetAnimationDataForRigRO(runtimeAnimationData.worldSpaceBonesBuffer, rd[aer.animatorEntity]);
            if (boneWorldPoses.Length > aer.boneIndexInAnimationRig)
            {
                var bt = boneWorldPoses[aer.boneIndexInAnimationRig];
                t = BoneTransform.Multiply(bt, t);
            }
            //  We have got rig relative position so must continue with parent entities of animated rig
            e = aer.animatorEntity;
        }
        else
        {
            t = BoneTransform.Multiply(new BoneTransform(lt), t);
        }

        if (pl.TryGetComponent(e, out var p))
        {
            GetEntityWorldTransform(p.Value, ref t, runtimeAnimationData, ltl, pl, aerc, rd);
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////

    public static BoneTransform GetRigRelativeEntityPose
    (
        Entity target,
        Entity animatorEntity,
        BoneTransform rigRootWorldPose,
		in RuntimeAnimationData runtimeAnimationData,
        ComponentLookup<LocalTransform> ltl,
        ComponentLookup<Parent> pl,
        ComponentLookup<AnimatorEntityRefComponent> aerc,
        ComponentLookup<RigDefinitionComponent> rd
    )
    {
        var targetEntityWorldPose = BoneTransform.Identity();
        GetEntityWorldTransform(target, ref targetEntityWorldPose, runtimeAnimationData, ltl, pl, aerc, rd);
        var animatedEntityWorldPose = BoneTransform.Inverse(rigRootWorldPose);
        GetEntityWorldTransform(animatorEntity, ref animatedEntityWorldPose, runtimeAnimationData, ltl, pl, aerc, rd);
        var rv = BoneTransform.Multiply(BoneTransform.Inverse(animatedEntityWorldPose), targetEntityWorldPose);
        return rv;
    }
}
}
