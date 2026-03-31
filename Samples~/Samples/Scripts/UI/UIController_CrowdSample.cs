using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.UI;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
[BurstCompile]
class UIController_CrowdSample: MonoBehaviour
{
	public TextMeshProUGUI spawnCountLabel;
	public TextMeshProUGUI counterLabel;
	public TextMeshProUGUI bonesCountLabel;
	public TextMeshProUGUI verticesCountLabel;
	public Slider spawnCountSlider;
	public Button spawnBtn;
	public Button despawnBtn;
	public Toggle visualizeSkeletonsToggle;
	public TMP_Dropdown modeSelector;
	public SimpleCameraDollyCart cameraCart;
	public GameObject mmRoot;
	public TextMeshProUGUI mmSwitchDistanceLabel;
	public Slider mmSwitchDistanceSlider;
	public TextMeshProUGUI animatorModeLabel;

	EntityQuery
		spawnerQuery,
		rigsQuery,
		skinnedMeshQuery,
		visualizedRigsQuery,
		notVisualizedRigsQuery,
		gpuEntitiesQuery,
		cpuEntitiesQuery;
	EntityManager em;
	DistanceBasedMixedModeAnimatorSystem dbs;

/////////////////////////////////////////////////////////////////////////////////

	void Start()
	{
		spawnBtn.onClick.AddListener(SpawnPrefabs);
		despawnBtn.onClick.AddListener(DespawnEntities);

		var worlds = World.All;
		foreach (var w in worlds)
		{
			if (RukhankaSystemsBootstrap.IsClientOrLocalSimulationWorld(w))
			{
				em = w.EntityManager;
				break;
			}
		}
		
		dbs = em.World.GetExistingSystemManaged<DistanceBasedMixedModeAnimatorSystem>();
		
		spawnerQuery = new EntityQueryBuilder(Allocator.Temp)
			.WithAll<SpawnPrefabComponent>()
			.Build(em);

		rigsQuery = new EntityQueryBuilder(Allocator.Temp)
			.WithAllRW<RigDefinitionComponent>()
			.Build(em);
		
		skinnedMeshQuery = new EntityQueryBuilder(Allocator.Temp)
			.WithAllRW<SkinnedMeshRendererComponent>()
			.Build(em);
		
		visualizedRigsQuery = new EntityQueryBuilder(Allocator.Temp)
			.WithAll<RigDefinitionComponent, BoneVisualizationComponent>()
			.Build(em);
		
		notVisualizedRigsQuery = new EntityQueryBuilder(Allocator.Temp)
			.WithAll<RigDefinitionComponent>()
			.WithNone<BoneVisualizationComponent>()
			.Build(em);
		
		gpuEntitiesQuery = new EntityQueryBuilder(Allocator.Temp)
			.WithAll<GPUAnimationEngineTag>()
			.Build(em);
		
		cpuEntitiesQuery = new EntityQueryBuilder(Allocator.Temp)
			.WithDisabled<GPUAnimationEngineTag>()
			.Build(em);
		
		AnimatorModeChange(false);

	#if !RUKHANKA_DEBUG_INFO
		visualizeSkeletonsToggle.enabled = false;
		visualizeSkeletonsToggle.isOn = false;
		var tmp = visualizeSkeletonsToggle.GetComponentInChildren<TextMeshProUGUI>();
		tmp.text += " (RUKHANKA_DEBUG_INFO is not defined)";
		tmp.color = Color.gray;
	#endif
	}

/////////////////////////////////////////////////////////////////////////////////

	void SpawnPrefabs()
	{
		var spawners = spawnerQuery.ToEntityArray(Allocator.Temp);

		var multiplier = 1.0f;
	#if ENABLE_INPUT_SYSTEM
		multiplier = Keyboard.current.ctrlKey.isPressed ? 10 : 1;
	#endif
		foreach (var s in spawners)
		{
			var scc = new SpawnCommandComponent()
			{
				spawnCount = (int)(spawnCountSlider.value / spawners.Length * multiplier),
				boneVisualizationOn = visualizeSkeletonsToggle.isOn,
				gpuAnimator = modeSelector.value == 1
			};

			em.AddComponentData(s, scc);
		}
	}
	
/////////////////////////////////////////////////////////////////////////////////

