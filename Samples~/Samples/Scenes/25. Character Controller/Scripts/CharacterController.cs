#if RUKHANKA_SAMPLES_WITH_CHARACTER_CONTROLLER 
using Unity.Burst;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
[BurstCompile]
partial struct CharacterControllerSystem: ISystem
{
    static readonly FastAnimatorParameter groundedParam = new ("Grounded");
    static readonly FastAnimatorParameter verticalSpeedParam = new ("VerticalSpeed");
    static readonly FastAnimatorParameter airbornVerticalSpeedParam = new ("AirborneVerticalSpeed");
    static readonly FastAnimatorParameter forwardSpeedParam = new ("ForwardSpeed");
    static readonly FastAnimatorParameter inputDetectedParam = new ("InputDetected");
    static readonly FastAnimatorParameter angleDeltaRadParam = new ("AngleDeltaRad");
    static readonly FastAnimatorParameter randomIdleParam = new ("RandomIdle");
    
    Random rng;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void OnCreate(ref SystemState ss)
    {
        rng = new Random(0xabcabc01);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
 
    float DeltaAngle(float current, float target)
    {
        float num = Mathf.Repeat(target - current, math.PI * 2);
        if (num > math.PI)
            num -= math.PI * 2;
        return num;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void OnUpdate(ref SystemState ss)
    {
        foreach (var (kcb, acpitc, acpc, lt, tpcc) in SystemAPI.Query<
                     RefRO<KinematicCharacterBody>,
                     AnimatorControllerParameterIndexTableComponent,
                     DynamicBuffer<AnimatorControllerParameterComponent>,
                     LocalTransform,
                     ThirdPersonCharacterControl
                 >())
        {
            var apa = new AnimatorParametersAspect(acpc, acpitc);
            var isGrounded = kcb.ValueRO.IsGrounded;
            apa.SetBoolParameter(groundedParam, isGrounded);
            var vertSpeed = math.select(0, kcb.ValueRO.RelativeVelocity.y, isGrounded);
            var airborneVertSpeed = kcb.ValueRO.RelativeVelocity.y;
            apa.SetFloatParameter(verticalSpeedParam, vertSpeed);
            if (!isGrounded)
                apa.SetFloatParameter(airbornVerticalSpeedParam, airborneVertSpeed);
            var fwdSpeed = math.length(kcb.ValueRO.RelativeVelocity);
            apa.SetFloatParameter(forwardSpeedParam, fwdSpeed);
            
            var curAngle = math.atan2(lt.Forward().x, lt.Forward().z);
            var targetAngle = math.atan2(kcb.ValueRO.RelativeVelocity.x, kcb.ValueRO.RelativeVelocity.z);
            var deltaAngle = DeltaAngle(curAngle, targetAngle);
            apa.SetFloatParameter(angleDeltaRadParam, deltaAngle);
            
            //  Attack
            var randomVal = rng.NextInt(0, 3);
            apa.SetIntParameter(randomIdleParam, randomVal);
            
            var inputDetected = math.length(tpcc.MoveVector) > 0;
            apa.SetBoolParameter(inputDetectedParam, inputDetected);
        }
    }
}
//----------------------------------------------------------------------------------------------------------------//
[UpdateInGroup(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(PhysicsCreateJacobiansGroup))]
[UpdateAfter(typeof(PhysicsCreateContactsGroup))]
[RequireMatchingQueriesForUpdate]
public partial struct HandleCannonballHitsSystem: ISystem
{
    static readonly FastAnimatorParameter hurtParam = new ("Hurt");
    static readonly FastAnimatorParameter hurtFromXParam = new ("HurtFromX");
    static readonly FastAnimatorParameter hurtFromYParam = new ("HurtFromY");
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [BurstCompile]
    private struct HandleCannonballCollisionsJob : IContactsJob
    {
        [ReadOnly]
        public ComponentLookup<CannonballTag> cannonBallLookup;
        [ReadOnly]
        public ComponentLookup<AnimatorControllerParameterIndexTableComponent> indexTableComponentLookup;
        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransformLookup;
        public BufferLookup<AnimatorControllerParameterComponent> parametersLookup;
        
        public EntityCommandBuffer ecb;
        
        public FastAnimatorParameter hurtFromXParam;
        public FastAnimatorParameter hurtFromYParam;
        public FastAnimatorParameter hurtParam;
        
        public void Execute(ref ModifiableContactHeader header, ref ModifiableContactPoint point)
        {
            var cannonballEntity = Entity.Null;
            
            if (cannonBallLookup.HasComponent(header.EntityA))
                cannonballEntity = header.EntityA;
            if (cannonBallLookup.HasComponent(header.EntityB))
                cannonballEntity = header.EntityB;
            
            var characterEntity = Entity.Null;
            if (parametersLookup.HasBuffer(header.EntityA))
                characterEntity = header.EntityA;
            if (parametersLookup.HasBuffer(header.EntityB))
                characterEntity = header.EntityB;
            
            if (cannonballEntity == Entity.Null || characterEntity == Entity.Null)
                return;
            
            parametersLookup.TryGetBuffer(characterEntity, out var animationParamBuffer);
            var paramsIndexTable = indexTableComponentLookup.GetRefRO(characterEntity).ValueRO;
            var apa = new AnimatorParametersAspect(animationParamBuffer, paramsIndexTable);
            
            var lt = localTransformLookup.GetRefRO(characterEntity).ValueRO;
            var hitNormalLocalSpace = -math.rotate(math.inverse(lt.Rotation), header.Normal);
            
            apa.SetTrigger(hurtParam);
            apa.SetFloatParameter(hurtFromXParam, math.sign(hitNormalLocalSpace.x));
            apa.SetFloatParameter(hurtFromYParam, math.sign(hitNormalLocalSpace.z));
            ecb.RemoveComponent<CannonballTag>(cannonballEntity);
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void OnUpdate(ref SystemState ss)
    {
        PhysicsWorld physicsWorld = SystemAPI.GetSingletonRW<PhysicsWorldSingleton>().ValueRW.PhysicsWorld;
        SimulationSingleton simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        
		var ecbs = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
		var ecb = ecbs.CreateCommandBuffer(ss.WorldUnmanaged);
        
        var handleCollisionJob = new HandleCannonballCollisionsJob()
        {
            hurtParam = hurtParam,
            hurtFromXParam = hurtFromXParam,
            hurtFromYParam = hurtFromYParam,
            parametersLookup = SystemAPI.GetBufferLookup<AnimatorControllerParameterComponent>(),
            indexTableComponentLookup = SystemAPI.GetComponentLookup<AnimatorControllerParameterIndexTableComponent>(true),
            cannonBallLookup = SystemAPI.GetComponentLookup<CannonballTag>(true),
            localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            ecb = ecb
        };
        
        ss.Dependency = handleCollisionJob.Schedule(simulationSingleton, ref physicsWorld, ss.Dependency);
    }
}
}
#endif