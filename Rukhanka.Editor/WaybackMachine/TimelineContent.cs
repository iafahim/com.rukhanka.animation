
using System;
using Rukhanka.Toolbox;
using Rukhanka.WaybackMachine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

///////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
[BurstCompile]
public partial class TimelineContent: VisualElement, IDisposable
{
    public NativeReference<TimelinePortalData> timelinePortal;
    public NativeReference<WaybackMachineData> recordedData;
    public bool isRecording;
    public float animHeaderHeight;
    public float eventsHeaderHeight;
    public float statesHeaderHeight;
    public int eventBarsCount;
    public int animBarsCount;
    public int stateBarsCount;
    
    readonly Color lineColor = new Color(1, 1, 1, 0.1f);
    
    struct AnimationRect
    {
        public Rect rect;
        public bool visible;
        public int eventId;
        public int rowIndex;
        public int2 weightHistoryPointRange;
        public int2 animTimeHistoryPointRange;
    }
    
    struct AnimatorStateRect
    {
        public Rect rect;
        public bool visible;
        public int eventId;
        public int rowIndex;
        public Color32 color;
    }
    
    struct AnimatorTransitionRect
    {
        public Rect rect;
        public bool visible;
        public int eventId;
        public int rowIndex;
        public Color32 colorA, colorB;
        public float2 yAB;
    }
    
    struct EventShape
    {
        public float2 pos;
        public bool visible;
        public int eventId;
        public int rowIndex;
    }
    
    struct EventLine
    {
        public float2 posMin;
        public float2 posMax;
        public bool visible;
    }
    
    NativeList<AnimationRect> animationRects;
    NativeList<AnimatorStateRect> animatorStateShapes;
    NativeList<AnimatorTransitionRect> animatorTransitionShapes;
    NativeList<EventShape> animationEventShapes;
    NativeList<EventShape> animatorEventShapes;
    NativeList<EventLine> animatorEventLines;
    NativeList<float2> timelinePoints;
    
    //  Used for text width measurements
    public Label textMeasurer;
    public WaybackMachineSettings settings;
    
///////////////////////////////////////////////////////////////////////////////////////////

