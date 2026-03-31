using System;
using System.Collections.Generic;
using System.Reflection;
using Rukhanka.Toolbox;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using Unity.Assertions;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Transforms;
using FixedStringName = Unity.Collections.FixedString512Bytes;
using Hash128 = Unity.Entities.Hash128;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Hybrid
{

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[TemporaryBakingType]
public struct SkinnedMeshRendererRootBoneEntity: IComponentData
{
	public Entity value;
}

[TemporaryBakingType]
public struct SkinnedMeshSplitSubmeshEntities: IComponentData { }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public partial class SkinnedMeshBaker: Baker<SkinnedMeshRenderer>
{
	public override unsafe void Bake(SkinnedMeshRenderer a)
	{
		if (a.sharedMesh == null)
			return;
		
		var smrHash = CalculateSkinnedMeshHash(a.sharedMesh);
		
		var isSMSBlobExists = TryGetBlobAssetReference<SkinnedMeshInfoBlob>(smrHash, out var smrBlobAsset);
		if (!isSMSBlobExists)
		{
			smrBlobAsset = CreateSkinnedMeshBlob(a, smrHash);
			AddBlobAssetWithCustomHash(ref smrBlobAsset, smrHash);
		}
		
		var rbe = new SkinnedMeshRendererRootBoneEntity()
		{
			value = _State.BakedEntityData->GetEntity(a.rootBone)
		};
		
		var smrc = new SkinnedMeshRendererComponent()
		{
			smrInfoBlob = smrBlobAsset,
			animatedRigEntity = GetEntity(a.gameObject.GetComponentInParent<RigDefinitionAuthoring>(true), TransformUsageFlags.Dynamic),
			rootBoneIndexInRig = -1,
			nameHash = a.name.CalculateHash32()
		};
		var e = GetEntity(a, TransformUsageFlags.Renderable);
		
		var splitOnSubmeshes = a.GetComponent<SkinnedMeshSplitOnSubmeshEntitiesAuthoring>();
		
		//	One or many render entities
		if (!splitOnSubmeshes || a.sharedMaterials.Length == 0)
		{
			AddEntityComponents(e, a, smrc, rbe, -1);
		}
		else
		{
			//	In case of submesh splitting requested create one entity per material in source skinned mesh renderer and configure it as ordinary
			//	skinned mesh entity
			var additionalEntities = new NativeArray<Entity>(a.sharedMaterials.Length - 1, Allocator.Temp);
			CreateAdditionalEntities(additionalEntities, TransformUsageFlags.ManualOverride);
			var parent = new Parent() { Value = GetEntity(a.transform.parent, TransformUsageFlags.Dynamic) };
			var lt = new LocalTransform() { Position = a.transform.localPosition, Rotation = a.transform.localRotation, Scale = a.transform.localScale.x };
			
			//	Original SMR with first material
			AddEntityComponents(e, a, smrc, rbe, 0);
			
			for (var i = 0; i < additionalEntities.Length; ++i)
			{
				var materialIndex = i + 1;
				var renderEntity = additionalEntities[i];
				if (parent.Value != Entity.Null)
					AddComponent(renderEntity, parent);
				AddComponent(renderEntity, lt);
				AddComponent<LocalToWorld>(renderEntity);
				
				AddEntityComponents(renderEntity, a, smrc, rbe, materialIndex);
			}
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void AddEntityComponents(Entity e, SkinnedMeshRenderer a, in SkinnedMeshRendererComponent smrc, SkinnedMeshRendererRootBoneEntity smrrbe, int materialIndex)
	{
		AddComponent(e, smrrbe);
		AddComponent(e, smrc);
		
		if (a.updateWhenOffscreen)
		{
			AddComponent<ShouldUpdateBoundingBoxTag>(e);
		}
		
		CheckMaterialCompatibility(a);
        CreateRenderComponents(e, a, materialIndex);
        CreateSkinMatricesBuffer(e, a);
        CreateBlendShapeWeightsBuffer(e, a);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	Hash128 CalculateSkinnedMeshHash(Mesh m)
	{
		var rv = new Hash128();
	#if UNITY_EDITOR
		var assetId = BakingUtils.GetAssetID(m);
		rv = new Hash128(assetId.x, assetId.y, 0, 0);
	#endif
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CreateSkinnedMeshBonesBlob(ref BlobBuilder bb, ref SkinnedMeshInfoBlob smrBlob, SkinnedMeshRenderer r)
	{
		var bonesArr = bb.Allocate(ref smrBlob.bones, r.bones.Length);
		for (int j = 0; j < bonesArr.Length; ++j)
		{
			var b = r.bones[j];
			ref var boneBlob = ref bonesArr[j];
			
			if (b != null)
			{
	#if RUKHANKA_DEBUG_INFO
				bb.AllocateString(ref boneBlob.name, b.name);
	#endif
				var bn = new FixedStringName(b.name);
				boneBlob.hash = bn.CalculateHash32();
				boneBlob.bindPose = r.sharedMesh.bindposes[j];
			}
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CreateBlendShapesBlob(ref BlobBuilder bb, ref SkinnedMeshInfoBlob smrBlob, SkinnedMeshRenderer r)
	{
		var blendShapeHashesArr = bb.Allocate(ref smrBlob.blendShapes, r.sharedMesh.blendShapeCount);
		for (var j = 0; j < blendShapeHashesArr.Length; ++j)
		{
			ref var bs = ref blendShapeHashesArr[j];
			var bsName = "blendShape." + r.sharedMesh.GetBlendShapeName(j);
			bs.hash = bsName.CalculateHash32();
		#if RUKHANKA_DEBUG_INFO
			if (bsName.Length > 0)
				bb.AllocateString(ref bs.name, bsName);
		#endif
		}
		smrBlob.meshBlendShapesCount = r.sharedMesh.blendShapeCount;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CreateBoneWeightsDataBlob(ref BlobBuilder bb, ref SkinnedMeshInfoBlob smrBlob, SkinnedMeshRenderer r)
	{
		CreateBoneWeightsIndicesBlob(ref bb, ref smrBlob, r);
		smrBlob.meshBoneWeightsCount = r.sharedMesh.GetAllBoneWeights().Length;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CreateBoneWeightsIndicesBlob(ref BlobBuilder bb, ref SkinnedMeshInfoBlob smrBlob, SkinnedMeshRenderer r)
	{
		var mesh = r.sharedMesh;
		var allBoneWeights = mesh.GetBonesPerVertex();
		
		using var outArr = new NativeArray<uint>(allBoneWeights.Length, Allocator.TempJob);
		var computeAbsoluteOffsetsJob = new ComputeAbsoluteBoneWeightsIndicesOffsetsJob()
		{
			bonesPerVertex = allBoneWeights,
			outIndicesArr = outArr
		};
		computeAbsoluteOffsetsJob.Run();
		
		var ba = bb.Allocate(ref smrBlob.boneWeightsIndices, allBoneWeights.Length);
		//UnsafeUtility.MemCpy(ba.GetUnsafePtr(), allBoneWeights.GetUnsafeReadOnlyPtr(), allBoneWeights.Length * UnsafeUtility.SizeOf<uint>());
		for (var i = 0; i < ba.Length; ++i)
		{
			ba[i] = outArr[i];		
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	BlobAssetReference<SkinnedMeshInfoBlob> CreateSkinnedMeshBlob(SkinnedMeshRenderer r, Hash128 smrHash)
	{ 
		var bb = new BlobBuilder(Allocator.Temp);
		ref var smrBlob = ref bb.ConstructRoot<SkinnedMeshInfoBlob>();
		smrBlob.hash = smrHash;
		
	#if RUKHANKA_DEBUG_INFO
		if (r.name.Length > 0)
			bb.AllocateString(ref smrBlob.skeletonName, r.name);
		var startTimeMarker = Time.realtimeSinceStartup;
	#endif
		
		CreateSkinnedMeshBonesBlob(ref bb, ref smrBlob, r);
		CreateBlendShapesBlob(ref bb, ref smrBlob, r);
		CreateBoneWeightsDataBlob(ref bb, ref smrBlob, r);
		
		smrBlob.meshVerticesCount = r.sharedMesh.vertexCount;
		
	#if RUKHANKA_DEBUG_INFO
		var dt = Time.realtimeSinceStartupAsDouble - startTimeMarker;
		smrBlob.bakingTime = (float)dt;
	#endif

		var rv = bb.CreateBlobAssetReference<SkinnedMeshInfoBlob>(Allocator.Persistent);
		return rv;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CreateRenderComponents(Entity renderEntity, SkinnedMeshRenderer a, int materialIndex)
	{
		var mmIndices = new MaterialMeshIndex[a.sharedMaterials.Length];
		for (var i = 0; i < mmIndices.Length; ++i)
		{
			mmIndices[i] = new () { MeshIndex = 0, MaterialIndex = i, SubMeshIndex = i };
		}
		var rma = new RenderMeshArray(a.sharedMaterials, new [] { a.sharedMesh }, mmIndices);
		var rmd = new RenderMeshDescription(a);
		
		var materialBaseIndex = math.select(0, materialIndex, materialIndex >= 0);
		var materialsCount = math.select(a.sharedMaterials.Length, 1, materialIndex >= 0);
		var mmi = MaterialMeshInfo.FromMaterialMeshIndexRange(materialBaseIndex, materialsCount);
		var materialList = new ReadOnlySpan<Material>(rma.GetMaterials(mmi).ToArray());
		
		var componentFlags = RenderMeshUtility.EntitiesGraphicsComponentFlags.UseRenderMeshArray;
		componentFlags.AppendMotionAndProbeFlags(rmd, false);
		componentFlags.AppendPerVertexMotionPassFlag(materialList);
		componentFlags.AppendDepthSortedFlag(rma.GetMaterials(mmi));
		AddComponent(renderEntity, RenderMeshUtility.ComputeComponentTypes(componentFlags));

		GetLayer(a);
		SetSharedComponent(renderEntity, rmd.FilterSettings);
		SetSharedComponentManaged(renderEntity, rma);
		SetComponent(renderEntity, mmi);
		AddComponent<DeformedMeshIndex>(renderEntity);
		AddMeshRendererBakingData(renderEntity, a);
		AddLODComponents(renderEntity, a);
		
		var rbb = a.localBounds.ToAABB();
		if (a.rootBone != null)
		{
			var rootBoneLocalMat = float4x4.TRS(a.rootBone.localPosition, a.rootBone.localRotation, a.rootBone.localScale);
			rbb = AABB.Transform(rootBoneLocalMat, rbb);
		}
		SetComponent(renderEntity, new RenderBounds { Value = rbb });
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void AddLODComponents(Entity renderEntity, SkinnedMeshRenderer a)
	{
		var lodGroup = a.GetComponentInParent<LODGroup>(true);
		var lge = GetEntity(lodGroup, TransformUsageFlags.Renderable);
		var lodMask = GetLODMask(lodGroup, a);
		
		if (lge == Entity.Null || lodMask == -1)
			return;
		
		var mlc = new MeshLODComponent () { Group = lge, LODMask = lodMask };
		AddComponent(renderEntity, mlc);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	int GetLODMask(LODGroup lodGroup, SkinnedMeshRenderer a)
	{
		if (lodGroup == null)
			return -1;
                
		var lods = lodGroup.GetLODs();
		int lodGroupMask = 0;

		// Find the renderer inside the LODGroup
		for (int i = 0; i < lods.Length; ++i)
		{
			foreach (var renderer in lods[i].renderers)
			{
				if (renderer == a)
				{
					lodGroupMask |= (1 << i);
				}
			}
		}
		return lodGroupMask > 0 ? lodGroupMask : -1;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	unsafe void AddMeshRendererBakingData(Entity renderEntity, SkinnedMeshRenderer a)
	{
        var entitiesGraphicsSystemType = typeof(EntitiesGraphicsSystem);
		var meshRendererBakingDataTypeName = "Unity.Rendering.MeshRendererBakingData";
        var meshRendererBakingDataType = entitiesGraphicsSystemType.Assembly.GetType(meshRendererBakingDataTypeName);
		var mrbdTypeIndex = TypeManager.GetTypeIndex(meshRendererBakingDataType);
		var typeInfo = TypeManager.GetTypeInfo(mrbdTypeIndex);
        var untypedComponentData = UnsafeUtility.Malloc(typeInfo.TypeSize, typeInfo.AlignmentInBytes, Allocator.Temp);
        UnityObjectRef<Renderer> meshRenderer = a;
        UnsafeUtility.MemCpy(untypedComponentData, &meshRenderer, typeInfo.TypeSize);
        
		UnsafeAddComponent(renderEntity, mrbdTypeIndex, typeInfo.TypeSize, untypedComponentData);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	unsafe void CreateRenderComponents2(SkinnedMeshRenderer a)
	{
		//	To bake skinned mesh using builtin Entities.Graphics methods, I need to do many reflection magic because all baking stuff
		//	og EG is private.
		//	All this equivalent to the following:
		//
		//	{
		//		Unity.Rendering.MeshRendererBakingUtility.ConvertConvertToMultipleEntities(...);
		//		var entity = GetEntity(a, TransformUsageFlags.Renderable);
		//		AddComponent(entity, new Unity.Rendering.MeshRendererBakingData() { MeshRenderer = authoring });
		//	}
		
        var entitiesGraphicsSystemType = typeof(EntitiesGraphicsSystem);
        
        var meshRendererBakingUtilityTypeName = "Unity.Rendering.MeshRendererBakingUtility";
        var meshRendererBakingUtilityType = entitiesGraphicsSystemType.Assembly.GetType(meshRendererBakingUtilityTypeName);
		if (meshRendererBakingUtilityType == null)
			throw new NullReferenceException($"Cannot find '{meshRendererBakingUtilityTypeName}'");
		
        var cvtMethodName = "ConvertOnPrimaryEntity";
        var convertNumParameters = 3;
        MethodInfo cvtMethod = null;
		var cvtMethods = meshRendererBakingUtilityType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
		for (var i = 0; i < cvtMethods.Length; ++i)
		{
			var m = cvtMethods[i];
			var methodParams = m.GetParameters();
			if (m.Name == cvtMethodName && methodParams.Length == convertNumParameters)
			{
				cvtMethod = m;
				break;
			}
		}
		if (cvtMethod == null)
			throw new NullReferenceException($"Cannot find method '{cvtMethodName}' in '{meshRendererBakingUtilityTypeName}'");
		
		var cvtMethodGenericInstantiation = cvtMethod.MakeGenericMethod(typeof(SkinnedMeshRenderer));
		
		var callParameters = new object[] { this, a, a.sharedMesh };
		cvtMethodGenericInstantiation.Invoke(null, callParameters);
		
		var e = GetEntity(a, TransformUsageFlags.Renderable);
		var rbb = a.localBounds.ToAABB();
		if (a.rootBone != null)
		{
			var rootBoneLocalMat = float4x4.TRS(a.rootBone.localPosition, a.rootBone.localRotation, a.rootBone.localScale);
			rbb = AABB.Transform(rootBoneLocalMat, rbb);
		}
		AddComponent<DeformedMeshIndex>(e);
		SetComponent(e, new RenderBounds { Value = rbb });
		
		var meshRendererBakingDataTypeName = "Unity.Rendering.MeshRendererBakingData";
        var meshRendererBakingDataType = entitiesGraphicsSystemType.Assembly.GetType(meshRendererBakingDataTypeName);
		var mrbdTypeIndex = TypeManager.GetTypeIndex(meshRendererBakingDataType);
		var typeInfo = TypeManager.GetTypeInfo(mrbdTypeIndex);
        var untypedComponentData = UnsafeUtility.Malloc(typeInfo.TypeSize, typeInfo.AlignmentInBytes, Allocator.Temp);
        UnityObjectRef<Renderer> meshRenderer = a;
        UnsafeUtility.MemCpy(untypedComponentData, &meshRenderer, typeInfo.TypeSize);
        
		UnsafeAddComponent(e, mrbdTypeIndex, typeInfo.TypeSize, untypedComponentData);
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CreateSkinMatricesBuffer(Entity e, SkinnedMeshRenderer a)
	{
		DependsOn(a.transform);
		
		var mesh = a.sharedMesh;
		
		var boneWeights = mesh.GetAllBoneWeights();
		var bindPoses = mesh.GetBindposes();
		var bones = a.bones;
		
		Assert.IsTrue(bones.Length == bindPoses.Length);
		
		var hasSkinning = boneWeights.Length > 0 && bindPoses.Length > 0;
		if (!hasSkinning)
			return;
		
		var ownInverseTransform = a.worldToLocalMatrix;
		var skinMatrixBuf = AddBuffer<Rukhanka.SkinMatrix>(e);
		skinMatrixBuf.Resize(bindPoses.Length, NativeArrayOptions.UninitializedMemory);
		
		for (var i = 0; i < bones.Length; ++i)
		{
			var b = bones[i];
			if (b == null)
				continue;

			DependsOn(b);

			var bp = bindPoses[i];
			var boneMatRootSpace = math.mul(ownInverseTransform, b.localToWorldMatrix);
			var skinMatRootSpace = math.mul(boneMatRootSpace, bp);
			var sm = new Rukhanka.SkinMatrix()
			{
				Value = new float3x4(skinMatRootSpace.c0.xyz, skinMatRootSpace.c1.xyz, skinMatRootSpace.c2.xyz, skinMatRootSpace.c3.xyz)
			};
			skinMatrixBuf[i] = sm;
		}
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CreateBlendShapeWeightsBuffer(Entity e, SkinnedMeshRenderer a)
	{
		var mesh = a.sharedMesh;
		if (mesh.blendShapeCount == 0)
			return;
		
		var bswb = AddBuffer<Rukhanka.BlendShapeWeight>(e);
		for (var i = 0; i < mesh.blendShapeCount; ++i)
		{
			var srcBlendShapeWeight = a.GetBlendShapeWeight(i);
			var bsw = new Rukhanka.BlendShapeWeight()
			{
				Value = srcBlendShapeWeight
			};
			bswb.Add(bsw);
		}
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void CheckMaterialCompatibility(SkinnedMeshRenderer a)
	{
		var materials = new List<Material>();
		a.GetSharedMaterials(materials);

		for (var i = 0; i < materials.Count; ++i)
		{
			var m = materials[i];
			if (m == null)
				continue;

		#if RUKHANKA_ENABLE_DEFORMATION_MOTION_VECTORS
			var deformationCompatibleShader = m.HasProperty("_DeformationParamsForMotionVectors");
		#else
			var deformationCompatibleShader = m.HasProperty("_DeformedMeshIndex");
		#endif
			if (!deformationCompatibleShader)
			{
				var s = $"Shader [{m.shader.name}] on [{a.name}] does not support skinning. This can result in incorrect rendering."
						+ " Please see the <a href=\"https://docs.rukhanka.com/shaders_with_deformations\">documentation</a>"
						+ " for information about making a deformation-compatible shader";
				Debug.LogWarning(s, a);
			}
		}
	}
}
}
