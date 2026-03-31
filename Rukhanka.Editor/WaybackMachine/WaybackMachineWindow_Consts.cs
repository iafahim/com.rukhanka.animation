
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using UnityEngine;

namespace Rukhanka.Editor
{
public partial class WaybackMachineWindow
{
    public static readonly int BINARY_VERSION = 1;
    public static readonly uint BINARY_MAGIC = 0x4D425752;
    
    public static readonly int TIMELINE_TICKS_MIN_SKIP_SPACE = 40;
    public static readonly int TIMELINE_MOUSE_SCROLL_SPEED = 10;
    
    public static readonly string DEFAULT_WORLD_TEXT = "<Pick World>";
    public static readonly string DEFAULT_ENTITY_TEXT = "<Pick Entity>";
    
    public static readonly float EVENT_SHAPE_RADIUS = 5;
    public static readonly float EVENT_ROW_HEIGHT = 26;
    public static readonly Color EVENT_TEXT_COLOR = new Color(1, 1, 1, 0.3f);
    public static readonly Color ANIMATOR_EVENT_COLOR = new Color(0.0f, 0.0f, 0.5450981f, 1f);
    public static readonly Color ANIMATOR_EVENT_ENTER_EXIT_MARKER_COLOR = new Color(1, 1, 1, 1);
    public static readonly Color EVENT_OUTLINE_COLOR = new Color(1, 1, 1, 1);
    public static readonly Color ANIMATION_EVENT_COLOR = new Color(0.0f, 0.3921569f, 0.0f, 1f);
    
    public static readonly Color ANIMATION_BAR_COLOR = new Color(0.0f, 0.5019608f, 0.5019608f, 1f);
    public static readonly Color ANIMATION_HISTORY_WEIGHT_LINE_COLOR = new Color(1f, 0.92156863f, 0.015686275f, 1f);
    public static readonly Color ANIMATION_HISTORY_TIME_LINE_COLOR = new Color(1, 0.5f, 0, 1);
    public static readonly float ANIMATION_BAR_HEIGHT = 60;
    public static readonly float ANIMATION_BAR_HORIZONTAL_SPACE = 4;
    public static readonly float ANIMATION_ROW_HEIGHT = ANIMATION_BAR_HEIGHT + ANIMATION_BAR_HORIZONTAL_SPACE;
    
    public static readonly float STATE_BAR_HEIGHT = 60;
    public static readonly float STATE_BAR_HORIZONTAL_SPACE = 4;
    public static readonly float STATE_ROW_HEIGHT = STATE_BAR_HEIGHT + STATE_BAR_HORIZONTAL_SPACE;
    public static readonly float STATE_BAR_VERTICAL_SPACE = 1;
    public static readonly Color STATE_BAR_COLOR1 = new Color(0.8f, 0.4470588f, 0.0f, 1f);
    public static readonly Color STATE_BAR_COLOR2 = new Color(0.2f, 0.4470588f, 0.8f, 1f);
}
}
