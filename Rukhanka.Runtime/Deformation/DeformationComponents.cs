using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
    //  Just a copy of EG SkinMatrix
    public struct SkinMatrix: IBufferElementData
    {
        public float3x4 Value;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    //  Just a copy of EG BlendShapeWeight
    public struct BlendShapeWeight : IBufferElementData
    {
        public float Value;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#if RUKHANKA_ENABLE_DEFORMATION_MOTION_VECTORS
    [MaterialProperty("_DeformationParamsForMotionVectors")]
    public struct DeformedMeshIndex: IComponentData
    {
        public uint4 Value;
    }
#else
    [MaterialProperty("_DeformedMeshIndex")]
    public struct DeformedMeshIndex: IComponentData
    {
        public uint Value;
    }
#endif
}