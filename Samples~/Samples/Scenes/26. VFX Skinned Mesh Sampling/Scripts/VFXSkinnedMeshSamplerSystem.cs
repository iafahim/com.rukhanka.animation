using Unity.Entities;
using UnityEngine;
#if RUKHANKA_SAMPLES_WITH_VFX_GRAPH
using UnityEngine.VFX;
#endif

///////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
    
struct VFXSkinnedMeshSamplerComponent: IComponentData
{
    public Entity skinnedMeshEntity;
}

///////////////////////////////////////////////////////////////////////////////////////////

[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
partial class VFXSkinnedMeshSamplerSystem: SystemBase
{
    protected override void OnUpdate()
    {
    #if RUKHANKA_SAMPLES_WITH_VFX_GRAPH
        foreach (var (smsc, e) in SystemAPI.Query<VFXSkinnedMeshSamplerComponent>().WithEntityAccess())
        {
            if (!EntityManager.HasComponent<VisualEffect>(e))
                continue;
            
            if (!EntityManager.HasComponent<DeformedMeshIndex>(smsc.skinnedMeshEntity)) 
                continue;
            
            var vfx = EntityManager.GetComponentObject<VisualEffect>(e);
            var deformedMeshIndex = EntityManager.GetComponentData<DeformedMeshIndex>(smsc.skinnedMeshEntity);
            vfx.SetInt("DeformedMeshIndex", (int)deformedMeshIndex.Value);
        }
    #endif
    }
}
}

