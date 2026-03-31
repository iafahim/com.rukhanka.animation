using Unity.Entities;
using UnityEngine;

///////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{

class VFXSkinnedMeshSamplerAuthoring: MonoBehaviour
{
    public GameObject skinnedMesh;
}

///////////////////////////////////////////////////////////////////////////////////////////

class VFXSkinnedMeshSamplerBaker: Baker<VFXSkinnedMeshSamplerAuthoring>
{
    public override void Bake(VFXSkinnedMeshSamplerAuthoring a)
    {
        var e = GetEntity(a, TransformUsageFlags.None);
        var smsc = new VFXSkinnedMeshSamplerComponent()
        {
            skinnedMeshEntity = GetEntity(a.skinnedMesh, TransformUsageFlags.None)
        };
        AddComponent(e, smsc);
    }
}
}

