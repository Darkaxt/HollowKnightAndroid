// Read-only selection boundary informed by HKDualScreen.Bottom.Inventory (MIT),
// igawa6/dualsouls 5c22451435b772acde0c7e6456f9019bc1baef73.
// Browsing is display-only. Native Select/PaneStart can write seen/new flags.
using System;

public interface IDsPortSelection
{
    bool IsCurrent(object owner, object item, object data);
    void Display(object owner, object item);
    bool CanSubmit(object owner, object item);
    bool Submit(object owner, object item);
}

public sealed class DsPortSelectState
{
    readonly IDsPortSelection _native;
    object _owner, _item, _data;
    int _revision;

    public DsPortSelectState(IDsPortSelection native) { _native = native; }
    public bool HasSelection => Validate();
    public bool ActionAvailable => Validate() && _native.CanSubmit(_owner, _item);

    public bool Select(object owner, object item, object data)
    {
        Clear();
        if (owner == null || item == null || !_native.IsCurrent(owner, item, data)) return false;
        _owner = owner; _item = item; _data = data;
        try { _native.Display(owner, item); }
        catch { Clear(); throw; }
        return Validate();
    }

    bool Validate()
    {
        if (_owner != null && _item != null && _native.IsCurrent(_owner, _item, _data)) return true;
        Clear();
        return false;
    }

    public bool TrySubmit()
    {
        int revision = _revision;
        if (!Validate() || !_native.CanSubmit(_owner, _item)) return false;
        // The prompt is not authority. Identity, legality, and the selection
        // revision are checked here; a native callback cannot substitute an item.
        if (!Validate() || revision != _revision) return false;
        return _native.Submit(_owner, _item);
    }

    public void Clear() { _revision++; _owner = null; _item = null; _data = null; }
}

// Only the optional owned detail transaction enters this boundary. Native owner
// loss and failed cleanup remain outer page failures, never a retained-list claim.
public static class DsPortDetailAttempt
{
    public static bool Try(Func<bool> current, Func<bool> display, Action clear, Action<Exception> report)
    {
        if (!current()) throw new InvalidOperationException("Detail owner replaced");
        try
        {
            bool shown = display();
            if (!current()) throw new InvalidOperationException("Detail owner replaced");
            return shown;
        }
        catch (Exception error)
        {
            if (!current()) throw;
            clear();
            if (!current()) throw new InvalidOperationException("Detail owner replaced during cleanup", error);
            report(error.GetBaseException());
            return false;
        }
    }
}

// One page/selection owns one native detail. There is deliberately no global
// prefab-keyed cache; even two simultaneous pages sharing a prefab cannot share
// its mutable display. Failed cleanup retains the exact instance for retry.
public sealed class DsPortOwnedDetail
{
    object _owner, _item, _prefab, _destination, _owned;
    Action<object> _release;
    bool _retiring;
    public void Show(object owner, object item, object prefab, object destination,
        Func<bool> current, Func<object> create, Action<object> present, Action<object> release)
    {
        if (_retiring) Clear();
        if (!current()) { Clear(); throw new InvalidOperationException("Counter owner replaced"); }
        if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(item, _item) ||
            !ReferenceEquals(prefab, _prefab) || !ReferenceEquals(destination, _destination)) Clear();
        try
        {
            if (_owned == null)
            {
                var owned = create();
                if (owned == null || ReferenceEquals(owned, prefab) || ReferenceEquals(owned, destination) ||
                    ReferenceEquals(owned, owner) || ReferenceEquals(owned, item))
                    throw new InvalidOperationException("Counter detail must be independently owned");
                _owned = owned; _owner = owner; _item = item; _prefab = prefab; _destination = destination; _release = release;
            }
            if (!current()) throw new InvalidOperationException("Counter owner replaced during creation");
            present(_owned);
            if (!current()) throw new InvalidOperationException("Counter owner replaced during display");
        }
        catch { Clear(); throw; }
    }
    public void Clear()
    {
        if (_owned == null) return;
        _retiring = true;
        _release(_owned);
        _owned = _owner = _item = _prefab = _destination = null; _release = null; _retiring = false;
    }
}

