using UnityEngine;
using Unity.Assertions;
using UnityEngine.UI;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
class ProceduralAnimationsSampleConf: MonoBehaviour
{
    public Slider dynamicBoneInertia;
    public Slider dynamicBoneDamping;
    public Slider dynamicBoneElasticity;
    public Slider dynamicBoneStiffness;
    
    public static ProceduralAnimationsSampleConf Instance { get; private set; }
    
/////////////////////////////////////////////////////////////////////////////////

    void Awake()
    {
        Assert.IsNull(Instance);
        Instance = this;
    }
}
}
