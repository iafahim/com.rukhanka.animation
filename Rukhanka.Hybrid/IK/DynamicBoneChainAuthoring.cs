using UnityEngine;

////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Hybrid
{
public class DynamicBoneChainAuthoring: MonoBehaviour
{
    public Transform tip;
    [Range(0, 1)]
    public float inertia = 1;
    [Range(0, 1)]
    public float damping = 0.1f;
    [Range(0, 1)]
    public float elasticity = 0.1f;
    [Range(0, 1)]
    public float stiffness = 0.1f;
    
////////////////////////////////////////////////////////////////////////////////////////

    void OnEnable() { }
}
}