// Refresh the complete native SaveEquips enumeration, never only the target.
// Read/admit all slots before presentation writes; recheck after callbacks run.
public static class DsPortSlotRefresh
{
    public static bool Try<TSlot, TItem>(TSlot[] slots, TSlot target, Func<TSlot, bool> admitted,
        Func<TSlot, TItem> saved, Func<TSlot, TItem> presented, Action<TSlot, TItem> refresh)
        where TSlot : class where TItem : class
    {
        if (slots == null || target == null) return false;
        bool found = false;
        var values = new TItem[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || !admitted(slot)) return false;
            for (int j = 0; j < i; j++) if (ReferenceEquals(slot, slots[j])) return false;
            values[i] = saved(slot);
            if (ReferenceEquals(slot, target))
            {
                found = true;
                if (!ReferenceEquals(values[i], presented(slot))) return false;
            }
        }
        if (!found) return false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!admitted(slots[i]) || !ReferenceEquals(values[i], saved(slots[i]))) return false;
            refresh(slots[i], values[i]);
        }
        for (int i = 0; i < slots.Length; i++)
            if (!admitted(slots[i]) || !ReferenceEquals(values[i], saved(slots[i])) ||
                !ReferenceEquals(values[i], presented(slots[i]))) return false;
        return true;
    }
}

// Temporary metadata belongs only to native presentation setup. Even a partial
// setup must return its retained instance to exact source action authority first.
public static class DsPortCrestSetup
{
    public static void Run<T>(T source, Func<T> create, Action<T> setup, Action<T> bindSource, Action<T> release) where T : class
    {
        var metadata = create();
        if (metadata == null || ReferenceEquals(metadata, source)) throw new InvalidOperationException("Crest metadata must be independently owned");
        try { setup(metadata); }
        finally
        {
            bindSource(source);
            release(metadata);
        }
    }
}

// Shared bound for one selected detail's explicitly prepared native child factories.
// Never truncates a native counter or permits arithmetic overflow to bypass admission.
public sealed class DsPortDetailBudget
{
    public const int MaximumEntries = 4096;
    int _used;
    public void Reserve(int count)
    {
        if (count < 0 || count > MaximumEntries - _used)
            throw new InvalidOperationException("Native detail exceeds finite entry bound");
        _used += count;
    }
}

// Exact-identity graph ownership: register before population so aliases/cycles and
// partial allocation share one retained release path. No native property access here.
public sealed class DsPortOwnedGraph
{
    sealed class Identity : System.Collections.Generic.IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
    readonly System.Collections.Generic.Dictionary<object, object> _copies =
        new System.Collections.Generic.Dictionary<object, object>(new Identity());
    readonly System.Collections.Generic.List<Action> _release = new System.Collections.Generic.List<Action>();
    int _depth;
    bool _failed, _retiring;
    public object Copy(object source, Func<object> create, Action<object> populate, Action<object> release)
    {
        if (_failed || _retiring) throw new InvalidOperationException("Owned native graph awaits retirement retry");
        if (source == null) return null;
        if (_copies.TryGetValue(source, out var previous)) return previous;
        if (_copies.Count >= 65536 || _depth >= 128) throw new InvalidOperationException("Owned native graph exceeds finite work bound");
        _depth++;
        try
        {
            var owned = create();
            if (owned == null || ReferenceEquals(source, owned)) throw new InvalidOperationException("Native graph resource must be independently owned");
            _copies.Add(source, owned);
            if (release != null) _release.Add(() => release(owned));
            populate(owned);
            return owned;
        }
        catch { _failed = true; throw; }
        finally { _depth--; }
    }
    public void Clear()
    {
        if (_depth != 0) throw new InvalidOperationException("Cannot retire inside native graph population");
        _retiring = true;
        while (_release.Count != 0)
        {
            int last = _release.Count - 1;
            _release[last]();
            _release.RemoveAt(last);
        }
        _copies.Clear(); _failed = _retiring = false;
    }
}

