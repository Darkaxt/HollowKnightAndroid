package dev.silksong.shell;

import android.app.Activity;
import android.graphics.Rect;
import android.os.SystemClock;
import android.util.Log;
import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.SurfaceView;

// Read-only, event-bound observations of Unity's PRIMARY SurfaceView. A callback
// observation is not a native renderer acknowledgment or proof of recovery.
// No polling, hierarchy discovery, Surface ownership or lifecycle dispatch.
// Event-only Logcat output; no file I/O. Native generation is not publicly exposed.
final class PlayerSurfaceTrace implements SurfaceHolder.Callback
{
    private final SurfaceHolder holder;
    private final int activityId;
    private int epoch;
    private boolean attached;
    private boolean disabled;

    static PlayerSurfaceTrace attach(Activity activity, SurfaceView view)
    {
        if (view == null) return null;
        return new PlayerSurfaceTrace(activity, view.getHolder());
    }

    private PlayerSurfaceTrace(Activity activity, SurfaceHolder holder)
    {
        this.holder = holder;
        activityId = System.identityHashCode(activity);
        holder.addCallback(this);
        attached = true;
        record("attached");
    }

    void record(String event)
    {
        if (!attached || disabled) return;
        try
        {
            Surface surface = holder.getSurface();
            Rect frame = holder.getSurfaceFrame();
            Log.i("PlayerSurfaceTrace", "event=" + event + " activity=" + activityId
                + " holder=" + System.identityHashCode(holder) + " epoch=" + epoch
                + " surface=" + System.identityHashCode(surface)
                + " nativeGeneration=unavailable-public-sdk"
                + " valid=" + (surface != null && surface.isValid())
                + " width=" + (frame != null ? frame.width() : -1)
                + " height=" + (frame != null ? frame.height() : -1)
                + " monoNs=" + SystemClock.elapsedRealtimeNanos());
        }
        catch (RuntimeException ignored)
        {
            // Observation must never interrupt Unity or retry on healthy frames.
            disabled = true;
        }
    }

    void detach()
    {
        if (!attached) return;
        record("detached");
        attached = false;
        holder.removeCallback(this);
    }

    @Override public void surfaceCreated(SurfaceHolder source)
    {
        if (source != holder || !attached) return;
        epoch++; // Observed create callbacks, NOT an invented native generation.
        record("surfaceCreated");
    }

    @Override public void surfaceChanged(SurfaceHolder source, int format, int width, int height)
    {
        if (source == holder) record("surfaceChanged");
    }

    @Override public void surfaceDestroyed(SurfaceHolder source)
    {
        if (source == holder) record("surfaceDestroyed");
    }
}
