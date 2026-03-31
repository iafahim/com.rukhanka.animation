using Unity;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Hybrid
{
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial class SkinnedMeshConversionSystem : SystemBase
{
	protected override unsafe void OnUpdate()
	{
		var actualizeSkinnedMeshDataJob = new ActualizeSkinnedMeshDataJob()
		{
			animEntityRefLookup = SystemAPI.GetComponentLookup<AnimatorEntityRefComponent>(true),
		};
		actualizeSkinnedMeshDataJob.ScheduleParallel();

		var ecb = new EntityCommandBuffer(Allocator.Temp);
		foreach (var (rma, e) in SystemAPI.Query<RenderMeshArray>().WithEntityAccess().WithAll<SkinnedMeshSplitSubmeshEntities>())
		{
			var eg = EntityManager.GetComponentData<EntityGuid>(e);
			for (var i = 0; i < rma.MaterialMeshIndices.Length; i++)
			{
				var originalSMR = i == 0;
				//	Modify original skinned mesh renderer to draw only first submesh
				var smrEntity = originalSMR ? e : ecb.Instantiate(e);
				
				var mmiForSubmesh = MaterialMeshInfo.FromMaterialMeshIndexRange(i, 1);
				mmiForSubmesh.Material = MaterialMeshInfo.ArrayIndexToStaticIndex(i);
				mmiForSubmesh.Mesh = MaterialMeshInfo.ArrayIndexToStaticIndex(0);
				ecb.SetComponent(smrEntity, mmiForSubmesh);
				
				if (!originalSMR)
				{
					//	Modify EntityGuid to prevent 'duplicated GUID' exceptions)
				#if UNITY_6000_5_OR_NEWER
					var osei = (int)eg.OriginatingSubEntityId.GetRawData() + 1;
					eg.OriginatingSubEntityId = *(EntityId*)&osei;
				#else
					eg.a += 1;
				#endif
					ecb.SetComponent(smrEntity, eg);
				}
				ecb.RemoveComponent<SkinnedMeshSplitSubmeshEntities>(smrEntity);
			}
		}
		ecb.Playback(EntityManager);
	}
}
}
