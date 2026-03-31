using System;
using System.Collections.Generic;
using Rukhanka.Toolbox;
using Rukhanka.WaybackMachine;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
public partial class WaybackMachineWindow: EditorWindow
{
    [SerializeField]
    private VisualTreeAsset windowAsset = default;
    [SerializeField]
    private VisualTreeAsset leftPaneAsset = default;
    [SerializeField]
    private VisualTreeAsset rightPaneAsset = default;
    [SerializeField]
    private VisualTreeAsset stateInfoAsset = default;
    [SerializeField]
    private VisualTreeAsset eventInfoAsset = default;
    [SerializeField]
    private VisualTreeAsset animInfoAsset = default;
    
    TwoPaneSplitView splitView;
    MinMaxSlider minMaxSlider;
    TimelineHeader timelineHeader;
    TimelineContent timelineContent;
    NativeReference<TimelinePortalData> timelinePortal;
    VisualElement recordBtn;
    ToolbarBreadcrumbs entityPath;
    ToolbarButton entityPathWorld, entityPathEntity;
    ToolbarButton globalSettingsButton;
    ToolbarButton previewBtn;
    Label memoryStat;
    ToolbarButton saveBtn;
    
    VisualElement leftPane, rightPane;
    
    Label statesHeader;
    VisualElement statesBarBodyVE;
    Button stateSettingsButton;
    
    Label animationsHeader;
    VisualElement animBarBodyVE;
    Button animationSettingsButton;
    
    Label eventsHeader;
    VisualElement eventsBarBodyVE;
    Button eventSettingsButton;
    
    List<VisualElement> stateInfoWidgets = new ();
    List<VisualElement> eventInfoWidgets = new ();
    List<VisualElement> animInfoWidgets = new ();
    
    int selectedWorldIndex = -1;
    Entity selectedEntity = Entity.Null;
    
    Action<PlayModeStateChange> playModeChangeFn;
    NativeReference<WaybackMachineData> recordedData;
    EntityQuery recordSingletonEq, playbackSingletonEq, animationDatabaseSingletonEq;
    
    int prevKnobFrame = 1000000;
    WaybackMachineSettings settings;
    
