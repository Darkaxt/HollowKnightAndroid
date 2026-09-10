// Tasks-specific native lifecycle; shares geometry/identity machinery, not Journal admission.
// Display-only Bottom.Inventory/Bottom.Select adaptation, igawa6/dualsouls
// 5c22451435b772acde0c7e6456f9019bc1baef73 (MIT).
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using TeamCherry.NestedFadeGroup;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using PaneText = TMProOld.TextMeshPro;

public sealed partial class DsPortProgress
{
    sealed class Tasks : IDsPortJournalNative, IDsPortSelection
    {
        readonly DsPortFrame _frame;
        readonly DsPortJournalState _state;
        readonly DsPortSelectState _selection;
        readonly HashSet<string> _reported = new HashSet<string>();
        DsJournalToken _current, _failed;
        bool _eligible, _completed;
        long _revision;
        float _nextContentProbe;

        sealed class TaskPage
        {
            public DsJournalToken Token;
            public GameObject Staging, Root, Template, CursorTemplate;
            public InventoryItemQuestManager Manager;
            public InventoryItemGrid Grid;
            public ScrollView Scroll;
            public InventoryCursor Cursor;
            public InventoryItemQuest[] Entries;
            public Bounds View, Content, Fit;
            public Vector3 Origin;
            public float Offset, Scale;
            public int Activated;
            public bool Released, Destroyed;
            public readonly DsPortOwnedText Text = new DsPortOwnedText();
            public CounterDetail ActiveCounter;
            public Exception CreationError;
            public readonly HashSet<MonoBehaviour> Stopped = new HashSet<MonoBehaviour>();
            public InventoryItemQuest Selected;
            public CounterBinding RegularCounter, MainCounter, SubCounter;
            public readonly DsPortOwnedDetail Counter = new DsPortOwnedDetail();
            public readonly HashSet<InventoryItemQuest> Visible = new HashSet<InventoryItemQuest>();
            public readonly HashSet<PlayerDataTestResponse> Evaluated = new HashSet<PlayerDataTestResponse>();
        }