// Pure native-markup preflight. Returned glyph inputs are inspection only; the
// original native text is never rewritten. Mirrors TMP's escaped-codepoint and
// style-hash routes, including resource tags hidden by style expansion.
public static class DsPortTextPreflight
{
    public static int StyleHash(string value)
    { int hash = 0; foreach (char c in value) hash = unchecked((hash * 33) ^ c); return hash; }
    static int TagHash(string value)
    { int hash = 0; foreach (char c in value) hash = unchecked(hash * 7 + c); return hash; }
    static bool Tag(string value, string name) => TagHash(value) == TagHash(name) || TagHash(value) == TagHash(name.ToUpperInvariant());
    static string Decode(string value, bool controls)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                char next = value[i + 1]; int digits = next == 'u' ? 4 : next == 'U' ? 8 : 0;
                if (digits != 0 && i + 1 + digits < value.Length)
                {
                    if (!int.TryParse(value.Substring(i + 2, digits), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int code) || code < 0 || code > 0x10ffff || (code >= 0xd800 && code <= 0xdfff))
                        throw new InvalidOperationException("unsupported native Unicode escape");
                    result.Append(char.ConvertFromUtf32(code)); i += digits + 1; continue;
                }
                if (controls && next == '\\' && i + 2 < value.Length)
                { result.Append(next).Append(value[i + 2]); i += 2; continue; }
                if (controls && (next == 'n' || next == 'r' || next == 't'))
                { result.Append(next == 'n' ? '\n' : next == 'r' ? '\r' : '\t'); i++; continue; }
            }
            result.Append(c);
        }
        return result.ToString();
    }
    public static System.Collections.Generic.HashSet<int> Check(string value, Func<int, string[]> styles,
        bool? parseControls = null, bool richText = true, bool decodeEscapes = true)
    {
        var result = new System.Collections.Generic.HashSet<int>();
        var visiting = new System.Collections.Generic.HashSet<int>(); int work = 0;
        bool upper = false, lower = false, noParse = false;
        void Visit(string input, int depth)
        {
            if (input == null) return;
            if (depth > 32 || input.Length > 65536 - work) throw new InvalidOperationException("native text/style expansion exceeds finite bound");
            work += input.Length;
            for (int i = 0; i < input.Length; i++)
            {
                if (richText && input[i] == '<')
                {
                    int end = input.IndexOf('>', i + 1);
                    if (end >= 0)
                    {
                        string raw = input.Substring(i + 1, end - i - 1);
                        int split = raw.IndexOfAny(new[] { '=', ' ' });
                        string tag = split < 0 ? raw : raw.Substring(0, split);
                        string name = tag.StartsWith("/") ? tag.Substring(1) : tag;
                        if (noParse && Tag(tag, "/noparse")) { noParse = false; i = end; continue; }
                        if (!noParse)
                        {
                        foreach (string resource in new[] { "font", "material", "sprite" })
                            if (Tag(name, resource)) throw new InvalidOperationException("unsupported resource-resolving tag <" + resource + ">" + (depth > 0 ? " in native style expansion" : ""));
                        if (Tag(name, "style") && split >= 0 && raw[split] == '=')
                        {
                            string key = raw.Substring(split + 1);
                            if (key.StartsWith("\""))
                            { int quote = key.IndexOf('"', 1); key = quote >= 0 ? key.Substring(1, quote - 1) : key.Substring(1); }
                            else { int space = key.IndexOf(' '); if (space >= 0) key = key.Substring(0, space); }
                            int hash = StyleHash(key);
                            var expansion = styles != null ? styles(hash) : null;
                            if (expansion == null) throw new InvalidOperationException("resident native style unavailable: " + key);
                            if (!visiting.Add(hash)) throw new InvalidOperationException("native style expansion cycle: " + key);
                            if (expansion.Length > 2) throw new InvalidOperationException("native style definitions exceed opening/closing pair");
                            if (expansion.Length > 0) Visit(expansion[0], depth + 1);
                            bool afterOpening = noParse;
                            // The caller can leave noparse before closing the style.
                            // Inspect its closing definition in that reachable state.
                            noParse = false;
                            if (expansion.Length > 1) Visit(expansion[1], depth + 1);
                            noParse = afterOpening;
                            visiting.Remove(hash);
                        }
                        upper |= Tag(name, "uppercase") || Tag(name, "allcaps") || Tag(name, "smallcaps");
                        lower |= Tag(name, "lowercase");
                        bool formatting = name.StartsWith("#");
                        foreach (string ordinary in new[] { "b", "i", "u", "s", "sub", "sup", "color", "alpha", "size", "font-weight", "align", "width", "cspace", "mspace", "voffset", "pos", "space", "indent", "line-indent", "line-height", "noparse", "link", "page", "br", "lowercase", "uppercase", "allcaps", "smallcaps", "nobr", "style" })
                            formatting |= Tag(name, ordinary);
                        // Unknown tags may be rendered literally by native TMP.
                        if (Tag(tag, "noparse")) noParse = true;
                        if (formatting) { i = end; continue; }
                        }
                    }
                }
                int character = input[i];
                if (char.IsHighSurrogate(input[i]) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                { character = char.ConvertToUtf32(input, i); i++; }
                else if (char.IsSurrogate(input[i])) throw new InvalidOperationException("unpaired native text surrogate");
                result.Add(character);
            }
        }
        if (value != null)
        {
            if (!decodeEscapes) Visit(value, 0);
            else if (parseControls.HasValue) Visit(Decode(value, parseControls.Value), 0);
            else
            {
                Visit(Decode(value, true), 0);
                // Independent native escape modes, each with its own bounded work.
                work = 0; noParse = false;
                Visit(Decode(value, false), 0);
            }
        }
        if (upper || lower) foreach (int character in new System.Collections.Generic.List<int>(result))
            if (character <= 65535)
            {
                if (upper) result.Add(char.ToUpper((char)character));
                if (lower) result.Add(char.ToLower((char)character));
            }
        return result;
    }
    // Native TextMeshPro tests direct glyph tables, never recursively traverses
    // a fallback's own fallback list. Keep local-before-settings ordering.
    public static object Resolve(object primary, int character, Func<object, bool> owned,
        Func<object, int, bool> hasGlyph, Func<object, System.Collections.Generic.IEnumerable<object>> fallbacks)
    {
        bool Has(object font)
        {
            if (!owned(font)) throw new InvalidOperationException("native fallback escaped owned graph");
            return hasGlyph(font, character);
        }
        if (primary == null) throw new InvalidOperationException("native primary font missing");
        if (Has(primary)) return primary;
        int count = 0;
        foreach (var font in fallbacks(primary))
        {
            if (++count > 65536) throw new InvalidOperationException("native fallback work bound exceeded");
            if (font != null && Has(font)) return font;
        }
        throw new InvalidOperationException("native glyph U+" + character.ToString("X") + " would escape owned fallbacks");
    }
}