    public const string iconPath = "Packages/com.rukhanka.animation/Rukhanka.Editor/Editor Default Resources/Icons/RukhankaWaybackMachine@16.png";
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [MenuItem("Window/Rukhanka Animation/Animation Wayback Machine")]
    public static void ShowExample()
    {
        var wnd = GetWindow<WaybackMachineWindow>();
        var icon = AssetDatabase.LoadAssetAtPath(iconPath, typeof(Texture)) as Texture;
        wnd.titleContent = new GUIContent("Rukhanka Wayback Machine", icon);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SetupIconImage(Button button, Background img)
    {
    #if !UNITY_2023_2_OR_NEWER
        var icon = button.Q<Image>();
        if(icon == null)
            button.Add(icon = new Image() {name = "LegacyIcon" });
        icon.image = img.texture;
    #else
        button.iconImage = img;
    #endif
    }

    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public unsafe void CreateGUI()
    {
        playModeChangeFn = (playModeStateChange) =>
        {
            PlayModeStateChanged(this);
        };
        EditorApplication.playModeStateChanged += playModeChangeFn;
        
        settings = WaybackMachineSettings.MakeDefault();
        
        recordedData = new (Allocator.Persistent);
        recordedData.GetUnsafePtr()->Construct();
        
        timelinePortal = new (Allocator.Persistent);
        timelinePortal.GetUnsafePtr()->Construct();
        
        // Each editor window contains a root VisualElement object
        var root = rootVisualElement;
        var doc = windowAsset.Instantiate();
        root.Add(doc);

        splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
        splitView.RegisterCallback<GeometryChangedEvent>(OnGeometryChange);
        root.Add(splitView);
        
        leftPane = leftPaneAsset.Instantiate();
        rightPane = rightPaneAsset.Instantiate();
        splitView.Add(leftPane);
        splitView.Add(rightPane);
        
        var tlh = rightPane.Q<VisualElement>("timeline");
        timelineHeader = new TimelineHeader();
        timelineHeader.timelinePortal = timelinePortal;
        tlh.Add(timelineHeader);
        
        var gearIconBackground = new Background() { texture = (Texture2D)EditorGUIUtility.IconContent("_Popup").image };
        
        animBarBodyVE = leftPane.Q<VisualElement>("animbody");
        animationsHeader = leftPane.Q<Label>("animheader");
        animationSettingsButton = animationsHeader.Q<Button>("settings");
        SetupIconImage(animationSettingsButton, gearIconBackground);
        animationSettingsButton.clickable.clicked += SelectAnimationOptions;
        animationsHeader.RegisterCallback<MouseDownEvent>(_ => ToggleVisibility(ref settings.animationsVisible, animationsHeader, animBarBodyVE));
        
        statesBarBodyVE = leftPane.Q<VisualElement>("smbody");
        statesHeader = leftPane.Q<Label>("smheader");
        stateSettingsButton = statesHeader.Q<Button>("settings");
        SetupIconImage(stateSettingsButton, gearIconBackground);
        statesHeader.RegisterCallback<MouseDownEvent>(_ => ToggleVisibility(ref settings.statesVisible, statesHeader, statesBarBodyVE));
        
        eventsBarBodyVE = leftPane.Q<VisualElement>("eventsbody");
        eventsHeader = leftPane.Q<Label>("eventsheader");
        eventSettingsButton = eventsHeader.Q<Button>("settings");
        SetupIconImage(eventSettingsButton, gearIconBackground);
        eventSettingsButton.clickable.clicked += SelectEventOptions;
        eventsHeader.RegisterCallback<MouseDownEvent>(_ => ToggleVisibility(ref settings.eventsVisible, eventsHeader, eventsBarBodyVE));
        
        var tlc = rightPane.Q<VisualElement>("content");
        timelineContent = new TimelineContent();
        timelineContent.timelinePortal = timelinePortal;
        timelineContent.recordedData = recordedData;
        timelineContent.textMeasurer = rightPane.Q<Label>("text-measurer");
        timelineContent.RegisterCallback<WheelEvent>(MouseScrollOnTimeline);
        tlc.Add(timelineContent);
        
        minMaxSlider = rightPane.Q<MinMaxSlider>("slider");
        minMaxSlider.minValue = minMaxSlider.lowLimit;
        minMaxSlider.maxValue = minMaxSlider.highLimit;
        
        recordBtn = doc.Q<ToolbarButton>("record-btn");
        recordBtn.generateVisualContent += DrawRecordBtn;
        recordBtn.RegisterCallback<ClickEvent>(ToggleRecording);
        
        entityPath = doc.Q<ToolbarBreadcrumbs>("entity-path");
        entityPath.PushItem(DEFAULT_WORLD_TEXT);
        entityPathWorld = entityPath[0] as ToolbarButton;
        entityPathWorld.RegisterCallback<MouseDownEvent>(SelectWorldMenu, TrickleDown.TrickleDown);
        
        entityPath.PushItem(DEFAULT_ENTITY_TEXT);
        entityPathEntity = entityPath[1] as ToolbarButton;
        entityPathEntity.RegisterCallback<MouseDownEvent>(SelectEntityMenu, TrickleDown.TrickleDown);
        
        globalSettingsButton = doc.Q<ToolbarButton>("settings-btn");
        SetupIconImage(globalSettingsButton, gearIconBackground);
        globalSettingsButton.RegisterCallback<MouseDownEvent>(SelectGlobalOptions, TrickleDown.TrickleDown);
        
        previewBtn = doc.Q<ToolbarButton>("preview-btn");
        previewBtn.RegisterCallback<ClickEvent>(TogglePreview, TrickleDown.TrickleDown);
        
        saveBtn = doc.Q<ToolbarButton>("save-btn");
        var saveIcon = new Background() { texture = (Texture2D)EditorGUIUtility.IconContent("SaveAs").image };
        SetupIconImage(saveBtn, saveIcon);
        saveBtn.clickable.clicked += SaveRecordedData;
        
        var importBtn = doc.Q<ToolbarButton>("import-btn");
        var importIcon = new Background() { texture = (Texture2D)EditorGUIUtility.IconContent("Import").image };
        SetupIconImage(importBtn, importIcon);
        importBtn.clickable.clicked += LoadRecordedData;
        
        memoryStat = doc.Q<Label>("memorystat");
    #if !RUKHANKA_DEBUG_INFO
        var noDebugInfoWarningLabel = doc.Q<Label>("nodebuginfowarning");
        noDebugInfoWarningLabel.text = "Enable 'Debug and Validation Mode' for object names";
    #endif
        
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    static void PlayModeStateChanged(WaybackMachineWindow w)
    {
        if (w != null)
            w.ResetEntityPath();
        
        w.DisposeEntityQueries();
        w.StopRecord();
        w.StopPreview();
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    unsafe void DisposeEntityQueries()
    {
        if (animationDatabaseSingletonEq.__impl != null)
            animationDatabaseSingletonEq.Dispose();
        if (playbackSingletonEq.__impl != null)
            playbackSingletonEq.Dispose();
        if (recordSingletonEq.__impl != null)
            recordSingletonEq.Dispose();
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void ResetEntityPath()
    {
        selectedWorldIndex = -1;
        selectedEntity = Entity.Null;
        entityPathEntity.text = DEFAULT_ENTITY_TEXT;
        entityPathWorld.text = DEFAULT_WORLD_TEXT;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    World GetSelectedWorld()
    {
        if (selectedWorldIndex < 0 || selectedWorldIndex >= World.All.Count)
            return null;
        
        return World.All[selectedWorldIndex];
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    string GetEntityName(EntityManager em, Entity e)
    {
        var eName = $"{em.GetName(e)} {e}";
        return eName;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SelectEntityMenu(MouseDownEvent evt)
    {
        var world = GetSelectedWorld();
        if (world == null)
            return;
        
        var eq = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<RigDefinitionComponent>()
            .Build(world.EntityManager);
        
        if (eq.IsEmpty)
            return;
        
        var entities = eq.ToEntityArray(Allocator.Temp);
        var m = new GenericDropdownMenu();
        
        for (var i = 0; i < entities.Length; ++i)
        {
            var e = entities[i];
            var eName = GetEntityName(world.EntityManager, e);
            m.AddItem(eName, false, _ => SelectEntity(e), null);
        }
        DropDown(m, entityPathEntity);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SelectAnimationOptions()
    {
        var m = new GenericDropdownMenu();
        
        m.AddItem("Time Graphs", settings.animationTimeGraphs, _ => settings.animationTimeGraphs = !settings.animationTimeGraphs, null);
        m.AddItem("Weight Graphs", settings.animationWeightGraphs, _ => settings.animationWeightGraphs = !settings.animationWeightGraphs, null);
        DropDown(m, animationSettingsButton);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SelectEventOptions()
    {
        var m = new GenericDropdownMenu();
        
        m.AddItem("Event Labels", settings.eventLabels, _ => settings.eventLabels = !settings.eventLabels, null);
        DropDown(m, eventSettingsButton);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SelectGlobalOptions(MouseDownEvent evt)
    {
        var m = new GenericDropdownMenu();
        
        m.AddItem("Seconds", settings.rulerMode == WaybackMachineSettings.RulerMode.Seconds, _ => settings.rulerMode = WaybackMachineSettings.RulerMode.Seconds, null);
        m.AddItem("Frames", settings.rulerMode == WaybackMachineSettings.RulerMode.Frames, _ => settings.rulerMode = WaybackMachineSettings.RulerMode.Frames, null);
        m.AddSeparator("Sample Rate");
        m.AddItem("120 FPS", recordedData.Value.fpsMode == WaybackMachineData.FPSMode.FPS120, _ => ChangeFrameDuration(WaybackMachineData.FPSMode.FPS120), null);
        m.AddItem("60 FPS", recordedData.Value.fpsMode == WaybackMachineData.FPSMode.FPS60, _ => ChangeFrameDuration(WaybackMachineData.FPSMode.FPS60), null);
        m.AddItem("30 FPS", recordedData.Value.fpsMode == WaybackMachineData.FPSMode.FPS30, _ => ChangeFrameDuration(WaybackMachineData.FPSMode.FPS30), null);
        DropDown(m, globalSettingsButton);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SelectWorldMenu(MouseDownEvent evt)
    {
        var m = new GenericDropdownMenu();
        for (var i = 0; i < World.All.Count; ++i)
        {
            var world = World.All[i];
            var worldIndex = i;
            m.AddItem(world.Name, false, _ => SelectWorld(worldIndex), null);
        }
        DropDown(m, entityPathWorld);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SelectEntity(Entity e)
    {
        var world = GetSelectedWorld();
        if (world == null)
            return;
        
        var eName = GetEntityName(world.EntityManager, e);
        entityPathEntity.text = eName;
        selectedEntity = e;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SelectWorld(int worldIndex)
    {
        var prevWorldIndex = selectedWorldIndex;
        
        var world = World.All[worldIndex];
        selectedWorldIndex = worldIndex;
        entityPathWorld.text = world.Name;
        
        //  Create queries
        recordSingletonEq = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<RecordComponent>()
            .Build(world.EntityManager);
        
        playbackSingletonEq = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<PlaybackComponent>()
            .Build(world.EntityManager);
        
        animationDatabaseSingletonEq = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BlobDatabaseSingleton>()
            .Build(world.EntityManager);
        
        if (prevWorldIndex != worldIndex)
        {
            //  Reset entity
            selectedEntity = Entity.Null;
            entityPathEntity.text = DEFAULT_ENTITY_TEXT;
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void OnGeometryChange(GeometryChangedEvent evt)
    {
        Update();
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void MouseScrollOnTimeline(WheelEvent evt)
    {
        // Make scroll amount constant in screen space
        var minT = timelinePortal.Value.GetFrameForPosX(0);
        var maxT = timelinePortal.Value.GetFrameForPosX(evt.delta.y * TIMELINE_MOUSE_SCROLL_SPEED);
        var dT = maxT - minT;
            
        var f = (evt.mousePosition.x - timelineContent.worldBound.xMin) / timelineContent.worldBound.width;
        minMaxSlider.minValue -= dT * f;
        minMaxSlider.maxValue += dT * (1 - f);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    unsafe void ChangeFrameDuration(WaybackMachineData.FPSMode newMode)
    {
        if (recordedData.Value.fpsMode == newMode)
            return;
        
        //  FPS cannot be changed for existing recordings, so warn user about recorded data reset
        if (recordedData.Value.lastRecordedFrame > 0 && !EditorUtility.DisplayDialog("Wayback Machine", "Sample rate cannot be changed for existing recording. Confirm to clear recorded data.", "OK", "Cancel"))
            return;
        
        recordedData.GetUnsafePtr()->Clear();
        recordedData.GetUnsafePtr()->fpsMode = newMode;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    
    unsafe void Update()
    {
        if (recordedData.IsCreated && recordedData.Value.lastRecordedFrame > 0)
        {
            var numFrames = recordedData.Value.lastRecordedFrame;
            if (numFrames > minMaxSlider.highLimit)
            {
                var dh = numFrames - minMaxSlider.highLimit;
                minMaxSlider.highLimit = numFrames;
                var v = minMaxSlider.value;
                v.x += dh;
                minMaxSlider.value = v;
            }
            if (IsRecording() && numFrames > minMaxSlider.value.y)
            {
                var v = minMaxSlider.value;
                v.y = numFrames;
                minMaxSlider.value = v;
            }
        }
        
        if (minMaxSlider.value.y - minMaxSlider.value.x < 4)
        {
            var v = minMaxSlider.value;
            v.y = v.x + 4;
            minMaxSlider.value = v;
        }
        timelinePortal.GetUnsafePtr()->frameSizeInSec = recordedData.Value.GetFrameDuration();
        timelinePortal.GetUnsafePtr()->visibleRange = minMaxSlider.value;
        timelinePortal.GetUnsafePtr()->frameRange = new Vector2(minMaxSlider.lowLimit, minMaxSlider.highLimit);
        timelinePortal.GetUnsafePtr()->contentWidth = timelineHeader.contentRect.width;
        timelinePortal.GetUnsafePtr()->ComputeTicks();
        
        timelineContent.animHeaderHeight = animationsHeader.localBound.height;
        timelineContent.eventsHeaderHeight = eventsHeader.localBound.height;
        timelineContent.statesHeaderHeight = statesHeader.localBound.height;
        timelineContent.eventBarsCount = 0;
        timelineContent.animBarsCount = 0;
        timelineContent.stateBarsCount = 0;
        
        timelineHeader.settings = settings;
        timelineContent.settings = settings;
        timelineContent.ComputeShapes();
        
        UpdateInfoWidgetCounts();
        
        timelineHeader.MarkDirtyRepaint();
        timelineContent.MarkDirtyRepaint();
        
        UpdatePreview();
        UpdateKnobTimeInfoPanes();
        UpdateMemoryStat();
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    
    void DrawRecordBtn(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        var r = math.min(recordBtn.contentRect.width, recordBtn.contentRect.height) / 2 * 0.8f;
        var c = recordBtn.contentRect.center;
        
        p.strokeColor = Color.white;
        p.BeginPath();
        p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360.0f));
        p.Stroke();
        
        if (IsRecording())
        {
            p.fillColor = Color.red;
            p.BeginPath();
            p.Arc(c, r * 0.6f, Angle.Degrees(0), Angle.Degrees(360.0f));
            p.Fill();
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void ToggleVisibility(ref int visStatus, Label header, VisualElement body)
    {
        visStatus = visStatus ^ 1;
        var s = header.text;
        var arrowChar = visStatus != 0 ? '▼' : '◀';
        s = $"{s.Substring(0, s.Length - 1)}{arrowChar}";
        header.text = s;
        body.style.display = new StyleEnum<DisplayStyle>(visStatus != 0 ? DisplayStyle.Flex : DisplayStyle.None);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void ToggleRecording(ClickEvent evt)
    {
        if (IsRecording())
            StopRecord();
        else
            StartRecord();
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void TogglePreview(ClickEvent evt)
    {
        if (IsPreview())
            StopPreview();
        else
            StartPreview();
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void StartRecord()
    {
        var world = GetSelectedWorld();
        if (world == null || selectedEntity == Entity.Null)
            return;
        
        timelineContent.isRecording = true;
        entityPath.SetEnabled(false);
        minMaxSlider.value = new Vector2(0, 1000);
        
        var isRecording = IsRecording();
        Assert.IsFalse(isRecording);
        
        //  Adjust recording system rate manager
        var sg = world.GetExistingSystemManaged<WaybackMachineRecordSystemGroup>();
        if (sg != null)
            sg.RateManager.Timestep = recordedData.Value.GetFrameDuration();
        
        recordedData.Value.Clear();
        var rc = new RecordComponent { wbData = recordedData };
        world.EntityManager.AddComponentData(selectedEntity, rc);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void StopRecord()
    {
        timelineContent.isRecording = false;
        entityPath.SetEnabled(true);
        
        var isRecording = IsRecording();
        if (!isRecording)
            return;
        
        var world = GetSelectedWorld();
        world.EntityManager.RemoveComponent<RecordComponent>(selectedEntity);
        
        recordBtn.MarkDirtyRepaint();
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateInfoWidgetCounts()
    {
        if (settings.IsEventsVisible())
            UpdateInfoWidgetCount(eventInfoWidgets, timelineContent.eventBarsCount, EVENT_ROW_HEIGHT, eventInfoAsset, eventsBarBodyVE);
        if (settings.IsStatesVisible())
            UpdateInfoWidgetCount(stateInfoWidgets, timelineContent.stateBarsCount, STATE_ROW_HEIGHT, stateInfoAsset, statesBarBodyVE);
        if (settings.IsAnimationsVisible())
            UpdateInfoWidgetCount(animInfoWidgets, timelineContent.animBarsCount, ANIMATION_ROW_HEIGHT, animInfoAsset, animBarBodyVE);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateInfoWidgetCount(List<VisualElement> elementsArr, int newCount, float widgetHeight, VisualTreeAsset template, VisualElement parent)
    {
        if (elementsArr.Count == newCount)
            return;
        
        //  Add if to few
        var l = elementsArr.Count;
        for (var i = l; i < newCount; ++i)
        {
            var siw = template.Instantiate();
            var bkg = siw.Q<VisualElement>("background");
            bkg.style.height = new StyleLength(widgetHeight);
            bkg.visible = false;
            parent.Add(bkg);
            elementsArr.Add(bkg);
        }
        
        //  Remove if too many
        for (var i = newCount; i < l; ++i)
        {
            var siw = elementsArr[i];
            parent.Remove(siw);
        }
        var countToRemove = l - newCount;
        if (countToRemove > 0)
            elementsArr.RemoveRange(newCount, countToRemove);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateKnobTimeInfoPanes()
    {
        var knobPos = timelinePortal.Value.GetKnobPosX();
        var knobFrame = timelinePortal.Value.knobFrame;
        if (prevKnobFrame == knobFrame)
            return;
        
        foreach (var eiw in eventInfoWidgets) eiw.visible = false;
        foreach (var siw in stateInfoWidgets)
        {
            foreach (var c in siw.Children()) c.style.visibility = StyleKeyword.Null;
            siw.visible = false;
        }
        foreach (var aiw in animInfoWidgets) aiw.visible = false;
        
        UpdateKnobTimeAnimationEventInfos(knobPos);
        UpdateKnobTimeAnimatorEventInfos(knobPos);
        UpdateKnobTimeStateInfos(knobPos);
        UpdateKnobTimeStateTransitionInfos(knobPos);
        UpdateKnobTimeAnimationInfos(knobPos, knobFrame);
        
        prevKnobFrame = knobFrame;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateKnobTimeAnimationInfos(float knobPos, int knobFrame)
    {
        NativeList<int2> tmpIndicesList = new (0xff, Allocator.Temp);
        
        timelineContent.GetAnimationShapeIndicesForPos(ref tmpIndicesList, knobPos);
        for (var i = 0; i < tmpIndicesList.Length; ++i)
        {
            var indexRow = tmpIndicesList[i];
            var h = recordedData.Value.animHistory[indexRow.x];
            var w = animInfoWidgets[indexRow.y];
            
            w.Q<Label>("name").text = h.GetName();
            
            var relativeKnobFrame = knobFrame - h.frameSpan.x;
            var weight = h.historyWeights[relativeKnobFrame].value;
            var weightColor = ColorTools.ToWebColor(ANIMATION_HISTORY_WEIGHT_LINE_COLOR);
            w.Q<Label>("weight").text = $"<color={weightColor}>Weight</color>: {weight:0.##}";
            
            var time = h.historyAnimTime[relativeKnobFrame].value;
            var timeColor = ColorTools.ToWebColor(ANIMATION_HISTORY_TIME_LINE_COLOR);
            w.Q<Label>("time").text = $"<color={timeColor}>Time</color>: {time:0.##}";
            
            w.visible = true;
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateKnobTimeStateInfos(float knobPos)
    {
        NativeList<int3> tmpIndicesList = new (0xff, Allocator.Temp);
        
        timelineContent.GetStateShapeIndicesForPos(ref tmpIndicesList, knobPos);
        for (var i = 0; i < tmpIndicesList.Length; ++i)
        {
            var indexRowColor = tmpIndicesList[i];
            var h = recordedData.Value.controllerStateHistory[indexRowColor.x];
            var w = stateInfoWidgets[indexRowColor.y];
            var srcStateInfo = w.Q<VisualElement>("srcstateinfo");
            var dstStateInfo = w.Q<VisualElement>("dststateinfo");
            var transitionInfo = w.Q<VisualElement>("transitioninfo");
            
            dstStateInfo.visible = false;
            transitionInfo.visible = false;
            
            var color = ColorTools.FromInt(indexRowColor.z);
            srcStateInfo.Q<VisualElement>("color").style.backgroundColor = new StyleColor(color);
            srcStateInfo.Q<Label>("name").text = h.GetName();
            srcStateInfo.Q<Label>("id").text = h.stateId.ToString();
            
            w.visible = true;
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateKnobTimeStateTransitionInfos(float knobPos)
    {
        NativeList<int4> tmpIndicesList = new (0xff, Allocator.Temp);
        
        timelineContent.GetTransitionShapeIndicesForPos(ref tmpIndicesList, knobPos);
        for (var i = 0; i < tmpIndicesList.Length; ++i)
        {
            var indexRowColor = tmpIndicesList[i];
            var h = recordedData.Value.controllerTransitionHistory[indexRowColor.x];
            var w = stateInfoWidgets[indexRowColor.y];
            var srcStateInfo = w.Q<VisualElement>("srcstateinfo");
            var dstStateInfo = w.Q<VisualElement>("dststateinfo");
            var transitionInfo = w.Q<VisualElement>("transitioninfo");
            
            var srcColor = ColorTools.FromInt(indexRowColor.z);
            var srcState = recordedData.Value.controllerStateHistory[h.srcStateDataIndex];
            srcStateInfo.visible = true;
            srcStateInfo.Q<VisualElement>("color").style.backgroundColor = new StyleColor(srcColor);
            srcStateInfo.Q<Label>("name").text = srcState.GetName();
            srcStateInfo.Q<Label>("id").text = h.srcStateId.ToString();
            
            var dstColor = ColorTools.FromInt(indexRowColor.w);
            var dstState = recordedData.Value.controllerStateHistory[h.dstStateDataIndex];
            dstStateInfo.visible = true;
            dstStateInfo.Q<VisualElement>("color").style.backgroundColor = new StyleColor(dstColor);
            dstStateInfo.Q<Label>("name").text = dstState.GetName();
            dstStateInfo.Q<Label>("id").text = h.dstStateId.ToString();
            
            transitionInfo.visible = true;
            transitionInfo.Q<Label>("name").text = h.GetName();
            transitionInfo.Q<Label>("id").text = h.transitionId.ToString();
            
            w.visible = true;
        }
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateKnobTimeAnimationEventInfos(float knobPos)
    {
        NativeList<int2> tmpIndicesList = new (0xff, Allocator.Temp);
        
        timelineContent.GetAnimationEventShapeIndicesForPos(ref tmpIndicesList, knobPos);
        for (var i = 0; i < tmpIndicesList.Length; ++i)
        {
            var indexAndRow = tmpIndicesList[i];
            var h = recordedData.Value.animationEventHistory[indexAndRow.x];
            var w = eventInfoWidgets[indexAndRow.y];
            w.Q<Label>("name").text = h.GetName();
            w.Q<Label>("stringv").text = $"<color=green>S</color>: '{h.GetStringParam()}' <color=green>F</color>: {h.floatParam} <color=green>I</color>: {h.intParam}";
            w.visible = true;
        }
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateKnobTimeAnimatorEventInfos(float knobPos)
    {
        NativeList<int2> tmpIndicesList = new (0xff, Allocator.Temp);
        
        timelineContent.GetAnimatorEventShapeIndicesForPos(ref tmpIndicesList, knobPos);
        for (var i = 0; i < tmpIndicesList.Length; ++i)
        {
            var indexAndRow = tmpIndicesList[i];
            var h = recordedData.Value.animatorEventHistory[indexAndRow.x];
            var w = eventInfoWidgets[indexAndRow.y];
            var trinagleSymbol = "▶";
            var prefix = h.eventType == AnimatorControllerEventComponent.EventType.StateEnter ? trinagleSymbol : "";
            var suffix = h.eventType == AnimatorControllerEventComponent.EventType.StateExit ? trinagleSymbol : "";
            w.Q<Label>("name").text = $"{prefix}{h.name}{suffix}";
            w.Q<Label>("stringv").text = $"<color=green>LayerId</color>: {h.layerId} <color=green>StateID</color>: {h.stateId}";
            w.visible = true;
        }
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdateMemoryStat()
    {
        var mem = recordedData.Value.GetDataSize();
        memoryStat.text = $"Memory size {CommonTools.FormatMemory(mem)}";
        saveBtn.SetEnabled(mem > 0);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void UpdatePreview()
    {
        var world = GetSelectedWorld();
        if (world == null || selectedEntity == Entity.Null)
            return;
        
        if (!IsPreview())
            return;
        
		if (!animationDatabaseSingletonEq.TryGetSingleton<BlobDatabaseSingleton>(out var animDBSingleton))
            return;
        
        var pc = playbackSingletonEq.GetSingletonRW<PlaybackComponent>();
        pc.ValueRW.playbackData.Clear();
        
        var knobFrame = timelinePortal.Value.knobFrame;
        for (var i = 0; i < recordedData.Value.animHistory.Length; ++i)
        {
            var ah = recordedData.Value.animHistory[i];
            
            if (knobFrame >= ah.frameSpan.x && knobFrame <= ah.frameSpan.y)
            {
                var animationWeight = AnimationHistoryData.GetHistoryValueForFrame(ah.historyWeights, knobFrame);
                var animationTime = AnimationHistoryData.GetHistoryValueForFrame(ah.historyAnimTime, knobFrame);
                var atp = new AnimationToProcessComponent()
                {
                    animation = BlobDatabaseSingleton.GetBlobAsset(ah.animationHash, animDBSingleton.animations),
                    avatarMask = BlobDatabaseSingleton.GetBlobAsset(ah.avatarMaskHash, animDBSingleton.avatarMasks),
                    blendMode = ah.blendMode,
                    layerIndex = ah.layerIndex,
                    layerWeight = ah.layerWeight,
                    motionId = ah.motionId,
                    time = animationTime,
                    weight = animationWeight
                };
                pc.ValueRW.playbackData.Add(atp);
            }
        }
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void StartPreview()
    {
        StopRecord();
        
        var world = GetSelectedWorld();
        if (world == null || selectedEntity == Entity.Null)
            return;
        
        var isPreview = IsPreview();
        Assert.IsFalse(isPreview);
        entityPath.SetEnabled(false);
        recordBtn.SetEnabled(false);
        previewBtn.style.color =  new StyleColor(Color.cyan);
        
        var pc = new PlaybackComponent() { playbackData = new (0xff, Allocator.Persistent) };
        world.EntityManager.AddComponentData(selectedEntity, pc);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void StopPreview()
    {
        previewBtn.style.color = StyleKeyword.Null;
        recordBtn.SetEnabled(true);
        entityPath.SetEnabled(true);
        
        var isPreview = IsPreview();
        if (!isPreview)
            return;
        
        var world = GetSelectedWorld();
        world.EntityManager.RemoveComponent<PlaybackComponent>(selectedEntity);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    unsafe bool IsRecording() => recordSingletonEq.__impl != null && recordSingletonEq.HasSingleton<RecordComponent>();
    unsafe bool IsPreview() => playbackSingletonEq.__impl != null && playbackSingletonEq.HasSingleton<PlaybackComponent>();

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void SaveRecordedData()
    {
        var path = EditorUtility.SaveFilePanel("Rukhanka Wayback Machine", "", "WaybackMachineRecord", "rwbm");
        recordedData.Value.SerializeToFile(path);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    unsafe void LoadRecordedData()
    {
        var path = EditorUtility.OpenFilePanel("Rukhanka Wayback Machine", "", "rwbm");
        recordedData.GetUnsafePtr()->SerializeFromFile(path);
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void DropDown(GenericDropdownMenu m, VisualElement targetElement)
    {
    #if UNITY_6000_3_OR_NEWER
        m.DropDown(entityPathEntity.worldBound, entityPathEntity, DropdownMenuSizeMode.Content);
    #else
        m.DropDown(targetElement.worldBound, targetElement, false);
    #endif
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    void OnDestroy()
    {
        StopRecord();
        StopPreview();
        if (recordedData.IsCreated)
            recordedData.Value.Dispose();
        recordedData.Dispose();
        timelinePortal.Dispose();
        timelineContent.Dispose();
        DisposeEntityQueries();
        EditorApplication.playModeStateChanged -= playModeChangeFn;
    }
}
}
