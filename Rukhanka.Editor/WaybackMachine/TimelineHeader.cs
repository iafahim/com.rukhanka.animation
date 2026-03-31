
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

///////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
public class TimelineHeader: VisualElement
{
    public NativeReference<TimelinePortalData> timelinePortal;
    readonly float knobWidth = 10;
    readonly Color knobColor = Color.white;
    public WaybackMachineSettings settings;
    
///////////////////////////////////////////////////////////////////////////////////////////

    public TimelineHeader()
    {
        style.flexGrow = 1;
        style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);
        generateVisualContent += DrawContent;
        RegisterCallback<MouseDownEvent>(OnMouseDown);
        RegisterCallback<MouseUpEvent>(OnMouseUp);
        RegisterCallback<MouseMoveEvent>(OnMouseMove);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    unsafe void OnMouseDown(MouseDownEvent evt)
    {
        MouseCaptureController.CaptureMouse(this);
        timelinePortal.GetUnsafePtr()->SetKnobFromPos(evt.localMousePosition.x);
        timelinePortal.GetUnsafePtr()->knobDragging = true;
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    unsafe void OnMouseMove(MouseMoveEvent evt)
    {
        if (!MouseCaptureController.IsMouseCaptured())
            return;
        
        timelinePortal.GetUnsafePtr()->SetKnobFromPos(evt.localMousePosition.x);
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    unsafe void OnMouseUp(MouseUpEvent evt)
    {
        MouseCaptureController.ReleaseMouse(this);
        timelinePortal.GetUnsafePtr()->knobDragging = false;
    }
    
///////////////////////////////////////////////////////////////////////////////////////////

    void DrawTimeline(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        
        p.lineWidth = 1f;
        p.lineCap = LineCap.Butt;
        p.strokeColor = Color.white;
        
        p.BeginPath();

        var textColor = new Color(1, 1, 1, 0.7f);
        foreach (var tl in timelinePortal.Value.tickLines)
        {
            var p0 = tl.lineFrom;
            var p1 = tl.lineTo;
            p0.y = contentRect.y + contentRect.height * p0.y;
            p1.y = contentRect.yMax * p1.y;
            
            p.MoveTo(p0);
            p.LineTo(p1);
            if (tl.majorTick)
            {
                var str = settings.rulerMode == WaybackMachineSettings.RulerMode.Frames
                    ? tl.frameIndex.ToString()
                    : timelinePortal.Value.GetTimeForFrame(tl.frameIndex).ToString("0.000");
                ctx.DrawText(str, tl.lineFrom + new float2(4, 2), 10, textColor);
            }
        }
        p.Stroke();
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void AddPointToBBox(ref Rect r, Vector2 pt)
    {
        r.xMin = math.min(r.xMin, pt.x);
        r.xMax = math.max(r.xMax, pt.x);
        r.yMin = math.min(r.yMin, pt.y);
        r.yMax = math.max(r.xMax, pt.y);
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void RestrictPoint(Rect r, ref Vector2 pt)
    {
        pt.x = math.max(r.xMin, pt.x);
        pt.x = math.min(r.xMax, pt.x);
        pt.y = math.max(r.yMin, pt.y);
        pt.y = math.min(r.yMax, pt.y);
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawKnob(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        p.strokeColor = knobColor;
        p.fillColor = knobColor;
        p.lineJoin = LineJoin.Miter;
        p.lineCap = LineCap.Butt;
        
        var knobPosX = timelinePortal.Value.GetKnobPosX();
        Vector2[] pts = new Vector2[5];
        pts[0] = new Vector2(knobPosX - knobWidth / 2, contentRect.yMin);
        pts[1] = new Vector2(knobPosX + knobWidth / 2, contentRect.yMin);
        pts[2] = new Vector2(pts[1].x, contentRect.y + contentRect.height * 0.7f);
        pts[3] = new Vector2(knobPosX, contentRect.yMax);
        pts[4] = new Vector2(pts[0].x, pts[2].y);
        
        p.BeginPath();
        p.MoveTo(pts[0]);
        for (var i = 1; i < pts.Length; ++i)
        {
            p.LineTo(pts[i]);
        }
        p.Fill();
        p.Stroke(); 
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawKnobCaption(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        var frameHalfWidth = 20.0f;
        var frameHeight = 10.0f;
        var frameYOffset = -2.0f;
        var knobPosX = timelinePortal.Value.GetKnobPosX();
        p.lineJoin = LineJoin.Round;
        p.lineWidth = 2;
        p.BeginPath();
        p.MoveTo(new Vector2(knobPosX - frameHalfWidth, contentRect.yMin - frameHeight + frameYOffset));
        p.MoveTo(new Vector2(knobPosX + frameHalfWidth, contentRect.yMin - frameHeight + frameYOffset));
        p.MoveTo(new Vector2(knobPosX + frameHalfWidth, contentRect.yMin - frameYOffset));
        p.MoveTo(new Vector2(knobPosX - frameHalfWidth, contentRect.yMin - frameYOffset));
        p.ClosePath();
        p.Stroke();
    }

///////////////////////////////////////////////////////////////////////////////////////////

    void DrawContent(MeshGenerationContext ctx)
    {
        DrawTimeline(ctx);
        DrawKnob(ctx);
        DrawKnobCaption(ctx);
    }
}
}
