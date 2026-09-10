// Native Loadout browse, adapted from Bottom.Inventory/Bottom.Select (MIT),
// igawa6/dualsouls 5c22451435b772acde0c7e6456f9019bc1baef73.
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TeamCherry.NestedFadeGroup;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using PaneText = TMProOld.TextMeshPro;

public sealed partial class DsPortProgress
{
    // Observation is not consumption or submit. Modal routing still wins once.
    public void ObserveGesture(DsGesture gesture)
    {
        try { _inventory.ObserveGesture(gesture); }
        finally { _loadout.ObserveGesture(gesture); }
    }

    sealed class Loadout : IDsPortJournalNative, IDsPortSelection
    {
        readonly DsPortFrame _frame;
        readonly DsPortJournalState _state;
        readonly DsPortSelectState _selection;
        readonly HashSet<string> _reported = new HashSet<string>();
        DsJournalToken _current, _failed;
        long _revision;
        float _nextContentProbe;
        bool _eligible;
        readonly DsPortActionHold _hold = new DsPortActionHold();
        readonly DsPortActionBoundary _actions = new DsPortActionBoundary();
        bool _singleTouchActive;
        bool _suppressReleaseTap;
        sealed class LoadoutPage
        {
            public DsJournalToken Token;
            public GameObject Staging, Root, Template, CursorTemplate;
            public InventoryItemToolManager Manager;
            public InventoryItemGrid Grid;
            public ScrollView Scroll;
            public InventoryCursor Cursor;
            public InventoryItemTool[] Entries;
            public Bounds View, Content, Fit;
            public Vector3 Origin;
            public float Offset, Scale;
            public int Activated;
            public bool Released, Destroyed;
            public Exception CreationError;
            public readonly DsPortOwnedText Text = new DsPortOwnedText();
            public readonly DsPortOwnedText DetailText = new DsPortOwnedText();
            public readonly HashSet<MonoBehaviour> Stopped = new HashSet<MonoBehaviour>();
            public InventoryItemSelectable Selected;
            public InventoryToolCrestList Crests;
            public readonly Dictionary<InventoryToolCrest, ToolCrest> SourceCrests = new Dictionary<InventoryToolCrest, ToolCrest>();
            public readonly Dictionary<InventoryToolCrest, GameObject> CrestVisuals = new Dictionary<InventoryToolCrest, GameObject>();
            public readonly HashSet<InventoryToolCrest> CrestVisualReady = new HashSet<InventoryToolCrest>();
            public readonly List<ToolCrest> Metadata = new List<ToolCrest>();
            public InventoryToolCrestSlot TargetSlot;
            public ToolItem PendingTool;
            public readonly Dictionary<PlayParticleEffects, bool> ParticleCallbacks = new Dictionary<PlayParticleEffects, bool>();
            public int ParticleFrame = -1;
            public InventoryItemToolBase ActionEntry;
            public CustomInventoryItemCollectableDisplay SocketIcon;
            public readonly Dictionary<InventoryItemToolBase, GameObject> ActionEffects = new Dictionary<InventoryItemToolBase, GameObject>();
            public readonly Dictionary<InventoryItemToolBase, GameObject> ActionEffectSources = new Dictionary<InventoryItemToolBase, GameObject>();
            public CollectableItem SocketResource;
            public ToolItem ActionItem;
            public long ActionGeneration;
            public bool ActionEnabled, UnlockStarted, UnlockFinished;
            public object UnlockEnd;
            public Coroutine UnlockHandle;
            public DsPortGuardedRoutine UnlockRoutine;
            public string EquippedCrestId;
            public GameObject DetailStaging, DetailRoot;
            public readonly List<Material> DetailMaterials = new List<Material>();
            public InventoryFloatingToolSlots Floating, SourceFloating;
            public Array FloatingConfigs, SourceFloatingConfigs;
            public object FloatingConfig, SourceFloatingConfig;
            public readonly Dictionary<InventoryToolCrestSlot, FloatingSlot> FloatingSlots = new Dictionary<InventoryToolCrestSlot, FloatingSlot>();
            public readonly HashSet<InventoryItemTool> Visible = new HashSet<InventoryItemTool>();
            public readonly Dictionary<InventoryItemTool, DsPortToolAnimatorAuthority> ToolAnimators = new Dictionary<InventoryItemTool, DsPortToolAnimatorAuthority>();
            public object[] ToolControllerVariants;
        }
        sealed class FloatingSlot
        {
            public object Descriptor, SourceDescriptor;
            public InventoryToolCrestSlot SourceSlot;
            public string Id;
            public ToolItemType Type;
            public object Getter, Setter;
        }
        public Loadout(DsPortFrame frame)
        { _frame = frame; _state = new DsPortJournalState(this); _selection = new DsPortSelectState(this); }
        public void SetTouchState(bool singleTouchActive) { _singleTouchActive = singleTouchActive; }
        bool _outgoing;
        public void SelectionChanged()
        {
            _selection.Clear(); _hold.Liveness(false); _nextContentProbe = 0;
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Loadout selection retirement deferred until callback unwind");
            var page = _state.Owned as LoadoutPage;
            if (page != null) CancelAction(page);
            _outgoing = _state.Ready && page != null && !page.Released && KeepOutgoing(_frame, DsPageRole.Loadout, page.Token) && _state.RetainOutgoingPresentation();
            if (!_outgoing) Invalidate();
        }
        public void Invalidate()
        {
            _outgoing = false;
            _hold.Liveness(false);
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Loadout retirement deferred until native callback unwind");
            _revision++; _nextContentProbe = 0; _current = null; _failed = null; _selection.Clear(); _state.Clear();
        }
        public void ObserveGesture(DsGesture gesture)
        {
            if (gesture.Type == DsGestureType.Down) _suppressReleaseTap = false;
            _hold.Observe(gesture.Type == DsGestureType.Down, gesture.Type == DsGestureType.Up);
            TouchHeld(_hold.Capture());
            // A new Down or an Up consumed by overlays still cancels the old native
            // continuation now, before any modal/page submit routing occurs.
            var page = _state.Owned as LoadoutPage;
            if (page != null && page.ActionEntry != null && !_hold.Current(page.ActionGeneration)) CancelAction(page);
        }
        bool TouchHeld(long generation)
        {
            _hold.Liveness(_singleTouchActive);
            return _hold.Current(generation);
        }
        public void Tick(bool eligible)
        {
            if (_actions.Active) return;
            if (_actions.Pending) Invalidate();
            if (_eligible != eligible) { Invalidate(); _eligible = eligible; }
            try
            {
                if (_outgoing)
                {
                    var outgoing = _state.Owned as LoadoutPage;
                    if (eligible && outgoing != null && !outgoing.Released && KeepOutgoing(_frame, DsPageRole.Loadout, outgoing.Token)) return;
                    Invalidate();
                }
                var actionPage = _state.Owned as LoadoutPage;
                if (actionPage != null) TickParticles(actionPage);
                if (actionPage != null && actionPage.ActionEntry != null && TickAction(actionPage)) return;
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
        { if (_reported.Add(problem)) Debug.LogWarning("[DualScreen][capability-gap] Loadout: " + problem); }
        DsJournalToken Capture()
        {
            if (!_eligible || !_frame.HudReady || _frame.SelectedRole != DsPageRole.Loadout || !DsGameData.InGame) return null;
            var pane = _frame.GetResidentPane(DsPageRole.Loadout);
            var host = _frame.GetOrCreatePageHost(DsPageRole.Loadout);
            var gm = GameManager.SilentInstance; var data = PlayerData.instance;
            var records = ManagerSingleton<ToolItemManager>.UnsafeInstance;
            if (pane == null || host == null || !host.gameObject.activeInHierarchy || gm == null || data == null || records == null ||
                !ReferenceEquals(gm.playerData, data) || gm.isPaused || gm.IsInSceneTransition || data.isInventoryOpen) return null;
            var manager = pane.GetComponentInChildren<InventoryItemToolManager>(true);
            if (manager == null) return null;
            return new DsJournalToken(pane, manager, data, records, host, _revision + _frame.SelectionEpoch,
                new DsPortPageSnapshot(ReadContent(pane, manager, data)));
        }
        static IEnumerable<object> ReadContent(InventoryPane pane, InventoryItemToolManager manager, PlayerData data)
        {
            yield return ToolItemManager.Version; yield return ToolItemManager.ActiveState;
            yield return data.CurrentCrestID; yield return manager.IsHeroCursed; yield return manager.CanChangeEquips();
            yield return CollectableItemManager.IsInHiddenMode();
            yield return manager.IsActionsBlocked;
            yield return manager.SlotUnlockItem;
            if (manager.SlotUnlockItem != null)
            {
                yield return manager.SlotUnlockItem.CollectedAmount;
                yield return manager.SlotUnlockItem.CustomInventoryDisplay;
                yield return manager.SlotUnlockItem.GetDisplayName(CollectableItem.ReadSource.Inventory);
                yield return manager.SlotUnlockItem.GetDescription(CollectableItem.ReadSource.Inventory);
            }
            foreach (CurrencyType currency in Enum.GetValues(typeof(CurrencyType))) yield return CurrencyManager.GetCurrencyAmount(currency);
            foreach (var tool in ToolItemManager.GetAllTools())
            {
                yield return tool;
                if (tool == null) continue;
                var saved = tool.SavedData;
                yield return tool.name; yield return tool.IsUnlockedNotHidden;
                yield return saved.IsUnlocked; yield return saved.IsHidden; yield return saved.AmountLeft;
                yield return tool.HasBeenSeen; yield return tool.HasBeenSelected;
                if (!tool.IsUnlockedNotHidden) continue;
                yield return tool.Type; yield return tool.InventorySpriteModified;
                yield return (string)tool.DisplayName; yield return (string)tool.Description;
                yield return tool.ExtraDescriptionSection;
                yield return tool.ReplenishUsage; yield return tool.CanReload(); yield return tool.CanToggle;
                yield return tool.DisplayTogglePrompt; yield return tool.CustomToggleText;
                yield return tool.HasCustomAction; yield return tool.CustomButtonCombo;
                if (tool is ToolItemStatesLiquid liquid)
                {
                    var reserve = liquid.LiquidSavedData;
                    yield return liquid.HasInfiniteRefills; yield return liquid.RefillsMax; yield return liquid.LiquidColor;
                    yield return reserve.RefillsLeft; yield return reserve.UsedExtra; yield return reserve.SeenEmptyState;
                }
            }
            yield return "crests";
            foreach (var crest in ToolItemManager.GetAllCrests())
            {
                yield return crest; yield return crest.name;
                yield return crest.IsVisible; yield return crest.IsHidden; yield return crest.DisplayPrefab;
                yield return (string)crest.DisplayName; yield return (string)crest.Description;
                yield return crest.CrestSprite; yield return crest.CrestSilhouette; yield return crest.CrestGlow;
                var saved = data.ToolEquips.GetData(crest.name);
                yield return saved.IsUnlocked; yield return saved.DisplayNewIndicator;
                yield return "native-slots";
                if (crest.Slots == null) throw new InvalidOperationException("Loadout content crest slots missing");
                foreach (var slot in crest.Slots) yield return slot; // SlotInfo is a scalar-only copied native struct.
                yield return "saved-slots";
                if (saved.Slots != null) foreach (var slot in saved.Slots)
                { yield return slot.EquippedTool; yield return slot.IsUnlocked; }
                yield return "end-crest";
            }
            var floating = Get(manager, "extraSlots") as InventoryFloatingToolSlots;
            yield return floating;
            if (floating != null)
            {
                var configs = Get(floating, "configs") as Array;
                if (configs == null || configs.Length > DsPortPageSnapshot.MaximumValues)
                    throw new InvalidOperationException("Loadout content floating configs unavailable");
                yield return configs; yield return LastFloatingConfig(configs, data);
                foreach (var config in configs)
                {
                    yield return config;
                    var slots = Get(config, "Slots") as Array;
                    if (slots == null) throw new InvalidOperationException("Loadout content floating slots missing");
                    foreach (var slot in slots)
                    {
                        yield return slot; yield return Get(slot, "SlotObject"); yield return Get(slot, "CursedSlot");
                        string id = (string)Get(slot, "Id"); yield return id; yield return Get(slot, "Type");
                        var saved = data.ExtraToolEquips.GetData(id);
                        yield return saved.EquippedTool; yield return saved.IsUnlocked;
                    }
                    yield return "end-floating-config";
                }
            }
            foreach (var value in ReadPaneConditions(pane, manager)) yield return value;
        }
        public bool IsCurrent(DsJournalToken token) => token != null && token.Same(Capture());
        void Current(DsJournalToken token)
        { if (!IsCurrent(token)) throw new InvalidOperationException("Loadout owner/selection replaced"); }
        static bool Allowed(Type type) => ComponentAllowed(type, "visual") || type == typeof(InventoryPane) ||
            type == typeof(InventoryPaneInput) || type == typeof(InventoryItemToolManager) ||
            type == typeof(InventoryItemTool) || type == typeof(InventoryItemGrid) || type == typeof(InventoryAutoNavGroup) ||
            type == typeof(ScrollView) || type == typeof(InventoryCursor) || type == typeof(Animator) ||
            type == typeof(InventoryItemExtraDescription) || type == typeof(InventoryToolCrestList) || type == typeof(InventoryToolCrest) ||
            type == typeof(InventoryToolCrestSlot) || type == typeof(InventoryFloatingToolSlots) || type == typeof(SpriteMask) ||
            type == typeof(TextMeshProContainerFitter) || type == typeof(TMProOld.TextContainer) ||
            type == typeof(CrestSocketUnlockInventoryDescription) || type == typeof(InventoryItemComboButtonPromptDisplay) ||
            type == typeof(InventoryItemSelectableButtonEvent) || type == typeof(InventoryItemCollectable) ||
            type == typeof(SetTextMeshProGameText) || type == typeof(AudioSource) || type == typeof(JitterSelf) ||
            type == typeof(PassColour) || type == typeof(PlayParticleEffects) || type == typeof(ParticleSystem) || type == typeof(ParticleSystemRenderer) ||
            type == typeof(InventoryItemButtonPromptDisplayList) || type == typeof(InventoryItemButtonPromptDisplay) || type == typeof(ActionButtonIcon);
        static void InspectScope(GameObject root, GameObject template, GameObject cursor)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || !Allowed(component.GetType()))
                    throw new InvalidOperationException("Loadout unadmitted component " + (component == null ? "missing script" : component.GetType().FullName));
                if (component is InventoryItemExtraDescription) continue; // Removed before any owned Awake.
                var item = component.GetComponentInParent<InventoryItemTool>(true);
                var local = item != null && Within(item.transform, root.transform) ? item.transform : root.transform;
                ValidateLoadoutReferences(component, local, template, cursor);
                if (component is ParticleSystem || component is PlayParticleEffects || component is PassColour)
                    InspectEffectComponent(component, local);
                if (component is PlayerDataTestResponse response)
                { ReadEvent(response.IsFullfilled, local); ReadEvent(response.IsNotFulfilled, local); }
                if (component is InventoryItemTool)
                {
                    NeedLocal(component, local, "itemIcon", "emptyNotch");
                    // Optional tool details are admitted only for the selected owned entry.
                }
                if (component is AudioSource audio && (audio.spatialBlend != 0f || audio.playOnAwake))
                    throw new InvalidOperationException("Loadout action audio requires nonspatial explicit native playback");
                if (component is CrestSocketUnlockInventoryDescription)
                    NeedLocal(component, component.transform, "slotIcon", "leftLock", "leftLockGlow", "rightLock", "rightLockGlow");
                if (component is InventoryItemComboButtonPromptDisplay)
                    NeedLocal(component, component.transform, "parentWithoutModifier", "actionButtonWithoutModifier",
                        "parentWithModifier", "actionButtonWithModifier", "modifierUp", "modifierDown", "promptText");
                if (component is InventoryFloatingToolSlots)
                {
                    NeedLocal(component, component.transform, "bracketsFader");
                    var configs = Get(component, "configs") as Array;
                    if (configs == null || configs.Length == 0) throw new InvalidOperationException("Loadout floating configs missing");
                    foreach (var config in configs)
                    {
                        if (config == null) throw new InvalidOperationException("Loadout floating config missing");
                        CheckValue("Brackets", Get(config, "Brackets"), component.transform);
                        var slots = Get(config, "Slots") as Array;
                        if (slots == null) throw new InvalidOperationException("Loadout floating slots missing");
                        foreach (var slot in slots) NeedLocal(slot, component.transform, "SlotObject", "CursedSlot");
                    }
                }
                if (component is InventoryToolCrest crest)
                {
                    var slots = Get(crest, "templateSlots") as InventoryToolCrestSlot[];
                    if (slots == null || slots.Length != Enum.GetValues(typeof(ToolItemType)).Length)
                        throw new InvalidOperationException("Loadout crest slot templates missing");
                    foreach (var slot in slots)
                        if (slot == null || !Within(slot.transform, crest.transform))
                            throw new InvalidOperationException("Loadout crest slot template escaped copy scope");
                }
                if (component is InventoryToolCrestList) NeedLocal(component, component.transform, "templateCrest", "scrollParent");
                if (component is NestedFadeGroupBase fade && fade.ParentOverride != null && fade.ParentOverride.Value != null &&
                    !Within(fade.ParentOverride.Value.transform, local)) throw new InvalidOperationException("Loadout escaped fade parent");
                if (component is InventoryItemGrid grid)
                {
                    if (grid.RowSplit <= 0) throw new InvalidOperationException("Loadout grid RowSplit invalid");
                    ValidateNavigation(Get(grid, "selectables"), root.transform); ValidateNavigation(Get(grid, "nextPages"), root.transform);
                }
            }
            ValidateBridgeCatalogue(root);
        }
        static void InspectPrompts(InventoryItemToolManager manager, Transform root)
        {
            NeedLocal(manager, root, "slotUnlockDescExtra", "slotUnlockItemDisplay", "comboButtonPromptDisplay",
                "equipPrompt", "reloadPrompt", "customTogglePrompt", "selectCrestPrompt", "changeCrestButton");
            var resource = manager.SlotUnlockItem;
            var display = manager.SlotUnlockItemDisplay;
            if (resource == null || display == null || display.GetType() != typeof(InventoryItemCollectable))
                throw new InvalidOperationException("Loadout native socket resource/display missing");
            if (resource.CustomInventoryDisplay != null)
            {
                string problem = Inventory.AuxiliaryProblem(resource.CustomInventoryDisplay.gameObject);
                if (problem != null) throw new InvalidOperationException("Loadout socket icon: " + problem);
            }
            if ((bool)Get(display, "isSelected"))
                throw new InvalidOperationException("Loadout socket resource display must not own native selection");
        }
        static void InspectAssets(DsJournalToken token)
        {
            var names = new HashSet<string>();
            foreach (var crest in ToolItemManager.GetAllCrests())
                if (crest == null || crest.Slots == null || !names.Add(crest.name))
                    throw new InvalidOperationException("Loadout native crest identity/slots missing or duplicate");
        }
        public string Inspect(DsJournalToken token)
        {
            var manager = (InventoryItemToolManager)token.List;
            var template = Get(manager, "templateItem") as InventoryItemTool;
            var cursor = Get(manager, "cursorPrefab") as InventoryCursor;
            if (template == null || cursor == null) return "Loadout native entry/cursor donor missing";
            foreach (var root in new[] { ((InventoryPane)token.Pane).gameObject, template.gameObject, cursor.gameObject })
                InspectScope(root, template.gameObject, cursor.gameObject);
            InspectAssets(token);
            InspectPrompts(manager, ((InventoryPane)token.Pane).transform);
            NeedLocal(manager, ((InventoryPane)token.Pane).transform, "itemList", "nameText", "descriptionText", "descriptionLayout");
            return null;
        }
        public object CloneInactive(DsJournalToken token)
        {
            var page = new LoadoutPage { Token = token };
            try
            {
                page.Staging = new GameObject("DsPortLoadoutStaging"); page.Staging.SetActive(false);
                page.Staging.transform.SetParent((Transform)token.Host, false);
                page.Root = Object.Instantiate(((InventoryPane)token.Pane).gameObject, page.Staging.transform, false);
                var source = (InventoryItemToolManager)token.List;
                page.Template = Object.Instantiate(((InventoryItemTool)Get(source, "templateItem")).gameObject, page.Staging.transform, false);
                page.CursorTemplate = Object.Instantiate(((InventoryCursor)Get(source, "cursorPrefab")).gameObject, page.Staging.transform, false);
                page.Template.SetActive(false); page.CursorTemplate.SetActive(false);
                return page;
            }
            catch (Exception error) { page.CreationError = error; return page; }
        }
        public void BindAndVerify(DsJournalToken token, object clone)
        {
            var page = (LoadoutPage)clone;
            if (page.CreationError != null) throw new InvalidOperationException("Loadout native page creation failed", page.CreationError);
            page.Manager = page.Root.GetComponentInChildren<InventoryItemToolManager>(true);
            if (page.Manager == null) throw new InvalidOperationException("Loadout owned manager missing");
            page.Manager.enabled = false; // Suppress Start -> EquipState setter/paneList writes.
            Set(page.Manager, "templateItem", page.Template.GetComponent<InventoryItemTool>());
            Set(page.Manager, "cursorPrefab", page.CursorTemplate.GetComponent<InventoryCursor>());
            foreach (var root in new[] { page.Root, page.Template, page.CursorTemplate })
            {
                page.Text.Prepare(root, token.Content);
                foreach (var input in root.GetComponentsInChildren<InventoryPaneInput>(true)) Object.DestroyImmediate(input);
                foreach (var extra in root.GetComponentsInChildren<InventoryItemExtraDescription>(true)) Object.DestroyImmediate(extra);
                PrepareToolAnimators(page, root);
                PrepareSlotAnimators(root);
                foreach (var list in root.GetComponentsInChildren<InventoryToolCrestList>(true)) list.enabled = false;
                foreach (var response in root.GetComponentsInChildren<PlayerDataTestResponse>(true))
                { var field = Field(response, "runOn"); field.SetValue(response, Enum.Parse(field.FieldType, "JustStart")); response.enabled = false; }
                foreach (var cursor in root.GetComponentsInChildren<InventoryCursor>(true))
                { var sound = (AudioEvent)Get(cursor, "changeSelectionSound"); sound.Volume = 0; Set(cursor, "changeSelectionSound", sound); }
                foreach (var clip in root.GetComponentsInChildren<TextMeshProClipRect>(true)) clip.enabled = false;
                Suppress(root, true); RouteOwned(root); InspectScope(root, page.Template, page.CursorTemplate);
            }
            page.EquippedCrestId = ((PlayerData)token.Data).CurrentCrestID;
            page.Crests = (InventoryToolCrestList)Get(page.Manager, "crestList");
            NeedLocal(page.Manager, page.Root.transform, "crestList", "extraSlots", "toolList", "descriptionLayout", "descriptionText");
            page.Grid = (InventoryItemGrid)Get(page.Manager, "itemList");
            page.Scroll = (ScrollView)Get(page.Grid, "scrollView");
            if (page.Scroll == null || !Within(page.Scroll.transform, page.Root.transform)) throw new InvalidOperationException("Loadout owned scroll missing");
            InspectPrompts(page.Manager, page.Root.transform);
            BuildOwnedCrests(page);
            Current(token);
        }
        void BuildOwnedCrests(LoadoutPage page)
        {
            Current(page.Token);
            if (page.Staging.activeInHierarchy) throw new InvalidOperationException("Loadout crest factory requires inactive ownership");
            var template = (InventoryToolCrest)Get(page.Crests, "templateCrest");
            var crests = (List<InventoryToolCrest>)Get(page.Crests, "crests");
            if (template == null || crests.Count != 0) throw new InvalidOperationException("Loadout native crest factory is not pristine");
            template.gameObject.SetActive(false);
            foreach (var source in ToolItemManager.GetAllCrests())
            {
                Current(page.Token);
                var crest = Object.Instantiate(template, template.transform.parent);
                crests.Add(crest); page.SourceCrests.Add(crest, source);
                // Native list Awake runs before these prebuilt crest Awakes. Seed only
                // its exact owned manager/initial indicator presentation dependencies.
                Set(crest, "manager", page.Manager);
                var indicator = Get(crest, "newIndicator") as GameObject;
                if (indicator != null) Set(crest, "newIndicatorInitialScale", indicator.transform.localScale);
                InventoryItemManager.PropagateSelectables(page.Crests, crest);
                DsPortCrestSetup.Run(source,
                    () => { var metadata = ScriptableObject.CreateInstance<ToolCrest>(); page.Metadata.Add(metadata); return metadata; },
                    metadata =>
                    {
                        // A blank native asset's OnEnable has no previousVersion links.
                        // Never clone/copy source asset lifecycle or mutable slot storage.
                        metadata.name = source.name;
                        foreach (string field in new[] { "displayName", "description", "crestSprite", "crestSilhouette", "crestGlow", "isHidden" })
                            Set(metadata, field, Get(source, field));
                        Set(metadata, "slots", (ToolCrest.SlotInfo[])source.Slots.Clone());
                        crest.Setup(metadata); // Native entry/slot factory; optional visual remains lazy.
                    },
                    exact => Set(crest, "<CrestData>k__BackingField", exact),
                    metadata => { Object.Destroy(metadata); page.Metadata.Remove(metadata); });
                VerifyCrestAuthority(page, crest, source);
                if (source.DisplayPrefab == null) page.CrestVisualReady.Add(crest);
                crest.gameObject.SetActive(true); // Still under inactive page staging.
            }
            // Validate generated native slots before their first Awake. Keep the
            // admitted template present during the exact graph-reference check.
            InspectScope(page.Root, page.Template, page.CursorTemplate);
            page.Text.Prepare(page.Root, page.Token.Content);
            PrepareParticleCallbacks(page, page.Root);
            Suppress(page.Root, true); RouteOwned(page.Root);
            // Native Setup still owns selection/layout/refresh, but SetupCrests must
            // not redo all-assets optional prefab creation on our prepopulated list.
            Set(page.Crests, "templateCrest", null);
            var changeButton = Get(page.Crests, "changeCrestButton") as Component;
            if (changeButton != null) changeButton.gameObject.SetActive(page.Crests.CanChangeCrests());
        }
        static void VerifyCrestAuthority(LoadoutPage page, InventoryToolCrest crest, ToolCrest source)
        {
            if (source == null || !ReferenceEquals(crest.CrestData, source) ||
                !ReferenceEquals(ToolItemManager.GetCrestByName(source.name), source) || crest.gameObject.name != source.name ||
                !Within(crest.transform, page.Crests.transform)) throw new InvalidOperationException("Loadout crest retained non-source action identity");
            var slots = new List<InventoryToolCrestSlot>(crest.GetSlots());
            if (slots.Count != source.Slots.Length) throw new InvalidOperationException("Loadout crest retained slot count changed");
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.Crest != crest || slot.SlotIndex != i || !slot.SlotInfo.Equals(source.Slots[i]) ||
                    !Within(slot.transform, crest.transform) || Get(slot, "getSavedDataOverride") != null || Get(slot, "setSavedDataOverride") != null ||
                    !SaveCallback(slot, crest, typeof(InventoryToolCrest)))
                    throw new InvalidOperationException("Loadout crest slot retained non-source action authority");
            }
        }
        bool TryCrestVisual(LoadoutPage page, InventoryToolCrest crest)
        {
            Current(page.Token);
            VerifyCrestAuthority(page, crest, page.SourceCrests[crest]);
            if (page.CrestVisualReady.Contains(crest)) return true;
            var prefab = crest.CrestData.DisplayPrefab;
            if (prefab == null) { page.CrestVisualReady.Add(crest); return true; }
            var cache = (Dictionary<GameObject, GameObject>)Get(crest, "spawnedDisplayObjects");
            if (cache.Count != 0) throw new InvalidOperationException("Loadout selected crest visual cache unexpectedly populated");
            string problem = Inventory.AuxiliaryProblem(prefab);
            if (problem != null) throw new InvalidOperationException("Crest " + crest.CrestData.name + ": " + problem);
            var staging = new GameObject("DsPortCrestVisualStaging"); staging.SetActive(false);
            staging.transform.SetParent(page.Staging.transform, false);
            try
            {
                var visual = Object.Instantiate(prefab, staging.transform, false); page.CrestVisuals[crest] = visual;
                visual.SetActive(false);
                problem = Inventory.AuxiliaryProblem(visual);
                if (problem != null) throw new InvalidOperationException(problem);
                Inventory.PrepareAuxiliary(visual);
                page.Text.Prepare(visual, page.Token.Content);
                visual.transform.SetParent(crest.transform, false); visual.transform.localPosition = Vector3.zero;
                cache.Add(prefab, visual); Set(crest, "activeDisplayObject", visual);
                visual.SetActive(true); Current(page.Token);
                foreach (var response in visual.GetComponentsInChildren<PlayerDataTestResponse>(true))
                {
                    response.IsFullfilled = ReconstructEvent(response.IsFullfilled, visual.transform);
                    response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, visual.transform);
                    typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null);
                    Current(page.Token);
                }
                page.CrestVisualReady.Add(crest); return true;
            }
            finally { Object.Destroy(staging); }
        }
        bool EnsureCrestVisual(LoadoutPage page, InventoryToolCrest crest)
        {
            return DsPortDetailAttempt.Try(() => !page.Released && IsCurrent(page.Token),
                () => TryCrestVisual(page, crest),
                () =>
                {
                    page.CrestVisualReady.Remove(crest);
                    if (page.CrestVisuals.TryGetValue(crest, out var visual))
                    { visual.SetActive(false); Object.Destroy(visual); page.CrestVisuals.Remove(crest); }
                    ((Dictionary<GameObject, GameObject>)Get(crest, "spawnedDisplayObjects")).Clear();
                    Set(crest, "activeDisplayObject", null); ClearSelection(page);
                }, error => Report("Loadout selected crest visual unavailable for " + crest.CrestData.name + ": " + error.Message));
        }
        static bool ValidateSlotAnimator(Animator animator)
        {
            var slot = animator.GetComponentInParent<InventoryToolCrestSlot>(true);
            var entry = animator.GetComponentInParent<InventoryItemTool>(true);
            Transform local;
            if (slot != null && Within(animator.transform, slot.transform) &&
                (ReferenceEquals(Get(slot, "slotAnimator"), animator) || ReferenceEquals(Get(slot, "slotFilledAnimator"), animator))) local = slot.transform;
            else if (entry != null && Within(animator.transform, entry.transform) && ReferenceEquals(Get(entry, "slotAnimator"), animator)) local = entry.transform;
            else return false;
            if (animator.applyRootMotion) throw new InvalidOperationException("Loadout slot animator has root-motion authority");
            InspectEffectComponent(animator, local);
            // An authored slot reference is necessary but not sufficient. Its
            // relative animation targets must be a visual island, never the pane,
            // manager, selectable, cache factory, event driver or input graph.
            foreach (var component in animator.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Loadout slot animator has missing target script");
                var type = component.GetType();
                if (type != typeof(Transform) && type != typeof(RectTransform) && type != typeof(Animator) &&
                    type != typeof(SpriteRenderer) && type != typeof(SpriteMask) && type != typeof(MeshRenderer) &&
                    type != typeof(MeshFilter) && type != typeof(PaneText) && type != typeof(TMProOld.TMP_SubMesh) &&
                    type != typeof(TMProOld.TextContainer) && type != typeof(NestedFadeGroup) &&
                    type != typeof(NestedFadeGroupSpriteRenderer) && type != typeof(NestedFadeGroupTextMeshPro))
                    throw new InvalidOperationException("Loadout slot animation target is not an owned visual island: " + type.FullName);
                if (component is Animator nested) InspectEffectComponent(nested, local);
                ValidateLoadoutReferences(component, local, local.gameObject, local.gameObject);
            }
            return true;
        }
        static object[] ReadToolControllers(InventoryItemTool entry)
        {
            var result = new List<object>();
            foreach (string field in new[] { "slotAnimatorControllers", "attackAnimatorControllers", "skillAnimatorControllers" })
            {
                var values = Get(entry, field) as RuntimeAnimatorController[];
                int expected = Enum.GetValues(field == "slotAnimatorControllers" ? typeof(ToolItemType) : typeof(AttackToolBinding)).Length;
                if (values == null || values.Length != expected || result.Count + values.Length > 4096)
                    throw new InvalidOperationException("Loadout native tool controller variant shape changed");
                foreach (var value in values) result.Add(value);
            }
            return result.ToArray();
        }
        static void PrepareToolAnimators(LoadoutPage page, GameObject root)
        {
            if (root.activeInHierarchy) throw new InvalidOperationException("Loadout controller admission requires inactive owned targets");
            foreach (var entry in root.GetComponentsInChildren<InventoryItemTool>(true))
            {
                var animator = Get(entry, "slotAnimator") as Animator;
                if (animator == null) continue; // Native SetData explicitly permits an absent animator.
                if (!ValidateSlotAnimator(animator)) throw new InvalidOperationException("Loadout tool animator escaped exact owned entry");
                var variants = ReadToolControllers(entry);
                var original = animator.runtimeAnimatorController;
                try
                {
                    // Only this born-inactive OWNED copy is rebound for inspection. Source assets and
                    // borrowed animators are never changed; native SetData/refresh alone selects live variants.
                    foreach (RuntimeAnimatorController controller in variants)
                    {
                        animator.runtimeAnimatorController = controller;
                        InspectEffectComponent(animator, entry.transform);
                    }
                }
                finally { animator.runtimeAnimatorController = original; }
                var authority = new DsPortToolAnimatorAuthority(animator, variants);
                if (!authority.Current(animator, variants, original)) throw new InvalidOperationException("Loadout initial tool controller is not an authored variant");
                page.ToolAnimators.Add(entry, authority);
                if (entry.gameObject == page.Template) page.ToolControllerVariants = variants;
            }
        }
        static void AssertToolAnimators(LoadoutPage page)
        {
            var template = page.Template.GetComponent<InventoryItemTool>();
            foreach (var entry in page.Root.GetComponentsInChildren<InventoryItemTool>(true))
            {
                var animator = Get(entry, "slotAnimator") as Animator;
                if (animator == null)
                {
                    if (page.ToolAnimators.ContainsKey(entry) || Get(template, "slotAnimator") != null)
                        throw new InvalidOperationException("Loadout required tool animator was removed");
                    continue;
                }
                if (!Within(entry.transform, page.Root.transform) || !Within(animator.transform, entry.transform) ||
                    (Get(entry, "manager") != null && !ReferenceEquals(Get(entry, "manager"), page.Manager)))
                    throw new InvalidOperationException("Loadout tool animation lost owned entry/manager authority");
                if (!page.ToolAnimators.TryGetValue(entry, out var authority))
                {
                    var sourceAnimator = Get(template, "slotAnimator") as Animator;
                    if (page.ToolAnimators.Count >= 4096 || sourceAnimator == null || page.ToolControllerVariants == null ||
                        MapDetailDestination(template.transform, entry.transform, sourceAnimator.transform) != animator.transform)
                        throw new InvalidOperationException("Loadout generated tool animator is not the exact admitted template copy");
                    authority = new DsPortToolAnimatorAuthority(animator, page.ToolControllerVariants);
                    page.ToolAnimators.Add(entry, authority);
                }
                if (!authority.Current(animator, ReadToolControllers(entry), animator.runtimeAnimatorController) ||
                    (entry.ItemData != null && animator.runtimeAnimatorController == null) || !animator.enabled || !ValidateSlotAnimator(animator))
                    throw new InvalidOperationException("Loadout tool animator/controller changed after native SetData or refresh");
            }
        }
        static void PrepareSlotAnimators(GameObject root)
        {
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                animator.enabled = ValidateSlotAnimator(animator);
        }
        static void AssertInputBlocked(LoadoutPage page)
        {
            AssertToolAnimators(page);
            foreach (var animator in page.Root.GetComponentsInChildren<Animator>(true))
                if (ValidateSlotAnimator(animator) && !animator.enabled)
                    throw new InvalidOperationException("Loadout required owned slot animation driver was disabled");
            if (page.Manager.enabled || page.Manager.CurrentSelected != null ||
                page.Manager.EquipState != InventoryItemToolManager.EquipStates.None ||
                page.Crests == null || page.Crests.enabled || page.Crests.IsSwitchingCrests ||
                page.Root.GetComponentsInChildren<InventoryPaneInput>(true).Length != 0)
                throw new InvalidOperationException("Loadout primary input/equip-state boundary changed");
            if (Get(page.Crests, "templateCrest") != null) throw new InvalidOperationException("Loadout native all-assets crest factory rearmed");
            foreach (var pair in page.SourceCrests) VerifyCrestAuthority(page, pair.Key, pair.Value);
        }
        public void ActivateForLayout(DsJournalToken token, object clone)
        {
            var page = (LoadoutPage)clone; Current(token);
            var scale = page.Staging.transform.parent.lossyScale;
            if (Mathf.Abs(scale.x) < .0001f || Mathf.Abs(scale.y) < .0001f || Mathf.Abs(scale.z) < .0001f) throw new InvalidOperationException("Loadout host scale invalid");
            page.Staging.transform.rotation = Quaternion.identity;
            page.Staging.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            page.Root.transform.localPosition = Vector3.zero; page.Root.transform.localRotation = Quaternion.identity; page.Root.transform.localScale = Vector3.one;
            AssertInputBlocked(page);
            page.Root.SetActive(true); Current(token); page.Staging.SetActive(true); Current(token);
            page.Text.Validate(page.Root);
            AssertInputBlocked(page);
            CaptureFloatingSlots(page);
            page.Activated = Time.frameCount;
            page.Cursor = Get(page.Manager, "cursor") as InventoryCursor;
            if (page.Cursor == null) throw new InvalidOperationException("Loadout native cursor missing after Awake");
            page.Cursor.gameObject.SetActive(true); Current(token); page.Cursor.gameObject.SetActive(false);
            foreach (var response in page.Root.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                var item = response.GetComponentInParent<InventoryItemTool>(true);
                var local = item != null ? item.transform : page.Root.transform;
                response.IsFullfilled = ReconstructEvent(response.IsFullfilled, local);
                response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, local);
                typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null); Current(token);
            }
            Suppress(page.Root, true);
        }
        public bool TrySettle(DsJournalToken token, object clone)
        {
            var page = (LoadoutPage)clone; Suppress(page.Root, true);
            if (Time.frameCount <= page.Activated + 2) return false;
            Current(token);
            if (page.Manager.CurrentSelected != null) throw new InvalidOperationException("Loadout native selection unexpectedly changed");
            page.Entries = page.Grid.GetComponentsInChildren<InventoryItemTool>(true);
            foreach (var entry in page.Entries)
                if (entry.gameObject.activeInHierarchy && (entry.ItemData == null || entry.GetComponent<BoxCollider2D>() == null)) throw new InvalidOperationException("Loadout generated item/collider missing");
            if (page.Crests.CurrentCrest != null) EnsureCrestVisual(page, page.Crests.CurrentCrest);
            Force(page);
            page.Scroll.StopAllCoroutines(); page.Scroll.enabled = false; page.Grid.StopAllCoroutines(); page.Grid.enabled = false;
            page.View = page.Scroll.ViewBounds; page.Content = (Bounds)Get(page.Scroll, "contentBounds"); page.Origin = page.Scroll.transform.localPosition;
            if (page.View.size.x <= 0 || page.View.size.y <= 0) throw new InvalidOperationException("Loadout viewport missing");
            page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y, page.Origin.y);
            NeutralizeOwnedClips(page.Root); RouteOwned(page.Root); Current(token); return true;
        }
        static void Force(LoadoutPage page)
        {
            page.Text.Validate(page.Root, page.DetailRoot);
            if (page.DetailRoot != null) page.DetailText.Validate(page.DetailRoot);
            ((LayoutGroup)Get(page.Manager, "descriptionLayout")).ForceUpdateLayoutNoCanvas();
            foreach (var text in page.Root.GetComponentsInChildren<PaneText>(true)) text.ForceMeshUpdate(true);
        }
        bool Inside(LoadoutPage page, Bounds world)
        {
            var host = (RectTransform)page.Token.Host; var mask = _frame.ContentMask;
            return mask != null && Contains(host.rect, InSpace(world, null, host)) && Contains(mask.rect, InSpace(world, null, mask));
        }
        void Fit(LoadoutPage page)
        {
            page.Fit = InSpace(page.View, page.Scroll.transform.parent, page.Root.transform);
            foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && !Within(renderer.transform, page.Scroll.transform) && !Within(renderer.transform, page.Cursor.transform))
                    page.Fit.Encapsulate(InSpace(renderer.bounds, null, page.Root.transform));
            var rect = ((RectTransform)page.Token.Host).rect; CheckValue("Loadout fit", page.Fit, page.Root.transform);
            if (page.Fit.size.x <= 0 || page.Fit.size.y <= 0 || rect.width <= 0 || rect.height <= 0) throw new InvalidOperationException("Loadout fit invalid");
            page.Scale = Mathf.Min(rect.width / page.Fit.size.x, rect.height / page.Fit.size.y) * .94f;
            page.Staging.transform.localRotation = Quaternion.identity; page.Staging.transform.localScale = Vector3.one * page.Scale;
            page.Staging.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0) - page.Fit.center * page.Scale;
        }
        public void Present(DsJournalToken token, object clone)
        {
            var page = (LoadoutPage)clone; Current(token); AssertInputBlocked(page);
            var pos = page.Origin; pos.y = page.Offset; page.Scroll.transform.localPosition = pos;
            Force(page); Fit(page); RouteOwned(page.Root); Suppress(page.Root, false); page.Visible.Clear();
            var view = InSpace(page.View, page.Scroll.transform.parent, null);
            foreach (var entry in page.Entries)
            {
                if (!entry.gameObject.activeInHierarchy) continue;
                bool fits = true;
                foreach (var renderer in entry.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy) fits &= Contains(view, renderer.bounds) && Inside(page, renderer.bounds);
                if (fits) page.Visible.Add(entry);
            }
            foreach (var entry in page.Entries)
            {
                bool rowFits = page.Visible.Contains(entry);
                foreach (var other in page.Entries)
                    if (other.gameObject.activeInHierarchy && Mathf.Abs(other.transform.localPosition.y - entry.transform.localPosition.y) < .01f)
                        rowFits &= page.Visible.Contains(other);
                if (!rowFits) page.Visible.Remove(entry);
                Suppress(entry.gameObject, !rowFits);
            }
            if (page.Selected is InventoryItemTool selected && !page.Visible.Contains(selected)) ClearSelection(page);
            Contain(page); Current(token);
        }
        void Contain(LoadoutPage page)
        {
            var view = InSpace(page.View, page.Scroll.transform.parent, null); bool cursorFits = page.Selected != null;
            foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
                if (Within(renderer.transform, page.Cursor.transform))
                { if (renderer.enabled && renderer.gameObject.activeInHierarchy) cursorFits &= Inside(page, renderer.bounds) && (!(page.Selected is InventoryItemTool) || Contains(view, renderer.bounds)); }
                else if (!Within(renderer.transform, page.Scroll.transform)) renderer.forceRenderingOff = !Inside(page, renderer.bounds);
            Suppress(page.Cursor.gameObject, !cursorFits);
            // Missing required custom art hides only that native crest subtree.
            foreach (var crest in page.SourceCrests.Keys)
                if (!page.CrestVisualReady.Contains(crest)) Suppress(crest.gameObject, true);
        }
        bool ActionBase(LoadoutPage page, InventoryItemToolBase entry)
        {
            return page != null && !page.Released && IsCurrent(page.Token) && entry != null &&
                ReferenceEquals(page.Selected, entry) && Within(entry.transform, page.Root.transform) && entry.gameObject.activeInHierarchy &&
                !page.Manager.enabled && page.Manager.CurrentSelected == null && !page.Manager.IsActionsBlocked &&
                !((InventoryItemToolManager)page.Token.List).IsActionsBlocked &&
                !page.Crests.IsSwitchingCrests && page.Manager.EquipState == InventoryItemToolManager.EquipStates.None &&
                !(bool)Get(page.Manager, "refreshCurrentSelected") &&
                !CollectableItemManager.IsInHiddenMode();
        }
        bool ExtraLegal(LoadoutPage page, InventoryItemToolBase entry, ToolItem item, bool reload)
        {
            if (!ActionBase(page, entry) || page.Crests.IsBlocked || item == null || !ReferenceEquals(entry.ItemData, item) ||
                !ReferenceEquals(ToolItemManager.GetToolByName(item.name), item) || !item.IsUnlockedNotHidden ||
                !page.Manager.CanChangeEquips()) return false;
            if (entry is InventoryItemTool tool && !page.Visible.Contains(tool)) return false;
            if (entry is InventoryToolCrestSlot slot)
            {
                if (slot.Crest == null) { if (!FloatingCurrent(page) || !page.FloatingSlots.ContainsKey(slot)) return false; }
                else
                {
                    if (slot.Crest != page.Crests.CurrentCrest || !page.SourceCrests.TryGetValue(slot.Crest, out var source)) return false;
                    VerifyCrestAuthority(page, slot.Crest, source);
                }
            }
            return reload ? item.ReplenishUsage == ToolItem.ReplenishUsages.OneForOne && item.CanReload() :
                item.DisplayTogglePrompt && item.CanToggle;
        }
        static object ToolNative(InventoryItemToolBase entry, string method, params object[] args) =>
            typeof(InventoryItemToolBase).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, args);
        static void ActionAudio(InventoryItemToolBase entry)
        {
            var audio = Get(entry, "reloadAudioSource") as AudioSource;
            if (audio == null || audio.spatialBlend != 0f || !Within(audio.transform, entry.transform))
                throw new InvalidOperationException("Loadout native reload/toggle audio must be owned and nonspatial");
        }
        static void ValidateLoadoutReferences(Component component, Transform local, GameObject template, GameObject cursor)
        {
            for (Type type = component.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(Component); type = type.BaseType)
                foreach (var field in type.GetFields(Fields))
                {
                    if (field.IsStatic || field.IsNotSerialized || (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true))) continue;
                    object value = field.GetValue(component);
                    if (component is InventoryItemManager && field.Name == "templateItem")
                    { if (AsTransform(value) != template.transform) throw new InvalidOperationException("Loadout template escaped donor"); continue; }
                    if (component is InventoryItemManager && field.Name == "cursorPrefab")
                    { if (AsTransform(value) != cursor.transform) throw new InvalidOperationException("Loadout cursor escaped donor"); continue; }
                    if (component is InventoryCursor && field.Name == "audioSourcePrefab") continue; // Owned cursor is muted before Awake.
                    if (component is InventoryItemToolBase && field.Name == "changeEffectPrefab") continue; // Selected owned factory, never native pooling.
                    if (component is InventoryToolCrestSlot && field.Name == "unlockBurstEffectPrefab")
                    { if (value is PassColour burst) InspectEffect(burst.gameObject); continue; }
                    CheckValue(field.Name, value, local);
                }
        }
        static void InspectEffectComponent(Component component, Transform root)
        {
            if (component is PassColour colour)
            {
                var targets = Get(colour, "passTo") as Array;
                if (targets == null || targets.Length > 4096) throw new InvalidOperationException("Loadout effect colour targets unavailable");
                foreach (var target in targets)
                {
                    if (target == null) throw new InvalidOperationException("Loadout effect colour target missing");
                    foreach (string field in new[] { "Sprite", "tk2dSprite", "ParticleSystem" }) CheckValue(field, Get(target, field), root);
                }
            }
            if (component is PlayParticleEffects particles)
            {
                if ((bool)Get(particles, "deparentOnPlay") || (bool)Get(particles, "useCollider"))
                    throw new InvalidOperationException("Loadout effect has external parent/physics authority");
                ReadEvent(particles.OnPlay, root); ReadEvent(particles.OnStop, root);
            }
            if (component is ParticleSystem system)
            {
                if (system.main.simulationSpace != ParticleSystemSimulationSpace.Local || system.collision.enabled || system.trigger.enabled || system.lights.enabled)
                    throw new InvalidOperationException("Loadout particle effect requires local nonphysical simulation");
                var children = system.subEmitters;
                for (int i = 0; i < children.subEmittersCount; i++)
                    if (!Within(children.GetSubEmitterSystem(i).transform, root)) throw new InvalidOperationException("Loadout subemitter escaped effect");
                var shape = system.shape;
                if ((shape.meshRenderer != null && !Within(shape.meshRenderer.transform, root)) ||
                    (shape.skinnedMeshRenderer != null && !Within(shape.skinnedMeshRenderer.transform, root)))
                    throw new InvalidOperationException("Loadout particle shape escaped effect");
            }
            if (component is Animator animator && animator.runtimeAnimatorController != null)
            {
                if (animator.GetBehaviours<StateMachineBehaviour>().Length != 0)
                    throw new InvalidOperationException("Loadout effect animator has unadmitted state callbacks");
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    if (clip != null && clip.events.Length != 0) throw new InvalidOperationException("Loadout effect animation has unadmitted events");
            }
            if (component is AudioSource audio && audio.spatialBlend != 0f)
                throw new InvalidOperationException("Loadout effect audio is not native UI audio");
        }
        static void InspectEffect(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Loadout effect missing native script");
                var type = component.GetType();
                if (!ComponentAllowed(type, "visual") && type != typeof(Animator) && type != typeof(ParticleSystem) &&
                    type != typeof(ParticleSystemRenderer) && type != typeof(PassColour) && type != typeof(PlayParticleEffects) && type != typeof(AudioSource))
                    throw new InvalidOperationException("Loadout effect unadmitted component " + type.FullName);
                ValidateSerializedReferences(component, root.transform, root, root);
                InspectEffectComponent(component, root.transform);
                if (component is PlayerDataTestResponse response) { ReadEvent(response.IsFullfilled, root.transform); ReadEvent(response.IsNotFulfilled, root.transform); }
            }
            ValidateBridgeCatalogue(root);
        }
        void PrepareParticleCallbacks(LoadoutPage page, GameObject root)
        {
            foreach (var particles in root.GetComponentsInChildren<PlayParticleEffects>(true))
            {
                if (page.ParticleCallbacks.ContainsKey(particles)) continue;
                if (particles.gameObject.activeInHierarchy) throw new InvalidOperationException("Loadout particle callbacks require inactive factory ownership");
                page.ParticleCallbacks.Add(particles, particles.enabled);
                // Native OnEnable/OnDisable both enter a creating global singleton.
                // Keep native Awake/play/stop/fade logic, but own its exact OnUpdate
                // dispatch once per frame rather than acquiring that shared hook.
                particles.enabled = false;
                particles.OnPlay = ReconstructEvent(particles.OnPlay, root.transform);
                particles.OnStop = ReconstructEvent(particles.OnStop, root.transform);
            }
        }
        void TickParticles(LoadoutPage page)
        {
            if (page.ParticleFrame == Time.frameCount || !page.Root.activeInHierarchy || !IsCurrent(page.Token)) return;
            page.ParticleFrame = Time.frameCount;
            Current(page.Token);
            _actions.Run(() =>
            {
                foreach (var pair in page.ParticleCallbacks)
                {
                    var particles = pair.Key;
                    if (particles == null || particles.enabled || !Within(particles.transform, page.Staging.transform))
                        throw new InvalidOperationException("Loadout owned particle callback escaped or rearmed");
                    if (pair.Value && particles.gameObject.activeInHierarchy)
                        typeof(PlayParticleEffects).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(particles, null);
                    Current(page.Token);
                }
            });
        }
        GameObject PrepareActionEffect(LoadoutPage page, InventoryItemToolBase entry)
        {
            var prefab = Get(entry, "changeEffectPrefab") as GameObject;
            if (prefab == null) return null;
            if (page.ActionEffects.TryGetValue(entry, out var previous))
            {
                if (!ReferenceEquals(page.ActionEffectSources[entry], prefab) || previous == null)
                    throw new InvalidOperationException("Loadout retained action effect source changed");
                return previous;
            }
            if (page.ActionEffects.Count >= 4096) throw new InvalidOperationException("Loadout action effects exceed owned factory bound");
            InspectEffect(prefab);
            var staging = new GameObject("DsPortActionEffectStaging"); staging.SetActive(false);
            staging.transform.SetParent(page.Staging.transform, false);
            var effect = Object.Instantiate(prefab, staging.transform, false);
            page.ActionEffects.Add(entry, effect); page.ActionEffectSources.Add(entry, prefab);
            InspectEffect(effect); Inventory.PrepareAuxiliary(effect); page.Text.Prepare(effect, page.Token.Content);
            PrepareParticleCallbacks(page, effect);
            foreach (var response in effect.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                response.IsFullfilled = ReconstructEvent(response.IsFullfilled, effect.transform);
                response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, effect.transform);
            }
            return effect;
        }
        void ShowActionEffect(LoadoutPage page, GameObject effect, InventoryItemToolBase entry)
        {
            if (effect == null) return;
            effect.SetActive(false);
            effect.transform.SetParent(entry.transform, false);
            effect.transform.localPosition = Vector3.zero;
            effect.transform.localRotation = Quaternion.Inverse(entry.transform.rotation); // Native spawned effect's world orientation.
            RouteOwned(effect); effect.SetActive(true);
            foreach (var response in effect.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null);
                Current(page.Token);
            }
        }
        bool RefreshActionPage(LoadoutPage page)
        {
            if (_actions.Pending) return false;
            var after = Capture();
            if (!_state.RefreshAfterAction(page, page.Token, after)) return false;
            page.Token = after; _current = after; _failed = null;
            AssertInputBlocked(page);
            return true;
        }
        bool Toggle(LoadoutPage page)
        {
            bool result = false;
            _actions.Run(() => result = ToggleCore(page));
            return result;
        }
        bool ToggleCore(LoadoutPage page)
        {
            var entry = page.Selected as InventoryItemToolBase; var item = entry != null ? entry.ItemData : null;
            if (!ExtraLegal(page, entry, item, false)) return false;
            ActionAudio(entry);
            var effect = PrepareActionEffect(page, entry);
            var animator = Get(entry, "reloadFailAnimator") as Animator;
            if (animator != null) InspectEffectComponent(animator, entry.transform);
            if (!ExtraLegal(page, entry, item, false)) return false;
            // Native InventoryItemToolBase "DoExtraPress" dispatch, with only its
            // global pooled visual replaced by the already-owned native prefab.
            if (!page.Manager.CanChangeEquips(item.Type, InventoryItemToolManager.CanChangeEquipsTypes.Transform) ||
                !ExtraLegal(page, entry, item, false) || !item.DoToggle(out bool changedVisually)) return false;
            typeof(InventoryItemUpdateable).GetMethod("UpdateDisplay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, null);
            page.Manager.RefreshTools();
            if (!RefreshActionPage(page)) { Invalidate(); return true; }
            if (changedVisually)
            {
                if (animator != null) { animator.enabled = true; animator.SetTrigger(Animator.StringToHash("Change")); }
                ShowActionEffect(page, effect, entry);
                item.PlayToggleAudio((AudioSource)Get(entry, "reloadAudioSource"));
            }
            page.Manager.SetDisplay(entry); Current(page.Token); Present(page.Token, page);
            return true;
        }
        bool StartReload(LoadoutPage page)
        {
            var entry = page.Selected as InventoryItemToolBase; var item = entry != null ? entry.ItemData : null;
            long generation = _hold.Capture();
            if (!TouchHeld(generation) || !ExtraLegal(page, entry, item, true)) return false;
            ActionAudio(entry);
            page.ActionEntry = entry; page.ActionItem = item; page.ActionGeneration = generation; page.ActionEnabled = entry.enabled;
            entry.enabled = false; // Only the guarded native Update may advance its timer.
            if (!_hold.Step(generation, () => ExtraLegal(page, entry, item, true), () => _actions.Run(() => entry.Extra())))
            { CancelAction(page); return false; }
            _suppressReleaseTap = true;
            return true;
        }
        bool UnlockLegal(LoadoutPage page, InventoryToolCrestSlot slot)
        {
            if (!ActionBase(page, slot) || slot.Crest == null || slot.Crest != page.Crests.CurrentCrest ||
                !page.CrestVisualReady.Contains(slot.Crest) || slot.EquippedItem != null || !slot.IsLocked ||
                !page.SourceCrests.TryGetValue(slot.Crest, out var source)) return false;
            VerifyCrestAuthority(page, slot.Crest, source);
            if (!slot.Crest.IsUnlocked || slot.Crest.IsHidden || !source.IsVisible || !page.Manager.CanUnlockSlot ||
                !ReferenceEquals(page.Manager.SlotUnlockItem, ((InventoryItemToolManager)page.Token.List).SlotUnlockItem)) return false;
            if (page.UnlockStarted)
                return page.Crests.IsBlocked && ReferenceEquals(Get(slot, "unlockHoldRoutine"), page.UnlockHandle) &&
                    ReferenceEquals(Get(slot, "onUnlockHoldEnd"), page.UnlockEnd);
            return !page.Crests.IsBlocked && Get(slot, "onUnlockHoldEnd") == null;
        }
        bool StartUnlockHold(LoadoutPage page, InventoryToolCrestSlot slot)
        {
            long generation = _hold.Capture();
            if (!TouchHeld(generation) || !UnlockLegal(page, slot)) return false;
            float duration = (float)Get(slot, "unlockHoldDuration");
            if (!DsJournalGeometry.Finite(duration) || duration <= 0f)
                throw new InvalidOperationException("Loadout native unlock must have a positive finite hold duration");
            InspectPrompts(page.Manager, page.Root.transform);
            var audioType = typeof(GlobalSettings.GlobalSettingsBase<GlobalSettings.Audio>);
            var audio = audioType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (audio == null || audioType.GetField("_foundInstance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as bool? != true ||
                !(Get(audio, "defaultUIAudioSourcePrefab") is AudioSource prefab) || prefab.spatialBlend != 0f)
                throw new InvalidOperationException("Loadout native unlock needs resident nonspatial UI audio");
            if (!UnlockLegal(page, slot) || Get(slot, "unlockHoldRoutine") != null) return false;
            page.ActionEntry = slot; page.ActionGeneration = generation; page.ActionEnabled = slot.enabled;
            var native = (IEnumerator)typeof(InventoryToolCrestSlot).GetMethod("UnlockHoldRoutine", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(slot, null);
            page.UnlockRoutine = new DsPortGuardedRoutine(() => TouchHeld(generation) && UnlockLegal(page, slot),
                UnlockSteps(page, slot, native), () => CancelUnlock(page, slot));
            // The first frame yields without advancing native work. Bind the exact
            // native cancellation handle before its first shake/audio/wait step.
            page.UnlockHandle = slot.StartCoroutine(RunUnlock(page));
            Set(slot, "unlockHoldRoutine", page.UnlockHandle);
            _suppressReleaseTap = true;
            return true;
        }
        IEnumerator UnlockSteps(LoadoutPage page, InventoryToolCrestSlot slot, IEnumerator native)
        {
            while (true)
            {
                bool more;
                try { more = native.MoveNext(); }
                finally
                {
                    page.UnlockEnd = Get(slot, "onUnlockHoldEnd");
                    page.UnlockStarted |= page.UnlockEnd != null;
                }
                if (!more)
                {
                    page.UnlockFinished = true;
                    // Native MoveNext has returned: do not replay its payment or
                    // retire its burst from inside the native action callback.
                    if (!RefreshActionPage(page)) _nextContentProbe = 0;
                    yield break;
                }
                yield return native.Current; // Preserve the exact native WaitForSecondsRealtime.
            }
        }
        IEnumerator RunUnlock(LoadoutPage page)
        {
            yield return null;
            while (true)
            {
                bool more = false;
                _actions.Run(() => more = page.UnlockRoutine.MoveNext());
                if (!more) yield break;
                yield return page.UnlockRoutine.Current;
            }
        }
        void CancelUnlock(LoadoutPage page, InventoryToolCrestSlot slot)
        {
            if (page.UnlockFinished) return;
            if (page.UnlockStarted)
            {
                if (!ReferenceEquals(Get(slot, "unlockHoldRoutine"), page.UnlockHandle))
                    throw new InvalidOperationException("Loadout native unlock cancellation handle changed or partially retired");
                slot.SubmitReleased(); // Native cancellation, never its commit or a synthetic saved flag.
            }
            else
            {
                if (page.UnlockHandle != null) slot.StopCoroutine(page.UnlockHandle);
                if (ReferenceEquals(Get(slot, "unlockHoldRoutine"), page.UnlockHandle)) Set(slot, "unlockHoldRoutine", null);
            }
        }
        void CancelAction(LoadoutPage page)
        {
            if (_actions.Active) { _actions.DeferRetirement(); return; }
            var entry = page.ActionEntry;
            if (entry == null) return;
            if (page.UnlockRoutine != null)
            {
                page.UnlockRoutine.Dispose();
                if (page.UnlockHandle != null) entry.StopCoroutine(page.UnlockHandle);
            }
            else
            {
                // ExtraReleased can toggle on an early release. Cancellation must
                // only stop the native reload effect and retire its owned timer.
                ToolNative(entry, "SetReloading", false);
                Set(entry, "holdTimerLeft", 0f);
            }
            entry.enabled = page.ActionEnabled;
            page.ActionEntry = null; page.ActionItem = null; page.UnlockRoutine = null; page.UnlockHandle = null;
            page.UnlockEnd = null; page.UnlockStarted = page.UnlockFinished = false;
            _nextContentProbe = 0;
        }
        bool TickAction(LoadoutPage page)
        {
            var entry = page.ActionEntry;
            if (!TouchHeld(page.ActionGeneration)) { CancelAction(page); return false; }
            if (page.UnlockRoutine != null)
            {
                if (page.UnlockFinished || !UnlockLegal(page, (InventoryToolCrestSlot)entry)) { CancelAction(page); return false; }
                Present(page.Token, page); return true; // Unity alone resumes the native wait.
            }
            if (!ExtraLegal(page, entry, page.ActionItem, true)) { CancelAction(page); return false; }
            if (!_hold.Step(page.ActionGeneration, () => ExtraLegal(page, entry, page.ActionItem, true), () => _actions.Run(() => ToolNative(entry, "Update"))))
            { CancelAction(page); return false; }
            // Only this synchronous guarded native step can refresh the snapshot.
            // External resource/equipment changes before a step fail ExtraLegal.
            if (!RefreshActionPage(page)) { CancelAction(page); return false; }
            if (!ExtraLegal(page, entry, page.ActionItem, true)) { CancelAction(page); return false; }
            page.Manager.SetDisplay(entry); Current(page.Token);
            Present(page.Token, page); return true;
        }
        public bool OnGesture(DsGesture gesture)
        {
            if (_actions.Active) return true;
            if (_actions.Pending) { Invalidate(); return true; }
            var page = _state.Owned as LoadoutPage;
            if (!_state.Ready || page == null) return false;
            var host = (RectTransform)page.Token.Host; Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, gesture.Position, DsPresentation.UiCamera, out point) || !host.rect.Contains(point)) return false;
            try
            {
                if (!IsCurrent(page.Token)) { _state.Clear(); return true; }
                if (gesture.Type == DsGestureType.Down)
                {
                    if (HitNative(page, Get(page.Manager, "reloadPrompt") as Component, host, point)) return StartReload(page);
                    if (page.Selected is InventoryToolCrestSlot locked && locked.IsLocked && locked.EquippedItem == null &&
                        (HitNative(page, locked, host, point) || HitNative(page, Get(page.Manager, "slotUnlockDescExtra") as Component, host, point)))
                        return StartUnlockHold(page, locked);
                    return false;
                }
                if (gesture.Type == DsGestureType.Tap && _suppressReleaseTap) { _suppressReleaseTap = false; return true; }
                if (page.ActionEntry != null) { CancelAction(page); return true; }
                if (gesture.Type == DsGestureType.Drag)
                {
                    page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y,
                        page.Offset + DsJournalGeometry.LocalDrag(gesture.Delta.y, page.Scale));
                    ClearSelection(page); Present(page.Token, page); return true;
                }
                if (gesture.Type != DsGestureType.Tap) return false;
                if (HitNative(page, Get(page.Manager, "customTogglePrompt") as Component, host, point)) return Toggle(page);
                if (page.Selected is InventoryToolCrest && HitNative(page, Get(page.Manager, "selectCrestPrompt") as Component, host, point))
                { _selection.TrySubmit(); return true; }
                if (HitNative(page, Get(page.Manager, "equipPrompt") as Component, host, point))
                {
                    if (page.Selected is InventoryToolCrest) return _selection.TrySubmit();
                    if (page.Selected is InventoryToolCrestSlot unequip) { page.TargetSlot = unequip; page.PendingTool = null; return _selection.TrySubmit(); }
                    if (page.Selected is InventoryItemTool selectedTool)
                    {
                        page.PendingTool = selectedTool.ItemData;
                        var equippedSlot = page.Crests.GetEquippedToolSlot(page.PendingTool);
                        if (equippedSlot == null && FloatingCurrent(page)) equippedSlot = page.Floating.GetEquippedToolSlot(page.PendingTool);
                        if (equippedSlot != null) { page.TargetSlot = equippedSlot; page.PendingTool = null; return _selection.TrySubmit(); }
                        // Slot choice is a subsequent explicit tap, never an automatic replacement.
                        return true;
                    }
                }
                foreach (var slot in AllSlots(page))
                    if (HitNative(page, slot, host, point))
                    {
                        if (page.PendingTool != null) { page.TargetSlot = slot; return _selection.TrySubmit(); }
                        if (ReferenceEquals(page.Selected, slot) && slot.IsLocked && slot.EquippedItem != null)
                        { page.TargetSlot = slot; return _selection.TrySubmit(); }
                        return _selection.Select(page, slot, page.Token.Data);
                    }
                if (HitNative(page, Get(page.Manager, "changeCrestButton") as Component, host, point))
                {
                    var candidates = page.Root.GetComponentsInChildren<InventoryToolCrest>(true);
                    int current = Array.IndexOf(candidates, page.Crests.CurrentCrest);
                    for (int i = 1; i <= candidates.Length; i++)
                    {
                        var crest = candidates[(current + i + candidates.Length) % candidates.Length];
                        if (crest.CrestData == null || !crest.IsUnlocked || crest.IsHidden) continue;
                        foreach (var other in candidates)
                        { other.gameObject.SetActive(other == crest); if (other == crest) other.Show(true, true); }
                        typeof(InventoryToolCrestList).GetMethod("SetCurrentCrest", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(page.Crests, new object[] { crest, false, false });
                        Current(page.Token);
                        if (!EnsureCrestVisual(page, crest)) { Present(page.Token, page); return true; }
                        return _selection.Select(page, crest, page.Token.Data);
                    }
                    return true;
                }
                foreach (var entry in page.Visible)
                {
                    var b = InSpace(entry.GetComponent<BoxCollider2D>().bounds, null, host);
                    if (point.x >= b.min.x && point.x <= b.max.x && point.y >= b.min.y && point.y <= b.max.y) return _selection.Select(page, entry, page.Token.Data);
                }
                return false;
            }
            catch (Exception e) { _failed = DsPortPageSnapshot.IsReadFailure(e) ? null : page.Token; _state.Clear(); Report(e.GetBaseException().Message); return true; }
        }
        bool HitNative(LoadoutPage page, Component component, Transform host, Vector2 point)
        {
            if (component == null || !component.gameObject.activeInHierarchy || !Within(component.transform, page.Root.transform)) return false;
            foreach (var renderer in component.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer.forceRenderingOff || !Inside(page, renderer.bounds)) continue;
                var b = InSpace(renderer.bounds, null, host);
                if (point.x >= b.min.x && point.x <= b.max.x && point.y >= b.min.y && point.y <= b.max.y) return true;
            }
            return false;
        }
        bool IDsPortSelection.IsCurrent(object owner, object item, object data)
        {
            var page = owner as LoadoutPage; var entry = item as InventoryItemSelectable;
            if (page == null || page.Released || entry == null || !IsCurrent(page.Token) || !ReferenceEquals(data, page.Token.Data) ||
                !Within(entry.transform, page.Root.transform) || !entry.gameObject.activeInHierarchy) return false;
            if (entry is InventoryItemTool tool) return tool.ItemData != null && page.Visible.Contains(tool);
            return entry is InventoryToolCrest || entry is InventoryToolCrestSlot;
        }
        sealed class ExtraBinding
        {
            public InventoryItemToolBase Template;
            public InventoryItemExtraDescription Condition;
            public Transform Destination;
        }
        static Transform MapDetailDestination(Transform source, Transform owned, Transform target)
        {
            if (!Within(target, source)) throw new InvalidOperationException("Loadout detail target escaped its exact source scope");
            var indices = new Stack<int>();
            for (var node = target; node != source; node = node.parent) indices.Push(node.GetSiblingIndex());
            while (indices.Count != 0) owned = owned.GetChild(indices.Pop());
            return owned;
        }
        ExtraBinding BindExtra(LoadoutPage page, InventoryItemSelectable entry)
        {
            var binding = new ExtraBinding(); InventoryToolCrest sourceCrest = null, ownedCrest = null;
            if (entry is InventoryItemTool) binding.Template = Get(page.Token.List, "templateItem") as InventoryItemTool;
            else if (entry is InventoryToolCrestSlot slot)
            {
                if (slot.Crest == null)
                {
                    if (!FloatingCurrent(page) || !page.FloatingSlots.TryGetValue(slot, out var retained))
                        throw new InvalidOperationException("Loadout selected floating detail source changed");
                    binding.Template = retained.SourceSlot;
                }
                else
                {
                    ownedCrest = slot.Crest;
                    if (!page.SourceCrests.TryGetValue(ownedCrest, out var crest))
                        throw new InvalidOperationException("Loadout selected crest detail source missing");
                    VerifyCrestAuthority(page, ownedCrest, crest);
                    var sourceList = Get(page.Token.List, "crestList") as InventoryToolCrestList;
                    sourceCrest = sourceList != null ? Get(sourceList, "templateCrest") as InventoryToolCrest : null;
                    var slots = sourceCrest != null ? Get(sourceCrest, "templateSlots") as InventoryToolCrestSlot[] : null;
                    if (slots == null || (int)slot.Type < 0 || (int)slot.Type >= slots.Length)
                        throw new InvalidOperationException("Loadout selected crest detail template missing");
                    // Exact native InventoryToolCrest.Setup selection, not the grid tool donor.
                    binding.Template = slots[(int)slot.Type];
                }
            }
            if (binding.Template == null) throw new InvalidOperationException("Loadout selected detail template unavailable");
            var conditions = binding.Template.GetComponents<InventoryItemExtraDescription>();
            if (conditions.Length > 1) throw new InvalidOperationException("Loadout selected detail condition ambiguous");
            if (conditions.Length == 0) return binding; // Native ToolBase.Awake has no extra-description route.
            binding.Condition = conditions[0];
            var target = Get(binding.Condition, "descSectionParent") as Transform;
            if (target == null) return binding;
            if (Within(target, binding.Template.transform))
                binding.Destination = MapDetailDestination(binding.Template.transform, entry.transform, target);
            else if (sourceCrest != null && Within(target, sourceCrest.transform))
                binding.Destination = MapDetailDestination(sourceCrest.transform, ownedCrest.transform, target);
            else binding.Destination = OwnedCopy(page, target);
            return binding;
        }
        void RequireExtraBinding(LoadoutPage page, InventoryItemSelectable entry, ToolItem item, ExtraBinding retained)
        {
            Current(page.Token);
            var current = BindExtra(page, entry);
            if (!ReferenceEquals((entry as InventoryItemToolBase)?.ItemData, item) ||
                !ReferenceEquals(current.Template, retained.Template) || !ReferenceEquals(current.Condition, retained.Condition) ||
                !ReferenceEquals(current.Destination, retained.Destination) || current.Condition == null || !current.Condition.WillDisplay)
                throw new InvalidOperationException("Loadout selected native detail authority replaced");
        }
        bool TryDisplayExtra(LoadoutPage page, InventoryItemSelectable entry)
        {
            ClearExtra(page);
            var item = (entry as InventoryItemToolBase)?.ItemData;
            if (item == null || !item.IsUnlockedNotHidden || item.ExtraDescriptionSection == null) return true;
            var binding = BindExtra(page, entry);
            if (binding.Condition == null || !binding.Condition.WillDisplay) return true;
            var prefab = item.ExtraDescriptionSection;
            var setupOwner = Inventory.ExtraSetupOwner(item);
            string problem = binding.Destination == null ? "native descSectionParent missing" : Inventory.AuxiliaryProblem(prefab, setupOwner);
            if (item is ToolItemStatesLiquid liquid && !liquid.HasInfiniteRefills && liquid.RefillsMax <= 0)
                throw new InvalidOperationException("Loadout native liquid meter maximum invalid");
            if (problem != null) throw new InvalidOperationException(problem);
            page.DetailStaging = new GameObject("DsPortLoadoutDetail");
            page.DetailStaging.SetActive(false); page.DetailStaging.transform.SetParent(page.Staging.transform, false);
            var detail = Object.Instantiate(prefab, page.DetailStaging.transform, false); page.DetailRoot = detail;
            detail.SetActive(false);
            problem = Inventory.AuxiliaryProblem(detail, setupOwner);
            if (problem != null) throw new InvalidOperationException(problem);
            Inventory.PrepareAuxiliary(detail);
            Inventory.OwnExtraMaterials(detail, page.DetailMaterials);
            page.DetailText.Prepare(detail, page.Token.Content);
            RequireExtraBinding(page, entry, item, binding);
            detail.transform.SetParent(binding.Destination, false);
            detail.SetActive(true); RequireExtraBinding(page, entry, item, binding);
            foreach (var response in detail.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                response.IsFullfilled = ReconstructEvent(response.IsFullfilled, detail.transform);
                response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, detail.transform);
                typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null);
                Current(page.Token);
            }
            RequireExtraBinding(page, entry, item, binding);
            if (!ReferenceEquals(item.ExtraDescriptionSection, prefab) || Inventory.ExtraSetupOwner(item) != setupOwner)
                throw new InvalidOperationException("Loadout native extra setup authority replaced");
            item.SetupExtraDescription(detail); // Exact SavedItem no-op or native LiquidMeter.SetDisplay; never a seen/refill setter.
            RequireExtraBinding(page, entry, item, binding);
            page.DetailText.Validate(detail);
            return true;
        }
        static void ClearExtra(LoadoutPage page)
        {
            if (page.DetailRoot != null)
            {
                foreach (var driver in page.DetailRoot.GetComponentsInChildren<MonoBehaviour>(true)) driver.StopAllCoroutines();
                page.DetailRoot.SetActive(false);
                Object.DestroyImmediate(page.DetailRoot); page.DetailRoot = null;
            }
            if (page.DetailStaging != null) { Object.DestroyImmediate(page.DetailStaging); page.DetailStaging = null; }
            page.DetailText.Clear();
            // Native text/submeshes must finish destruction before their private materials.
            while (page.DetailMaterials.Count != 0)
            {
                int last = page.DetailMaterials.Count - 1;
                if (page.DetailMaterials[last] != null) Object.DestroyImmediate(page.DetailMaterials[last]);
                page.DetailMaterials.RemoveAt(last);
            }
        }
        void PrepareSocketIcon(LoadoutPage page)
        {
            Current(page.Token);
            var resource = page.Manager.SlotUnlockItem;
            var entry = page.Manager.SlotUnlockItemDisplay;
            if (page.SocketResource != null && !ReferenceEquals(page.SocketResource, resource))
                throw new InvalidOperationException("Loadout socket resource owner changed");
            if ((bool)Get(entry, "isSelected") || Get(entry, "extraDesc") != null)
                throw new InvalidOperationException("Loadout socket icon has native selection/extra factory authority");
            if (resource.CustomInventoryDisplay != null && page.SocketIcon == null)
            {
                var prefab = resource.CustomInventoryDisplay;
                string problem = Inventory.AuxiliaryProblem(prefab.gameObject);
                if (problem != null) throw new InvalidOperationException("Loadout socket icon: " + problem);
                var staging = new GameObject("DsPortSocketIconStaging"); staging.SetActive(false);
                staging.transform.SetParent(page.Staging.transform, false);
                // Register the root before preparation. The page owns failures and
                // every native text/mesh lifetime, never a prefab-keyed global cache.
                var display = Object.Instantiate(prefab, staging.transform, false); page.SocketIcon = display;
                problem = Inventory.AuxiliaryProblem(display.gameObject);
                if (problem != null) throw new InvalidOperationException(problem);
                Inventory.PrepareAuxiliary(display.gameObject); page.Text.Prepare(display.gameObject, page.Token.Content);
                display.Owner = entry;
                display.transform.SetParent(entry.IconTransform, false);
                display.transform.localPosition = Vector3.zero; display.transform.localRotation = Quaternion.identity;
                display.transform.localScale = prefab.transform.localScale;
                Set(entry, "currentCustomDisplay", display);
                display.gameObject.SetActive(true); Current(page.Token);
            }
            // Exact native UpdateItemDisplay formatting, sharing Inventory's owned
            // icon adaptation. Never call the nonvirtual cache-entering Item setter.
            Set(entry, "item", resource); entry.gameObject.name = resource.name; page.SocketResource = resource;
            var sprite = Get(entry, "spriteRenderer") as SpriteRenderer;
            if (sprite != null) sprite.sprite = resource.CustomInventoryDisplay != null ? null : resource.GetIcon(CollectableItem.ReadSource.Inventory);
            var amount = Get(entry, "amountText") as PaneText;
            if (amount != null)
            {
                amount.text = (bool)Get(entry, "forceShowAmount") || resource.DisplayAmount ? resource.CollectedAmount.ToString() : string.Empty;
                amount.color = (Color)Get(entry, resource.IsAtMax() ? "maxAmountTextColor" : "regularAmountTextColor");
            }
            var prompt = Get(entry, "consumePrompt") as GameObject;
            if (prompt != null && (!resource.IsConsumable() || !resource.CanConsumeRightNow())) prompt.SetActive(false);
            typeof(InventoryItemUpdateable).GetMethod("UpdateDisplay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, null);
            Current(page.Token);
        }
        void DisplayLockedSlot(LoadoutPage page, InventoryToolCrestSlot locked)
        {
            Current(page.Token);
            // The native locked branch has no tool/reload/custom action. Preserve
            // its native base formatting and prompt drivers, replacing only the
            // socket resource Item assignment that would enter the global cache.
            page.Manager.SetDisplay(locked.gameObject);
            foreach (string field in new[] { "nameText", "descriptionText" })
            {
                var text = Get(page.Manager, field) as PaneText;
                if (text == null) continue;
                string value = field == "nameText" ? locked.DisplayName : locked.Description;
                text.text = (string)typeof(InventoryItemManager).GetMethod(field == "nameText" ? "FormatDisplayName" : "FormatDescription",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page.Manager, new object[] { value });
            }
            if (page.Manager.CanUnlockSlot)
            {
                PrepareSocketIcon(page);
                page.Manager.SocketUnlockInventoryDescription.SetSlotSprite(locked.Sprite, locked.SpriteTint);
                page.Manager.SocketUnlockInventoryDescription.gameObject.SetActive(true);
            }
            var equip = Get(page.Manager, "equipPrompt") as NestedFadeGroupBase;
            if (equip != null) equip.AlphaSelf = page.Manager.CanChangeEquips() && !page.Manager.IsHeroCursed ? 1f : (float)Get(page.Manager, "disabledListSectionOpacity");
            var label = Get(page.Manager, "equipPromptText") as PaneText;
            if (label != null) label.text = (TeamCherry.Localization.LocalisedString)Get(page.Manager, locked.Type == ToolItemType.Skill ? "equipSkillText" : "equipText");
            typeof(InventoryItemToolManager).GetMethod("UpdateButtonPrompts", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page.Manager, null);
            Current(page.Token);
        }
        void IDsPortSelection.Display(object owner, object item)
        {
            var page = (LoadoutPage)owner; var entry = (InventoryItemSelectable)item;
            Current(page.Token); AssertInputBlocked(page); ClearPrompts(page);
            page.PendingTool = null; page.TargetSlot = null;
            if (!DsPortDetailAttempt.Try(() => !page.Released && IsCurrent(page.Token),
                () => TryDisplayExtra(page, entry), () => ClearSelection(page),
                error => Report("Loadout selected detail unavailable: " + error.Message))) return;
            if (entry is InventoryToolCrestSlot locked && locked.IsLocked) DisplayLockedSlot(page, locked);
            else page.Manager.SetDisplay((InventoryItemSelectable)entry);
            Current(page.Token);
            // Native authored select prompt, without the switching setter that writes paneList.
            var crestPrompt = Get(page.Manager, "selectCrestPrompt") as NestedFadeGroupBase;
            if (crestPrompt != null)
            {
                crestPrompt.gameObject.SetActive(entry is InventoryToolCrest);
                crestPrompt.AlphaSelf = page.Manager.CanChangeEquips() && !page.Manager.IsHeroCursed ? 1f : .5f;
            }
            Force(page); Fit(page); RouteOwned(page.Root); NeutralizeOwnedClips(page.Root);
            var world = entry is InventoryItemTool ? InSpace(page.View, page.Scroll.transform.parent, null) :
                InSpace(new Bounds(((RectTransform)page.Token.Host).rect.center, ((RectTransform)page.Token.Host).rect.size), (Transform)page.Token.Host, null);
            page.Cursor.SetClampedPos(world.min, world.max);
            page.Cursor.Activate(); Current(page.Token); page.Cursor.SetTarget(entry.transform); Current(page.Token);
            page.Selected = entry; Contain(page);
        }
        static object LastFloatingConfig(Array configs, PlayerData data)
        {
            object selected = null;
            if (configs == null) throw new InvalidOperationException("Loadout floating configs unavailable");
            foreach (var config in configs)
            {
                var condition = Get(config, "Condition") as PlayerDataTest;
                if (condition == null || condition.TestGroups == null) throw new InvalidOperationException("Loadout floating condition unavailable");
                var authority = (PlayerDataBase)Get(condition, "playerDataOverride") ?? data;
                bool fulfilled = condition.TestGroups.Length == 0;
                foreach (var group in condition.TestGroups) if (group.IsFulfilled(authority)) { fulfilled = true; break; }
                if (fulfilled) selected = config; // Native Evaluate deliberately uses LAST matching config.
            }
            return selected;
        }
        static Transform OwnedCopy(LoadoutPage page, Transform source)
        {
            var root = ((InventoryPane)page.Token.Pane).transform;
            if (!Within(source, root)) throw new InvalidOperationException("Loadout serialized donor escaped source pane");
            var indices = new Stack<int>();
            for (var node = source; node != root; node = node.parent) indices.Push(node.GetSiblingIndex());
            var owned = page.Root.transform;
            while (indices.Count != 0) owned = owned.GetChild(indices.Pop());
            return owned;
        }
        void CaptureFloatingSlots(LoadoutPage page)
        {
            page.Floating = Get(page.Manager, "extraSlots") as InventoryFloatingToolSlots;
            page.SourceFloating = Get(page.Token.List, "extraSlots") as InventoryFloatingToolSlots;
            if (page.Floating == null || page.SourceFloating == null || OwnedCopy(page, page.SourceFloating.transform) != page.Floating.transform)
                throw new InvalidOperationException("Loadout floating native owner changed");
            page.FloatingConfigs = Get(page.Floating, "configs") as Array;
            page.SourceFloatingConfigs = Get(page.SourceFloating, "configs") as Array;
            if (page.FloatingConfigs == null || page.SourceFloatingConfigs == null || page.FloatingConfigs.Length != page.SourceFloatingConfigs.Length)
                throw new InvalidOperationException("Loadout floating config copy changed");
            page.FloatingConfig = Get(page.Floating, "currentConfig");
            page.SourceFloatingConfig = LastFloatingConfig(page.SourceFloatingConfigs, (PlayerData)page.Token.Data);
            if (!ReferenceEquals(LastFloatingConfig(page.FloatingConfigs, (PlayerData)page.Token.Data), page.FloatingConfig))
                throw new InvalidOperationException("Loadout floating config already stale");
            int selected = -1;
            for (int i = 0; i < page.FloatingConfigs.Length; i++)
                if (ReferenceEquals(page.FloatingConfigs.GetValue(i), page.FloatingConfig)) selected = i;
            if (selected < 0)
            {
                if (page.SourceFloatingConfig != null) throw new InvalidOperationException("Loadout floating native config missing from copy");
                return;
            }
            if (!ReferenceEquals(page.SourceFloatingConfigs.GetValue(selected), page.SourceFloatingConfig))
                throw new InvalidOperationException("Loadout floating source condition diverged");
            var ownedSlots = (Array)Get(page.FloatingConfig, "Slots");
            var sourceSlots = (Array)Get(page.SourceFloatingConfig, "Slots");
            if (ownedSlots.Length != sourceSlots.Length) throw new InvalidOperationException("Loadout floating descriptor count changed");
            var ids = new HashSet<string>();
            for (int i = 0; i < ownedSlots.Length; i++)
            {
                var descriptor = ownedSlots.GetValue(i); var sourceDescriptor = sourceSlots.GetValue(i);
                var slot = (InventoryToolCrestSlot)Get(descriptor, "SlotObject");
                var sourceSlot = (InventoryToolCrestSlot)Get(sourceDescriptor, "SlotObject");
                string id = (string)Get(descriptor, "Id"); var type = (ToolItemType)Get(descriptor, "Type");
                if (slot == null || sourceSlot == null || string.IsNullOrEmpty(id) || !ids.Add(id) ||
                    id != (string)Get(sourceDescriptor, "Id") || type != (ToolItemType)Get(sourceDescriptor, "Type") ||
                    OwnedCopy(page, sourceSlot.transform) != slot.transform)
                    throw new InvalidOperationException("Loadout floating source slot identity changed");
                page.FloatingSlots.Add(slot, new FloatingSlot { Descriptor = descriptor, SourceDescriptor = sourceDescriptor,
                    SourceSlot = sourceSlot, Id = id, Type = type, Getter = Get(slot, "getSavedDataOverride"), Setter = Get(slot, "setSavedDataOverride") });
            }
        }
        bool FloatingCurrent(LoadoutPage page)
        {
            if (!IsCurrent(page.Token) || page.Floating == null || page.SourceFloating == null || page.Manager.IsHeroCursed ||
                !page.Floating.gameObject.activeInHierarchy || !Within(page.Floating.transform, page.Root.transform) ||
                !ReferenceEquals(Get(page.Token.List, "extraSlots"), page.SourceFloating) ||
                !ReferenceEquals(Get(page.Manager, "extraSlots"), page.Floating) ||
                !ReferenceEquals(Get(page.Floating, "configs"), page.FloatingConfigs) ||
                !ReferenceEquals(Get(page.SourceFloating, "configs"), page.SourceFloatingConfigs) || page.FloatingConfig == null ||
                !ReferenceEquals(Get(page.Floating, "currentConfig"), page.FloatingConfig) ||
                !ReferenceEquals(LastFloatingConfig(page.FloatingConfigs, (PlayerData)page.Token.Data), page.FloatingConfig) ||
                !ReferenceEquals(LastFloatingConfig(page.SourceFloatingConfigs, (PlayerData)page.Token.Data), page.SourceFloatingConfig)) return false;
            var slots = (Array)Get(page.FloatingConfig, "Slots"); var sources = (Array)Get(page.SourceFloatingConfig, "Slots");
            if (slots.Length != page.FloatingSlots.Count || sources.Length != slots.Length) return false;
            for (int i = 0; i < slots.Length; i++)
            {
                var descriptor = slots.GetValue(i); var slot = (InventoryToolCrestSlot)Get(descriptor, "SlotObject");
                if (slot == null || !page.FloatingSlots.TryGetValue(slot, out var retained) ||
                    !ReferenceEquals(retained.Descriptor, descriptor) || !ReferenceEquals(retained.SourceDescriptor, sources.GetValue(i)) ||
                    !ReferenceEquals(Get(retained.SourceDescriptor, "SlotObject"), retained.SourceSlot) ||
                    retained.Id != (string)Get(descriptor, "Id") || retained.Id != (string)Get(retained.SourceDescriptor, "Id") ||
                    retained.Type != (ToolItemType)Get(descriptor, "Type") || retained.Type != (ToolItemType)Get(retained.SourceDescriptor, "Type") ||
                    !(slot.Crest == null && slot.SlotIndex == -1) || !slot.SlotInfo.Equals(new ToolCrest.SlotInfo { Type = retained.Type }) ||
                    !Within(slot.transform, page.Floating.transform) || !slot.gameObject.activeInHierarchy ||
                    retained.Getter == null || retained.Setter == null ||
                    !ReferenceEquals(Get(slot, "getSavedDataOverride"), retained.Getter) || !ReferenceEquals(Get(slot, "setSavedDataOverride"), retained.Setter) ||
                    !SaveCallback(slot, page.Floating, typeof(InventoryFloatingToolSlots))) return false;
            }
            return true;
        }
        IEnumerable<InventoryToolCrestSlot> AllSlots(LoadoutPage page)
        {
            if (page.Crests.CurrentCrest != null && page.CrestVisualReady.Contains(page.Crests.CurrentCrest))
                foreach (var slot in page.Crests.GetSlots()) yield return slot;
            if (FloatingCurrent(page)) foreach (var slot in page.Floating.GetSlots()) yield return slot;
        }
        static bool SaveCallback(InventoryToolCrestSlot slot, object owner, Type type)
        {
            var saved = Get(slot, "OnSetEquipSaved") as Action;
            if (saved == null || saved.GetInvocationList().Length != 1) return false;
            var callback = saved.GetInvocationList()[0];
            if (callback.Method.Name != "SaveEquips") return false;
            return ReferenceEquals(callback.Target, owner) && callback.Method.DeclaringType == type;
        }
        bool Legal(LoadoutPage page)
        {
            bool lockedRemoval = page.TargetSlot != null && page.TargetSlot.IsLocked &&
                page.TargetSlot.EquippedItem != null && page.PendingTool == null;
            if (!IsCurrent(page.Token) || page.Manager.enabled || page.Manager.CurrentSelected != null ||
                page.Manager.IsActionsBlocked || page.Crests.IsBlocked || page.Crests.IsSwitchingCrests ||
                page.Manager.EquipState != InventoryItemToolManager.EquipStates.None ||
                (!lockedRemoval && (!page.Manager.CanChangeEquips() || page.Manager.IsHeroCursed)) || CollectableItemManager.IsInHiddenMode() ||
                page.EquippedCrestId != ((PlayerData)page.Token.Data).CurrentCrestID) return false;
            var current = ToolItemManager.GetCrestByName(page.EquippedCrestId);
            if (current == null || current.IsHidden || !current.IsVisible) return false;
            if (page.Selected is InventoryToolCrest crest)
                return crest == page.Crests.CurrentCrest && page.CrestVisualReady.Contains(crest) && crest.CrestData != null && crest.CrestData.IsVisible && !crest.CrestData.IsHidden &&
                    ReferenceEquals(ToolItemManager.GetCrestByName(crest.CrestData.name), crest.CrestData) &&
                    page.Crests.CanChangeCrests() && crest.CrestData.name != page.EquippedCrestId;
            var slot = page.TargetSlot; var item = page.PendingTool;
            if (slot == null || !Within(slot.transform, page.Root.transform) || !slot.gameObject.activeInHierarchy ||
                !DsPortSlotAction.CanPlaceOrRemove(slot.IsLocked, slot.EquippedItem != null, item != null)) return false;
            if (slot.Crest == null)
            {
                if (!FloatingCurrent(page) || !page.FloatingSlots.ContainsKey(slot)) return false;
            }
            else
            {
                if (!page.SourceCrests.TryGetValue(slot.Crest, out var selectedSource) ||
                    !DsPortSlotAction.CanTargetCrest(page.Crests.CurrentCrest, slot.Crest, selectedSource, slot.Crest.CrestData,
                        slot.Crest.IsUnlocked, selectedSource.IsVisible, selectedSource.IsHidden) ||
                    !page.CrestVisualReady.Contains(slot.Crest) || !slot.Crest.HasSlot(slot) ||
                    !ReferenceEquals(ToolItemManager.GetCrestByName(selectedSource.name), selectedSource) ||
                    slot.SlotIndex < 0 || slot.SlotIndex >= selectedSource.Slots.Length ||
                    !slot.SlotInfo.Equals(selectedSource.Slots[slot.SlotIndex])) return false;
                // Native SaveEquips names this exact displayed crest, not the
                // globally equipped crest. Verify all callback peers before its
                // explicit slot action; the global owner ID above stays fenced.
                VerifyCrestAuthority(page, slot.Crest, selectedSource);
            }
            if (item != null && (!(page.Selected is InventoryItemTool selected) || !ReferenceEquals(selected.ItemData, item) ||
                !ReferenceEquals(ToolItemManager.GetToolByName(item.name), item) || !item.IsUnlockedNotHidden || item.Type != slot.Type ||
                InventoryItemToolManager.IsToolEquipped(item))) return false;
            return item != null || slot.EquippedItem != null;
        }
        bool RefreshActionSlots(LoadoutPage page)
        {
            var crest = page.TargetSlot.Crest;
            if (crest == null)
            {
                if (!FloatingCurrent(page)) return false;
                var floatingSlots = new List<InventoryToolCrestSlot>(page.Floating.GetSlots()).ToArray();
                ToolItem Saved(InventoryToolCrestSlot slot)
                {
                    string id = ((PlayerData)page.Token.Data).ExtraToolEquips.GetData(page.FloatingSlots[slot].Id).EquippedTool;
                    if (string.IsNullOrEmpty(id)) return null;
                    return ToolItemManager.GetToolByName(id) ?? throw new InvalidOperationException("Loadout unknown saved extra tool: " + id);
                }
                return DsPortSlotRefresh.Try(floatingSlots, page.TargetSlot,
                    slot => FloatingCurrent(page) && page.FloatingSlots.ContainsKey(slot), Saved, slot => slot.EquippedItem,
                    (slot, fresh) => slot.SetEquipped(fresh, isManual: false, refreshTools: false));
            }
            var slots = new List<InventoryToolCrestSlot>(crest.GetSlots()).ToArray();
            if (slots.Length != crest.CrestData.Slots.Length) return false;
            ToolItem CrestSaved(InventoryToolCrestSlot slot)
            {
                var equipped = ToolItemManager.GetEquippedToolsForCrest(crest.CrestData.name);
                if (equipped != null && equipped.Count > slots.Length) throw new InvalidOperationException("Loadout saved crest slot count changed");
                return equipped != null && slot.SlotIndex < equipped.Count ? equipped[slot.SlotIndex] : null;
            }
            return DsPortSlotRefresh.Try(slots, page.TargetSlot,
                slot => IsCurrent(page.Token) && slot.Crest == crest && slot.SlotIndex == Array.IndexOf(slots, slot) &&
                    Within(slot.transform, crest.transform) && SaveCallback(slot, crest, typeof(InventoryToolCrest)),
                CrestSaved, slot => slot.EquippedItem, (slot, fresh) => slot.SetEquipped(fresh, isManual: false, refreshTools: false));
        }
        bool IDsPortSelection.CanSubmit(object owner, object item) => Legal((LoadoutPage)owner);
        bool IDsPortSelection.Submit(object owner, object item)
        {
            var page = (LoadoutPage)owner;
            bool result = false;
            _actions.Run(() => result = SubmitCore(page, item));
            return result;
        }
        bool SubmitCore(LoadoutPage page, object item)
        {
            if (!ReferenceEquals(page.Selected, item) || !Legal(page)) return false;
            AssertInputBlocked(page);
            if (page.Selected is InventoryToolCrest crest)
            {
                Current(page.Token);
                // Native Update -> CanApplyCrest -> DoEquip -> StopSwitchingCrests -> SetCurrentCrest(doSave:true).
                // The intervening shell transitions mutate paneList/selection; only their native commit is used.
                ToolItemManager.SetEquippedCrest(crest.CrestData.name);
            }
            else
            {
                if (!RefreshActionSlots(page) || !Legal(page)) return false;
                var slot = page.TargetSlot;
                var pending = page.PendingTool;
                var equipped = slot.EquippedItem;
                // Native locked Submit's existing-item removal precedes its bench/
                // cursed equip gate. Empty locked slots are exclusively the hold route.
                if (!slot.IsLocked && !page.Manager.CanChangeEquips(slot.Type, InventoryItemToolManager.CanChangeEquipsTypes.Regular)) return false;
                if (!Legal(page)) return false;
                Current(page.Token);
                // These are distinct callback-bearing native operations. A successful
                // first operation is never replayed if its callback retires authority.
                if (pending == null)
                {
                    ToolItemManager.UnequipTool(equipped);
                    if (_actions.Pending || !RefreshActionPage(page) ||
                        !ReferenceEquals(page.Selected, item) || !ReferenceEquals(page.TargetSlot, slot) ||
                        !ReferenceEquals(page.PendingTool, pending) || !ReferenceEquals(slot.EquippedItem, equipped) || !Legal(page))
                        throw new InvalidOperationException("Loadout removal authority changed after native unequip");
                }
                // Exact native manual callback -> crest SaveEquips/SetEquippedTools or
                // floating SaveEquips/SetExtraEquippedTool; retain slot through all
                // native work after SaveEquips and the immediate RefreshTools callback.
                slot.SetEquipped(pending, isManual: true, refreshTools: true);
            }
            if (!RefreshActionPage(page) || !ReferenceEquals(page.Selected, item))
                throw new InvalidOperationException("Loadout submit page could not refresh under retained authority");
            page.EquippedCrestId = ((PlayerData)page.Token.Data).CurrentCrestID;
            page.PendingTool = null; page.TargetSlot = null;
            page.Manager.SetDisplay((InventoryItemSelectable)item);
            Current(page.Token); Present(page.Token, page);
            return true;
        }
        static void ClearPrompts(LoadoutPage page)
        {
            if (page.Root == null) return;
            foreach (var prompts in page.Root.GetComponentsInChildren<InventoryItemButtonPromptDisplayList>(true)) prompts.Clear();
            // CustomButtonCombo is native gameplay instruction, not an inventory
            // submit endpoint. Its owned presentation follows native SetDisplay.
            foreach (var combo in page.Root.GetComponentsInChildren<InventoryItemComboButtonPromptDisplay>(true)) combo.Hide();
        }
        public void ClearSelection(object clone)
        {
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Loadout selection retirement deferred until native callback unwind");
            var page = (LoadoutPage)clone; CancelAction(page); _selection.Clear(); page.Selected = null; page.PendingTool = null; page.TargetSlot = null; ClearPrompts(page); ClearExtra(page);
            if (page.Cursor != null) { page.Cursor.StopAllCoroutines(); page.Cursor.SetTarget(null); page.Cursor.Deactivate(); }
            if (page.Manager != null) page.Manager.SetDisplay((GameObject)null);
        }
        public void DestroyOwned(object clone)
        {
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Loadout destruction deferred until native callback unwind");
            var page = (LoadoutPage)clone; if (page.Destroyed) return; CancelAction(page); page.Released = true;
            Suppress(page.Root, true);
            ClearExtra(page);
            DsJournalAdmission.ReleaseOwned(
                () => { if (page.Staging != null) foreach (var driver in page.Staging.GetComponentsInChildren<MonoBehaviour>(true))
                    if (driver != null && !page.Stopped.Contains(driver)) { driver.StopAllCoroutines(); page.Stopped.Add(driver); } },
                () => { if (page.Staging != null && page.Staging.activeSelf) page.Staging.SetActive(false); },
                () =>
                {
                    if (page.Staging != null) Object.DestroyImmediate(page.Staging);
                    page.Staging = null;
                    page.Text.Clear();
                    while (page.Metadata.Count != 0)
                    {
                        var metadata = page.Metadata[page.Metadata.Count - 1];
                        if (metadata != null) Object.Destroy(metadata);
                        page.Metadata.RemoveAt(page.Metadata.Count - 1);
                    }
                });
            page.Destroyed = true;
        }
    }
}
#endif