public static class DsPortSlotAction
{
    public static bool CanTargetCrest(object selected, object slotCrest, object retainedSource, object actualSource,
        bool unlocked, bool visible, bool hidden) => selected != null && retainedSource != null &&
        ReferenceEquals(selected, slotCrest) && ReferenceEquals(retainedSource, actualSource) && unlocked && visible && !hidden;
    // Native locked Submit removes an existing item; only empty locked slots
    // enter the paid hold. A pending placement must never trigger that hold.
    public static bool CanPlaceOrRemove(bool locked, bool equipped, bool pending) =>
        locked ? equipped && !pending : equipped || pending;
}

// One accepted single-stream Down, never a fabricated physical finger identity.
// Raw observation happens before modal precedence; a swallowed Up still retires it.
// Native actions unwind before a reentrant owner-loss request may retire their page.
// Pending retirement is drained by the next outer page/runtime callback, never by
// replaying the native action and never from IEnumerator.MoveNext's own finally.
public sealed class DsPortActionBoundary
{
    public bool Active { get; private set; }
    public bool Pending { get; private set; }
    public bool DeferRetirement()
    {
        Pending = Active;
        return Active;
    }
    public void Run(Action native)
    {
        if (Active || Pending) throw new InvalidOperationException("Native action is executing or awaiting retirement");
        Active = true;
        try { native(); }
        catch { Pending = true; throw; }
        finally { Active = false; }
    }
}

public sealed class DsPortActionHold
{
    long _generation;
    bool _pressed;
    public void Observe(bool began, bool ended)
    {
        if (!began && !ended) return;
        _generation++; _pressed = began && !ended;
    }
    public void Liveness(bool live) { if (!live && _pressed) Observe(false, true); }
    public long Capture() => _generation;
    public bool Current(long generation) => _pressed && generation == _generation;
    public bool Step(long generation, Func<bool> legal, Action step)
    {
        if (!Current(generation) || !legal() || !Current(generation)) return false;
        step(); return true;
    }
}

