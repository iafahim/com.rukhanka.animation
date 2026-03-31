
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
public struct WaybackMachineSettings
{
    public enum RulerMode
    {
        Seconds,
        Frames
    }
    public RulerMode rulerMode;
    
    public int statesVisible;
    public int animationsVisible;
    public int eventsVisible;
    public bool IsEventsVisible() => eventsVisible != 0;
    public bool IsAnimationsVisible() => animationsVisible != 0;
    public bool IsStatesVisible() => statesVisible != 0;
    
    public bool eventLabels;
    public bool animationWeightGraphs;
    public bool animationTimeGraphs;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static WaybackMachineSettings MakeDefault()
    {
        var rv = new WaybackMachineSettings()
        {
            statesVisible = 1,
            animationsVisible = 1,
            eventsVisible = 1,
            rulerMode = RulerMode.Frames,
            animationTimeGraphs = false,
            eventLabels = true,
            animationWeightGraphs = false,
        };
        return rv;
    }
}
}
