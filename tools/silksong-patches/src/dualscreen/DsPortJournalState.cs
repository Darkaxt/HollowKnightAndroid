// Journal-only lifecycle for native pane/entry/cursor ownership. No game data writes.
using System;
using System.Collections.Generic;

// Explicit page inputs only: copied scalars/strings and exact native identities.
// Never hashes or serializes PlayerData, nor borrows mutable collection storage.
public sealed class DsPortPageReadException : InvalidOperationException
{
    public DsPortPageReadException(Exception cause) : base("Native page content unavailable; retry next poll: " + cause.Message, cause) { }
}
public sealed class DsPortPageSnapshot
{
    public const int MaximumValues = 65536;
    readonly object[] _values;
    public DsPortPageSnapshot(IEnumerable<object> values)
    {
        try
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new List<object>();
            foreach (var value in values)
            {
                if (copy.Count == MaximumValues) throw new InvalidOperationException("Native page snapshot exceeds finite input bound");
                copy.Add(value);
            }
            _values = copy.ToArray();
        }
        catch (Exception error) { throw new DsPortPageReadException(error); }
    }
    public static bool IsReadFailure(Exception error)
    {
        for (; error != null; error = error.InnerException) if (error is DsPortPageReadException) return true;
        return false;
    }
    public IEnumerable<string> TextInputs
    {
        get { foreach (var value in _values) if (value is string text) yield return text; }
    }
    public bool Same(DsPortPageSnapshot other)
    {
        if (other == null || _values.Length != other._values.Length) return false;
        for (int i = 0; i < _values.Length; i++)
        {
            var a = _values[i]; var b = other._values[i];
            if (ReferenceEquals(a, b)) continue;
            if (a == null || b == null || a.GetType() != b.GetType() ||
                (!(a is string) && !a.GetType().IsValueType) || !a.Equals(b)) return false;
        }
        return true;
    }
}

