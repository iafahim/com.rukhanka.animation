using Unity.Entities;
using UnityEngine;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Hybrid
{
[RequireComponent(typeof(Renderer))]
public class GPUAttachmentAuthoring: MonoBehaviour
{
	public int attachedBoneIndex = -1;
}

/////////////////////////////////////////////////////////////////////////////////

class GPUAttachmentBaker: Baker<GPUAttachmentAuthoring>
{
	public override void Bake(GPUAttachmentAuthoring a)
	{
		var e = GetEntity(a, TransformUsageFlags.Dynamic);
		
		var ga = new GPUAttachmentComponent()
		{
			attachedBoneIndex = GetComponentInParent<RigDefinitionAuthoring>(a) != null ? -1 : a.attachedBoneIndex
		};
		AddComponent(e, ga);
		AddComponent<GPUAttachmentBoneIndexMPComponent>(e);
		AddComponent<GPUAttachmentToBoneTransformMPComponent>(e);
		AddComponent<GPURigEntityLocalToWorldMPComponent>(e);
	}
}
}
