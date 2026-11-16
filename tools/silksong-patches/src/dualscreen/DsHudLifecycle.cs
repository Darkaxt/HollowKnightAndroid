// Silksong HUD lifecycle: manager-event ownership and the enclosing release gate.
// No Unity dependency, so the host tests compile this file directly.
using System;

// Silksong manager-event ownership; Unity supplies the actual current manager.
public sealed class DsHudManagerCallbacks
{
    public sealed class Subscription
    {
        public Action<bool, bool> State;
        public Action<bool> Pause;
        public Action Unloading, Finished;
    }

    readonly Func<object> _current;
    readonly Action _restore, _before, _rearm;
    readonly Action<bool> _pause;
    object _owner;
    public bool TransitionPending { get; private set; }

    public DsHudManagerCallbacks(Func<object> current, Action restore, Action before,
                                 Action rearm, Action<bool> pause = null)
    { _current = current; _restore = restore; _before = before; _rearm = rearm; _pause = pause; }

    public Subscription Bind(object owner, bool transition)
    {
        _owner = owner;
        TransitionPending = owner == null || transition;
        if (TransitionPending) _before(); else _rearm();
        return new Subscription
        {
            State = (boundary, playing) =>
            {
                if (!Accept(owner)) return;
                if (boundary) Before(); else if (!playing) _restore();
            },
            Pause = paused =>
            {
                if (!Accept(owner)) return;
                if (_pause != null) _pause(paused);
                if (paused) _restore();
            },
            Unloading = () => { if (Accept(owner)) Before(); },
            Finished = () =>
            {
                if (!Accept(owner) || !TransitionPending) return;
                TransitionPending = false;
                _rearm(); // never binds; native readiness remains independently required
            },
        };
    }

    bool Accept(object owner) => owner != null && ReferenceEquals(owner, _owner) &&
        ReferenceEquals(owner, _current());
    void Before() { TransitionPending = true; _before(); }
    public void Unbind() { _owner = null; TransitionPending = true; }
}

// Local Silksong enclosing-release decision. DirectDisplayHost remains unchanged
// and may attempt this callback after failed deactivation/content disposal.
public sealed class DsHudReleaseState
{
    readonly Action _restore, _disposeContent, _release, _retire;
    public bool ShutdownRequested { get; private set; }
    public bool Completed { get; private set; }
    public bool Pending => ShutdownRequested && !Completed;
    public bool BlocksReplacement => !Completed;
    public bool CanRoute => !ShutdownRequested && !Completed;
    public Exception LastFailure { get; private set; }

    public DsHudReleaseState(Action restore, Action disposeContent, Action release, Action retire)
    { _restore = restore; _disposeContent = disposeContent; _release = release; _retire = retire; }

    public void RequestShutdown(Action disposeHost)
    {
        if (Completed) return;
        ShutdownRequested = true;
        try { disposeHost(); }
        catch (Exception e) { LastFailure = e; }
    }

    public void ReleasePresentation()
    {
        if (Completed) return;
        ShutdownRequested = true;
        // The shared host deliberately continues after a content failure. This
        // local gate must retry restoration itself before releasing ANY parent.
        _restore();
        _disposeContent();
        _release();
        _retire();
        Completed = true;
        LastFailure = null;
    }

    public bool Retry()
    {
        if (!Pending) return Completed;
        try { ReleasePresentation(); return true; }
        catch (Exception e) { LastFailure = e; return false; }
    }
}
