using System;
using System.Linq;
using UnityEngine;

static class Program
{
    static int assertions;

    static void Main()
    {
        Mapping();
        FastTap();
        HeldState();
        Cancellation();
        Resize();
        Pinch();
        DragAndFling();
        Console.WriteLine("Dual-screen gestures and coordinates: " + assertions + " assertions passed");
    }

    static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    static DsTouch.Point P(TouchPhase phase, float x, float y, double time, int id = 0) =>
        new DsTouch.Point { Phase = phase, Position = new Vector2(x, y), Time = time, FingerId = id };

    static void Types(DsInput input, params DsGestureType[] expected)
    {
        Assert(input.Gestures.Select(g => g.Type).SequenceEqual(expected),
            "Unexpected gestures: " + string.Join(", ", input.Gestures.Select(g => g.Type)));
    }

    static void Mapping()
    {
        // Insets, density, a resized surface and a different canvas scale.
        var surfaces = new[] {
            new Vector2(1240, 1080), new Vector2(1240, 969),
            new Vector2(992, 864), new Vector2(2480, 2160),
        };
        var canvases = new[] {
            new Vector2(1240, 1080), new Vector2(1309, 1023),
        };
        foreach (var surface in surfaces)
        foreach (var canvas in canvases)
        foreach (float inset in new[] { 0f, 55f, 111f })
        foreach (float y in new[] { 1f, 44f, 87f, 300f, 800f })
        for (int tab = 0; tab < 5; tab++)
        {
            float x = (tab + 0.5f) * 1240f / 5f;
            Vector2 rendered = new Vector2(x / canvas.x * surface.x,
                                           y / canvas.y * surface.y + inset);
            Vector2 local = rendered - new Vector2(0, inset);
            Vector2 mapped = DsTouch.MapToCanvas(
                new Vector2(local.x / surface.x, local.y / surface.y), canvas, 1080);
            Assert(Math.Abs(mapped.x - x) < 0.001f && Math.Abs(1080 - mapped.y - y) < 0.001f,
                "A rendered point did not map back into its own layout");
            if (y < 88) Assert(mapped.y >= 1080 - 88, "A tab tap fell below the tab strip");
        }
    }

    static void FastTap()
    {
        var input = new DsInput();
        input.Poll(new[] { P(TouchPhase.Began, 300, 1040, 1), P(TouchPhase.Ended, 300, 1040, 1.01) }, 1);
        Types(input, DsGestureType.Down, DsGestureType.Up, DsGestureType.Tap);
        input.Poll(Array.Empty<DsTouch.Point>(), 1);
        Types(input);
        input.Poll(new[] { P(TouchPhase.Began, 400, 1040, 2), P(TouchPhase.Ended, 400, 1040, 2.1) }, 1);
        Types(input, DsGestureType.Down, DsGestureType.Up, DsGestureType.Tap);
    }

    static void HeldState()
    {
        var input = new DsInput();
        input.Poll(new[] { P(TouchPhase.Began, 20, 30, 1) }, 1);
        Assert(input.SingleTouchActive, "Down did not establish held touch state");
        input.Poll(Array.Empty<DsTouch.Point>(), 1);
        Assert(input.SingleTouchActive, "Stationary held frame lost active touch state");
        input.Poll(new[] { P(TouchPhase.Moved, 25, 35, 1.1) }, 1);
        Assert(input.SingleTouchActive, "Move lost active touch state");
        input.Poll(new[] { P(TouchPhase.Ended, 25, 35, 1.2) }, 1);
        Assert(!input.SingleTouchActive, "Release retained held touch state");

        input.Poll(new[] { P(TouchPhase.Began, 40, 50, 2) }, 2);
        Assert(input.SingleTouchActive, "Second down did not establish held touch state");
        input.Poll(new[] { P(TouchPhase.Canceled, 40, 50, 2.1) }, 2);
        Assert(!input.SingleTouchActive, "Cancellation retained held touch state");
    }

    static void Cancellation()
    {
        var input = new DsInput();
        input.Poll(new[] { P(TouchPhase.Began, 20, 30, 1), P(TouchPhase.Canceled, 20, 30, 1.1) }, 1);
        Types(input, DsGestureType.Down, DsGestureType.Up);
        input.Poll(new[] { P(TouchPhase.Moved, 25, 35, 1.2), P(TouchPhase.Ended, 25, 35, 1.3) }, 1);
        Types(input);
        input.Poll(new[] { P(TouchPhase.Began, 20, 30, 2) }, 1);
        input.Cancel();
        Assert(!input.SingleTouchActive, "Explicit cancellation retained held touch state");
        Types(input, DsGestureType.Up);
        input.Poll(new[] { P(TouchPhase.Ended, 20, 30, 2.1) }, 1);
        Types(input);
    }

    static void Resize()
    {
        var input = new DsInput();
        input.Poll(new[] { P(TouchPhase.Began, 20, 30, 1) }, 1);
        input.Poll(new[] { P(TouchPhase.Moved, 40, 60, 1.1), P(TouchPhase.Ended, 40, 60, 1.2) }, 2);
        Types(input, DsGestureType.Up);
        input.Poll(new[] { P(TouchPhase.Began, 20, 30, 2), P(TouchPhase.Ended, 20, 30, 2.1) }, 2);
        Types(input, DsGestureType.Down, DsGestureType.Up, DsGestureType.Tap);
    }

    static void Pinch()
    {
        var input = new DsInput();
        input.Poll(new[] {
            P(TouchPhase.Began, 100, 100, 1, 2),
            P(TouchPhase.Began, 300, 100, 1.01, 7),
            P(TouchPhase.Moved, 400, 100, 1.02, 7),
        }, 1);
        Types(input, DsGestureType.Down, DsGestureType.Up, DsGestureType.Pinch);
        Assert(Math.Abs(input.Gestures.Last().Scale - 1.5f) < 0.001f, "Incorrect pinch scale");
        input.Poll(new[] {
            P(TouchPhase.Ended, 100, 100, 1.1, 2),
            P(TouchPhase.Moved, 450, 100, 1.2, 7),
            P(TouchPhase.Ended, 450, 100, 1.3, 7),
        }, 1);
        Types(input);
    }

    static void DragAndFling()
    {
        var input = new DsInput();
        input.Poll(new[] {
            P(TouchPhase.Began, 100, 100, 1),
            P(TouchPhase.Moved, 300, 100, 1.05),
            P(TouchPhase.Ended, 300, 100, 1.06),
        }, 1);
        Types(input, DsGestureType.Down, DsGestureType.Drag, DsGestureType.Up, DsGestureType.Fling);
        input.Poll(new[] {
            P(TouchPhase.Began, 100, 100, 2),
            P(TouchPhase.Moved, 300, 100, 2.05),
            P(TouchPhase.Ended, 300, 100, 3),
        }, 1);
        Types(input, DsGestureType.Down, DsGestureType.Drag, DsGestureType.Up);
        input.Poll(new[] { P(TouchPhase.Began, 100, 100, 4), P(TouchPhase.Ended, 200, 100, 4.5) }, 1);
        Assert(input.Gestures.All(g => g.Type != DsGestureType.Tap), "Release movement became a tap");
    }
}

// Only the scene-dependent lookup is replaced. The tests execute the actual
// gesture recognizer and coordinate mapping against Unity's managed types.
public static class DsPresentation
{
    public static Vector2 FromSurface(Vector2 point) =>
        DsTouch.MapToCanvas(point, new Vector2(1240, 1080), 1080);
}
