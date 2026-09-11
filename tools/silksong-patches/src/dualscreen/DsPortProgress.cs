// Native Journal adaptation of the display-only Bottom.Inventory/Bottom.Select boundary.
// Reference: igawa6/dualsouls 5c22451435b772acde0c7e6456f9019bc1baef73 (MIT).
// Silksong's pane, grid, entries, details and cursor remain the visual authority.
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TeamCherry.NestedFadeGroup;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using PaneText = TMProOld.TextMeshPro;

public sealed partial class DsPortProgress : IDisposable, IDsPortJournalNative, IDsPortSelection
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    readonly DsPortFrame _frame;
    readonly DsPortJournalState _journal;
    readonly DsPortSelectState _selection;
    readonly Tasks _tasks;
    readonly Inventory _inventory;
    readonly Loadout _loadout;
    readonly HashSet<string> _reported = new HashSet<string>();
    DsJournalToken _current, _failed;
    bool _eligible, _disposed, _retiringPages;
    long _revision;
    float _nextContentProbe;

    sealed class Page
    {
        public DsJournalToken Token;
        public GameObject Staging, Root, EntryTemplate, CursorTemplate;
        public JournalItemManager Manager;
        public InventoryItemGrid Grid;
        public ScrollView Scroll;
        public InventoryCursor Cursor;
        public JournalEntryItem[] Entries;
        public Transform ScrollParent;
        public Bounds View, Content, Fit;
        public Vector3 ScrollOrigin;
        public float Offset, Scale;
        public int Activated, Settles;
        public readonly HashSet<PlayerDataTestResponse> Evaluated = new HashSet<PlayerDataTestResponse>();
        public readonly HashSet<JournalEntryItem> Visible = new HashSet<JournalEntryItem>();
        public JournalEntryItem Selected;
        public bool Released, Destroyed;
        public Exception CreationError;
        public readonly DsPortOwnedText Text = new DsPortOwnedText();
        public readonly HashSet<MonoBehaviour> Stopped = new HashSet<MonoBehaviour>();
    }

    public DsPortProgress(DsPortFrame frame)
    {
        _frame = frame;
        _journal = new DsPortJournalState(this);
        _selection = new DsPortSelectState(this);
        _tasks = new Tasks(frame);
        _inventory = new Inventory(frame);
        _loadout = new Loadout(frame);
        _frame.BeforeCompositionDestroyed += Invalidate;
        _frame.SelectionChanged += SelectionChanged;
    }

    public void SetTouchState(bool singleTouchActive)
    {
        _inventory.SetTouchState(singleTouchActive);
        _loadout.SetTouchState(singleTouchActive);
    }

    public void Tick(bool eligible)
    {
        if (_disposed) return;
        if (_retiringPages) Invalidate();
        if (_eligible != eligible) { Invalidate(); _eligible = eligible; }
        _tasks.Tick(eligible);
        _inventory.Tick(eligible);
        _loadout.Tick(eligible);
        try
        {
            if (_outgoing)
            {
                var outgoing = _journal.Owned as Page;
                if (eligible && outgoing != null && !outgoing.Released && KeepOutgoing(_frame, DsPageRole.Journal, outgoing.Token)) return;
                _outgoing = false; _journal.Clear(); _nextContentProbe = 0;
            }
            if ((_journal.Ready || _journal.Owned == null) && Time.unscaledTime < _nextContentProbe) return;
            _nextContentProbe = Time.unscaledTime + .125f;
            _current = null;
            _current = Capture();
            if (_current != null && _current.Same(_failed)) return;
            _journal.Tick(_current, Time.frameCount);
            if (_journal.Problem != null) Report(_journal.Problem);
        }
        catch (Exception e)
        {
            _failed = DsPortPageSnapshot.IsReadFailure(e) ? null : _current;
            _journal.Clear();
            Report(e.GetBaseException().Message);
        }
    }

    DsJournalToken Capture()
    {
        if (!_eligible || !_frame.HudReady || _frame.SelectedRole != DsPageRole.Journal || !DsGameData.InGame) return null;
        var pane = _frame.GetResidentPane(DsPageRole.Journal);
        var host = _frame.GetOrCreatePageHost(DsPageRole.Journal);
        var data = PlayerData.instance;
        var gm = GameManager.SilentInstance;
        var records = typeof(EnemyJournalManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as EnemyJournalManager;
        if (pane == null || host == null || !host.gameObject.activeInHierarchy || gm == null ||
            data == null || !ReferenceEquals(gm.playerData, data) || records == null) return null;
        var manager = pane.GetComponentInChildren<JournalItemManager>(true);
        if (manager == null || Get(records, "recordList") == null) return null;
        return new DsJournalToken(pane, manager, data, records, host, _revision + _frame.SelectionEpoch,
            new DsPortPageSnapshot(ReadContent(pane, manager, data, records)));
    }

    static IEnumerable<object> ReadContent(InventoryPane pane, JournalItemManager manager, PlayerData data, EnemyJournalManager records)
    {
        var list = Get(records, "recordList") as IEnumerable<EnemyJournalRecord>;
        if (list == null) throw new InvalidOperationException("Journal content list missing");
        yield return list; yield return data.hasJournal; yield return data.permadeathMode;
        foreach (var record in list)
        {
            yield return record;
            if (record == null) throw new InvalidOperationException("Journal content record missing");
            var kill = data.EnemyJournalKillData.GetKillData(record.name); // TryGetValue; never inserts or marks seen.
            yield return record.name; yield return kill.Kills; yield return kill.HasBeenSeen;
            yield return record.IsVisible; yield return record.IsRequiredForCompletion;
            yield return record.IsAlwaysUnlocked; yield return record.KillsRequired; yield return record.RecordType;
            yield return record.IconSprite; yield return record.EnemySprite;
            yield return (string)record.DisplayName; yield return (string)record.Description; yield return (string)record.Notes;
        }
        foreach (var value in ReadPaneConditions(pane, manager)) yield return value;
    }
    static IEnumerable<object> ReadPaneConditions(InventoryPane pane, InventoryItemManager manager)
    {
        // Exact source display-test results, not reflected PlayerData serialization.
        foreach (var root in new[] { pane.gameObject, ((Component)Get(manager, "templateItem"))?.gameObject,
            ((Component)Get(manager, "cursorPrefab"))?.gameObject })
        {
            yield return root;
            if (root == null) continue;
            foreach (var response in root.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                yield return response;
                var test = (PlayerDataTest)Get(response, "test");
                yield return test; yield return test.IsFulfilled;
            }
            foreach (var extra in root.GetComponentsInChildren<InventoryItemExtraDescription>(true))
            { yield return extra; yield return Get(extra, "descSectionParent"); yield return extra.WillDisplay; }
        }
    }

    public bool IsCurrent(DsJournalToken token)
    {
        var now = Capture();
        return token != null && token.Same(now);
    }
    void RequireCurrent(DsJournalToken token)
    { if (!IsCurrent(token)) throw new InvalidOperationException("Journal owner/selection replaced"); }

    bool _outgoing;
    // Keep only the already-presented owned clone, never recapture stale originals.
    // Content may become stale during the 0.24s exit; owner identity may not.
    static bool KeepOutgoing(DsPortFrame frame, DsPageRole role, DsJournalToken token)
    {
        if (token == null || !frame.IsOutgoingPresentation(role) || !DsGameData.InGame) return false;
        var game = GameManager.SilentInstance;
        var data = PlayerData.instance;
        var pane = frame.GetResidentPane(role);
        if (game == null || data == null || pane == null || game.isPaused || game.IsInSceneTransition || data.isInventoryOpen ||
            !ReferenceEquals(game.playerData, data) || !ReferenceEquals(token.Data, data) || !ReferenceEquals(token.Pane, pane) ||
            !ReferenceEquals(token.Host, frame.GetOrCreatePageHost(role))) return false;
        object list, records;
        switch (role)
        {
            case DsPageRole.Inventory:
                list = pane.GetComponentInChildren<InventoryItemCollectableManager>(true);
                records = ManagerSingleton<CollectableItemManager>.UnsafeInstance; break;
            case DsPageRole.Loadout:
                list = pane.GetComponentInChildren<InventoryItemToolManager>(true);
                records = ManagerSingleton<ToolItemManager>.UnsafeInstance; break;
            case DsPageRole.Tasks:
                list = pane.GetComponentInChildren<InventoryItemQuestManager>(true);
                records = typeof(QuestManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null); break;
            default:
                list = pane.GetComponentInChildren<JournalItemManager>(true);
                records = typeof(EnemyJournalManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null); break;
        }
        return list != null && records != null && ReferenceEquals(token.List, list) && ReferenceEquals(token.Records, records);
    }
    void SelectionChanged()
    {
        _selection.Clear(); // Policy only; native detail/cursor remains visible during exit.
        _nextContentProbe = 0;
        var page = _journal.Owned as Page;
        _outgoing = _journal.Ready && page != null && !page.Released && KeepOutgoing(_frame, DsPageRole.Journal, page.Token) && _journal.RetainOutgoingPresentation();
        try { if (!_outgoing) _journal.Clear(); }
        finally
        {
            try { _tasks.SelectionChanged(); }
            finally { try { _inventory.SelectionChanged(); } finally { _loadout.SelectionChanged(); } }
        }
    }

    public void Invalidate()
    {
        _outgoing = false;
        _revision++;
        _nextContentProbe = 0;
        _retiringPages = true;
        _current = null; _failed = null;
        _selection.Clear();
        try { _journal.Clear(); }
        finally
        {
            try { _tasks.Invalidate(); }
            finally
            {
                try { _inventory.Invalidate(); }
                finally { _loadout.Invalidate(); }
            }
        }
        _retiringPages = false;
    }
    void Report(string problem)
    {
        if (_reported.Add(problem)) Debug.LogWarning("[DualScreen][capability-gap] Journal: " + problem);
    }

    public string Inspect(DsJournalToken token)
    {
        var pane = (InventoryPane)token.Pane;
        var manager = (JournalItemManager)token.List;
        var entry = Get(manager, "templateItem") as JournalEntryItem;
        var cursor = Get(manager, "cursorPrefab") as InventoryCursor;
        if (entry == null || cursor == null) return "missing owned Entry/Cursor donor";
        InspectScope(pane.gameObject, "Pane", entry.gameObject, cursor.gameObject);
        InspectScope(entry.gameObject, "Entry", entry.gameObject, cursor.gameObject);
        InspectScope(cursor.gameObject, "Cursor", entry.gameObject, cursor.gameObject);
        NeedLocal(manager, pane.transform, "itemList", "nameText", "descriptionText", "descriptionLayout",
            "notesGroup", "notesText", "enemySprite", "completionParent");
        NeedLocal(Get(manager, "encounteredText"), pane.transform, "countText", "totalText");
        NeedLocal(Get(manager, "completedText"), pane.transform, "countText", "totalText");
        return null;
    }

    // Exactly three ownership scopes. Unknown component subclasses are rejected,
    // not disabled optimistically or admitted through a base-class match.
    static bool ComponentAllowed(Type type, string scope)
    {
        if (type == typeof(Transform) || type == typeof(RectTransform) || type == typeof(SpriteRenderer) ||
            type == typeof(MeshRenderer) || type == typeof(MeshFilter) || type == typeof(PaneText) ||
            type == typeof(TMProOld.TMP_SubMesh) || type == typeof(BoxCollider2D) ||
            type == typeof(PlayerDataTestResponse) || type == typeof(NestedFadeGroup) ||
            type == typeof(NestedFadeGroupSpriteRenderer) || type == typeof(NestedFadeGroupTextMeshPro) ||
            type == typeof(TextMeshProClipRect) || type == typeof(HorizontalLayoutGroup) ||
            type == typeof(VerticalLayoutGroup) || type == typeof(LayoutElement) || type == typeof(ContentSizeFitter)) return true;
        if (scope == "Pane") return type == typeof(InventoryPane) || type == typeof(JournalItemManager) ||
            type == typeof(InventoryItemGrid) || type == typeof(InventoryPaneInput) ||
            type == typeof(ScrollView) || type == typeof(InventoryAutoNavGroup);
        if (scope == "Entry") return type == typeof(JournalEntryItem);
        return scope == "Cursor" && type == typeof(InventoryCursor);
    }

    static void InspectScope(GameObject root, string scope, GameObject entry, GameObject cursor)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) throw new InvalidOperationException(scope + ": missing script");
            Transform local = root.transform;
            string kind = scope;
            var item = component.GetComponentInParent<JournalEntryItem>(true);
            var pointer = component.GetComponentInParent<InventoryCursor>(true);
            if (item != null && Within(item.transform, root.transform)) { local = item.transform; kind = "Entry"; }
            else if (pointer != null && Within(pointer.transform, root.transform)) { local = pointer.transform; kind = "Cursor"; }
            if (!ComponentAllowed(component.GetType(), kind))
                throw new InvalidOperationException(kind + ": unadmitted component " + component.GetType().FullName);
            ValidateSerializedReferences(component, local, entry, cursor);
            var response = component as PlayerDataTestResponse;
            if (response != null)
            {
                ReadEvent(response.IsFullfilled, local);
                ReadEvent(response.IsNotFulfilled, local);
            }
            var fade = component as NestedFadeGroupBase;
            if (fade != null)
            {
                var parent = fade.ParentOverride;
                if (parent != null && parent.Value != null && !Within(parent.Value.transform, local))
                    throw new InvalidOperationException(kind + ": escaped fade parentOverride");
                if (component.GetType() == typeof(NestedFadeGroupTextMeshPro) &&
                    (component.GetComponent<PaneText>() == null || component.GetComponent<MeshRenderer>() == null))
                    throw new InvalidOperationException("TMP fade bridge missing local components");
                if (component.GetType() == typeof(NestedFadeGroupSpriteRenderer))
                {
                    if (component.GetComponent<SpriteRenderer>() == null) throw new InvalidOperationException("sprite bridge missing renderer");
                    if (Get(component, "displayType").ToString() == "Frames")
                    {
                        var frames = Get(component, "frames") as Sprite[];
                        if (frames == null || frames.Length == 0 || Array.Exists(frames, s => s == null))
                            throw new InvalidOperationException("sprite bridge has invalid frame assets");
                    }
                }
            }
            var grid = component as InventoryItemGrid;
            if (grid != null)
            {
                if (grid.RowSplit <= 0) throw new InvalidOperationException("Journal grid RowSplit must be positive");
                NeedLocal(grid, root.transform, "scrollView");
                ValidateNavigation(Get(grid, "selectables"), root.transform);
                ValidateNavigation(Get(grid, "nextPages"), root.transform);
            }
        }
        ValidateBridgeCatalogue(root);
    }

    static void ValidateBridgeCatalogue(GameObject root)
    {
        if (root.GetComponentInChildren<NestedFadeGroup>(true) == null) return;
        var bridges = typeof(NestedFadeGroup).GetField("_bridges", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as IEnumerable;
        if (bridges == null) throw new InvalidOperationException("Journal native fade bridge catalogue is not resident");
        foreach (var bridge in bridges)
        {
            var source = (Type)Get(bridge, "SourceType");
            var destination = (Type)Get(bridge, "DestinationType");
            foreach (var component in root.GetComponentsInChildren(source, true))
            {
                bool required = source == typeof(SpriteRenderer) ? component.GetComponent<SpriteRenderer>() != null :
                    source == typeof(PaneText) && component.GetComponent<MeshRenderer>() != null;
                if (!DsJournalAdmission.BridgeAllowed(source.FullName, destination.FullName, true, required))
                    throw new InvalidOperationException("Journal unadmitted dynamic fade bridge " + source.FullName + " -> " + destination.FullName);
            }
        }
    }

    static void ValidateSerializedReferences(Component component, Transform local, GameObject entry, GameObject cursor)
    {
        for (Type type = component.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(Component); type = type.BaseType)
            foreach (var field in type.GetFields(Fields))
            {
                if (field.IsStatic || field.IsNotSerialized || (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true))) continue;
                object value = field.GetValue(component);
                if (component is InventoryItemManager && field.Name == "templateItem")
                { if (AsTransform(value) != entry.transform) throw new InvalidOperationException("Journal templateItem escaped Entry"); continue; }
                if (component is InventoryItemManager && field.Name == "cursorPrefab")
                { if (AsTransform(value) != cursor.transform) throw new InvalidOperationException("Journal cursorPrefab escaped Cursor"); continue; }
                // This prefab is never spawned: the owned AudioEvent value is muted before Awake.
                if (component is InventoryCursor && field.Name == "audioSourcePrefab") continue;
                CheckValue(field.Name, value, local);
            }
    }
    static void CheckValue(string name, object value, Transform local)
    {
        var array = value as Array;
        if (array != null) { foreach (object item in array) CheckValue(name, item, local); return; }
        var transform = AsTransform(value);
        if (transform != null && !Within(transform, local)) throw new InvalidOperationException(name + ": escaped owned scope");
        if (value is float && !DsJournalGeometry.Finite((float)value)) throw new InvalidOperationException(name + ": nonfinite value");
        if (value is Vector2) { var v = (Vector2)value; CheckValue(name, v.x, local); CheckValue(name, v.y, local); }
        if (value is Vector3) { var v = (Vector3)value; CheckValue(name, v.x, local); CheckValue(name, v.y, local); CheckValue(name, v.z, local); }
        if (value is Bounds) { var b = (Bounds)value; CheckValue(name, b.center, local); CheckValue(name, b.size, local); }
    }
    static void ValidateNavigation(object value, Transform local)
    {
        var array = value as Array;
        if (array == null) throw new InvalidOperationException("Journal navigation array missing");
        foreach (object group in array) if (group != null) CheckValue("Selectables", Get(group, "Selectables"), local);
    }
    static void NeedLocal(object owner, Transform local, params string[] names)
    {
        if (owner == null) throw new InvalidOperationException("Journal required reference owner missing");
        foreach (string name in names)
        {
            var target = AsTransform(Get(owner, name));
            string problem = DsJournalAdmission.ReferenceProblem(name, "owned", target == null ? null : Within(target, local) ? "owned" : "external", false);
            if (problem != null) throw new InvalidOperationException(problem);
        }
    }
    static Transform AsTransform(object value)
    { var c = value as Component; var go = value as GameObject; return c != null ? c.transform : go != null ? go.transform : null; }
    static bool Within(Transform target, Transform root) => target != null && root != null && (target == root || target.IsChildOf(root));
    static FieldInfo Field(object owner, string name)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, Fields); if (field != null) return field; }
        throw new MissingFieldException(owner.GetType().FullName, name);
    }
    static object Get(object owner, string name) => Field(owner, name).GetValue(owner);
    static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);

    sealed class DisplayCall { public GameObject Target; public bool Argument; public string State; }
    static List<DisplayCall> ReadEvent(UnityEvent value, Transform local)
    {
        if (value == null) throw new InvalidOperationException("Journal display event missing");
        var runtime = Get(Get(value, "m_Calls"), "m_RuntimeCalls") as IList;
        if (runtime == null || runtime.Count != 0) throw new InvalidOperationException("Journal display event contains unowned runtime listeners");
        var calls = Get(Get(value, "m_PersistentCalls"), "m_Calls") as IEnumerable;
        if (calls == null) throw new InvalidOperationException("Journal persistent call list missing");
        var result = new List<DisplayCall>();
        foreach (var call in calls)
        {
            var target = Get(call, "m_Target") as GameObject;
            string method = Get(call, "m_MethodName") as string;
            string mode = Get(call, "m_Mode").ToString(), state = Get(call, "m_CallState").ToString();
            if (!DsJournalAdmission.CallbackAllowed(method, mode, state, target != null && Within(target.transform, local)))
                throw new InvalidOperationException("Journal unadmitted display callback " + method + "/" + mode + "/" + state);
            result.Add(new DisplayCall { Target = target, State = state, Argument = (bool)Get(Get(call, "m_Arguments"), "m_BoolArgument") });
        }
        return result;
    }
    static UnityEvent ReconstructEvent(UnityEvent source, Transform local)
    {
        var result = new UnityEvent();
        foreach (var call in ReadEvent(source, local))
        {
            var retained = call;
            if (call.State != "Off") result.AddListener(() => retained.Target.SetActive(retained.Argument));
        }
        return result;
    }

    public object CloneInactive(DsJournalToken token)
    {
        var page = new Page { Token = token };
        try
        {
            var staging = new GameObject("DsPortJournalStaging");
            page.Staging = staging;
            staging.SetActive(false);
            staging.transform.SetParent((Transform)token.Host, false);
            var source = ((InventoryPane)token.Pane).gameObject;
            page.Root = Object.Instantiate(source, staging.transform, false);
            page.Root.name = "DsPortJournal";
            var manager = (JournalItemManager)token.List;
            page.EntryTemplate = Object.Instantiate(((JournalEntryItem)Get(manager, "templateItem")).gameObject, staging.transform, false);
            page.CursorTemplate = Object.Instantiate(((InventoryCursor)Get(manager, "cursorPrefab")).gameObject, staging.transform, false);
            page.EntryTemplate.SetActive(false); page.CursorTemplate.SetActive(false);
            page.Manager = page.Root.GetComponentInChildren<JournalItemManager>(true);
            if (page.Manager == null) throw new InvalidOperationException("Journal manager clone missing");
            return page;
        }
        catch (Exception error) { page.CreationError = error; return page; }
    }

    public void BindAndVerify(DsJournalToken token, object clone)
    {
        var page = (Page)clone;
        if (page.CreationError != null) throw new InvalidOperationException("Journal native page creation failed", page.CreationError);
        Set(page.Manager, "templateItem", page.EntryTemplate.GetComponent<JournalEntryItem>());
        Set(page.Manager, "cursorPrefab", page.CursorTemplate.GetComponent<InventoryCursor>());
        foreach (var root in new[] { page.Root, page.EntryTemplate, page.CursorTemplate })
        {
            page.Text.Prepare(root, token.Content);
            foreach (var input in root.GetComponentsInChildren<InventoryPaneInput>(true)) Object.DestroyImmediate(input);
            foreach (var response in root.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                var runOn = Field(response, "runOn");
                runOn.SetValue(response, Enum.Parse(runOn.FieldType, "JustStart"));
                response.enabled = false;
                if (!DsJournalAdmission.ResponseIsPrepared(runOn.GetValue(response).ToString(), response.enabled))
                    throw new InvalidOperationException("Journal response preparation failed");
            }
            foreach (var cursor in root.GetComponentsInChildren<InventoryCursor>(true))
            {
                var sound = (AudioEvent)Get(cursor, "changeSelectionSound");
                sound.Volume = 0f;
                Set(cursor, "changeSelectionSound", sound);
            }
            foreach (var clip in root.GetComponentsInChildren<TextMeshProClipRect>(true)) clip.enabled = false;
            Suppress(root, true);
            RouteOwned(root);
        }
        InspectScope(page.Root, "Pane", page.EntryTemplate, page.CursorTemplate);
        InspectScope(page.EntryTemplate, "Entry", page.EntryTemplate, page.CursorTemplate);
        InspectScope(page.CursorTemplate, "Cursor", page.EntryTemplate, page.CursorTemplate);
        page.Grid = (InventoryItemGrid)Get(page.Manager, "itemList");
        page.Scroll = (ScrollView)Get(page.Grid, "scrollView");
        page.ScrollParent = page.Scroll.transform.parent;
        NeedLocal(page.Manager, page.Root.transform, "itemList", "nameText", "descriptionText", "descriptionLayout",
            "notesGroup", "notesText", "enemySprite", "completionParent");
        NeedLocal(Get(page.Manager, "completedText"), page.Root.transform, "countText", "totalText");
        NeedLocal(Get(page.Manager, "encounteredText"), page.Root.transform, "countText", "totalText");
        NeedLocal(page.CursorTemplate.GetComponent<InventoryCursor>(), page.CursorTemplate.transform,
            "topLeft", "topRight", "bottomLeft", "bottomRight", "back", "backGlow", "group");
        NeedLocal(page.EntryTemplate.GetComponent<JournalEntryItem>(), page.EntryTemplate.transform,
            "emptyIcon", "standardFrame", "completeFrame", "iconSprite", "newDot");
        RequireCurrent(token);
    }

    public void ActivateForLayout(DsJournalToken token, object clone)
    {
        var page = (Page)clone;
        // All native world/local scrolling runs hidden in an unrotated unit basis.
        page.Staging.transform.rotation = Quaternion.identity;
        var parentScale = page.Staging.transform.parent.lossyScale;
        if (Mathf.Abs(parentScale.x) < .0001f || Mathf.Abs(parentScale.y) < .0001f || Mathf.Abs(parentScale.z) < .0001f)
            throw new InvalidOperationException("Journal host has degenerate scale");
        page.Staging.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
        page.Root.transform.localRotation = Quaternion.identity;
        page.Root.transform.localScale = Vector3.one;
        page.Root.transform.localPosition = Vector3.zero;
        page.Root.SetActive(true);
        RequireCurrent(token);
        page.Staging.SetActive(true); // Manager.Awake owns the one initial UpdateList.
        RequireCurrent(token);
        page.Text.Validate(page.Root);
        page.Activated = Time.frameCount;
        page.Cursor = Get(page.Manager, "cursor") as InventoryCursor;
        if (page.Cursor == null) throw new InvalidOperationException("Journal native cursor was not created");
        // Cursor.Awake must run even when the authored cursor template was inactive.
        page.Cursor.gameObject.SetActive(true);
        RequireCurrent(token);
        page.Cursor.gameObject.SetActive(false);
        Suppress(page.Root, true);
        EvaluateResponses(page, page.Root);
    }

    void EvaluateResponses(Page page, GameObject root)
    {
        foreach (var response in root.GetComponentsInChildren<PlayerDataTestResponse>(true))
        {
            if (!page.Evaluated.Add(response)) continue;
            RequireCurrent(page.Token);
            var entry = response.GetComponentInParent<JournalEntryItem>(true);
            Transform local = entry != null ? entry.transform : page.Root.transform;
            response.IsFullfilled = ReconstructEvent(response.IsFullfilled, local);
            response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, local);
            typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null);
            RequireCurrent(page.Token);
        }
    }

    public bool TrySettle(DsJournalToken token, object clone)
    {
        var page = (Page)clone;
        RequireCurrent(token);
        Suppress(page.Root, true);
        if (Time.frameCount <= page.Activated + 1) return false;
        if (++page.Settles > 3) throw new InvalidOperationException("Journal native layout did not settle");
        page.Entries = page.Grid.GetComponentsInChildren<JournalEntryItem>(true);
        var records = new HashSet<EnemyJournalRecord>();
        foreach (var entry in page.Entries)
        {
            if (!entry.gameObject.activeSelf) continue;
            if (entry.Record == null || !records.Add(entry.Record) || entry.GetComponent<Collider2D>() == null)
                throw new InvalidOperationException("Journal generated entry/record/collider mismatch");
            if (!Within(entry.transform, page.Grid.transform)) throw new InvalidOperationException("Journal generated entry escaped grid");
        }
        EvaluateResponses(page, page.Root);
        ForceText(page);
        page.Scroll.StopAllCoroutines();
        page.Scroll.enabled = false;
        page.Grid.StopAllCoroutines();
        page.Grid.enabled = false;
        NeutralizeOwnedClips(page.Root);
        page.View = page.Scroll.ViewBounds;
        CheckValue("viewBounds", page.View, page.Root.transform);
        if (page.View.size.x <= 0f || page.View.size.y <= 0f) throw new InvalidOperationException("Journal native viewport is empty");
        page.Content = (Bounds)Get(page.Scroll, "contentBounds");
        page.ScrollOrigin = page.Scroll.transform.localPosition;
        page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y, page.ScrollOrigin.y);
        SetScroll(page);
        UpdateFit(page);
        RouteOwned(page.Root);
        RequireCurrent(token);
        return true;
    }

    static void UpdateFit(Page page)
    {
        page.Fit = InSpace(page.View, page.ScrollParent, page.Root.transform);
        foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.gameObject.activeInHierarchy || !renderer.enabled || IsListOrCursor(renderer, page)) continue;
            page.Fit.Encapsulate(InSpace(renderer.bounds, null, page.Root.transform));
        }
    }
    static void RouteOwned(GameObject root)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            node.gameObject.layer = DsPresentation.CONTENT_LAYER;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var orders = new SortedSet<int>();
        foreach (var renderer in renderers) orders.Add(renderer.sortingOrder);
        var ranked = new List<int>(orders);
        if (ranked.Count >= DsPortLayers.MASK_RENDER_ORDER - DsPortLayers.PAGE_RENDER_ORDER)
            throw new InvalidOperationException("Journal native sort range exceeds page band");
        foreach (var renderer in renderers)
        {
            int rank = ranked.BinarySearch(renderer.sortingOrder);
            renderer.sortingLayerID = 0;
            renderer.sortingOrder = DsPortLayers.PAGE_RENDER_ORDER + rank;
        }
    }

    static bool IsListOrCursor(Renderer renderer, Page page) => Within(renderer.transform, page.Scroll.transform) || Within(renderer.transform, page.Cursor.transform);
    static void ForceText(Page page)
    {
        page.Text.Validate(page.Root);
        var layout = Get(page.Manager, "descriptionLayout") as LayoutGroup;
        if (layout != null) layout.ForceUpdateLayoutNoCanvas();
        foreach (var text in page.Root.GetComponentsInChildren<PaneText>(true)) text.ForceMeshUpdate(true);
    }
    static void NeutralizeOwnedClips(GameObject root)
    {
        foreach (var clip in root.GetComponentsInChildren<TextMeshProClipRect>(true))
        {
            clip.enabled = false;
            foreach (var renderer in clip.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetVector(Shader.PropertyToID("_ClipRect"), new Vector4(-100000f, -100000f, 100000f, 100000f));
                renderer.SetPropertyBlock(block);
            }
        }
    }
    static void Suppress(GameObject root, bool hidden)
    { if (root != null) foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = hidden; }

    public void Present(DsJournalToken token, object clone)
    {
        var page = (Page)clone;
        RequireCurrent(token);
        var host = (RectTransform)token.Host;
        var rect = host.rect;
        SetScroll(page);
        ForceText(page);
        UpdateFit(page);
        ApplyFit(page);
        Suppress(page.Root, false);
        page.Visible.Clear();
        var viewport = InSpace(page.View, page.ScrollParent, host);
        foreach (var entry in page.Entries)
        {
            if (!entry.gameObject.activeInHierarchy) continue;
            bool fits = true;
            foreach (var renderer in entry.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds bounds = InSpace(renderer.bounds, null, host);
                fits &= Contains(viewport, bounds) && InsidePage(page, renderer.bounds);
            }
            if (fits) page.Visible.Add(entry);
            Suppress(entry.gameObject, !fits);
        }
        // Cull entire native rows, not individual overflowing sprites.
        foreach (var entry in page.Entries)
            if (!page.Visible.Contains(entry))
                foreach (var sibling in page.Entries)
                    if (Mathf.Abs(sibling.transform.localPosition.y - entry.transform.localPosition.y) < .001f)
                    { page.Visible.Remove(sibling); Suppress(sibling.gameObject, true); }
        if (page.Selected != null && !page.Visible.Contains(page.Selected)) ClearSelection(page);
        if (page.Selected == null)
            foreach (var entry in page.Entries)
                if (page.Visible.Contains(entry)) { _selection.Select(page, entry, token.Data); break; }
        ContainFixed(page);
        RequireCurrent(token);
    }
    static void ApplyFit(Page page)
    {
        var rect = ((RectTransform)page.Token.Host).rect;
        CheckValue("fit", page.Fit, page.Root.transform);
        if (rect.width <= 0 || rect.height <= 0 || page.Fit.size.x <= 0 || page.Fit.size.y <= 0)
            throw new InvalidOperationException("Journal presentation bounds invalid");
        page.Staging.transform.localRotation = Quaternion.identity;
        page.Scale = Mathf.Min(rect.width / page.Fit.size.x, rect.height / page.Fit.size.y) * .94f;
        page.Staging.transform.localScale = Vector3.one * page.Scale;
        page.Staging.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0f) - page.Fit.center * page.Scale;
    }
    bool InsidePage(Page page, Bounds world)
    {
        var host = (RectTransform)page.Token.Host;
        var mask = _frame.ContentMask;
        return mask != null && Contains(host.rect, InSpace(world, null, host)) &&
            Contains(mask.rect, InSpace(world, null, mask));
    }
    void ContainFixed(Page page)
    {
        bool cursorFits = page.Selected != null;
        var viewport = InSpace(page.View, page.ScrollParent, null);
        foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
        {
            if (Within(renderer.transform, page.Cursor.transform))
            {
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    cursorFits &= InsidePage(page, renderer.bounds) && Contains(viewport, renderer.bounds);
            }
            else if (!Within(renderer.transform, page.Scroll.transform))
                renderer.forceRenderingOff = !InsidePage(page, renderer.bounds);
        }
        Suppress(page.Cursor.gameObject, !cursorFits);
    }
    static void SetScroll(Page page)
    { var pos = page.ScrollOrigin; pos.y = page.Offset; page.Scroll.transform.localPosition = pos; }
    static bool Contains(Bounds outer, Bounds inner) => DsJournalGeometry.Contains(outer.min.x, outer.max.x, inner.min.x, inner.max.x) && DsJournalGeometry.Contains(outer.min.y, outer.max.y, inner.min.y, inner.max.y);
    static bool Contains(Rect outer, Bounds inner) => DsJournalGeometry.Contains(outer.xMin, outer.xMax, inner.min.x, inner.max.x) && DsJournalGeometry.Contains(outer.yMin, outer.yMax, inner.min.y, inner.max.y);
    static Bounds InSpace(Bounds bounds, Transform from, Transform to)
    {
        Bounds result = new Bounds();
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x, (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
            if (from != null) p = from.TransformPoint(p);
            if (to != null) p = to.InverseTransformPoint(p);
            if (i == 0) result = new Bounds(p, Vector3.zero); else result.Encapsulate(p);
        }
        return result;
    }

    public bool OnGesture(DsGesture gesture)
    {
        if (_retiringPages) return true;
        if (_frame.SelectedRole == DsPageRole.Tasks) return _tasks.OnGesture(gesture);
        if (_frame.SelectedRole == DsPageRole.Inventory) return _inventory.OnGesture(gesture);
        if (_frame.SelectedRole == DsPageRole.Loadout) return _loadout.OnGesture(gesture);
        var page = _journal.Owned as Page;
        if (!_journal.Ready || page == null) return false;
        var host = (RectTransform)page.Token.Host;
        Vector2 point;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, gesture.Position, DsPresentation.UiCamera, out point) || !host.rect.Contains(point)) return false;
        try
        {
            if (!IsCurrent(page.Token)) { _journal.Clear(); return true; }
            if (gesture.Type == DsGestureType.Drag)
            {
                page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y,
                    page.Offset + DsJournalGeometry.LocalDrag(gesture.Delta.y, page.Scale));
                ClearSelection(page);
                Present(page.Token, page);
                return true;
            }
            if (gesture.Type != DsGestureType.Tap) return false;
            foreach (var entry in page.Entries)
            {
                if (!page.Visible.Contains(entry)) continue;
                var collider = entry.GetComponent<Collider2D>();
                var bounds = InSpace(collider.bounds, null, host);
                if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.y >= bounds.min.y && point.y <= bounds.max.y)
                    return _selection.Select(page, entry, page.Token.Data);
            }
            return false;
        }
        catch (Exception e) { _failed = DsPortPageSnapshot.IsReadFailure(e) ? null : page.Token; _journal.Clear(); Report(e.GetBaseException().Message); return true; }
    }

    bool IDsPortSelection.IsCurrent(object owner, object item, object data)
    {
        var page = owner as Page; var entry = item as JournalEntryItem;
        return page != null && !page.Released && entry != null && IsCurrent(page.Token) &&
            ReferenceEquals(data, page.Token.Data) && page.Visible.Contains(entry) && entry.Record != null && Within(entry.transform, page.Grid.transform);
    }
    void IDsPortSelection.Display(object owner, object item)
    {
        var page = (Page)owner; var entry = (JournalEntryItem)item;
        RequireCurrent(page.Token);
        page.Manager.SetDisplay((InventoryItemSelectable)entry);
        RequireCurrent(page.Token);
        ForceText(page);
        RequireCurrent(page.Token);
        UpdateFit(page);
        ApplyFit(page);
        var world = InSpace(page.View, page.ScrollParent, null);
        page.Cursor.SetClampedPos(world.min, world.max);
        page.Cursor.Activate();
        RequireCurrent(page.Token);
        page.Cursor.SetTarget(entry.transform);
        RequireCurrent(page.Token);
        page.Selected = entry;
        ContainFixed(page);
    }
    bool IDsPortSelection.CanSubmit(object owner, object item) => false;
    bool IDsPortSelection.Submit(object owner, object item) => false;

    public void ClearSelection(object clone)
    {
        var page = (Page)clone;
        _selection.Clear(); page.Selected = null;
        if (page.Cursor != null)
        {
            page.Cursor.StopAllCoroutines();
            page.Cursor.SetTarget(null);
            page.Cursor.Deactivate();
        }
        if (page.Manager != null) page.Manager.SetDisplay((GameObject)null);
    }
    public void DestroyOwned(object clone)
    {
        var page = (Page)clone;
        if (page.Destroyed) return;
        page.Released = true;
        DsJournalAdmission.ReleaseOwned(
            () => { if (page.Staging != null) foreach (var driver in page.Staging.GetComponentsInChildren<MonoBehaviour>(true))
                if (driver != null && !page.Stopped.Contains(driver)) { driver.StopAllCoroutines(); page.Stopped.Add(driver); } },
            () => { if (page.Staging != null && page.Staging.activeSelf) page.Staging.SetActive(false); },
            () => { if (page.Staging != null) Object.DestroyImmediate(page.Staging); page.Staging = null; });
        page.Text.Clear();
        page.Destroyed = true;
        // Native fade OnDisable unsubscribes its global hook; OnDestroy alone does not.
    }
    public void Dispose()
    {
        if (_disposed) return;
        Invalidate();
        _frame.BeforeCompositionDestroyed -= Invalidate;
        _frame.SelectionChanged -= SelectionChanged;
        _disposed = true;
    }
}

