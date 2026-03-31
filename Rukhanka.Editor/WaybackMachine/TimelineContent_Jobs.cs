
using System;
using Rukhanka.WaybackMachine;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

///////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
public partial class TimelineContent
{
    
[BurstCompile]
unsafe struct BuildRectanglesJob: IJob
{
    [ReadOnly]
    [NativeDisableContainerSafetyRestriction]
    public NativeReference<WaybackMachineData> wbData;
    [ReadOnly]
    [NativeDisableContainerSafetyRestriction]
    public NativeReference<TimelinePortalData> timelinePortal;
    
    public Rect contentRect;
    
    public NativeList<AnimationRect> animationShapes;
    public NativeList<AnimatorStateRect> animatorStateShapes;
    public NativeList<AnimatorTransitionRect> animatorTransitionShapes;
    public NativeList<EventShape> animatorEventShapes;
    public NativeList<EventShape> animationEventShapes;
    public NativeList<EventLine> animationEventLines;
    public NativeList<float2> timelinePoints;
    
    public float animHeaderHeight;
    public float eventsHeaderHeight;
    public float statesHeaderHeight;
    
    public WaybackMachineSettings settings;
    
    [NativeDisableUnsafePtrRestriction]
    public int* outEventBarsCount;
    [NativeDisableUnsafePtrRestriction]
    public int* outAnimBarCount;
    [NativeDisableUnsafePtrRestriction]
    public int* outStatesBarCount;
    
    float baseY;
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void Execute()
    {
        baseY = eventsHeaderHeight;
        
        //  Animation event shapes
        var l2 = wbData.Value.animationEventHistory.Length * settings.eventsVisible;
        for (var k = 0; k < l2; ++k)
        {
            var eh = wbData.Value.animationEventHistory[k];
            ComputeEventShape(k, eh.frameIndex, 0, animationEventShapes);
        }
        
        //  Animator event shapes
        var l3 = wbData.Value.animatorEventHistory.Length * settings.eventsVisible;
        var baseRowIndex = *outEventBarsCount;
        for (var k = 0; k < l3; ++k)
        {
            var eh = wbData.Value.animatorEventHistory[k];
            if (eh.eventType != AnimatorControllerEventComponent.EventType.StateUpdate)
            {
                ComputeEventShape(k, eh.frameRange.x, baseRowIndex, animatorEventShapes);
            }
            else
            {
                //ComputeEventLine(k, eh);
            }
        }
        
        //  Animator states shapes
        baseY += *outEventBarsCount * WaybackMachineWindow.EVENT_ROW_HEIGHT + statesHeaderHeight;
        
        var l4 = wbData.Value.controllerStateHistory.Length * settings.statesVisible;
        Span<int> layerCounters = stackalloc int[0xff];
        for (var k = 0; k < l4; ++k)
        {
            ComputeControllerStateShape(k, layerCounters);
        }
        
        //  Animator transition shapes
        var l5 = wbData.Value.controllerTransitionHistory.Length * settings.statesVisible;
        for (var k = 0; k < l5; ++k)
        {
            ComputeControllerTransitionShape(k);
        }
        
        //  Animation shapes
        baseY += *outStatesBarCount * WaybackMachineWindow.STATE_ROW_HEIGHT + animHeaderHeight;
        
        var l = wbData.Value.animHistory.Length * settings.animationsVisible;
        for (var i = 0; i  < l; ++i)
        {
            ComputeAnimationShape(i);
        }
    }

///////////////////////////////////////////////////////////////////////////////////////////

