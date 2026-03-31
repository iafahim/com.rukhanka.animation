using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Hybrid
{
public partial class SkinnedMeshConversionSystem
{

//=================================================================================================================//

	[BurstCompile]
	[WithOptions(EntityQueryOptions.IncludePrefab)]
	partial struct ActualizeSkinnedMeshDataJob: IJobEntity
	{
		[ReadOnly]
		public ComponentLookup<AnimatorEntityRefComponent> animEntityRefLookup;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		void Execute(Entity e, ref SkinnedMeshRendererComponent asmc, in SkinnedMeshRendererRootBoneEntity rbe, in LocalTransform lt)
		{
			if (animEntityRefLookup.HasComponent(rbe.value))
			{
				var are = animEntityRefLookup[rbe.value];
				asmc.rootBoneIndexInRig = are.boneIndexInAnimationRig;
			}
		}
	}
}
}