        sealed class CounterBinding
        {
            public InventoryItemQuest Template;
            public InventoryItemExtraDescription Condition;
            public Transform Destination;
            public string Problem;
        }
        sealed class CounterDetail
        {
            public GameObject Staging, Root;
            public bool Activated;
            public readonly List<Material> Materials = new List<Material>();
            public readonly DsPortOwnedText Text = new DsPortOwnedText();
        }
        public Tasks(DsPortFrame frame)
        { _frame = frame; _state = new DsPortJournalState(this); _selection = new DsPortSelectState(this); }
        bool _outgoing;
        public void SelectionChanged()
        {
            _selection.Clear(); _nextContentProbe = 0;
            var page = _state.Owned as TaskPage;
            _outgoing = _state.Ready && page != null && !page.Released && KeepOutgoing(_frame, DsPageRole.Tasks, page.Token) && _state.RetainOutgoingPresentation();
            if (!_outgoing) Invalidate();
        }
        public void Invalidate()
        { _outgoing = false; _revision++; _nextContentProbe = 0; _current = null; _failed = null; _selection.Clear(); _state.Clear(); }
        public void Tick(bool eligible)
        {
            if (_eligible != eligible) { Invalidate(); _eligible = eligible; }
            try
            {
                if (_outgoing)
                {
                    var outgoing = _state.Owned as TaskPage;
                    if (eligible && outgoing != null && !outgoing.Released && KeepOutgoing(_frame, DsPageRole.Tasks, outgoing.Token)) return;
                    Invalidate();
                }
                if ((_state.Ready || _state.Owned == null) && Time.unscaledTime < _nextContentProbe) return;
                _nextContentProbe = Time.unscaledTime + .125f;
                _current = null;
                _current = Capture();
                if (_current != null && _current.Same(_failed)) return;
                _state.Tick(_current, Time.frameCount);
                if (_state.Problem != null) Report(_state.Problem);
            }
            catch (Exception e) { _failed = DsPortPageSnapshot.IsReadFailure(e) ? null : _current; _state.Clear(); Report(e.GetBaseException().Message); }
        }
        void Report(string problem)
        { if (_reported.Add(problem)) Debug.LogWarning("[DualScreen][capability-gap] Tasks: " + problem); }
        DsJournalToken Capture()
        {
            if (!_eligible || !_frame.HudReady || _frame.SelectedRole != DsPageRole.Tasks || !DsGameData.InGame) return null;
            var pane = _frame.GetResidentPane(DsPageRole.Tasks);
            var host = _frame.GetOrCreatePageHost(DsPageRole.Tasks);
            var gm = GameManager.SilentInstance;
            var data = PlayerData.instance;
            if (pane == null || host == null || !host.gameObject.activeInHierarchy || gm == null || data == null ||
                !ReferenceEquals(data, gm.playerData) || gm.isPaused || gm.IsInSceneTransition || data.isInventoryOpen) return null;
            var manager = pane.GetComponentInChildren<InventoryItemQuestManager>(true);
            var quests = typeof(QuestManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as QuestManager;
            if (manager == null || quests == null) return null;
            return new DsJournalToken(pane, manager, data, quests, host, _revision + _frame.SelectionEpoch,
                new DsPortPageSnapshot(ReadContent(pane, manager, quests)));
        }
        static IEnumerable<object> ReadContent(InventoryPane pane, InventoryItemQuestManager manager, QuestManager quests)
        {
            yield return QuestManager.Version;
            var master = Get(quests, "masterList") as IEnumerable<BasicQuestBase>;
            if (master == null) throw new InvalidOperationException("Tasks content master list missing");
            yield return master;
            // Read the native master rather than mutating its version-keyed accepted cache.
            foreach (var quest in master)
            {
                yield return quest;
                if (quest == null) throw new InvalidOperationException("Tasks content quest missing");
                yield return quest.IsAccepted;
                foreach (var value in ReadQuestContent(quest)) yield return value;
                if (quest is MainQuest main)
                {
                    yield return "subquests";
                    foreach (var sub in main.SubQuests)
                    {
                        yield return sub;
                        if (sub == null) throw new InvalidOperationException("Tasks content subquest missing");
                        var current = sub.GetCurrent();
                        foreach (var value in ReadQuestContent(current)) yield return value;
                        yield return current.IsLinkedBoolComplete;
                    }
                    yield return "end-subquests";
                }
            }
            foreach (var value in ReadPaneConditions(pane, manager)) yield return value;
        }
        public static IEnumerable<object> ReadQuestContent(BasicQuestBase quest)
        {
            yield return quest;
            if (quest == null) throw new InvalidOperationException("Tasks current quest missing");
            yield return quest.name; yield return quest.IsAccepted; yield return quest.IsHidden; yield return quest.HasBeenSeen;
            yield return quest.QuestType; yield return (string)quest.DisplayName; yield return quest.Location;
            yield return quest.GetDescription(BasicQuestBase.ReadSource.Inventory);
            if (quest is FullQuestBase full)
            {
                yield return full.IsCompleted; yield return full.DescCounterType; yield return full.ListCounterType;
                yield return full.CustomDescPrefab; yield return full.HideMax; yield return full.ProgressBarTint;
                yield return full.RewardItem; yield return full.RewardCount; yield return full.RewardIcon; yield return full.RewardIconType;
                yield return full.CounterIconScale; yield return full.CounterIconPadding;
                yield return "targets";
                foreach (var target in full.Targets)
                {
                    yield return target.Counter; yield return target.Count; yield return target.HideInCount;
                    yield return target.AltSprite; yield return (string)target.ItemName;
                }
                yield return "counters";
                foreach (var count in full.Counters) yield return count;
                yield return "end-counters";
            }
        }
        public bool IsCurrent(DsJournalToken token) => token != null && token.Same(Capture());
        void Current(DsJournalToken token)
        { if (!IsCurrent(token)) throw new InvalidOperationException("Tasks owner/selection replaced"); }

        // Each additional type below has a separately inspected Tasks display lifecycle.
        // Animators are retained but disabled before activation: no animation events or
        // state-machine behaviours are admitted merely by owning an Animator component.
        static bool Allowed(Type type)
        {
            return ComponentAllowed(type, "visual") || type == typeof(InventoryPane) ||
                type == typeof(InventoryPaneInput) || type == typeof(InventoryItemQuestManager) ||
                type == typeof(InventoryItemQuest) || type == typeof(InventoryItemMainQuest) ||
                type == typeof(InventoryItemGrid) || type == typeof(InventoryAutoNavGroup) ||
                type == typeof(ScrollView) || type == typeof(QuestItemDescription) ||
                type == typeof(QuestItemDescriptionText) || type == typeof(IconCounter) ||
                type == typeof(IconCounterItem) || type == typeof(ImageSlider) ||
                type == typeof(ProgressBarSegmented) || type == typeof(GridLayoutGroup) ||
                type == typeof(InventoryItemExtraDescription) || type == typeof(Animator) ||
                type == typeof(InventoryCursor) || type == typeof(Canvas) || type == typeof(CanvasRenderer) ||
                type == typeof(Image) || type == typeof(TextMeshProContainerFitter) || type == typeof(TMProOld.TextContainer);
        }
        static void InspectTaskScope(GameObject root, GameObject template, GameObject cursor)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Tasks missing script");
                if (!Allowed(component.GetType())) throw new InvalidOperationException("Tasks unadmitted component " + component.GetType().FullName);
                // Its Awake registers a global prefab-keyed description cache through
                // OnUpdateDisplay. Remove ONLY this proven optional cloned driver.
                if (component.GetType() == typeof(InventoryItemExtraDescription)) continue;
                ValidateSerializedReferences(component, root.transform, template, cursor);
                if (component is PlayerDataTestResponse response)
                { ReadEvent(response.IsFullfilled, root.transform); ReadEvent(response.IsNotFulfilled, root.transform); }
                if (component is NestedFadeGroupBase fade)
                {
                    if (fade.ParentOverride != null && fade.ParentOverride.Value != null && !Within(fade.ParentOverride.Value.transform, root.transform))
                        throw new InvalidOperationException("Tasks escaped fade parent override");
                    if (component.GetType() == typeof(NestedFadeGroupTextMeshPro) &&
                        (component.GetComponent<PaneText>() == null || component.GetComponent<MeshRenderer>() == null))
                        throw new InvalidOperationException("Tasks TMP fade bridge missing local renderer");
                    if (component.GetType() == typeof(NestedFadeGroupSpriteRenderer) && Get(component, "displayType").ToString() == "Frames")
                    {
                        var frames = Get(component, "frames") as Sprite[];
                        if (frames == null || frames.Length == 0 || Array.Exists(frames, s => s == null))
                            throw new InvalidOperationException("Tasks sprite fade frame array invalid");
                    }
                }
                if (component is IconCounterItem) NeedLocal(component, component.transform, "spriteRenderer");
                if (component is InventoryItemMainQuest)
                    NeedLocal(component, component.transform, "subQuestItemTemplate", "subQuestsParent", "subQuestsLayout", "layoutGroup");
                if (component is InventoryItemGrid grid)
                {
                    if (grid.RowSplit <= 0) throw new InvalidOperationException("Tasks grid RowSplit must be positive");
                    ValidateNavigation(Get(grid, "selectables"), root.transform);
                    ValidateNavigation(Get(grid, "nextPages"), root.transform);
                }
            }
            ValidateBridgeCatalogue(root);
        }
        public string Inspect(DsJournalToken token)
        {
            var pane = (InventoryPane)token.Pane;
            var manager = (InventoryItemQuestManager)token.List;
            var template = Get(manager, "templateItem") as InventoryItemQuest;
            var cursor = Get(manager, "cursorPrefab") as InventoryCursor;
            if (template == null || cursor == null) return "Tasks Entry/Cursor donor missing";
            InspectTaskScope(pane.gameObject, template.gameObject, cursor.gameObject);
            InspectTaskScope(template.gameObject, template.gameObject, cursor.gameObject);
            InspectTaskScope(cursor.gameObject, template.gameObject, cursor.gameObject);
            NeedLocal(manager, pane.transform, "mainQuestTemplateItem", "itemList", "itemListLayout", "itemListScrollView",
                "nameText", "descriptionText", "descriptionLayout", "descriptionGroup", "questItemDescription");
            return null;
        }
        public object CloneInactive(DsJournalToken token)
        {
            var page = new TaskPage { Token = token };
            try
            {
                page.Staging = new GameObject("DsPortTasksStaging");
                page.Staging.SetActive(false);
                page.Staging.transform.SetParent((Transform)token.Host, false);
                page.Root = Object.Instantiate(((InventoryPane)token.Pane).gameObject, page.Staging.transform, false);
                var manager = (InventoryItemQuestManager)token.List;
                page.Template = Object.Instantiate(((InventoryItemQuest)Get(manager, "templateItem")).gameObject, page.Staging.transform, false);
                page.CursorTemplate = Object.Instantiate(((InventoryCursor)Get(manager, "cursorPrefab")).gameObject, page.Staging.transform, false);
                page.Template.SetActive(false); page.CursorTemplate.SetActive(false);
                page.Manager = page.Root.GetComponentInChildren<InventoryItemQuestManager>(true);
                if (page.Manager == null) throw new InvalidOperationException("Tasks cloned manager missing");
                return page;
            }
            catch (Exception error) { page.CreationError = error; return page; }
        }
        public void BindAndVerify(DsJournalToken token, object clone)
        {
            var page = (TaskPage)clone;
            if (page.CreationError != null) throw new InvalidOperationException("Tasks native page creation failed", page.CreationError);
            Set(page.Manager, "templateItem", page.Template.GetComponent<InventoryItemQuest>());
            Set(page.Manager, "cursorPrefab", page.CursorTemplate.GetComponent<InventoryCursor>());
            Set(page.Manager, "isPaneVisible", false);
            Set(page.Manager, "isCompletedQuestsVisible", _completed);
            // These are the exact three native factory inputs, not quest-name
            // guesses. Retain their read-only condition/destination before removing
            // cloned cache writers. Generated main/subquest entries bind separately.
            var regular = (InventoryItemQuest)Get(token.List, "templateItem");
            var main = (InventoryItemMainQuest)Get(token.List, "mainQuestTemplateItem");
            page.RegularCounter = BindCounter(page, regular);
            page.MainCounter = BindCounter(page, main);
            page.SubCounter = BindCounter(page, main != null ? Get(main, "subQuestItemTemplate") as InventoryItemQuest : null);
            // Disable only this manager's primary-input Update/Start. Unity still calls
            // its source-read Awake, including native list/main/subquest construction.
            page.Manager.enabled = false;
            foreach (var root in new[] { page.Root, page.Template, page.CursorTemplate })
            {
                page.Text.Prepare(root, token.Content);
                foreach (var input in root.GetComponentsInChildren<InventoryPaneInput>(true)) Object.DestroyImmediate(input);
                foreach (var extra in root.GetComponentsInChildren<InventoryItemExtraDescription>(true)) Object.DestroyImmediate(extra);
                foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var response in root.GetComponentsInChildren<PlayerDataTestResponse>(true))
                {
                    var mode = Field(response, "runOn");
                    mode.SetValue(response, Enum.Parse(mode.FieldType, "JustStart"));
                    response.enabled = false;
                }
                foreach (var cursor in root.GetComponentsInChildren<InventoryCursor>(true))
                { var sound = (AudioEvent)Get(cursor, "changeSelectionSound"); sound.Volume = 0; Set(cursor, "changeSelectionSound", sound); }
                foreach (var clip in root.GetComponentsInChildren<TextMeshProClipRect>(true)) clip.enabled = false;
                Hide(root, true); Route(root);
                InspectTaskScope(root, page.Template, page.CursorTemplate);
            }
            NeedLocal(page.Manager, page.Root.transform, "mainQuestTemplateItem", "itemList", "itemListLayout", "itemListScrollView",
                "nameText", "descriptionText", "descriptionLayout", "descriptionGroup", "questItemDescription");
            page.Grid = (InventoryItemGrid)Get(page.Manager, "itemList");
            page.Scroll = (ScrollView)Get(page.Manager, "itemListScrollView");
            Current(token);
        }
        static void AssertInputBlocked(TaskPage page)
        {
            if (page.Manager.enabled || (bool)Get(page.Manager, "isPaneVisible") ||
                page.Root.GetComponentsInChildren<InventoryPaneInput>(true).Length != 0 || page.Manager.CurrentSelected != null)
                throw new InvalidOperationException("Tasks primary input/selection boundary changed");
        }
        public void ActivateForLayout(DsJournalToken token, object clone)
        {
            var page = (TaskPage)clone;
            AssertInputBlocked(page);
            page.Staging.transform.rotation = Quaternion.identity;
            var scale = page.Staging.transform.parent.lossyScale;
            if (Mathf.Abs(scale.x) < .0001f || Mathf.Abs(scale.y) < .0001f || Mathf.Abs(scale.z) < .0001f)
                throw new InvalidOperationException("Tasks host has degenerate scale");
            page.Staging.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            page.Root.transform.localRotation = Quaternion.identity;
            page.Root.transform.localScale = Vector3.one;
            page.Root.transform.localPosition = Vector3.zero;
            page.Root.SetActive(true);
            Current(token);
            page.Staging.SetActive(true);
            Current(token);
            page.Text.Validate(page.Root);
            AssertInputBlocked(page);
            page.Activated = Time.frameCount;
            page.Cursor = Get(page.Manager, "cursor") as InventoryCursor;
            if (page.Cursor == null) throw new InvalidOperationException("Tasks native cursor missing after Awake");
            page.Cursor.gameObject.SetActive(true);
            Current(token);
            page.Cursor.gameObject.SetActive(false);
            Hide(page.Root, true);
            Evaluate(page);
            // Start is deliberately disabled; reproduce only its clone-local text setup.
            var text = Get(page.Manager, _completed ? "toggleCompletedOffText" : "toggleCompletedOnText");
            typeof(InventoryItemQuestManager).GetMethod("SetToggleCompletedText", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(page.Manager, new object[] { (string)(TeamCherry.Localization.LocalisedString)text });
            Current(token);
        }
        void Evaluate(TaskPage page)
        {
            foreach (var response in page.Root.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                if (!page.Evaluated.Add(response)) continue;
                Current(page.Token);
                response.IsFullfilled = ReconstructEvent(response.IsFullfilled, page.Root.transform);
                response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, page.Root.transform);
                typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null);
                Current(page.Token);
            }
        }
        public bool TrySettle(DsJournalToken token, object clone)
        {
            var page = (TaskPage)clone;
            Hide(page.Root, true);
            if (Time.frameCount <= page.Activated + 2) return false;
            Current(token);
            if (page.Manager.CurrentSelected != null) throw new InvalidOperationException("Tasks native selection unexpectedly changed");
            page.Entries = page.Scroll.GetComponentsInChildren<InventoryItemQuest>(true);
            foreach (var entry in page.Entries)
                if (entry.gameObject.activeInHierarchy && (entry.Quest == null || entry.GetComponent<BoxCollider2D>() == null))
                    throw new InvalidOperationException("Tasks generated entry/quest/collider missing");
            Evaluate(page); Force(page);
            foreach (var scroll in page.Root.GetComponentsInChildren<ScrollView>(true)) { scroll.StopAllCoroutines(); scroll.enabled = false; }
            foreach (var grid in page.Root.GetComponentsInChildren<InventoryItemGrid>(true)) { grid.StopAllCoroutines(); grid.enabled = false; }
            NeutralizeOwnedClips(page.Root);
            page.View = page.Scroll.ViewBounds;
            page.Content = (Bounds)Get(page.Scroll, "contentBounds");
            page.Origin = page.Scroll.transform.localPosition;
            if (page.View.size.x <= 0 || page.View.size.y <= 0) throw new InvalidOperationException("Tasks viewport missing");
            page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y, page.Origin.y);
            Route(page.Root);
            Current(token);
            return true;
        }
        static void Force(TaskPage page)
        {
            page.Text.Validate(page.Root, page.ActiveCounter?.Root);
            if (page.ActiveCounter?.Root != null) page.ActiveCounter.Text.Validate(page.ActiveCounter.Root);
            ((LayoutGroup)Get(page.Manager, "descriptionLayout")).ForceUpdateLayoutNoCanvas();
            foreach (var text in page.Root.GetComponentsInChildren<PaneText>(true)) text.ForceMeshUpdate(true);
        }
        static void Hide(GameObject root, bool hide)
        {
            Suppress(root, hide);
            if (root != null) foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>(true)) renderer.cull = hide;
        }
        static void Route(GameObject root)
        {
            RouteOwned(root);
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = DsPresentation.UiCamera;
                canvas.targetDisplay = DsPresentation.DISPLAY;
                canvas.overrideSorting = false;
            }
        }
        bool Inside(TaskPage page, Bounds world)
        {
            var host = (RectTransform)page.Token.Host; var mask = _frame.ContentMask;
            return mask != null && Contains(host.rect, InSpace(world, null, host)) && Contains(mask.rect, InSpace(world, null, mask));
        }
        void Fit(TaskPage page)
        {
            page.Fit = InSpace(page.View, page.Scroll.transform.parent, page.Root.transform);
            foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && !Within(renderer.transform, page.Scroll.transform) && !Within(renderer.transform, page.Cursor.transform))
                    page.Fit.Encapsulate(InSpace(renderer.bounds, null, page.Root.transform));
            var rect = ((RectTransform)page.Token.Host).rect;
            CheckValue("Tasks fit", page.Fit, page.Root.transform);
            if (page.Fit.size.x <= 0 || page.Fit.size.y <= 0 || rect.width <= 0 || rect.height <= 0) throw new InvalidOperationException("Tasks fit bounds invalid");
            page.Scale = Mathf.Min(rect.width / page.Fit.size.x, rect.height / page.Fit.size.y) * .94f;
            page.Staging.transform.localRotation = Quaternion.identity;
            page.Staging.transform.localScale = Vector3.one * page.Scale;
            page.Staging.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0) - page.Fit.center * page.Scale;
        }
        public void Present(DsJournalToken token, object clone)
        {
            var page = (TaskPage)clone;
            Current(token);
            AssertInputBlocked(page);
            var pos = page.Origin; pos.y = page.Offset; page.Scroll.transform.localPosition = pos;
            Force(page); Fit(page); Route(page.Root); Hide(page.Root, false);
            page.Visible.Clear();
            var viewport = InSpace(page.View, page.Scroll.transform.parent, null);
            foreach (var entry in page.Entries)
            {
                if (!entry.gameObject.activeInHierarchy) continue;
                bool fits = true;
                foreach (var renderer in entry.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy) fits &= Contains(viewport, renderer.bounds) && Inside(page, renderer.bounds);
                if (fits) page.Visible.Add(entry);
                Hide(entry.gameObject, !fits);
            }
            foreach (var entry in page.Entries)
                if (entry is InventoryItemMainQuest && !page.Visible.Contains(entry))
                    foreach (var child in entry.GetComponentsInChildren<InventoryItemQuest>(true)) { page.Visible.Remove(child); Hide(child.gameObject, true); }
            if (page.Selected != null && !page.Visible.Contains(page.Selected)) ClearSelection(page);
            Contain(page);
            Current(token);
        }
        void Contain(TaskPage page)
        {
            bool cursorFits = page.Selected != null;
            var view = InSpace(page.View, page.Scroll.transform.parent, null);
            foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (Within(renderer.transform, page.Cursor.transform))
                { if (renderer.enabled && renderer.gameObject.activeInHierarchy) cursorFits &= Inside(page, renderer.bounds) && Contains(view, renderer.bounds); }
                else if (!Within(renderer.transform, page.Scroll.transform)) renderer.forceRenderingOff = !Inside(page, renderer.bounds);
            }
            Hide(page.Cursor.gameObject, !cursorFits);
            foreach (var graphic in page.Root.GetComponentsInChildren<Graphic>(true))
            {
                var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
                var bounds = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 4; i++) bounds.Encapsulate(corners[i]);
                bool visible = Inside(page, bounds);
                if (Within(graphic.transform, page.Scroll.transform)) visible &= Contains(view, bounds);
                var entry = graphic.GetComponentInParent<InventoryItemQuest>();
                if (entry != null) visible &= page.Visible.Contains(entry);
                graphic.canvasRenderer.cull = !visible;
            }
        }
        public bool OnGesture(DsGesture gesture)
        {
            var page = _state.Owned as TaskPage;
            if (!_state.Ready || page == null) return false;
            var host = (RectTransform)page.Token.Host;
            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, gesture.Position, DsPresentation.UiCamera, out point) || !host.rect.Contains(point)) return false;
            try
            {
                if (!IsCurrent(page.Token)) { _state.Clear(); return true; }
                if (gesture.Type == DsGestureType.Drag)
                {
                    page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y,
                        page.Offset + DsJournalGeometry.LocalDrag(gesture.Delta.y, page.Scale));
                    ClearSelection(page); Present(page.Token, page); return true;
                }
                if (gesture.Type != DsGestureType.Tap) return false;
                var toggle = Get(page.Manager, "toggleCompletedParent") as GameObject;
                if (toggle != null && toggle.activeInHierarchy)
                    foreach (var renderer in toggle.GetComponentsInChildren<Renderer>())
                        if (Hit(renderer.bounds, host, point)) { _completed = !_completed; Invalidate(); return true; }
                for (int i = page.Entries.Length - 1; i >= 0; i--)
                {
                    var entry = page.Entries[i];
                    if (page.Visible.Contains(entry) && Hit(entry.GetComponent<BoxCollider2D>().bounds, host, point))
                    { _selection.Select(page, entry, page.Token.Data); return true; }
                }
                return false;
            }
            catch (Exception e) { _failed = DsPortPageSnapshot.IsReadFailure(e) ? null : page.Token; _state.Clear(); Report(e.GetBaseException().Message); return true; }
        }
        static bool Hit(Bounds world, Transform host, Vector2 point)
        { var b = InSpace(world, null, host); return point.x >= b.min.x && point.x <= b.max.x && point.y >= b.min.y && point.y <= b.max.y; }
        bool IDsPortSelection.IsCurrent(object owner, object item, object data)
        {
            var page = owner as TaskPage; var entry = item as InventoryItemQuest;
            return page != null && !page.Released && entry != null && entry.Quest != null && IsCurrent(page.Token) &&
                ReferenceEquals(data, page.Token.Data) && page.Visible.Contains(entry) && Within(entry.transform, page.Scroll.transform);
        }
        static Transform CounterDestination(TaskPage page, InventoryItemExtraDescription condition)
        {
            var source = condition != null ? Get(condition, "descSectionParent") as Transform : null;
            var pane = ((InventoryPane)page.Token.Pane).transform;
            var grid = ((InventoryItemGrid)Get(page.Token.List, "itemList")).transform;
            if (source == null || !Within(source, pane) || Within(source, grid))
                throw new InvalidOperationException("Tasks custom counter destination is not in fixed native pane");
            var path = new Stack<int>();
            for (var node = source; node != pane; node = node.parent) path.Push(node.GetSiblingIndex());
            var mapped = page.Root.transform;
            while (path.Count != 0) mapped = mapped.GetChild(path.Pop());
            return mapped;
        }
        static CounterBinding BindCounter(TaskPage page, InventoryItemQuest template)
        {
            var binding = new CounterBinding { Template = template };
            try
            {
                if (template == null) throw new InvalidOperationException("Tasks counter native entry template missing");
                var conditions = template.GetComponents<InventoryItemExtraDescription>();
                if (conditions.Length > 1) throw new InvalidOperationException("Tasks counter template condition ambiguous");
                binding.Condition = conditions.Length == 1 ? conditions[0] : null;
                if (binding.Condition != null) binding.Destination = CounterDestination(page, binding.Condition);
            }
            catch (Exception error) { binding.Problem = error.GetBaseException().Message; }
            return binding;
        }
        static CounterBinding SelectedCounterBinding(TaskPage page, InventoryItemQuest entry)
        {
            var sourceMain = (InventoryItemMainQuest)Get(page.Token.List, "mainQuestTemplateItem");
            if (!ReferenceEquals(sourceMain, page.MainCounter.Template) ||
                !ReferenceEquals(Get(page.Token.List, "templateItem"), page.RegularCounter.Template) ||
                !ReferenceEquals(Get(sourceMain, "subQuestItemTemplate"), page.SubCounter.Template))
                throw new InvalidOperationException("Tasks counter factory identity replaced");
            if (entry is InventoryItemMainQuest) return page.MainCounter;
            var parent = entry.transform.parent != null ? entry.transform.parent.GetComponentInParent<InventoryItemMainQuest>(true) : null;
            if (parent == null) return page.RegularCounter;
            var spawned = Get(parent, "spawnedSubQuestItems") as List<InventoryItemQuest>;
            if (spawned == null || !spawned.Contains(entry)) throw new InvalidOperationException("Tasks counter is not an exact generated subquest");
            return page.SubCounter;
        }
        static void OwnCounterMaterials(CounterDetail detail)
        {
            var owned = new Dictionary<Material, Material>();
            Material Copy(Material source)
            {
                if (source == null) return null;
                if (!owned.TryGetValue(source, out var material))
                {
                    material = new Material(source); detail.Materials.Add(material); owned.Add(source, material);
                }
                return material;
            }
            foreach (var renderer in detail.Root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = Copy(materials[i]);
                renderer.sharedMaterials = materials;
            }
            foreach (var text in detail.Root.GetComponentsInChildren<PaneText>(true))
                Set(text, "m_sharedMaterial", Copy(Get(text, "m_sharedMaterial") as Material));
        }
        bool TryDisplayCounter(TaskPage page, InventoryItemQuest entry)
        {
            if (!(entry.Quest is FullQuestBase full) || (bool)Get(entry, "wasInCompletedSection") || !full.IsDescCounterTypeCustom)
            { page.Counter.Clear(); return true; }
            var binding = SelectedCounterBinding(page, entry);
            if (binding.Problem != null) throw new InvalidOperationException(binding.Problem);
            if (binding.Condition == null || !binding.Condition.WillDisplay) { page.Counter.Clear(); return true; }
            var prefab = full.CustomDescPrefab;
            if (prefab == null) { page.Counter.Clear(); return true; } // Native extra-description null prefab displays nothing.
            if (binding.Destination == null || !ReferenceEquals(CounterDestination(page, binding.Condition), binding.Destination))
                throw new InvalidOperationException("Tasks counter destination replaced");
            string problem = Inventory.AuxiliaryProblem(prefab);
            if (problem != null) throw new InvalidOperationException("Tasks native custom counter graph: " + problem);
            bool Exact() => IsCurrent(page.Token) && ReferenceEquals(entry.Quest, full) &&
                ReferenceEquals(full.CustomDescPrefab, prefab) && binding.Condition != null && binding.Condition.WillDisplay &&
                ReferenceEquals(binding.Template.GetComponent<InventoryItemExtraDescription>(), binding.Condition) &&
                ReferenceEquals(CounterDestination(page, binding.Condition), binding.Destination);
            page.Counter.Show(page, entry, prefab, binding.Destination, Exact, () => new CounterDetail(), value =>
            {
                var detail = (CounterDetail)value;
                page.ActiveCounter = detail;
                if (detail.Root == null)
                {
                    detail.Staging = new GameObject("DsPortTasksCounterStaging");
                    detail.Staging.SetActive(false); detail.Staging.transform.SetParent(page.Staging.transform, false);
                    detail.Root = Object.Instantiate(prefab, detail.Staging.transform, false);
                    detail.Root.SetActive(false);
                    problem = Inventory.AuxiliaryProblem(detail.Root);
                    if (problem != null) throw new InvalidOperationException("Tasks owned counter graph changed: " + problem);
                    Inventory.PrepareAuxiliary(detail.Root);
                    OwnCounterMaterials(detail);
                    detail.Text.Prepare(detail.Root, page.Token.Content);
                    foreach (var response in detail.Root.GetComponentsInChildren<PlayerDataTestResponse>(true))
                    {
                        response.IsFullfilled = ReconstructEvent(response.IsFullfilled, detail.Root.transform);
                        response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, detail.Root.transform);
                    }
                    // Preserve the exact native layout destination and prefab-local
                    // pose: staging is never a synthetic counter or layout wrapper.
                    detail.Root.transform.SetParent(binding.Destination, false);
                }
                if (!detail.Activated) { detail.Root.SetActive(true); detail.Activated = true; }
                if (!Exact()) throw new InvalidOperationException("Tasks counter identity replaced during activation");
                foreach (var response in detail.Root.GetComponentsInChildren<PlayerDataTestResponse>(true))
                {
                    typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null);
                    if (!Exact()) throw new InvalidOperationException("Tasks counter identity replaced during native condition evaluation");
                }
                detail.Text.Validate(detail.Root);
                NeutralizeOwnedClips(detail.Root); RouteOwned(detail.Root);
            }, ReleaseCounter);
            return true;
        }
        static void ReleaseCounter(object value)
        {
            var detail = (CounterDetail)value;
            if (detail.Root != null)
            {
                foreach (var driver in detail.Root.GetComponentsInChildren<MonoBehaviour>(true)) driver.StopAllCoroutines();
                detail.Root.SetActive(false);
                Object.DestroyImmediate(detail.Root); detail.Root = null;
            }
            if (detail.Staging != null) { Object.DestroyImmediate(detail.Staging); detail.Staging = null; }
            detail.Text.Clear();
            while (detail.Materials.Count != 0)
            {
                int last = detail.Materials.Count - 1;
                if (detail.Materials[last] != null) Object.DestroyImmediate(detail.Materials[last]);
                detail.Materials.RemoveAt(last);
            }
        }
        void IDsPortSelection.Display(object owner, object item)
        {
            var page = (TaskPage)owner; var entry = (InventoryItemQuest)item;
            Current(page.Token);
            var quest = entry.Quest;
            if (!DsPortDetailAttempt.Try(() => IsCurrent(page.Token) && ReferenceEquals(entry.Quest, quest), () =>
                {
                    page.Manager.SetDisplay((InventoryItemSelectable)entry);
                    Current(page.Token);
                    return TryDisplayCounter(page, entry);
                }, () => ClearSelection(page), error => Report("Tasks native counter detail unavailable: " + error.Message))) return;
            Current(page.Token);
            Force(page); Fit(page); Route(page.Root);
            var world = InSpace(page.View, page.Scroll.transform.parent, null);
            page.Cursor.SetClampedPos(world.min, world.max);
            page.Cursor.Activate(); Current(page.Token);
            page.Cursor.SetTarget(entry.transform); Current(page.Token);
            page.Selected = entry; Contain(page);
        }
        bool IDsPortSelection.CanSubmit(object owner, object item) => false;
        bool IDsPortSelection.Submit(object owner, object item) => false;
        public void ClearSelection(object clone)
        {
            var page = (TaskPage)clone;
            _selection.Clear(); page.Selected = null;
            page.Counter.Clear();
            if (page.Cursor != null) { page.Cursor.StopAllCoroutines(); page.Cursor.SetTarget(null); page.Cursor.Deactivate(); }
            if (page.Manager != null) page.Manager.SetDisplay((GameObject)null);
        }
        public void DestroyOwned(object clone)
        {
            var page = (TaskPage)clone;
            if (page.Destroyed) return;
            page.Released = true;
            Hide(page.Root, true);
            page.Counter.Clear();
            DsJournalAdmission.ReleaseOwned(
                () => { if (page.Staging != null) foreach (var driver in page.Staging.GetComponentsInChildren<MonoBehaviour>(true))
                    if (driver != null && !page.Stopped.Contains(driver)) { driver.StopAllCoroutines(); page.Stopped.Add(driver); } },
                () => { if (page.Staging != null && page.Staging.activeSelf) page.Staging.SetActive(false); },
                () => { if (page.Staging != null) Object.DestroyImmediate(page.Staging); page.Staging = null; });
            page.Text.Clear();
            page.Destroyed = true;
        }
    }
}
#endif
