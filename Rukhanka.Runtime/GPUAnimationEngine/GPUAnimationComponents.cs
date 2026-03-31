
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
//  If present and enabled, then GPU animation engine is used
public struct GPUAnimationEngineTag: IComponentData, IEnableableComponent { }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct GPUAttachmentComponent: IComponentData
{
    public int attachedBoneIndex;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

//  Used to move attachments using GPU computed bone poses in shader
[MaterialProperty("_RukhankaGPUBoneIndex")]
public struct GPUAttachmentBoneIndexMPComponent: IComponentData
{
    public int boneIndex; 
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[MaterialProperty("_RukhankaAttachmentToBoneTransform")]
public struct GPUAttachmentToBoneTransformMPComponent: IComponentData
{
    public float4x4 value; 
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[MaterialProperty("_RukhankaAnimatedEntityLocalToWorld")]
public struct GPURigEntityLocalToWorldMPComponent: IComponentData
{
    public float4x4 value; 
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct GPURigFrameOffsetsComponent: IComponentData
{
    public int boneIndex;
    public int rigIndex;
    public int animationToProcessIndex;
    
    public void AddOffsets(GPURigFrameOffsetsComponent o)
    {
        boneIndex += o.boneIndex;
        rigIndex += o.rigIndex;
        animationToProcessIndex += o.animationToProcessIndex;
    }
}

}
