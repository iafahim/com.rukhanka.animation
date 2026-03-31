using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{ 
[DisableAutoCreation]
[UpdateAfter(typeof(AnimationApplicationSystem))]
public partial class WaybackMachineRecordSystemGroup: ComponentSystemGroup
{
    protected override void OnCreate()
    {
        base.OnCreate();
        RateManager = new RateUtils.FixedRateCatchUpManager(1 / 60.0f);
    }
}
}

