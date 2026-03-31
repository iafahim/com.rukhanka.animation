using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
partial class DynamicBoneChainSampleSystem: SystemBase
{
	
////////////////////////////////////////////////////////////////////////////////////////

	protected override void OnUpdate()
	{
		var cfg = ProceduralAnimationsSampleConf.Instance;
        if (cfg == null)
            return;
        
        foreach (var dbc in SystemAPI.Query<RefRW<DynamicBoneChainComponent>>())
        {
	        dbc.ValueRW.damping = cfg.dynamicBoneDamping.value;
	        dbc.ValueRW.elasticity = cfg.dynamicBoneElasticity.value;
	        dbc.ValueRW.stiffness = cfg.dynamicBoneStiffness.value;
	        dbc.ValueRW.inertia = cfg.dynamicBoneInertia.value;
        }
	}
}
}
