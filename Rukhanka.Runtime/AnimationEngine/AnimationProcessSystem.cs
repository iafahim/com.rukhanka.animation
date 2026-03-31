
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{

[DisableAutoCreation]
[UpdateAfter(typeof(AnimationCullingSystem))]
public partial struct AnimationProcessSystem: ISystem
{
	EntityQuery animatedObjectQuery;

	NativeList<int2> bonePosesOffsetsArr;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public void OnCreate(ref SystemState ss)
	{
		InitializeRuntimeData(ref ss);

		bonePosesOffsetsArr = new (Allocator.Persistent);

		var eqb0 = new EntityQueryBuilder(Allocator.Temp)
			.WithAll<RigDefinitionComponent, AnimationToProcessComponent>()
			.WithDisabled<GPUAnimationEngineTag>();
		animatedObjectQuery = ss.GetEntityQuery(eqb0);
		
		ss.RequireForUpdate(animatedObjectQuery);
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public void OnDestroy(ref SystemState ss)
	{
		bonePosesOffsetsArr.Dispose();

		if (SystemAPI.TryGetSingleton<RuntimeAnimationData>(out var rad))
		{
			rad.Dispose();
			ss.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<RuntimeAnimationData>());
		}
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	void InitializeRuntimeData(ref SystemState ss)
	{
		var rad = RuntimeAnimationData.MakeDefault();
		ss.EntityManager.CreateSingleton(rad, "Rukhanka.RuntimeAnimationData");
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle PrepareComputationData(ref SystemState ss, NativeArray<int> chunkBaseEntityIndices, ref RuntimeAnimationData runtimeData, NativeList<Entity> entitiesArr, JobHandle dependsOn)
	{
		var rigDefinitionTypeHandle = SystemAPI.GetComponentTypeHandle<RigDefinitionComponent>(true);
		var cullAnimationsTagComponentLookup = SystemAPI.GetComponentLookup<CullAnimationsTag>(true);
		var rigDefTypeHandle = SystemAPI.GetComponentTypeHandle<RigDefinitionComponent>();
		
		//	Calculate bone offsets per entity
		var calcBoneOffsetsJob = new CalculateBoneOffsetsJob()
		{
			chunkBaseEntityIndices = chunkBaseEntityIndices,
			bonePosesOffsets = bonePosesOffsetsArr,
			rigDefinitionTypeHandle = rigDefinitionTypeHandle,
			cullAnimationsTagLookup = cullAnimationsTagComponentLookup,
			entities = entitiesArr,
		};
		
		var calcBoneOffsetsJobJH = calcBoneOffsetsJob.ScheduleParallel(animatedObjectQuery, dependsOn);
		
		//	Do prefix sum to calculate absolute offsets
		var prefixSumJob = new DoPrefixSumJob()
		{
			boneOffsets = bonePosesOffsetsArr
		};

		var prefixSumJH = prefixSumJob.Schedule(calcBoneOffsetsJobJH);
		
		//	Resize data buffers depending on current workload
		var resizeDataBuffersJob = new ResizeDataBuffersJob()
		{
			boneOffsets = bonePosesOffsetsArr,
			runtimeData = runtimeData
		};

		var resizeDataBuffersJH = resizeDataBuffersJob.Schedule(prefixSumJH);
		
		//	Fill boneToEntityArr with proper values
		var boneToEntityArrFillJob = new CalculatePerBoneInfoJob()
		{
			bonePosesOffsets = bonePosesOffsetsArr,
			boneToEntityIndices = runtimeData.boneToEntityArr,
			chunkBaseEntityIndices = chunkBaseEntityIndices,
			rigDefinitionTypeHandle = rigDefTypeHandle
		};

		var boneToEntityJH = boneToEntityArrFillJob.ScheduleParallel(animatedObjectQuery, resizeDataBuffersJH);
		return boneToEntityJH;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle AnimationCalculation(ref SystemState ss, NativeList<Entity> entitiesArr, in RuntimeAnimationData runtimeData, JobHandle dependsOn)
	{
		var animationToProcessBufferLookup = SystemAPI.GetBufferLookup<AnimationToProcessComponent>(true);
		var rootMotionAnimationStateBufferLookupRW = SystemAPI.GetBufferLookup<RootMotionAnimationStateComponent>();

		var rigDefsArr = animatedObjectQuery.ToComponentDataListAsync<RigDefinitionComponent>(ss.WorldUpdateAllocator, dependsOn, out var rigDefsLookupJH);

		var computeAnimationsJob = new ComputeBoneAnimationJob()
		{
			animationsToProcessLookup = animationToProcessBufferLookup,
			entityArr = entitiesArr,
			rigDefs = rigDefsArr,
			boneTransformFlagsArr = runtimeData.boneTransformFlagsHolderArr,
			animatedBonesBuffer = runtimeData.animatedBonesBuffer,
			boneToEntityArr = runtimeData.boneToEntityArr,
			rootMotionAnimStateBufferLookup = rootMotionAnimationStateBufferLookupRW,
		};

		var jh = computeAnimationsJob.Schedule(runtimeData.animatedBonesBuffer, 16, rigDefsLookupJH);
		return jh;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle ProcessAnimatorParameterCurves(ref SystemState ss, JobHandle dependsOn)
	{
		var genericCurveProcessJob = new ProcessAnimatorParameterCurveJob();
		var jh = genericCurveProcessJob.ScheduleParallel(dependsOn);
		return jh;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle CopyEntityBonesToAnimationTransforms(ref SystemState ss, ref RuntimeAnimationData runtimeData, JobHandle dependsOn)
	{
		var rigDefinitionLookup = SystemAPI.GetComponentLookup<RigDefinitionComponent>(true);
		var gpuAnimationEngineTagLookup = SystemAPI.GetComponentLookup<GPUAnimationEngineTag>(true);
		var parentComponentLookup = SystemAPI.GetComponentLookup<Parent>();
			
		//	Now take available entity transforms as ref poses overrides
		var copyEntityBoneTransforms = new CopyEntityBoneTransformsToAnimationBuffer()
		{
			rigDefComponentLookup = rigDefinitionLookup,
			boneTransformFlags = runtimeData.boneTransformFlagsHolderArr,
			animatedBoneTransforms = runtimeData.animatedBonesBuffer,
			parentComponentLookup = parentComponentLookup,
			gpuAnimationEngineTagLookup = gpuAnimationEngineTagLookup
		};

		var jh = copyEntityBoneTransforms.ScheduleParallel(dependsOn);
		return jh;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle MakeAbsoluteBoneTransforms(ref SystemState ss, in RuntimeAnimationData runtimeData, JobHandle dependsOn)
	{
		var rigDefTypeHandle = SystemAPI.GetComponentTypeHandle<RigDefinitionComponent>(true);
		
		var makeAbsTransformsJob = new MakeAbsoluteTransformsJob()
		{
			localBoneTransforms = runtimeData.animatedBonesBuffer,
			worldBoneTransforms = runtimeData.worldSpaceBonesBuffer,
			boneTransformFlags = runtimeData.boneTransformFlagsHolderArr,
			rigDefTypeHandle = rigDefTypeHandle
		};

		var query = SystemAPI.QueryBuilder()
			.WithAll<RigDefinitionComponent>()
			.WithNone<GPUAnimationEngineTag>()
			.Build();
		var jh = makeAbsTransformsJob.ScheduleParallel(query, dependsOn);
		return jh;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle ComputeRootMotion(ref SystemState ss, in RuntimeAnimationData runtimeData, JobHandle dependsOn)
	{
		var computeRootMotionJob = new ComputeRootMotionJob()
		{
			animatedBonePoses = runtimeData.animatedBonesBuffer,
			deltaTime = SystemAPI.Time.DeltaTime,
			parentLookup = SystemAPI.GetComponentLookup<Parent>(true),
			ptmLookup = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
			localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
		};

		var jh = computeRootMotionJob.ScheduleParallel(dependsOn);
		return jh;
	}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	JobHandle AnimateBlendShapeWeights(ref SystemState ss, JobHandle dependsOn)
	{
		var animateBlendWeightsJob = new AnimateBlendShapeWeightsJob()
		{
			animationToProcessLookup = SystemAPI.GetBufferLookup<AnimationToProcessComponent>(true),
			cullAnimationsLookup = SystemAPI.GetComponentLookup<CullAnimationsTag>(true)
		};
		var jh = animateBlendWeightsJob.ScheduleParallel(dependsOn);
		return jh;
	}
	
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	public void OnUpdate(ref SystemState ss)
	{
		ref var runtimeData = ref SystemAPI.GetSingletonRW<RuntimeAnimationData>().ValueRW;
		
		var entityCount = animatedObjectQuery.CalculateEntityCount();
		if (entityCount == 0) return;
		
		bonePosesOffsetsArr.Resize(entityCount + 1, NativeArrayOptions.UninitializedMemory);
		var chunkBaseEntityIndices = animatedObjectQuery.CalculateBaseEntityIndexArrayAsync(ss.WorldUpdateAllocator, ss.Dependency, out var baseIndexCalcJH);
		var entitiesArr = animatedObjectQuery.ToEntityListAsync(ss.WorldUpdateAllocator, ss.Dependency, out var entityArrJH);

		var combinedJH = JobHandle.CombineDependencies(baseIndexCalcJH, entityArrJH);
		
		//	Define array with bone pose offsets for calculated bone poses
		var calcBoneOffsetsJH = PrepareComputationData(ref ss, chunkBaseEntityIndices, ref runtimeData, entitiesArr, combinedJH);
		
		//	Animate blend shape weights
		var animateBlendShapeWeights = AnimateBlendShapeWeights(ref ss, calcBoneOffsetsJH);
		
		//	Curves that control controller parameters
		var parameterControlByCurvesJob = ProcessAnimatorParameterCurves(ref ss, calcBoneOffsetsJH);
		
		var combinedJH2 = JobHandle.CombineDependencies(parameterControlByCurvesJob, animateBlendShapeWeights);

		//	Spawn jobs for animation calculation
		var computeAnimationJH = AnimationCalculation(ref ss, entitiesArr, runtimeData, combinedJH2);
		
		//	Copy entities poses into animation buffer for non-animated parts
		var copyEntityTransformsIntoAnimationBufferJH = CopyEntityBonesToAnimationTransforms(ref ss, ref runtimeData, computeAnimationJH);
		
		//	Compute root motion
		var rootMotionJH = ComputeRootMotion(ref ss, runtimeData, copyEntityTransformsIntoAnimationBufferJH);
		
		//	Make world space transforms out of local bone poses
		var makeAbsTransformsJH = MakeAbsoluteBoneTransforms(ref ss, runtimeData, rootMotionJH);

		ss.Dependency = makeAbsTransformsJH;
	}
}
}
