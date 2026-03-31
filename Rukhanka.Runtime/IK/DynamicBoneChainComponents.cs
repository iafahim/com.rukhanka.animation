using Unity.Entities;
using Unity.Mathematics;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public struct DynamicBoneChainNode: IBufferElementData
{
    public int parentIndex;
    public float3 position;
    public float3 prevPosition;
    public BoneTransform referenceLocalPose;
    public Entity boneEntity;
}
    
/////////////////////////////////////////////////////////////////////////////////

public struct DynamicBoneChainComponent: IComponentData, IEnableableComponent
{
    public float inertia;
    public float damping;
    public float elasticity;
    public float stiffness;
    public float timeAccumulator;
    //  Previous entity position. Used to simulate inertial motion of whole chain
    public float3 prevPosition;
}
}
