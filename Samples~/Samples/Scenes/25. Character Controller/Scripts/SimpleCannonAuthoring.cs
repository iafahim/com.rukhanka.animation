#if RUKHANKA_SAMPLES_WITH_CHARACTER_CONTROLLER
using Unity.Entities;
using UnityEngine;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
public struct CannonballTag: IComponentData {}
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    
public class SimpleCannonAuthoring: MonoBehaviour
{
    public GameObject cannonPrefab;
    public GameObject target;
    public float distanceToTarget;
    public float startSpeed;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public class SimpleCannonBaker: Baker<SimpleCannonAuthoring>
{
    public override void Bake(SimpleCannonAuthoring a)
    {
        var ccc = new SimpleCannonComponent()
        {
            cannonBallPrefab = GetEntity(a.cannonPrefab, TransformUsageFlags.Dynamic),
            targetEntity = GetEntity(a.target, TransformUsageFlags.Dynamic),
            distanceFromTarget = a.distanceToTarget,
            startSpeed = a.startSpeed
        };
        var e = GetEntity(TransformUsageFlags.None);
        AddComponent(e, ccc);
    }
}
}
#endif