public sealed class DsJournalToken
{
    public readonly object Pane, List, Data, Records, Host;
    public readonly long Epoch;
    public readonly DsPortPageSnapshot Content;
    public DsJournalToken(object pane, object list, object data, object records, object host, long epoch,
        DsPortPageSnapshot content = null)
    { Pane=pane; List=list; Data=data; Records=records; Host=host; Epoch=epoch; Content=content; }
    public bool Same(DsJournalToken other) => other!=null && ReferenceEquals(Pane,other.Pane) &&
        ReferenceEquals(List,other.List) && ReferenceEquals(Data,other.Data) &&
        ReferenceEquals(Records,other.Records) && ReferenceEquals(Host,other.Host) && Epoch==other.Epoch &&
        (Content == null ? other.Content == null : Content.Same(other.Content));
}
public interface IDsPortJournalNative
{
    bool IsCurrent(DsJournalToken token);
    string Inspect(DsJournalToken token);
    object CloneInactive(DsJournalToken token);
    void BindAndVerify(DsJournalToken token, object clone);
    void ActivateForLayout(DsJournalToken token, object clone);
    bool TrySettle(DsJournalToken token, object clone);
    void Present(DsJournalToken token, object clone);
    void ClearSelection(object clone);
    void DestroyOwned(object clone);
}
public sealed class DsPortJournalState
{
    readonly IDsPortJournalNative _native;
    DsJournalToken _token;
    object _owned;
    int _generation, _activationFrame;
    bool _attempted, _settled, _retiring, _selectionCleared, _releasing, _constructing, _ticking, _refreshPending;
    public DsPortJournalState(IDsPortJournalNative native) { _native=native; }
    public bool Ready { get; private set; }
    public object Owned => _owned;
    public string Problem { get; private set; }
    bool Current(DsJournalToken token,int generation) => generation==_generation && ReferenceEquals(token,_token) && _native.IsCurrent(token);
    void Abort(DsJournalToken token,int generation)
    { if (generation==_generation && ReferenceEquals(token,_token)) Clear(); }
    public void Tick(DsJournalToken requested,int frame)
    {
        if (_releasing) return;
        if (_ticking)
        {
            if (requested == null || !requested.Same(_token))
            { _generation++; Ready = false; _refreshPending = true; }
            return;
        }
        _ticking = true;
        try { TickCore(requested, frame); }
        finally
        {
            try { if (_refreshPending) { _refreshPending = false; Clear(); } }
            finally { _ticking = false; }
        }
    }
    void TickCore(DsJournalToken requested,int frame)
    {
        // A native release callback cannot reenter and publish a new page while
        // the exact prior page (including selected custom detail) is retained.
        if (_releasing || _constructing) return;
        if (_retiring) Clear();
        if (requested==null) { Clear(); return; }
        if (!requested.Same(_token)) { Clear(); _token=requested; }
        var token=_token; int generation=_generation;
        try
        {
            if (!Current(token,generation)) { Abort(token,generation); return; }
            if (!_attempted)
            {
                _attempted=true;
                string problem=_native.Inspect(token);
                if (!Current(token,generation)) { Abort(token,generation); return; }
                if (problem!=null) { Problem=problem; return; }
                object created;
                _constructing=true;
                try { created=_native.CloneInactive(token); }
                finally { _constructing=false; }
                _owned=created;
                if (!Current(token,generation)) { Clear(); return; }
                if (_owned==null) throw new InvalidOperationException("Journal clone missing");
                _native.BindAndVerify(token,_owned);
                if (!Current(token,generation)) { Abort(token,generation); return; }
                _native.ActivateForLayout(token,_owned);
                if (!Current(token,generation)) { Abort(token,generation); return; }
                _activationFrame=frame;
                return;
            }
            if (_owned==null || frame<=_activationFrame) return;
            if (!_settled)
            {
                bool settled=_native.TrySettle(token,_owned);
                if (!Current(token,generation)) { Abort(token,generation); return; }
                if (!settled) return;
                _settled=true;
            }
            Ready=false;
            _native.Present(token,_owned);
            if (!Current(token,generation)) { Abort(token,generation); return; }
            Ready=true;
        }
        catch (Exception error)
        {
            try { Abort(token,generation); }
            catch (Exception retirementError)
            { throw new AggregateException("Native page operation and retirement failed", error, retirementError); }
            throw;
        }
    }
    // Explicit successful native-action refresh, never a browse/poll shortcut.
    // The page has already refreshed through its native driver. Keep that exact
    // instance (and its in-flight visual effects) only under unchanged owners.
    public bool RefreshAfterAction(object owned, DsJournalToken previous, DsJournalToken next)
    {
        if (!Ready || _ticking || _releasing || _retiring || _constructing || previous == null || next == null ||
            !ReferenceEquals(_owned, owned) || !ReferenceEquals(_token, previous) ||
            !ReferenceEquals(previous.Pane, next.Pane) || !ReferenceEquals(previous.List, next.List) ||
            !ReferenceEquals(previous.Data, next.Data) || !ReferenceEquals(previous.Records, next.Records) ||
            !ReferenceEquals(previous.Host, next.Host) || previous.Epoch != next.Epoch) return false;
        int generation = _generation;
        if (!_native.IsCurrent(next) || generation != _generation || !Ready ||
            !ReferenceEquals(_owned, owned) || !ReferenceEquals(_token, previous)) return false;
        _token = next;
        return true;
    }
    // Frame retains this exact presentation only until exit completes/interruption.
    // Revoke the token immediately without clearing native rows/detail/cursor.
    // Existing Clear remains the sole retirement path, including failed cleanup retry.
    public bool RetainOutgoingPresentation()
    {
        if (!Ready || _owned == null || _ticking || _releasing || _retiring || _constructing) return false;
        _generation++; Ready = false; _token = null;
        return true;
    }
    public void Clear()
    {
        _generation++; Ready=false; _settled=false; _attempted=false; Problem=null; _token=null;
        if (_owned==null) return;
        _retiring=true;
        if (_releasing) return;
        _releasing=true;
        try
        {
            try
            {
                if (!_selectionCleared) { _native.ClearSelection(_owned); _selectionCleared=true; }
            }
            finally
            {
                // Do not surrender the page before this returns. Tick(null), a
                // new requested page, inactive runtime restore and the existing
                // retained disposal pump can all retry the same failed retirement.
                _native.DestroyOwned(_owned);
                _owned=null; _retiring=false; _selectionCleared=false;
            }
        }
        finally { _releasing=false; }
    }
}
public static class DsJournalAdmission
{
    public static bool BridgeAllowed(string source,string destination,bool ownedParent,bool localRequired) =>
        ownedParent && localRequired &&
        ((source=="UnityEngine.SpriteRenderer" && destination=="TeamCherry.NestedFadeGroup.NestedFadeGroupSpriteRenderer") ||
         (source=="TMProOld.TextMeshPro" && destination=="NestedFadeGroupTextMeshPro"));
    public static void ReleaseOwned(Action clear,Action deactivate,Action destroy)
    {
        try { clear(); }
        finally { try { deactivate(); } finally { destroy(); } }
    }
    public static bool ResponseIsPrepared(string mode,bool enabled) => !enabled && mode=="JustStart";
    public static bool CursorAcceptsInput(bool present,bool enabled) => !present || enabled;
    public static string ReferenceProblem(string field,string expected,string actual,bool optional)
    {
        if (actual==null) return optional ? null : field+": missing "+expected+" reference";
        return actual==expected ? null : field+": escaped "+expected+" to "+actual;
    }
    public static bool CallbackAllowed(string method,string mode,string callState,bool local) =>
        local && method=="SetActive" && mode=="Bool" &&
        (callState=="Off" || callState=="RuntimeOnly" || callState=="EditorAndRuntime");
}
public static class DsJournalGeometry
{
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static float ClampScroll(float cmin,float cmax,float vmin,float vmax,float desired)
    {
        if (!Finite(cmin) || !Finite(cmax) || !Finite(vmin) || !Finite(vmax) || !Finite(desired) || cmax<cmin || vmax<=vmin)
            throw new ArgumentException("Non-finite or inverted Journal geometry");
        float top=vmax-cmax;
        if (cmax-cmin<=vmax-vmin) return top;
        return Math.Max(top,Math.Min(vmin-cmin,desired));
    }
    public static float LocalDrag(float pixels,float scale)
    {
        if (!Finite(pixels) || !Finite(scale) || scale<=0) throw new ArgumentException("Invalid Journal presentation scale");
        return pixels/scale;
    }
    public static bool Contains(float min,float max,float itemMin,float itemMax) =>
        Finite(min) && Finite(max) && Finite(itemMin) && Finite(itemMax) &&
        max>=min && itemMax>=itemMin && itemMin>=min && itemMax<=max;
}