	void DespawnEntities()
	{
		var rigsArr = rigsQuery.ToEntityArray(Allocator.Temp);
		var multiplier = 1.0f;
	#if ENABLE_INPUT_SYSTEM
		multiplier = Keyboard.current.ctrlKey.isPressed ? 10 : 1;
	#endif
		var numToDespawn = math.min(rigsArr.Length, (int)(spawnCountSlider.value * multiplier));

		var allRigsIndices = new NativeList<int>(rigsArr.Length, Allocator.Temp);
		var rigsToDespawn = new NativeArray<int>(numToDespawn, Allocator.Temp);
		
		for (int i = 0; i < rigsArr.Length; ++i)
		{
			allRigsIndices.Add(i);
		}
		
		var rng = new Unity.Mathematics.Random((uint)numToDespawn + 1);
		for (var i = 0; i < rigsToDespawn.Length; ++i)
		{
			var randomIndex = rng.NextInt(allRigsIndices.Length);
			rigsToDespawn[i] = allRigsIndices[randomIndex];
			allRigsIndices.RemoveAtSwapBack(randomIndex);
		}
		
		for (var i = 0; i < rigsToDespawn.Length; ++i)
		{
			em.DestroyEntity(rigsArr[rigsToDespawn[i]]);
		}
	}

/////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	static int CalculateTotalBonesCount(ref EntityQuery eq)
	{
		var rv = 0;
		var rigs = eq.ToComponentDataArray<RigDefinitionComponent>(Allocator.Temp);
		foreach (var r in rigs)
		{
			rv += r.rigBlob.Value.bones.Length;
		}

		return rv;
	}
    
/////////////////////////////////////////////////////////////////////////////////

	[BurstCompile]
	static int CalculateTotalSkinnedVerticesCount(ref EntityQuery eq)
	{
		var rv = 0;
		var sms = eq.ToComponentDataArray<SkinnedMeshRendererComponent>(Allocator.Temp);
		foreach (var sm in sms)
		{
			rv += sm.smrInfoBlob.Value.meshVerticesCount;
		}

		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////

	public void SwitchBoneVisualization(Toggle t)
	{
		if (t.isOn)
		{
			em.AddComponent<BoneVisualizationComponent>(notVisualizedRigsQuery);
		}
		else
		{
			em.RemoveComponent<BoneVisualizationComponent>(visualizedRigsQuery);
		}
	}

/////////////////////////////////////////////////////////////////////////////////

	public void AnimatorModeChange(bool switchAnimationEngine)
	{
		switch (modeSelector.value)
		{
		case 0:
			dbs.Enabled = false;
			mmRoot.SetActive(false);
			if (switchAnimationEngine)
				em.SetComponentEnabled<GPUAnimationEngineTag>(gpuEntitiesQuery, false);
			animatorModeLabel.text = "All entities use <color=#0FF>CPU</color> animator";
			break;
		case 1:
			dbs.Enabled = false;
			mmRoot.SetActive(false);
			if (switchAnimationEngine)
				em.SetComponentEnabled<GPUAnimationEngineTag>(cpuEntitiesQuery, true);
			animatorModeLabel.text = "All entities use <color=#0F0>GPU</color> animator";
			break;
		case 2:
			mmRoot.SetActive(true);
			dbs.Enabled = true;
			animatorModeLabel.text = "Camera distance based animator switch";
			break;
		}
	}

/////////////////////////////////////////////////////////////////////////////////

	public void SwitchCameraCartMovement(Toggle toggle)
	{
		cameraCart.enabled = toggle.isOn;
	}

/////////////////////////////////////////////////////////////////////////////////

	void Update()
	{
		spawnCountLabel.text = $"{spawnCountSlider.value}";
		var animatedObjectsCount = rigsQuery.CalculateEntityCount();
		counterLabel.text = $"Total Instance Count: {animatedObjectsCount}";
		var totalBonesCount = CalculateTotalBonesCount(ref rigsQuery);
		bonesCountLabel.text = $"Total Bone Count: {totalBonesCount}";
		mmSwitchDistanceLabel.text = $"{mmSwitchDistanceSlider.value}";
		if (verticesCountLabel != null)
		{
			var totalVerticesCount = CalculateTotalSkinnedVerticesCount(ref skinnedMeshQuery);
			verticesCountLabel.text = $"Total Skinned Vertices Count: {totalVerticesCount}";
		}
		dbs.switchDistance = mmSwitchDistanceSlider.value;
	}
}
}

