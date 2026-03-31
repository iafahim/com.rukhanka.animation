
using System;
using Rukhanka.WaybackMachine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

///////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
public struct TimelinePortalData: IDisposable
{
    public float frameSizeInSec;
    public float2 frameRange, visibleRange;
    public float contentWidth;
    public int knobFrame;
    public bool knobDragging;
    
    public struct VLineDef
    {
        public bool majorTick;
        public float2 lineFrom, lineTo;
        public int frameIndex;
    }
    public NativeList<VLineDef> tickLines;
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void Construct()
    {
        tickLines = new (0xff, Allocator.Persistent);
    }

///////////////////////////////////////////////////////////////////////////////////////////

    public void SetKnobFromPos(float xPos)
    {
        var zeroFramePos = GetPosXForFrame(0);
        var zeroRelativePos = xPos - zeroFramePos;
        var fIndex = math.round(zeroRelativePos / OneFrameWidth());
        knobFrame = (int)math.clamp(fIndex, frameRange.x, frameRange.y);
    }

///////////////////////////////////////////////////////////////////////////////////////////

    public float GetFrameForTime(float t) =>  t / frameSizeInSec;
    
///////////////////////////////////////////////////////////////////////////////////////////

    public float GetTimeForFrame(float frame) =>  frame * frameSizeInSec;
    
///////////////////////////////////////////////////////////////////////////////////////////

    public float GetFrameForPosX(float x) => GetFrameForTime(GetTimeForPosX(x));

///////////////////////////////////////////////////////////////////////////////////////////

    public float GetPosXForFrame(int frameIndex)
    {
        var oneFrameWidth = OneFrameWidth();
        var startOffset = visibleRange.x * oneFrameWidth;
        var rv = frameIndex * oneFrameWidth - startOffset;
        return rv;
    }

///////////////////////////////////////////////////////////////////////////////////////////

    public float GetKnobPosX() => GetPosXForFrame(knobFrame);

///////////////////////////////////////////////////////////////////////////////////////////
    
    public float GetTimeForPosX(float xPos)
    {
        var oneFrameWidth = OneFrameWidth();
        var startOffset = visibleRange.x * oneFrameWidth;
        var rv = (xPos + startOffset) / oneFrameWidth * frameSizeInSec;
        return rv;
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public float OneFrameWidth()
    {
        var visibleFramesCount = visibleRange.y - visibleRange.x;
        var oneFrameWidth = contentWidth / visibleFramesCount;
        return oneFrameWidth;
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void ComputeTicks()
    {
        tickLines.Clear();
        
        var oneFrameWidth = OneFrameWidth();
        var d = WaybackMachineWindow.TIMELINE_TICKS_MIN_SKIP_SPACE / oneFrameWidth;
        var skipStep = math.ceilpow2(math.max(1, (int)math.ceil(d)));
        var prevStep = skipStep >> 1;
        var lerpFactor = (d - prevStep) / (skipStep - prevStep) ;
        
        var startIndex = (int)(math.floor(visibleRange.x / skipStep) * skipStep);
        var endIndex = (int)math.ceil(visibleRange.y);
        
        for (int i = startIndex; i < endIndex; i += skipStep)
        {
            var marked = math.select(1, 0, (i / skipStep & 1) == 0 || skipStep == 1);
            var x0 = GetPosXForFrame(i);
            var y0 = lerpFactor * marked;
            //  0.5 offset for 1 point width lines
            var lineP0 = new Vector2((int)x0 + 0.5f, y0);
            var lineP1 = new Vector2(lineP0.x, 1);
            
            var ld = new VLineDef
            {
                lineFrom = lineP0,
                lineTo = lineP1,
                majorTick = marked == 0,
                frameIndex = i
            };
            tickLines.Add(ld);
        }
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        tickLines.Dispose();
    }
}
}