// Unity remains responsible for each yielded wait. This never flattens/skips a
// nested yield or changes native timing. Failed cancellation cannot resume work.
public sealed class DsPortGuardedRoutine : System.Collections.IEnumerator, IDisposable
{
    readonly Func<bool> _current;
    readonly System.Collections.IEnumerator _native;
    readonly Action _cancel;
    bool _retiring, _finished;
    public DsPortGuardedRoutine(Func<bool> current, System.Collections.IEnumerator native, Action cancel)
    { _current = current; _native = native; _cancel = cancel; }
    public object Current => _native.Current;
    public bool MoveNext()
    {
        if (_finished) return false;
        if (_retiring || !_current()) { Dispose(); return false; }
        try
        {
            if (_native.MoveNext()) return true;
            _finished = true; return false;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (_finished) return;
        _retiring = true;
        _cancel();
        (_native as IDisposable)?.Dispose();
        _finished = true;
    }
    public void Reset() => throw new NotSupportedException();
}

// Native InventoryItemCollectable commit ordering. A callback exception cannot
// authorize replay. Pending closing use survives release, like native ResetConsume.
public sealed class DsPortConsumeCommit
{
    bool _started, _closeEventAttempted;
    public bool PendingClose { get; private set; }
    public void Commit(bool closing, bool take, Func<bool> current, Action response, Action takeItem)
    {
        if (_started || !current()) throw new InvalidOperationException("Inventory consume commit no longer authorized");
        _started = true;
        if (closing) PendingClose = true;
        else response();
        if (!current()) throw new InvalidOperationException("Inventory owner lost after consume response");
        if (take) takeItem();
        if (!current()) throw new InvalidOperationException("Inventory owner lost after native take");
    }
    public void FinishClose(Func<bool> authority, Action cancelInventory, Action useItem, bool cancelled)
    {
        if (!PendingClose) return;
        if (!authority()) throw new InvalidOperationException("Inventory pending close requires its exact native owner");
        if (!cancelled && !_closeEventAttempted)
        {
            _closeEventAttempted = true;
            cancelInventory();
        }
        if (!authority()) throw new InvalidOperationException("Inventory pending use lost owner after close event");
        PendingClose = false; // Native call may throw after committing; never replay it.
        useItem();
    }
}

// Only native-selected controller variants may drive this exact owned tool-entry Animator.
// Freeze the array contents; a later refresh cannot adopt replacement assets or targets.
public sealed class DsPortToolAnimatorAuthority
{
    readonly object _animator;
    readonly object[] _variants;
    public DsPortToolAnimatorAuthority(object animator, object[] variants)
    {
        if (animator == null || variants == null || variants.Length == 0 || variants.Length > 4096)
            throw new InvalidOperationException("Tool animator authority unavailable");
        _animator = animator; _variants = (object[])variants.Clone();
    }
    public bool Current(object animator, object[] variants, object controller)
    {
        if (!ReferenceEquals(_animator, animator) || variants == null || variants.Length != _variants.Length) return false;
        bool found = controller == null; // Inactive native template has not run SetData yet.
        for (int i = 0; i < variants.Length; i++)
        {
            if (!ReferenceEquals(_variants[i], variants[i])) return false;
            found |= ReferenceEquals(controller, _variants[i]);
        }
        return found;
    }
}

// Native EndConsume does not own committed visual retirement; OnPaneEnd does.
// One record per exact owned entry survives release/new holds without accumulating effects.
public sealed class DsPortConsumeVisualLifetime
{
    bool _committed, _retiring, _retired;
    public void Commit()
    {
        if (_retiring || _retired) throw new InvalidOperationException("Consume visual is retiring");
        _committed = true;
    }
    public void ReleaseHold(Action release)
    { if (!_committed) Retire(release); }
    public void Retire(Action release)
    {
        if (_retired) return;
        _retiring = true;
        release();
        _retired = true;
    }
}

public static class DsPortGesturePrecedence
{
    public static bool Consume(Func<bool> overlay, Func<bool> mods, Func<bool> tabs, Func<bool> page)
    {
        return (overlay != null && overlay()) || (mods != null && mods()) ||
               (tabs != null && tabs()) || (page != null && page());
    }
}