    int GetEventRow(float2 eventPos, int baseRowIndex, NativeList<EventShape> eventShapes)
    {
        var rv = baseRowIndex - 1;
        var minI = math.max(0, eventShapes.Length - 32);
        var isOverlapped = false;
        var esr2 = WaybackMachineWindow.EVENT_SHAPE_RADIUS * WaybackMachineWindow.EVENT_SHAPE_RADIUS;
        
        do
        {
            isOverlapped = false;
            eventPos.y += ++rv * WaybackMachineWindow.EVENT_ROW_HEIGHT;
            for (var i = eventShapes.Length - 1; i >= minI; --i)
            {
                var es = eventShapes[i];
                var dv = es.pos - eventPos;
                var d = math.lengthsq(dv);
                if (d < esr2)
                {
                    isOverlapped = true;
                    break;
                }
            }
        }
        while (isOverlapped);
        
        return rv;
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void ComputeEventShape(int idx, int frameIndex, int baseRowIndex, NativeList<EventShape> outEventShapes)
    {
        var x0 = timelinePortal.Value.GetPosXForFrame(frameIndex);
        var eventShape = new EventShape();
        
        eventShape.pos = new float2(x0, baseY + WaybackMachineWindow.EVENT_ROW_HEIGHT * 0.5f);
        var eventRow = GetEventRow(eventShape.pos, baseRowIndex, outEventShapes);
        eventShape.pos.y += eventRow * WaybackMachineWindow.EVENT_ROW_HEIGHT;
        
        var r = new Rect(eventShape.pos.x - WaybackMachineWindow.EVENT_SHAPE_RADIUS * 2, eventShape.pos.y, WaybackMachineWindow.EVENT_SHAPE_RADIUS * 4 , 1);
        eventShape.visible = contentRect.Overlaps(r);
        eventShape.eventId = idx;
        eventShape.rowIndex = eventRow;
        
        *outEventBarsCount = math.max(eventRow + 1, *outEventBarsCount);
        
        outEventShapes.Add(eventShape);
    } 
    
///////////////////////////////////////////////////////////////////////////////////////////

    int GetFreeLaneIndex(int curIdx, float x0)
    {
        var collision = false;
        var laneIndex = 0;
        do
        {
            collision = false;
            for (var i = 0; i < curIdx; ++i)
            {
                var s = animationShapes[i];
                var pt = new Vector2(x0, baseY + (0.5f + laneIndex) * WaybackMachineWindow.ANIMATION_BAR_HEIGHT);
                if (s.rect.Contains(pt))
                {
                    collision = true;
                    laneIndex += 1;
                    break;
                }
            }
        } while (collision);
        return laneIndex;
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void AddPoint(HistoryValue hv, float dy, float y0, float2 xBounds)
    {
        float2 pt = default;
        pt.x = timelinePortal.Value.GetPosXForFrame(hv.frameIndex);
        pt.x = math.clamp(pt.x, xBounds.x, xBounds.y);
        pt.y = (1 - hv.value) * (dy - 2) + y0 + 2;
        timelinePoints.Add(pt);
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void ComputeHistoryLine(ref int2 pointRange,  Rect r, int2 frameSpan, UnsafeList<HistoryValue> hvs)
    {
        pointRange.x = timelinePoints.Length;
        float2 xBounds = new float2(r.xMin + 2, r.xMax - 2);
        for (var i = 0; i < hvs.Length; ++i)
        {
            var hv = hvs[i];
            if (hv.value > 1)
                hv.value = math.frac(hv.value);
            
            AddPoint(hv, r.height, r.y, xBounds);
        }
        
        pointRange.y = timelinePoints.Length;
    }

///////////////////////////////////////////////////////////////////////////////////////////

    int2 CutTransitionArea(int2 frameSpan, int layerIndex)
    {
        var rv = frameSpan;
        for (var i = 0; i < wbData.Value.controllerTransitionHistory.Length; ++i)
        {
            ref var td = ref wbData.Value.controllerTransitionHistory.ElementAt(i);
            if (td.layerIndex != layerIndex)
                continue;
            
            if (td.frameSpan.y >= frameSpan.x && td.frameSpan.x <= frameSpan.y)
            {
                // Is this is dst state for transition
                if (rv.x >= td.frameSpan.x)
                {
                    rv.x = math.max(td.frameSpan.y, rv.x);
                }
                // Is this is src state for transition
                if (rv.y <= td.frameSpan.y)
                {
                    rv.y = math.min(td.frameSpan.x, rv.y);
                }
            }
        }
        return rv;
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void ComputeControllerTransitionShape(int idx)
    {
        var rd = wbData.Value.controllerTransitionHistory[idx];
        
        var x0 = timelinePortal.Value.GetPosXForFrame(rd.frameSpan.x);
        var x1 = timelinePortal.Value.GetPosXForFrame(rd.frameSpan.y);
        
        var laneIndex = rd.layerIndex;
        var y0 = baseY + laneIndex * WaybackMachineWindow.STATE_ROW_HEIGHT;
        var y1 = y0 + WaybackMachineWindow.STATE_BAR_HEIGHT;
        var dx = x1 - x0;
        var dy = y1 - y0;
        
        var r = new Rect(x0, y0, dx, dy);
        var dr = new AnimatorTransitionRect();
        dr.rect = r;
        dr.yAB = rd.weightRange * dy;
        dr.visible = contentRect.Overlaps(r);
        dr.eventId = idx;
        dr.rowIndex = laneIndex;
        dr.colorA = animatorStateShapes[rd.dstStateDataIndex].color;
        dr.colorB = animatorStateShapes[rd.srcStateDataIndex].color;
        
        *outStatesBarCount = math.max(*outStatesBarCount, laneIndex + 1);
        
        animatorTransitionShapes.Add(dr);
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void ComputeControllerStateShape(int idx, Span<int> layerCounters)
    {
        var rd = wbData.Value.controllerStateHistory[idx];
        
        var ts = CutTransitionArea(rd.frameSpan, rd.layerIndex);
        var x0 = timelinePortal.Value.GetPosXForFrame(ts.x);
        var x1 = timelinePortal.Value.GetPosXForFrame(ts.y);
        
        var laneIndex = rd.layerIndex;
        var y0 = baseY + laneIndex * (WaybackMachineWindow.STATE_BAR_HEIGHT + WaybackMachineWindow.STATE_BAR_HORIZONTAL_SPACE);
        var y1 = y0 + WaybackMachineWindow.STATE_BAR_HEIGHT;
        var dx = x1 - x0;
        var dy = y1 - y0;
        
        var r = new Rect(x0 - 1, y0, dx + 2, dy);
        var dr = new AnimatorStateRect();
        dr.rect = r;
        dr.visible = contentRect.Overlaps(r);
        dr.eventId = idx;
        dr.rowIndex = laneIndex;
        var counter = layerCounters[rd.layerIndex]++;
        dr.color = (counter & 1) == 0 ? WaybackMachineWindow.STATE_BAR_COLOR1 : WaybackMachineWindow.STATE_BAR_COLOR2;
        
        *outStatesBarCount = math.max(*outStatesBarCount, laneIndex + 1);
        
        animatorStateShapes.Add(dr);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    void ComputeAnimationShape(int idx)
    {
        var rd = wbData.Value.animHistory[idx];
        var x0 = timelinePortal.Value.GetPosXForFrame(rd.frameSpan.x);
        var x1 = timelinePortal.Value.GetPosXForFrame(rd.frameSpan.y);
        var laneIndex = GetFreeLaneIndex(idx, x0 + 0.001f);
        var y0 = baseY + laneIndex * WaybackMachineWindow.ANIMATION_ROW_HEIGHT;
        var y1 = y0 + WaybackMachineWindow.ANIMATION_BAR_HEIGHT;
        var dx = x1 - x0;
        var dy = y1 - y0;
        
        *outAnimBarCount = math.max(*outAnimBarCount, laneIndex + 1);
        var r = new Rect(x0, y0, dx, dy);
        
        //  Rectangle
        var dr = new AnimationRect();
        dr.rect = r;
        dr.visible = contentRect.Overlaps(r);
        dr.eventId = idx;
        dr.rowIndex = laneIndex;
        
        //  Weight line
        ComputeHistoryLine(ref dr.weightHistoryPointRange, r, rd.frameSpan, rd.historyWeights);
        //  Animation time line
        ComputeHistoryLine(ref dr.animTimeHistoryPointRange, r, rd.frameSpan, rd.historyAnimTime);
        
        animationShapes.Add(dr);
    }
}
}
}