// One finite ownership path for born-inactive native page/counter/Map text.
// Native font initialization may mutate FaceInfo, glyphs and kerning dictionaries.
// Only atlas textures and already-resident global read-only formatting state are borrowed.
public sealed class DsPortOwnedText
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    readonly DsPortOwnedGraph _graph = new DsPortOwnedGraph();
    readonly HashSet<TMProOld.TMP_FontAsset> _fonts = new HashSet<TMProOld.TMP_FontAsset>();
    readonly HashSet<Material> _materials = new HashSet<Material>();
    readonly HashSet<string> _inputs = new HashSet<string>();
    readonly HashSet<PaneText> _preparedTexts = new HashSet<PaneText>();
    readonly HashSet<List<TMProOld.TMP_FontAsset>> _augmentedFallbacks = new HashSet<List<TMProOld.TMP_FontAsset>>();
    readonly List<Mesh> _coldMeshes = new List<Mesh>();
    readonly List<GameObject> _coldRoots = new List<GameObject>();
    object _settings, _styles, _rules;
    int _inputSize, _dataWork;
    bool _failed;
    static FieldInfo Field(object owner, string name)
    {
        for (var type = owner.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, Flags); if (field != null) return field; }
        throw new MissingFieldException(owner.GetType().FullName, name);
    }
    static object Get(object owner, string name) => Field(owner, name).GetValue(owner);
    static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
    static object Static(Type type, string name) => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
    static Exception Missing(string reason) => new DsPortPageReadException(new InvalidOperationException("Owned native text: " + reason + "; retry on next page poll or detail selection"));
    void Resident()
    {
        var settings = Static(typeof(TMProOld.TMP_Settings), "s_Instance");
        var styles = Static(typeof(TMProOld.TMP_StyleSheet), "s_Instance");
        var rules = settings != null ? Get(settings, "m_linebreakingRules") : null;
        if (settings == null || styles == null || rules == null || Get(rules, "leadingCharacters") == null || Get(rules, "followingCharacters") == null ||
            Static(typeof(TMProOld.TMP_UpdateManager), "s_Instance") == null || !(bool)Static(typeof(TMProOld.ShaderUtilities), "isInitialized"))
            throw Missing("resident settings/style/linebreak/update/shader backing state unavailable");
        if (_settings != null && (!ReferenceEquals(settings, _settings) || !ReferenceEquals(styles, _styles) || !ReferenceEquals(rules, _rules)))
            throw Missing("resident native formatting owner replaced");
        _settings = settings; _styles = styles; _rules = rules;
        var table = Get(styles, "m_StyleDictionary") as IDictionary;
        if (table == null || table.Count > 4096) throw Missing("resident style dictionary unavailable or exceeds finite bound");
    }
    void Input(string value, PaneText text = null, bool decoded = false)
    {
        if (value == null) return;
        try
        {
            DsPortTextPreflight.Check(value, hash =>
            {
                var table = (IDictionary)Get(_styles, "m_StyleDictionary");
                var entry = table[hash]; if (entry == null) return null;
                var definitions = new List<string>();
                foreach (string field in new[] { "m_OpeningTagArray", "m_ClosingTagArray" })
                {
                    var codes = Get(entry, field) as int[];
                    if (codes == null || codes.Length > 65536) throw Missing("resident style definition unavailable");
                    var definition = new System.Text.StringBuilder();
                    foreach (int code in codes) if (code != 0)
                        definition.Append(code <= 65535 ? ((char)code).ToString() : char.ConvertFromUtf32(code));
                    definitions.Add(definition.ToString());
                }
                return definitions.ToArray();
            }, text != null ? (bool?)Get(text, "m_parseCtrlCharacters") : null,
                text == null || (bool)Get(text, "m_isRichText"), !decoded);
            if (_inputs.Add(value))
            {
                if (value.Length > 65536 - _inputSize) throw Missing("text input exceeds finite work bound");
                _inputSize += value.Length;
            }
        }
        catch (DsPortPageReadException) { throw; }
        catch (Exception error) { throw Missing(error.Message); }
    }
    Material Material(Material source)
    {
        if (source == null) throw Missing("native material missing");
        if (_materials.Contains(source)) return source;
        return (Material)_graph.Copy(source, () =>
        { var copy = new Material(source); _materials.Add(copy); return copy; }, _ => { }, value => Object.DestroyImmediate((Material)value));
    }
    object Data(object source)
    {
        if (++_dataWork > 8388608) throw Missing("native text graph exceeds aggregate data bound");
        if (source == null) return null;
        var type = source.GetType();
        if (source is string || type.IsPrimitive || type.IsEnum) return source;
        if (source is TMProOld.TMP_FontAsset font) return Font(font);
        if (source is Material material) return Material(material);
        if (source is TMProOld.TMP_ColorGradient gradient)
            return _graph.Copy(source, () => ScriptableObject.CreateInstance<TMProOld.TMP_ColorGradient>(), value =>
            {
                foreach (var field in typeof(TMProOld.TMP_ColorGradient).GetFields(Flags))
                {
                    if (field.IsStatic) continue;
                    if (field.FieldType != typeof(Color)) throw Missing("unadmitted native gradient field " + field.Name);
                    field.SetValue(value, field.GetValue(gradient));
                }
            }, value => Object.DestroyImmediate((Object)value));
        if (source is Array array)
        {
            if (array.Rank != 1 || array.Length > 65536) throw Missing("font array exceeds finite bound");
            return _graph.Copy(source, () => Array.CreateInstance(type.GetElementType(), array.Length), value =>
            { var copy = (Array)value; for (int i = 0; i < array.Length; i++) copy.SetValue(Data(array.GetValue(i)), i); }, null);
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)source;
            if (list.Count > 65536) throw Missing("font list exceeds finite bound");
            return _graph.Copy(source, () => Activator.CreateInstance(type), value =>
            { foreach (var item in list) ((IList)value).Add(Data(item)); }, null);
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(int))
        {
            var dictionary = (IDictionary)source;
            if (dictionary.Count > 65536) throw Missing("font dictionary exceeds finite bound");
            return _graph.Copy(source, () => Activator.CreateInstance(type), value =>
            { foreach (DictionaryEntry entry in dictionary) ((IDictionary)value).Add(entry.Key, Data(entry.Value)); }, null);
        }
        if (type != typeof(TMProOld.FaceInfo) && type != typeof(TMProOld.TMP_Glyph) && type != typeof(TMProOld.KerningPair) &&
            type != typeof(TMProOld.KerningTable) && type != typeof(TMProOld.TMP_FontWeights))
            throw Missing("unadmitted font data " + type.FullName);
        return _graph.Copy(source, () => typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null), value =>
        {
            for (var owner = type; owner != null && owner != typeof(object) && owner != typeof(ValueType); owner = owner.BaseType)
                foreach (var field in owner.GetFields(Flags)) if (!field.IsStatic) field.SetValue(value, Data(field.GetValue(source)));
        }, null);
    }
    TMProOld.TMP_FontAsset Font(TMProOld.TMP_FontAsset source)
    {
        if (source == null) throw Missing("native font missing");
        if (_fonts.Contains(source)) return source;
        return (TMProOld.TMP_FontAsset)_graph.Copy(source, () =>
        { var font = ScriptableObject.CreateInstance<TMProOld.TMP_FontAsset>(); _fonts.Add(font); return font; }, value =>
        {
            var font = (TMProOld.TMP_FontAsset)value; font.name = source.name;
            font.atlas = source.atlas; // Borrowed immutable atlas; never initialized or written.
            font.material = Material(source.material);
            foreach (string field in new[] { "m_fontInfo", "m_glyphInfoList", "m_characterDictionary", "m_kerningDictionary", "m_kerningInfo", "m_kerningPair", "m_characterSet", "fontWeights", "fallbackFontAssets" })
                Set(font, field, Data(Get(source, field)));
            foreach (string field in new[] { "fontAssetType", "normalStyle", "normalSpacingOffset", "boldStyle", "boldSpacing", "italicStyle", "tabSize", "hashCode", "materialHashCode" })
                Set(font, field, Get(source, field));
            if (font.atlas == null || Get(font, "m_fontInfo") == null || Get(font, "m_glyphInfoList") == null || Get(font, "m_kerningInfo") == null)
                throw Missing("native font definition incomplete");
        }, value => Object.DestroyImmediate((TMProOld.TMP_FontAsset)value));
    }
    void InitializeFonts()
    {
        var global = Get(_settings, "m_fallbackFontAssets") as List<TMProOld.TMP_FontAsset>;
        if (global != null && global.Count > 4096) throw Missing("global fallback list exceeds finite bound");
        if (global != null) foreach (var fallback in global) if (fallback != null) Font(fallback);
        var fonts = new List<TMProOld.TMP_FontAsset>(_fonts);
        foreach (var font in fonts)
        {
            // Shared fallback lists keep their aliases and are extended exactly once.
            if (font.fallbackFontAssets == null) font.fallbackFontAssets = new List<TMProOld.TMP_FontAsset>();
            if (_augmentedFallbacks.Add(font.fallbackFontAssets))
            {
                // Exact native search order is direct base, direct local fallbacks,
                // then direct settings fallbacks; it is not recursive font search.
                if (global != null) foreach (var fallback in global)
                    font.fallbackFontAssets.Add(fallback != null ? Font(fallback) : null);
            }
            // Never touch source.characterDictionary: its getter initializes source data.
            if (Get(font, "m_characterDictionary") == null) font.ReadFontDefinition();
        }
    }
    static object Native(PaneText text, string method, params object[] args) =>
        typeof(TMProOld.TMP_Text).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(text, args);
    void ValidateText(PaneText text)
    {
        var font = Get(text, "m_fontAsset") as TMProOld.TMP_FontAsset;
        if (!_fonts.Contains(font)) throw Missing("native text font identity escaped owned graph");
        // Use the admitted native decoder and tag/weight methods, not a second
        // formatting implementation. These operate only on this owned component;
        // resource tags are rejected before ValidateHtmlTag can resolve anything.
        var chars = Get(text, "m_char_buffer") as int[] ?? new int[1];
        string inputSource = Get(text, "m_inputSource").ToString();
        if (inputSource == "Text" || inputSource == "String")
        {
            DsPortTextPreflight.Check(Get(text, "m_text") as string, null,
                (bool)Get(text, "m_parseCtrlCharacters"), false, inputSource == "Text");
            object[] args = { Get(text, "m_text"), chars };
            Native(text, "StringToCharArray", args); chars = (int[])args[1];
        }
        else if (inputSource == "SetText")
        {
            int length = (int)Get(text, "m_charArray_Length");
            if (length < 0 || length > 65536) throw Missing("native numeric text exceeds finite bound");
            object[] args = { Get(text, "m_input_CharArray"), chars };
            Native(text, "SetTextArrayToCharArray", args); chars = (int[])args[1];
        }
        if (chars.Length > 131072) throw Missing("native decoded text exceeds finite bound");
        var decoded = new System.Text.StringBuilder();
        foreach (int code in chars)
        {
            if (code == 0) break;
            if (code < 0 || code > 0x10ffff || (code >= 0xd800 && code <= 0xdfff)) throw Missing("native decoded Unicode invalid");
            decoded.Append(char.ConvertFromUtf32(code));
        }
        Input(decoded.ToString(), text, true);
        var style = (TMProOld.FontStyles)Get(text, "m_fontStyle");
        Set(text, "m_style", style); Set(text, "tag_NoParsing", false); Set(text, "m_isParsingText", false);
        Set(text, "m_currentFontAsset", font); Set(text, "m_currentMaterial", Get(text, "m_sharedMaterial"));
        Set(text, "m_currentMaterialIndex", 0);
        int weight = (style & TMProOld.FontStyles.Bold) != 0 ? 700 : (int)Get(text, "m_fontWeight");
        Set(text, "m_fontWeightInternal", weight);
        var stack = Get(text, "m_fontWeightStack");
        stack.GetType().GetMethod("SetDefault").Invoke(stack, new object[] { weight }); Set(text, "m_fontWeightStack", stack);
        if (Get(text, "m_textInfo") == null) Set(text, "m_textInfo", new TMProOld.TMP_TextInfo());
        bool rich = (bool)Get(text, "m_isRichText");
        int work = 0;
        for (int i = 0; i < chars.Length && chars[i] != 0; i++)
        {
            int character = chars[i];
            if (rich && character == '<')
            {
                object[] args = { chars, i + 1, 0 };
                if ((bool)Native(text, "ValidateHtmlTag", args)) { i = (int)args[2]; continue; }
            }
            var activeStyle = (TMProOld.FontStyles)Get(text, "m_style");
            if ((activeStyle & TMProOld.FontStyles.UpperCase) != 0)
            { if (char.IsLower((char)character)) character = char.ToUpper((char)character); }
            else if ((activeStyle & TMProOld.FontStyles.LowerCase) != 0)
            { if (char.IsUpper((char)character)) character = char.ToLower((char)character); }
            else if (((activeStyle | style) & TMProOld.FontStyles.SmallCaps) != 0 && char.IsLower((char)character)) character = char.ToUpper((char)character);
            var chosen = Native(text, "GetFontAssetForWeight", Get(text, "m_fontWeightInternal")) as TMProOld.TMP_FontAsset ?? font;
            try
            {
                DsPortTextPreflight.Resolve(chosen, character, value => _fonts.Contains((TMProOld.TMP_FontAsset)value),
                    (value, code) =>
                    {
                        if (++work > 1048576) throw Missing("native glyph checks exceed finite bound");
                        var dictionary = Get(value, "m_characterDictionary") as IDictionary;
                        return dictionary != null && dictionary.Contains(code) && dictionary[code] != null;
                    }, value => ((TMProOld.TMP_FontAsset)value).fallbackFontAssets);
            }
            catch (Exception error) { throw Missing(error.Message + " for native text " + text.name + " font " + chosen.name); }
        }
        // Native generation resets its parser state; retain the unmodified input.
        Set(text, "m_isInputParsingRequired", true);
    }
    public void Prepare(GameObject root, DsPortPageSnapshot content = null)
    {
        if (_failed) throw Missing("partial owned text graph awaits page/detail retirement");
        try { PrepareCore(root, content); }
        catch { _failed = true; throw; }
    }
    void PrepareCore(GameObject root, DsPortPageSnapshot content)
    {
        if (root.GetComponentsInChildren<PaneText>(true).Length == 0) return;
        if (root.activeInHierarchy) throw Missing("text must be owned before native Awake");
        Resident();
        // Prospective native values are resource-checked before any setup/Awake.
        // Glyph coverage remains per actual destination text and chosen typeface.
        if (content != null) foreach (var input in content.TextInputs) Input(input);
        foreach (var text in root.GetComponentsInChildren<PaneText>(true))
        {
            if (!_preparedTexts.Add(text)) continue;
            var source = Get(text, "m_fontAsset") as TMProOld.TMP_FontAsset ?? Get(_settings, "m_defaultFontAsset") as TMProOld.TMP_FontAsset;
            var ownedFont = Font(source);
            Set(text, "m_fontAsset", ownedFont);
            // Serialized TMP_TextInfo can retain borrowed glyph/material/mesh
            // references. Native Awake must build the owned instance afresh.
            Set(text, "m_textInfo", null);
            foreach (string field in new[] { "m_fontMaterial", "m_fontMaterials", "m_fontSharedMaterials", "m_fontColorGradientPreset" })
                Set(text, field, Data(Get(text, field)));
            var renderer = text.GetComponent<Renderer>();
            if (renderer == null) throw Missing("owned text renderer missing");
            var values = renderer.sharedMaterials;
            for (int i = 0; i < values.Length; i++) values[i] = Material(values[i]);
            renderer.sharedMaterials = values;
            var shared = Get(text, "m_sharedMaterial") as Material;
            Set(text, "m_sharedMaterial", shared != null ? Material(shared) : Font(source).material);
        }
        foreach (var submesh in root.GetComponentsInChildren<TMProOld.TMP_SubMesh>(true))
        {
            var font = Get(submesh, "m_fontAsset") as TMProOld.TMP_FontAsset;
            if (font != null) Set(submesh, "m_fontAsset", Font(font));
            foreach (string field in new[] { "m_material", "m_sharedMaterial" })
            {
                var material = Get(submesh, field) as Material;
                if (material != null) Set(submesh, field, Material(material));
            }
            var renderer = submesh.GetComponent<Renderer>();
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = Material(materials[i]);
            renderer.sharedMaterials = materials;
            // Serialized component references must point into the owned clone.
            Set(submesh, "m_renderer", renderer);
            Set(submesh, "m_meshFilter", submesh.GetComponent<MeshFilter>());
            var parent = submesh.GetComponentInParent<PaneText>(true);
            if (parent == null || !_fonts.Contains(Get(parent, "m_fontAsset") as TMProOld.TMP_FontAsset)) throw Missing("owned subtext parent missing");
            Set(submesh, "m_TextComponent", parent);
        }
        InitializeFonts();
        Validate(root);
    }
    public void Validate(GameObject root, GameObject excluded = null)
    {
        if (_failed) throw Missing("partial owned text graph awaits page/detail retirement");
        if (root.GetComponentsInChildren<PaneText>(true).Length == 0) return;
        Resident();
        foreach (var text in root.GetComponentsInChildren<PaneText>(true))
        {
            if (excluded != null && (text.gameObject == excluded || text.transform.IsChildOf(excluded.transform))) continue;
            try { ValidateText(text); }
            catch (DsPortPageReadException) { throw; }
            catch (Exception error) { throw Missing(error.GetBaseException().Message); }
        }
    }
    public Func<bool> WatchCold(PaneText source)
    {
        var reads = new List<Func<bool>>();
        void WatchField(object owner, FieldInfo field)
        {
            var value = field.GetValue(owner);
            if (value is Array array)
            {
                if (array.Rank != 1 || array.Length > 65536) throw Missing("cold text watch exceeds finite bound");
                var retained = (Array)array.Clone();
                reads.Add(() =>
                {
                    if (!ReferenceEquals(field.GetValue(owner), array) || array.Length != retained.Length) return false;
                    for (int i = 0; i < array.Length; i++) if (!Equals(array.GetValue(i), retained.GetValue(i))) return false;
                    return true;
                });
            }
            else reads.Add(() => Equals(field.GetValue(owner), value));
        }
        for (var type = typeof(PaneText); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            foreach (var field in type.GetFields(Flags))
                if (!field.IsStatic && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), true))) WatchField(source, field);
        foreach (string field in new[] { "m_mesh", "m_char_buffer", "m_input_CharArray", "m_charArray_Length" }) WatchField(source, Field(source, field));
        var rect = source.transform as RectTransform;
        var matrix = source.transform.localToWorldMatrix;
        var size = rect != null ? rect.rect : default(Rect);
        var renderer = source.GetComponent<Renderer>(); var materials = renderer.sharedMaterials;
        int layer = renderer.sortingLayerID, order = renderer.sortingOrder;
        return () =>
        {
            if (source == null || renderer == null || source.transform.localToWorldMatrix != matrix ||
                (rect != null && rect.rect != size) || renderer.sortingLayerID != layer || renderer.sortingOrder != order) return false;
            var current = renderer.sharedMaterials;
            if (current.Length != materials.Length) return false;
            for (int i = 0; i < current.Length; i++) if (!ReferenceEquals(current[i], materials[i])) return false;
            foreach (var read in reads) if (!read()) return false;
            return true;
        };
    }
    // Only a genuinely ungenerated Map label takes this path. Generation lives
    // on a standalone root that stays inactive for its entire Unity lifetime.
    public PaneText CopyCold(PaneText source)
    {
        if (source == null) throw Missing("cold text staging source invalid");
        var rect = source.transform as RectTransform;
        if (rect == null) throw Missing("cold native text RectTransform missing");
        var staging = new GameObject("Owned native cold staging text", typeof(RectTransform));
        staging.SetActive(false);
        _coldRoots.Add(staging);
        var ownedRect = (RectTransform)staging.transform;
        ownedRect.anchorMin = rect.anchorMin; ownedRect.anchorMax = rect.anchorMax;
        ownedRect.pivot = rect.pivot; ownedRect.sizeDelta = rect.sizeDelta;
        ownedRect.localPosition = Vector3.zero; ownedRect.localRotation = Quaternion.identity; ownedRect.localScale = Vector3.one;
        staging.layer = source.gameObject.layer;
        var owned = staging.AddComponent<PaneText>();
        for (var type = typeof(PaneText); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            foreach (var field in type.GetFields(Flags))
            {
                if (field.IsStatic || field.IsNotSerialized || (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true)) ||
                    field.Name == "m_textInfo" || field.Name == "m_renderer" || field.Name == "m_subTextObjects") continue;
                var value = field.GetValue(source);
                if (value is Component || value is GameObject) throw Missing("cold native text escaped serialized " + field.Name);
                field.SetValue(owned, value == null || value is string || value.GetType().IsValueType ? value : Data(value));
            }
        // These serialized component graphs are reconstructed, never borrowed or
        // discarded: exact local renderer now, native required subtexts at layout.
        var renderer = staging.GetComponent<MeshRenderer>() ?? staging.AddComponent<MeshRenderer>();
        Set(owned, "m_renderer", renderer);
        Set(owned, "m_subTextObjects", new TMProOld.TMP_SubMesh[16]);
        foreach (string field in new[] { "m_char_buffer", "m_input_CharArray", "m_charArray_Length" })
            Set(owned, field, Data(Get(source, field)));
        var container = source.GetComponent<TMProOld.TextContainer>();
        if (container != null)
        {
            var copy = staging.GetComponent<TMProOld.TextContainer>() ?? staging.AddComponent<TMProOld.TextContainer>();
            foreach (var field in typeof(TMProOld.TextContainer).GetFields(Flags))
                if (!field.IsStatic && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), true)))
                {
                    var value = field.GetValue(container);
                    if (value != null && !value.GetType().IsValueType && !(value is string)) throw Missing("cold text container escaped " + field.Name);
                    field.SetValue(copy, value);
                }
        }
        renderer.sharedMaterials = source.GetComponent<Renderer>().sharedMaterials;
        renderer.sortingLayerID = source.GetComponent<Renderer>().sortingLayerID;
        renderer.sortingOrder = source.GetComponent<Renderer>().sortingOrder;
        return owned;
    }
    public Renderer[] GenerateCold(PaneText text)
    {
        Validate(text.gameObject);
        if (text.gameObject.activeInHierarchy) throw Missing("cold native text staging became active");
        try
        {
            // Explicit owned Awake while permanently inactive. The staging root is
            // never parented beneath a published hierarchy, so Unity cannot run a
            // second activation lifecycle on these behavior components.
            var preparedInput = Get(text, "m_char_buffer");
            typeof(PaneText).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(text, null);
            if (Get(text, "m_inputSource").ToString() == "SetCharArray" && preparedInput != null)
                Set(text, "m_char_buffer", preparedInput);
            text.ForceMeshUpdate(true);
            Validate(text.gameObject);
            var mesh = Get(text, "m_mesh") as Mesh;
            if (mesh == null || mesh.vertexCount == 0) throw Missing("NEEDS_CONTEXT ungenerated native text: " + text.name);
            return text.GetComponentsInChildren<Renderer>(true);
        }
        finally
        {
            // Never-active Unity objects may not receive OnDestroy. Retain the
            // native-created meshes even if Awake/layout throws partway through.
            var mesh = Get(text, "m_mesh") as Mesh;
            if (mesh != null && !_coldMeshes.Contains(mesh)) _coldMeshes.Add(mesh);
            foreach (var submesh in text.GetComponentsInChildren<TMProOld.TMP_SubMesh>(true))
            {
                mesh = Get(submesh, "m_mesh") as Mesh;
                if (mesh != null && !_coldMeshes.Contains(mesh)) _coldMeshes.Add(mesh);
            }
        }
    }
    public void RetireCold()
    {
        // Unity owns lifecycle dispatch. Remove each permanently inactive staging
        // root only after exact destruction succeeds so restoration can retry.
        while (_coldRoots.Count != 0)
        {
            int last = _coldRoots.Count - 1;
            var root = _coldRoots[last];
            if (root != null) Object.DestroyImmediate(root);
            _coldRoots.RemoveAt(last);
        }
    }
    public void Clear()
    {
        RetireCold();
        while (_coldMeshes.Count != 0)
        {
            int last = _coldMeshes.Count - 1;
            if (_coldMeshes[last] != null) Object.DestroyImmediate(_coldMeshes[last]);
            _coldMeshes.RemoveAt(last);
        }
        _graph.Clear(); _fonts.Clear(); _materials.Clear(); _inputs.Clear();
        _preparedTexts.Clear(); _augmentedFallbacks.Clear(); _coldRoots.Clear();
        _inputSize = _dataWork = 0; _failed = false; _settings = _styles = _rules = null;
    }
}
#endif