    public TimelineContent()
    {
        style.flexGrow = 1;
        style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);
        generateVisualContent += DrawContent;
        animationRects = new (0xff, Allocator.Persistent);
        timelinePoints = new (0xffff, Allocator.Persistent);
        animationEventShapes = new (0xff, Allocator.Persistent);
        animatorEventShapes = new (0xff, Allocator.Persistent);
        animatorEventLines = new (0xff, Allocator.Persistent);
        animatorStateShapes = new (0xff, Allocator.Persistent);
        animatorTransitionShapes = new (0xff, Allocator.Persistent);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        animationRects.Dispose();
        timelinePoints.Dispose();
        animationEventShapes.Dispose();
        animatorEventShapes.Dispose();
        animatorEventLines.Dispose();
        animatorStateShapes.Dispose();
        animatorTransitionShapes.Dispose();
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawVLines(Painter2D p)
    {
        p.lineWidth = 1f;
        p.lineCap = LineCap.Butt;
        
        p.BeginPath();
        p.strokeColor = lineColor;
        foreach (var tl in timelinePortal.Value.tickLines)
        {
            if (!tl.majorTick)
                continue;
            
            var p0 = tl.lineFrom;
            var p1 = tl.lineTo;
            p0.y = contentRect.y + contentRect.height * p0.y;
            p1.y = contentRect.yMax * p1.y;
            p.MoveTo(p0);
            p.LineTo(p1);
        }
        p.Stroke();
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    void DrawKnobLine(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        var knobPosX = timelinePortal.Value.GetKnobPosX();
        var knobTooltipWidth = 0f;
        var knobTooltipHeight = 0f;
        if (timelinePortal.Value.knobDragging)
        {
            var knobFrame = timelinePortal.Value.knobFrame;
            var knobTime = timelinePortal.Value.GetTimeForFrame(knobFrame);
            var knobPosStr = knobFrame.ToString();
            if (settings.rulerMode == WaybackMachineSettings.RulerMode.Seconds)
                knobPosStr = knobTime.ToString("0.00");
            var tooltipWidth = new Vector2(knobPosStr.Length * 6, 14.0f);
            knobTooltipWidth = tooltipWidth.x * 1.3f;
            knobTooltipHeight = tooltipWidth.y * 1.1f;
            var hpt0 = new Vector2(knobPosX - knobTooltipWidth * 0.5f, contentRect.yMin + 1);
            var hpt1 = new Vector2(hpt0.x + knobTooltipWidth, contentRect.yMin + 1);
            var hpt2 = new Vector2(hpt1.x, contentRect.yMin + knobTooltipHeight);
            var hpt3 = new Vector2(hpt0.x, hpt2.y);
            p.lineJoin = LineJoin.Round;
            p.fillColor = Color.black;
            p.BeginPath();
            p.MoveTo(hpt0);
            p.LineTo(hpt1);
            p.LineTo(hpt2);
            p.LineTo(hpt3);
            p.ClosePath();
            p.Fill();

            var textPt = new Vector2(knobPosX - tooltipWidth.x * 0.5f, contentRect.yMin + knobTooltipHeight - tooltipWidth.y);
            ctx.DrawText(knobPosStr, textPt, 11, Color.white);
        }
        
        p.fillColor = Color.white;
        p.lineWidth = 1;
        p.lineJoin = LineJoin.Miter;
        var knobYOffset = math.select(0, knobTooltipHeight, timelinePortal.Value.knobDragging);
        var pt0 = new Vector2((int)knobPosX + 0.5f, contentRect.yMin + knobYOffset);
        var pt1 = new Vector2(pt0.x, contentRect.yMax);
        p.strokeColor = Color.white;
        p.lineWidth = 1;
        p.BeginPath();
        p.MoveTo(pt0);
        p.LineTo(pt1);
        p.Stroke();
        
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public unsafe void ComputeShapes()
    {
        animationRects.Clear();
        animationEventShapes.Clear();
        animatorEventShapes.Clear();
        animatorEventLines.Clear();
        animatorStateShapes.Clear();
        animatorTransitionShapes.Clear();
        timelinePoints.Clear();
        
        if (!recordedData.IsCreated || recordedData.Value.animHistory.Length == 0)
            return;
        
        fixed (int* animBarsCountPtr = &animBarsCount, stateBarsCountPtr = &stateBarsCount, eventsBatCountPtr = &eventBarsCount)
        {
            var computeRectsJob = new BuildRectanglesJob()
            {
                timelinePortal = timelinePortal,
                wbData = recordedData,
                contentRect = contentRect,
                timelinePoints = timelinePoints,
                animationShapes = animationRects,
                animationEventShapes = animationEventShapes,
                animatorEventShapes = animatorEventShapes,
                animationEventLines = animatorEventLines,
                animatorStateShapes = animatorStateShapes,
                animatorTransitionShapes = animatorTransitionShapes,
                outEventBarsCount = eventsBatCountPtr,
                outAnimBarCount = animBarsCountPtr,
                outStatesBarCount = stateBarsCountPtr,
                animHeaderHeight = animHeaderHeight,
                eventsHeaderHeight = eventsHeaderHeight,
                statesHeaderHeight = statesHeaderHeight,
                settings = settings
            };
            
            computeRectsJob.Run();
        }
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawHistoryLine(int2 pointRange, Painter2D p, float lineWidth, Color color)
    {
        p.strokeColor = color;
        p.lineWidth = lineWidth;
        p.lineJoin = LineJoin.Bevel;
        p.BeginPath();
        for (var k = pointRange.x; k < pointRange.y; ++k)
        {
            if (k == 0)
                p.MoveTo(timelinePoints[k]);
            else
                p.LineTo(timelinePoints[k]);
        }
        p.Stroke();
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawAnimatorStateShapes(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        for (var i = 0; i < animatorStateShapes.Length; ++i)
        {
            var rd = animatorStateShapes.ElementAt(i);
            //  Quick reject of invisible rects
            if (!rd.visible)
                continue;
            
            var pt0 = new Vector2(rd.rect.xMin, rd.rect.yMin);
            var pt1 = new Vector2(rd.rect.xMax, rd.rect.yMin);
            var pt2 = new Vector2(rd.rect.xMax, rd.rect.yMax);
            var pt3 = new Vector2(rd.rect.xMin, rd.rect.yMax);
            p.strokeColor = Color.white;
            p.lineWidth = 1;
            p.BeginPath();
            p.LineTo(pt0);
            p.LineTo(pt1);
            p.LineTo(pt2);
            p.LineTo(pt3);
            p.ClosePath();
            p.fillColor = rd.color;
            p.Fill();
            
            //  Draw text
            var ah = recordedData.Value.controllerStateHistory[rd.eventId];
            var name = ah.GetName();
            DrawTextInRect(name, rd.rect, ctx);
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    void DrawAnimatorTransitionShapes(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        for (var i = 0; i < animatorTransitionShapes.Length; ++i)
        {
            var rd = animatorTransitionShapes[i];
            //  Quick reject of invisible rects
            if (!rd.visible)
                continue;
            
            var pt0 = new Vector2(rd.rect.xMin, rd.rect.yMin);
            var pt1 = new Vector2(rd.rect.xMax, rd.rect.yMin);
            var pt2 = new Vector2(rd.rect.xMax, rd.rect.yMin + rd.yAB.y);
            var pt3 = new Vector2(rd.rect.xMin, rd.rect.yMin + rd.yAB.x);
            p.strokeColor = Color.white;
            p.lineWidth = 1;
            
            // src side
            p.BeginPath();
            p.MoveTo(pt0);
            p.LineTo(pt1);
            p.LineTo(pt2);
            p.LineTo(pt3);
            p.ClosePath();
            p.fillColor = rd.colorA;
            p.Fill();
            
            //  dst side
            pt0.y = rd.rect.yMax;
            pt1.y = rd.rect.yMax;
            p.BeginPath();
            p.MoveTo(pt0);
            p.LineTo(pt1);
            p.LineTo(pt2);
            p.LineTo(pt3);
            p.ClosePath();
            p.fillColor = rd.colorB;
            p.Fill();
        }
        
        //  Divider lines
        p.lineCap = LineCap.Butt;
        p.strokeColor = Color.black;
        p.lineWidth = WaybackMachineWindow.STATE_BAR_VERTICAL_SPACE;
        p.BeginPath();
        for (var i = 0; i < animatorTransitionShapes.Length; ++i)
        {
            var rd = animatorTransitionShapes[i];
            var pt0 = new Vector2(rd.rect.xMin, rd.rect.yMin);
            var pt1 = new Vector2(rd.rect.xMin, rd.rect.yMin + rd.yAB.x);
            var pt2 = new Vector2(rd.rect.xMax, rd.rect.yMin + rd.yAB.y);
            var pt3 = new Vector2(rd.rect.xMax, rd.rect.yMax);
            p.MoveTo(pt0);
            p.LineTo(pt1);
            p.LineTo(pt2);
            p.LineTo(pt3);
        }
        p.Stroke();
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawAnimationShapes(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        for (var i = 0; i < animationRects.Length; ++i)
        {
            ref var rd = ref animationRects.ElementAt(i);
            //  Quick reject of invisible rects
            if (!rd.visible)
                continue;
            
            var pt0 = new Vector2(rd.rect.xMin, rd.rect.yMin);
            var pt1 = new Vector2(rd.rect.xMax, rd.rect.yMin);
            var pt2 = new Vector2(rd.rect.xMax, rd.rect.yMax);
            var pt3 = new Vector2(rd.rect.xMin, rd.rect.yMax);
            p.strokeColor = Color.white;
            p.lineWidth = 1;
            p.BeginPath();
            p.MoveTo(pt0 + new Vector2(1, 0));
            p.LineTo(pt1 + new Vector2(-1, 0));
            p.LineTo(pt2 + new Vector2(-1, 0));
            p.LineTo(pt3 + new Vector2(1, 0));
            p.ClosePath();
            p.fillColor = WaybackMachineWindow.ANIMATION_BAR_COLOR;
            p.Fill();
            
            //  Draw weights
            if (settings.animationWeightGraphs)
                DrawHistoryLine(rd.weightHistoryPointRange, p, 2, WaybackMachineWindow.ANIMATION_HISTORY_WEIGHT_LINE_COLOR);
            //  Draw animation times
            if (settings.animationTimeGraphs)
                DrawHistoryLine(rd.animTimeHistoryPointRange, p, 2, WaybackMachineWindow.ANIMATION_HISTORY_TIME_LINE_COLOR);
            
            //  Draw text
            var ah = recordedData.Value.animHistory[rd.eventId];
        #if RUKHANKA_DEBUG_INFO
            var animName = ah.animationName.ToString();
        #else
            var animName = ah.animationHash.ToString();
        #endif
            DrawTextInRect(animName, rd.rect, ctx);
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    void DrawTextInRect(string text, Rect r, MeshGenerationContext ctx)
    {
        var textSize = textMeasurer.MeasureTextSize(text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined);
        // Trim text size if out of bounds
        var f = math.min(1, r.width * 0.5f / textSize.x);
        if (f < 1)
        {
            var offset = (int)(text.Length * f);
            text = offset > 0 ? text.Substring(0, offset) : "";
            textSize.x *= f;
        }
        if (textSize.x > 5)
        {
            var tpt = r.center - textSize * 0.5f;
            ctx.DrawText(text, tpt, 16, Color.white);
        }
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawRecordingRect(Painter2D p)
    {
        if (!isRecording)
            return;
        
        var lineWidth = 4;
        var halfLineWidth = lineWidth / 2;
        p.strokeColor = Color.red;
        p.lineWidth = lineWidth;
        p.lineJoin = LineJoin.Bevel;
        p.BeginPath();
        p.MoveTo(new Vector2(contentRect.xMin + halfLineWidth, contentRect.yMin + halfLineWidth));
        p.LineTo(new Vector2(contentRect.xMax - halfLineWidth, contentRect.yMin + halfLineWidth));
        p.LineTo(new Vector2(contentRect.xMax - halfLineWidth, contentRect.yMax - halfLineWidth));
        p.LineTo(new Vector2(contentRect.xMin + halfLineWidth, contentRect.yMax - halfLineWidth));
        p.ClosePath();
        p.Stroke();
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawAnimationEvents(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        p.strokeColor = Color.white;
        p.fillColor = WaybackMachineWindow.ANIMATION_EVENT_COLOR;
        p.lineWidth = 1;
        for (var i = 0; i < animationEventShapes.Length; ++i)
        {
            ref var es = ref animationEventShapes.ElementAt(i);
            if (!es.visible)
                continue;
            
            p.BeginPath();
            p.MoveTo(new Vector2(es.pos.x - WaybackMachineWindow.EVENT_SHAPE_RADIUS, es.pos.y));
            p.LineTo(new Vector2(es.pos.x, es.pos.y - WaybackMachineWindow.EVENT_SHAPE_RADIUS));
            p.LineTo(new Vector2(es.pos.x + WaybackMachineWindow.EVENT_SHAPE_RADIUS, es.pos.y));
            p.LineTo(new Vector2(es.pos.x, es.pos.y + WaybackMachineWindow.EVENT_SHAPE_RADIUS));
            p.ClosePath();
            p.Fill();
            p.Stroke();
            
            //  Draw text labels
            if (settings.eventLabels)
            {
                var ehd = recordedData.Value.animationEventHistory[es.eventId];
                var evtName = ehd.GetName();
                ctx.DrawText(evtName, new Vector2(es.pos.x + 10, es.pos.y - 9), 14, WaybackMachineWindow.EVENT_TEXT_COLOR);
            }
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    void DrawAnimatorEventLines(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        p.strokeColor = Color.white;
        p.lineWidth = 1;
        
        for (var i = 0; i < animatorEventLines.Length; ++i)
        {
            var el = animatorEventLines.ElementAt(i);
            if (!el.visible)
                continue;
            
            p.BeginPath();
            var p0 = el.posMin;
            p0.y -= WaybackMachineWindow.EVENT_SHAPE_RADIUS;
            p.MoveTo(p0);
            p.LineTo(el.posMin); 
            p.LineTo(el.posMax);
            var p3 = el.posMax;
            p0.y -= WaybackMachineWindow.EVENT_SHAPE_RADIUS;
            p.LineTo(p3);
            p.Stroke();
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    void DrawAnimatorEventShapes(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        p.lineWidth = 1;
        for (var i = 0; i < animatorEventShapes.Length; ++i)
        {
            ref var es = ref animatorEventShapes.ElementAt(i);
            if (!es.visible)
                continue;
            
            p.BeginPath();
            p.fillColor = WaybackMachineWindow.ANIMATOR_EVENT_COLOR;
            p.strokeColor = WaybackMachineWindow.EVENT_OUTLINE_COLOR;
            p.MoveTo(new Vector2(es.pos.x - WaybackMachineWindow.EVENT_SHAPE_RADIUS, es.pos.y - WaybackMachineWindow.EVENT_SHAPE_RADIUS));
            p.LineTo(new Vector2(es.pos.x + WaybackMachineWindow.EVENT_SHAPE_RADIUS, es.pos.y - WaybackMachineWindow.EVENT_SHAPE_RADIUS));
            p.LineTo(new Vector2(es.pos.x + WaybackMachineWindow.EVENT_SHAPE_RADIUS, es.pos.y + WaybackMachineWindow.EVENT_SHAPE_RADIUS));
            p.LineTo(new Vector2(es.pos.x - WaybackMachineWindow.EVENT_SHAPE_RADIUS, es.pos.y + WaybackMachineWindow.EVENT_SHAPE_RADIUS));
            p.ClosePath();
            p.Fill();
            p.Stroke();
            
            //  Enter/Exit triangle
            p.BeginPath();
            p.fillColor = WaybackMachineWindow.ANIMATOR_EVENT_ENTER_EXIT_MARKER_COLOR;
            p.strokeColor = WaybackMachineWindow.EVENT_OUTLINE_COLOR;
            var ehd = recordedData.Value.animatorEventHistory[es.eventId];
            var offset = 0f;
            switch (ehd.eventType)
            {
            case AnimatorControllerEventComponent.EventType.StateEnter: offset = -2.5f; break;
            case AnimatorControllerEventComponent.EventType.StateExit: offset = 1.5f; break;
            }
            var pt0 = new Vector2(es.pos.x + WaybackMachineWindow.EVENT_SHAPE_RADIUS * offset, es.pos.y - WaybackMachineWindow.EVENT_SHAPE_RADIUS);
            p.MoveTo(pt0);
            p.LineTo(pt0 + new Vector2(WaybackMachineWindow.EVENT_SHAPE_RADIUS, WaybackMachineWindow.EVENT_SHAPE_RADIUS));
            p.LineTo(pt0 + new Vector2(0, WaybackMachineWindow.EVENT_SHAPE_RADIUS * 2));
            p.ClosePath();
            p.Fill();
            p.Stroke();
            
            //  Draw text labels
            if (settings.eventLabels)
            {
                var evtName = ehd.name.ToString();
                ctx.DrawText(evtName, new Vector2(es.pos.x + WaybackMachineWindow.EVENT_SHAPE_RADIUS * 3, es.pos.y - 9), 14, WaybackMachineWindow.EVENT_TEXT_COLOR);
            }
        }
    }

///////////////////////////////////////////////////////////////////////////////////////////

    [BurstCompile]
    static void GetAnimationEventShapeIndicesForPosBurst(ref NativeList<int2> outIndices, in NativeList<EventShape> hd, float posX)
    {
        outIndices.Clear();
        for (int i = 0; i < hd.Length; ++i)
        {
            var hv = hd[i];
            if (posX >= hv.pos.x - WaybackMachineWindow.EVENT_SHAPE_RADIUS && posX <= hv.pos.x + WaybackMachineWindow.EVENT_SHAPE_RADIUS)
                outIndices.Add(new int2(hv.eventId, hv.rowIndex));
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    [BurstCompile]
    static void GetStateShapeIndicesForPosBurst(ref NativeList<int3> outIndices, in NativeList<AnimatorStateRect> hd, float posX)
    {
        outIndices.Clear();
        for (int i = 0; i < hd.Length; ++i)
        {
            var hv = hd[i];
            if (hv.rect.xMin <= posX && hv.rect.xMax >= posX)
            {
                outIndices.Add(new int3(hv.eventId, hv.rowIndex, hv.color.ToInt()));
            }
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    [BurstCompile]
    static void GetTransitionShapeIndicesForPosBurst(ref NativeList<int4> outIndices, in NativeList<AnimatorTransitionRect> hd, float posX)
    {
        outIndices.Clear();
        for (int i = 0; i < hd.Length; ++i)
        {
            var hv = hd[i];
            if (hv.rect.xMin <= posX && hv.rect.xMax >= posX)
            {
                outIndices.Add(new int4(hv.eventId, hv.rowIndex, hv.colorA.ToInt(), hv.colorB.ToInt()));
            }
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    [BurstCompile]
    static void GetAnimationShapeIndicesForPosBurst(ref NativeList<int2> outIndices, in NativeList<AnimationRect> hd, float posX)
    {
        outIndices.Clear();
        for (int i = 0; i < hd.Length; ++i)
        {
            var hv = hd[i];
            if (hv.rect.xMin <= posX && hv.rect.xMax >= posX)
            {
                outIndices.Add(new int2(hv.eventId, hv.rowIndex));
            }
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void GetAnimationShapeIndicesForPos(ref NativeList<int2> outIndices, float posX)
    {
        GetAnimationShapeIndicesForPosBurst(ref outIndices, animationRects, posX);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void GetTransitionShapeIndicesForPos(ref NativeList<int4> outIndices, float posX)
    {
        GetTransitionShapeIndicesForPosBurst(ref outIndices, animatorTransitionShapes, posX);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void GetStateShapeIndicesForPos(ref NativeList<int3> outIndices, float posX)
    {
        GetStateShapeIndicesForPosBurst(ref outIndices, animatorStateShapes, posX);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void GetAnimationEventShapeIndicesForPos(ref NativeList<int2> outIndices, float posX)
    {
        GetAnimationEventShapeIndicesForPosBurst(ref outIndices, animationEventShapes, posX);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void GetAnimatorEventShapeIndicesForPos(ref NativeList<int2> outIndices, float posX)
    {
        GetAnimationEventShapeIndicesForPosBurst(ref outIndices, animatorEventShapes, posX);
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawContent(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        DrawVLines(p); 
        DrawAnimationShapes(ctx);
        DrawAnimatorStateShapes(ctx);
        DrawAnimatorTransitionShapes(ctx);
        DrawAnimationEvents(ctx);
        DrawAnimatorEventShapes(ctx);
        DrawAnimatorEventLines(ctx);
        DrawRecordingRect(p);
        DrawKnobLine(ctx);
    }
}
}
