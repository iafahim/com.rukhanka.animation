#if RUKHANKA_SAMPLES_WITH_CHARACTER_CONTROLLER
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = Unity.Mathematics.Random;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{

public struct SimpleCannonComponent: IComponentData
{
    public float distanceFromTarget;
    public float startSpeed;
    public Entity cannonBallPrefab;
    public Entity targetEntity;
}
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[BurstCompile]
public partial struct SimpleCannonSystem: ISystem
{
    Random rng;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void OnCreate(ref SystemState ss)
    {
        rng = new Random(0x12312313);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    float3 GetWorldPosition(ref SystemState ss, Entity e)
    {
        var targetPose = SystemAPI.GetComponent<LocalTransform>(e);
        var bt = new BoneTransform(targetPose);
        while (SystemAPI.HasComponent<Parent>(e))
        {
            var pe = SystemAPI.GetComponent<Parent>(e);
            var parentPose = SystemAPI.GetComponent<LocalTransform>(pe.Value);
            var pbt = new BoneTransform(parentPose);
            bt = BoneTransform.Multiply(pbt, bt);
            e = pe.Value;
        }
        return bt.pos;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void OnUpdate(ref SystemState ss)
    {
        if (!Keyboard.current.fKey.wasPressedThisFrame)
            return;
        
		var ecbs = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbs.CreateCommandBuffer(ss.WorldUnmanaged);
        
        foreach (var (ccc, _) in SystemAPI.Query<SimpleCannonComponent>().WithEntityAccess())
        {
            var targetPos = GetWorldPosition(ref ss, ccc.targetEntity);
            var randomDir = rng.NextFloat3Direction();
            randomDir.y = randomDir.y * 0.5f + 0.5f;
            
            var cannonEntity = ss.EntityManager.Instantiate(ccc.cannonBallPrefab);
            var l2w = new LocalTransform()
            {
                Position = targetPos + randomDir * ccc.distanceFromTarget,
                Rotation = quaternion.identity,
                Scale = 1
            };
            var pv = new PhysicsVelocity()
            {
                Angular = float3.zero,
                Linear = -randomDir * ccc.startSpeed
            };
            SystemAPI.SetComponent(cannonEntity, pv);
            SystemAPI.SetComponent(cannonEntity, l2w);
            ecb.AddComponent<CannonballTag>(cannonEntity);
        }
    }
}
}
#endif