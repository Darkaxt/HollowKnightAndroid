// Native Inventory browse, adapted from Bottom.Inventory/Bottom.Select (MIT),
// igawa6/dualsouls 5c22451435b772acde0c7e6456f9019bc1baef73.
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TeamCherry.NestedFadeGroup;
using TeamCherry.SharedUtils;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using PaneText = TMProOld.TextMeshPro;

public sealed partial class DsPortProgress
{
    sealed class Inventory : IDsPortJournalNative, IDsPortSelection
    {
        readonly DsPortFrame _frame;
        readonly DsPortJournalState _state;
        readonly DsPortSelectState _selection;
        readonly HashSet<string> _reported = new HashSet<string>();
        DsJournalToken _current, _failed;
        long _revision;
        float _nextContentProbe;
        bool _eligible;
        readonly DsPortActionBoundary _actions = new DsPortActionBoundary();
        readonly DsPortActionHold _hold = new DsPortActionHold();
        readonly List<Touch> _touches = new List<Touch>();
        int _downFrame;
        bool _suppressReleaseTap;
        DsJournalToken _fallback;
        long _fallbackEpoch;
        int _fallbackSection, _fallbackItem;
        sealed class Consumption
        {
            public InventoryItemCollectable Entry;
            public CollectableItem Item;
            public GameManager Game;
            public PlayerData Data;
            public HeroController Hero;
            public HeroChargeEffects Charge;
            public string Scene;
            public long Generation;
            public DsPortConsumeCommit Commit = new DsPortConsumeCommit();
            public GameObject Staging, ExtraSource;
            public ConsumeVisual Visual;
            public AudioSource Audio, AudioSource;
            public CaptureAnimationEvent Signal;
            public Action SignalCallback;
            public bool HitSignal, Closing, Finished, Cancelling, Empty, Blocked;
            public Animator FailedAnimator;
            public bool FailedAnimatorEnabled;
            public RandomAudioClipTable FailedSound;
            public readonly Dictionary<RandomAudioClipTable, RandomAudioClipTable> FailedSounds = new Dictionary<RandomAudioClipTable, RandomAudioClipTable>();
            public Coroutine Handle;
            public CustomInventoryItemCollectableDisplay Display;
            public Vector3 Position, Scale;
            public readonly Dictionary<AudioClip, double> AudioNext = new Dictionary<AudioClip, double>();
        }
        sealed class ConsumeVisual
        {
            public InventoryItemCollectable Entry;
            public CollectableItem Item;
            public CaptureAnimationEvent Signal;
            public GameObject Staging, ExtraSource;
            public int ExtraNodes;
            public readonly List<GameObject> Extras = new List<GameObject>();
            public readonly DsPortConsumeVisualLifetime Lifetime = new DsPortConsumeVisualLifetime();
            public readonly List<Material> Materials = new List<Material>();
        }
        sealed class InventoryPage
        {
            public DsJournalToken Token;
            public GameObject Staging, Root, Template, CursorTemplate;
            public DsPortCollectableManager Manager;
            public InventoryItemGrid Grid;
            public ScrollView Scroll;
            public InventoryCursor Cursor;
            public InventoryItemCollectable[] Entries;
            public Bounds View, Content, Fit;
            public Vector3 Origin;
            public float Offset, Scale;
            public int Activated;
            public bool Released, Destroyed;
            public Exception CreationError;
            public readonly DsPortOwnedText Text = new DsPortOwnedText();
            public readonly DsPortOwnedText DetailText = new DsPortOwnedText();
            public readonly HashSet<MonoBehaviour> Stopped = new HashSet<MonoBehaviour>();
            public InventoryItemCollectable Selected;
            public Consumption Consume;
            public readonly Dictionary<InventoryItemCollectable, ConsumeVisual> ConsumeVisuals = new Dictionary<InventoryItemCollectable, ConsumeVisual>();
            public int ConsumeExtraNodes;
            public bool MemoryMessage;
            public double MemoryHideAfter;
            public readonly HashSet<CaptureAnimationEvent> PreparedSignals = new HashSet<CaptureAnimationEvent>();
            public readonly List<Material> ConsumeMaterials = new List<Material>();
            public Transform DetailParent;
            public InventoryItemExtraDescription DetailCondition;
            public GameObject DetailStaging, DetailRoot;
            public readonly List<Material> DetailMaterials = new List<Material>();
            public readonly HashSet<InventoryItemCollectable> Visible = new HashSet<InventoryItemCollectable>();
        }
        public Inventory(DsPortFrame frame)
        { _frame = frame; _state = new DsPortJournalState(this); _selection = new DsPortSelectState(this); }
        bool _outgoing;
        public void SelectionChanged()
        {
            _selection.Clear(); _hold.Liveness(false); _nextContentProbe = 0;
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Inventory selection retirement deferred until callback unwind");
            var page = _state.Owned as InventoryPage;
            if (page != null) CancelConsume(page);
            _outgoing = _state.Ready && page != null && !page.Released && KeepOutgoing(_frame, DsPageRole.Inventory, page.Token) && _state.RetainOutgoingPresentation();
            if (!_outgoing) Invalidate();
        }
        public void Invalidate()
        {
            _hold.Liveness(false);
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Inventory retirement deferred until native callback unwind");
            _outgoing = false; _fallback = null; _revision++; _nextContentProbe = 0; _current = null; _failed = null; _selection.Clear(); _state.Clear();
        }
        public void ObserveGesture(DsGesture gesture)
        {
            if (gesture.Type == DsGestureType.Down) { _downFrame = Time.frameCount; _suppressReleaseTap = false; }
            _hold.Observe(gesture.Type == DsGestureType.Down, gesture.Type == DsGestureType.Up);
            if (gesture.Type == DsGestureType.Drag) _hold.Liveness(false);
            var page = _state.Owned as InventoryPage;
            if (page != null && page.Consume != null && !page.Consume.Blocked && !TouchHeld(page.Consume.Generation)) CancelConsume(page);
        }
        bool TouchHeld(long generation)
        {
            DsTouch.CollectSecondScreen(_touches);
            bool live = _touches.Count == 1 && _touches[0].phase != TouchPhase.Ended && _touches[0].phase != TouchPhase.Canceled &&
                (_touches[0].phase != TouchPhase.Began || Time.frameCount == _downFrame);
            _hold.Liveness(live);
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
                    var outgoing = _state.Owned as InventoryPage;
                    if (eligible && outgoing != null && !outgoing.Released && KeepOutgoing(_frame, DsPageRole.Inventory, outgoing.Token)) return;
                    Invalidate();
                }
                var activePage = _state.Owned as InventoryPage;
                if (activePage != null && activePage.Consume != null)
                {
                    var consume = activePage.Consume;
                    if (consume.Finished || (!consume.Blocked && !TouchHeld(consume.Generation)) || !ConsumeCurrent(activePage, consume)) CancelConsume(activePage);
                    else { Present(activePage.Token, activePage); return; }
                }
                if ((_state.Ready || _state.Owned == null) && Time.unscaledTime < _nextContentProbe) return;
                _nextContentProbe = Time.unscaledTime + .125f;
                _current = null;
                _current = Capture();
                if (_current != null && _current.Same(_failed)) return;
                _state.Tick(_current, Time.frameCount);
                if (_state.Ready && _fallback != null)
                {
                    var prior = _fallback; _fallback = null;
                    var page = _state.Owned as InventoryPage;
                    if (page != null && _fallbackEpoch == _frame.SelectionEpoch && ReferenceEquals(prior.Pane, page.Token.Pane) &&
                        ReferenceEquals(prior.List, page.Token.List) && ReferenceEquals(prior.Data, page.Token.Data) &&
                        ReferenceEquals(prior.Records, page.Token.Records) && ReferenceEquals(prior.Host, page.Token.Host))
                    {
                        var next = page.Grid.GetItemOrFallback(_fallbackSection, _fallbackItem) as InventoryItemCollectable;
                        if (next != null && page.Visible.Contains(next)) _selection.Select(page, next, page.Token.Data);
                    }
                }
                if (_state.Problem != null) Report(_state.Problem);
            }
            catch (Exception e) { _failed = DsPortPageSnapshot.IsReadFailure(e) ? null : _current; _state.Clear(); Report(e.GetBaseException().Message); }
        }
        void Report(string problem)
        { if (_reported.Add(problem)) Debug.LogWarning("[DualScreen][capability-gap] Inventory: " + problem); }
        DsJournalToken Capture()
        {
            if (!_eligible || !_frame.HudReady || _frame.SelectedRole != DsPageRole.Inventory || !DsGameData.InGame) return null;
            var pane = _frame.GetResidentPane(DsPageRole.Inventory);
            var host = _frame.GetOrCreatePageHost(DsPageRole.Inventory);
            var gm = GameManager.SilentInstance; var data = PlayerData.instance;
            var records = ManagerSingleton<CollectableItemManager>.UnsafeInstance;
            if (pane == null || host == null || !host.gameObject.activeInHierarchy || gm == null || data == null || records == null ||
                !ReferenceEquals(gm.playerData, data) || gm.isPaused || gm.IsInSceneTransition || data.isInventoryOpen) return null;
            var manager = pane.GetComponentInChildren<InventoryItemCollectableManager>(true);
            if (manager == null) return null;
            return new DsJournalToken(pane, manager, data, records, host, _revision + _frame.SelectionEpoch,
                new DsPortPageSnapshot(ReadContent(pane, manager, data, records)));
        }
        static IEnumerable<object> ReadContent(InventoryPane pane, InventoryItemCollectableManager manager, PlayerData data, CollectableItemManager records)
        {
            var hero = HeroController.instance;
            yield return hero; yield return hero != null ? hero.Config : null;
            var charge = ManagerSingleton<HeroChargeEffects>.UnsafeInstance;
            yield return charge; yield return charge != null && charge.IsCharging;
            bool bare = hero != null && hero.Config != null && hero.Config.ForceBareInventory;
            yield return bare; yield return CollectableItemManager.IsInHiddenMode();
            var all = records.GetAllCollectables();
            if (all == null) throw new InvalidOperationException("Inventory content master list missing");
            foreach (var item in all)
            {
                yield return item;
                if (item == null) throw new InvalidOperationException("Inventory content item missing");
                var saved = item.SaveData;
                yield return item.name; yield return saved.Amount; yield return saved.AmountWhileHidden; yield return saved.IsSeenMask;
                yield return item.IsVisible; yield return item.IsVisibleWithBareInventory;
                if (!item.IsVisible || (bare && !item.IsVisibleWithBareInventory && saved.AmountWhileHidden <= 0)) continue;
                yield return item.CollectedAmount; yield return item.DisplayAmount; yield return item.IsAtMax(); yield return item.IsConsumable();
                if (item.IsConsumable())
                {
                    yield return item.CanConsumeRightNow(); yield return item.ConsumeClosesInventory(extraCondition: true);
                    yield return item.ConsumeClosesInventory(extraCondition: false); yield return item.TakeItemOnConsume;
                    yield return item.PreventUseChaining; yield return item.IsConsumeAtMax(); yield return item.AlwaysPlayInstantUse;
                    yield return item.ExtraUseEffect;
                    foreach (var value in ReadConsumeAudioInputs(item.UseSounds)) yield return value;
                    foreach (var value in ReadConsumeAudioInputs(item.InstantUseSounds)) yield return value;
                }
                yield return item.CustomInventoryDisplay; yield return item.ExtraDescriptionSection;
                yield return item.GetIcon(CollectableItem.ReadSource.Inventory);
                yield return item.GetDisplayName(CollectableItem.ReadSource.Inventory);
                yield return item.GetDescription(CollectableItem.ReadSource.Inventory);
                foreach (var value in ReadQuestExtraContent(item)) yield return value;
            }
            yield return "saved-collectable-names";
            foreach (var name in data.Collectables.GetValidNames()) yield return name;
            foreach (var value in ReadPaneConditions(pane, manager)) yield return value;
        }
        static IEnumerable<object> ReadConsumeAudioInputs(AudioEventRandom sound)
        {
            yield return sound.PitchMin; yield return sound.PitchMax; yield return sound.Volume;
            yield return sound.Clips; yield return sound.vibrations;
            if ((sound.Clips != null && sound.Clips.Length > 4096) || (sound.vibrations != null && sound.vibrations.Length > 4096))
                throw new InvalidOperationException("Inventory consume audio input bound exceeded");
            if (sound.Clips != null) foreach (var clip in sound.Clips) yield return clip;
            if (sound.vibrations != null) foreach (var vibration in sound.vibrations) yield return vibration;
        }
        public bool IsCurrent(DsJournalToken token) => token != null && token.Same(Capture());
        void Current(DsJournalToken token)
        { if (!IsCurrent(token)) throw new InvalidOperationException("Inventory owner/selection replaced"); }
        static bool Allowed(Type type) => ComponentAllowed(type, "visual") || type == typeof(InventoryPane) ||
            type == typeof(InventoryPaneInput) || type == typeof(InventoryItemCollectableManager) || type == typeof(DsPortCollectableManager) ||
            type == typeof(InventoryItemCollectable) || type == typeof(InventoryItemGrid) || type == typeof(InventoryAutoNavGroup) ||
            type == typeof(ScrollView) || type == typeof(InventoryCursor) || type == typeof(Animator) ||
            type == typeof(InventoryItemExtraDescription) || type == typeof(InventoryCollectableItemSelectionHelper) ||
            type == typeof(TextMeshProContainerFitter) || type == typeof(TMProOld.TextContainer) ||
            type == typeof(CustomInventoryItemCollectableDisplay) || type == typeof(CaptureAnimationEvent) ||
            type == typeof(InventoryItemButtonPromptDisplayList) || type == typeof(InventoryItemButtonPromptDisplay) || type == typeof(ActionButtonIcon);
        static void InspectScope(GameObject root, GameObject template, GameObject cursor)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || !Allowed(component.GetType()))
                    throw new InvalidOperationException("Inventory unadmitted component " + (component == null ? "missing script" : component.GetType().FullName));
                if (component is InventoryItemExtraDescription) continue; // Removed before any owned Awake.
                var item = component.GetComponentInParent<InventoryItemCollectable>(true);
                var local = item != null && Within(item.transform, root.transform) ? item.transform : root.transform;
                ValidateInventoryReferences(component, local, template, cursor);
                if (component is CaptureAnimationEvent signal)
                {
                    if (item == null || !ReferenceEquals(Get(item, "consumeEffect"), signal))
                        throw new InvalidOperationException("Inventory consume signal is not the exact owned entry reference");
                    InspectConsumeVisual(signal.gameObject, signal);
                }
                if (component is PlayerDataTestResponse response)
                { ReadEvent(response.IsFullfilled, local); ReadEvent(response.IsNotFulfilled, local); }
                if (component is InventoryItemCollectable)
                {
                    NeedLocal(component, local, "spriteRenderer", "amountText", "group");
                    var asset = Get(component, "item") as CollectableItem;
                    CheckItem(asset);
                }
                if (component is NestedFadeGroupBase fade && fade.ParentOverride != null && fade.ParentOverride.Value != null &&
                    !Within(fade.ParentOverride.Value.transform, local)) throw new InvalidOperationException("Inventory escaped fade parent");
                if (component is InventoryItemGrid grid)
                {
                    if (grid.RowSplit <= 0) throw new InvalidOperationException("Inventory grid RowSplit invalid");
                    ValidateNavigation(Get(grid, "selectables"), root.transform); ValidateNavigation(Get(grid, "nextPages"), root.transform);
                }
            }
            ValidateBridgeCatalogue(root);
        }
        static IEnumerable<object> ReadQuestExtraContent(CollectableItem item)
        {
            if (!(item is CollectableItemQuestDisplay)) yield break;
            var quest = Get(item, "quest") as Quest;
            yield return quest;
            if (quest != null)
            {
                foreach (var value in Tasks.ReadQuestContent(quest)) yield return value;
                yield return quest.IsDonateType;
                int index = 0;
                foreach (var pair in quest.TargetsAndCounters)
                {
                    yield return quest.GetCollectedCountOverride(pair.target, pair.count);
                    if (quest.DescCounterType == FullQuestBase.DescCounterTypes.Icons)
                        for (int i = 0; i < pair.target.Count; i++)
                        {
                            if (index >= DsPortDetailBudget.MaximumEntries) throw new InvalidOperationException("Quest detail icon input bound exceeded");
                            yield return quest.GetCounterSpriteOverride(pair.target, index++);
                        }
                    else yield return quest.GetCounterSpriteOverride(pair.target, 0);
                }
            }
            var prefab = item.ExtraDescriptionSection;
            if (prefab == null) yield break;
            foreach (var icon in prefab.GetComponentsInChildren<IconCounterItem>(true))
            {
                yield return icon;
                var condition = (PlayerDataTest)Get(icon, "customCondition");
                yield return condition; yield return condition.IsDefined; yield return condition.IsFulfilled;
                var other = Get(icon, "orItemCondition") as CollectableItem;
                yield return other; if (other != null) yield return other.CollectedAmount;
            }
            foreach (var response in prefab.GetComponentsInChildren<PlayerDataTestResponse>(true))
            { yield return response; yield return ((PlayerDataTest)Get(response, "test")).IsFulfilled; }
        }
        static void PrepareQuestExtra(GameObject root, SavedItem item)
        {
            if (!(item is CollectableItemQuestDisplay)) return;
            if (root.activeInHierarchy) throw new InvalidOperationException("Quest extra factory must remain born-inactive");
            var quest = Get(item, "quest") as Quest;
            int textCount = 0, iconCount = 0;
            if (quest != null && !quest.IsCompleted)
                foreach (var pair in quest.TargetsAndCounters)
                {
                    if (++textCount > DsPortDetailBudget.MaximumEntries || pair.target.Count < 0)
                        throw new InvalidOperationException("Quest native target bound exceeded");
                    if (quest.DescCounterType == FullQuestBase.DescCounterTypes.Icons)
                    {
                        if (pair.target.Count > DsPortDetailBudget.MaximumEntries - iconCount)
                            throw new InvalidOperationException("Quest native icon bound exceeded");
                        iconCount += pair.target.Count;
                    }
                }
            var budget = new DsPortDetailBudget();
            budget.Reserve(root.GetComponentsInChildren<Transform>(true).Length);
            // Native SetDisplay still owns formatting and layout. Prebuild only the
            // exact template children it would otherwise instantiate while active.
            foreach (var counter in root.GetComponentsInChildren<IconCounter>(true))
            {
                var template = Get(counter, "templateItem") as IconCounterItem;
                if (template == null || template.transform.parent != counter.transform)
                    throw new InvalidOperationException("Quest icon template is not an exact local direct child");
                int required = Math.Max((int)Get(counter, "maxValue"), iconCount);
                if (required < 0) throw new InvalidOperationException("Quest native initial icon count invalid");
                int count = 0;
                foreach (Transform child in counter.transform)
                    if (child.GetComponent<IconCounterItem>() != null && child != template.transform) count++;
                int nodes = template.GetComponentsInChildren<Transform>(true).Length;
                while (count < required)
                {
                    budget.Reserve(nodes);
                    var copy = Object.Instantiate(template, counter.transform, false);
                    copy.gameObject.SetActive(false); count++;
                }
            }
            foreach (var description in root.GetComponentsInChildren<QuestItemDescription>(true))
            {
                var template = Get(description, "textDisplayTemplate") as QuestItemDescriptionText;
                if (template == null) continue;
                var displays = new List<QuestItemDescriptionText> { template };
                template.gameObject.SetActive(false);
                int nodes = template.GetComponentsInChildren<Transform>(true).Length;
                while (displays.Count < textCount)
                {
                    budget.Reserve(nodes);
                    var copy = Object.Instantiate(template, template.transform.parent, false);
                    copy.gameObject.SetActive(false); displays.Add(copy);
                }
                Set(description, "spawnedTextDisplays", displays);
            }
        }
        public static Type ExtraSetupOwner(SavedItem item)
        {
            var setup = item.GetType().GetMethod("SetupExtraDescription", new[] { typeof(GameObject) });
            if (setup != null && (setup.DeclaringType == typeof(SavedItem) || setup.DeclaringType == typeof(ToolItemStatesLiquid) ||
                setup.DeclaringType == typeof(CollectableItemQuestDisplay)))
                return setup.DeclaringType;
            throw new InvalidOperationException("unadmitted native SetupExtraDescription override on " + item.GetType().FullName);
        }
        public static void OwnExtraMaterials(GameObject root, List<Material> materials)
        {
            var copies = new Dictionary<Material, Material>();
            Material Copy(Material source)
            {
                if (source == null) return null;
                if (!copies.TryGetValue(source, out var owned))
                { owned = new Material(source); materials.Add(owned); copies.Add(source, owned); }
                return owned;
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var values = renderer.sharedMaterials;
                for (int i = 0; i < values.Length; i++) values[i] = Copy(values[i]);
                renderer.sharedMaterials = values;
            }
            foreach (var text in root.GetComponentsInChildren<PaneText>(true))
                Set(text, "m_sharedMaterial", Copy(Get(text, "m_sharedMaterial") as Material));
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                var source = Get(graphic, "m_Material") as Material;
                if (source == null) throw new InvalidOperationException("Quest extra requires an explicit native UI material");
                graphic.material = Copy(source);
            }
            foreach (var description in root.GetComponentsInChildren<QuestItemDescription>(true))
            {
                var table = (Array)((Array)Get(description, "counterMaterials")).Clone();
                for (int i = 0; i < table.Length; i++)
                {
                    var entry = table.GetValue(i); Set(entry, "Material", Copy(Get(entry, "Material") as Material)); table.SetValue(entry, i);
                }
                Set(description, "counterMaterials", table);
            }
            foreach (var icon in root.GetComponentsInChildren<IconCounterItem>(true))
                foreach (string field in new[] { "activeState", "inactiveState" })
                {
                    var state = Get(icon, field); Set(state, "Material", Copy(Get(state, "Material") as Material)); Set(icon, field, state);
                }
        }
        public static string AuxiliaryProblem(GameObject root, Type setupOwner = null)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) return "missing script";
                var type = component.GetType();
                bool liquid = setupOwner == typeof(ToolItemStatesLiquid) && type == typeof(LiquidMeter);
                bool quest = setupOwner == typeof(CollectableItemQuestDisplay) &&
                    (type == typeof(QuestItemDescription) || type == typeof(QuestItemDescriptionText) ||
                     type == typeof(IconCounter) || type == typeof(IconCounterItem) || type == typeof(ImageSlider) ||
                     type == typeof(GridLayoutGroup) || type == typeof(Canvas) || type == typeof(CanvasRenderer) || type == typeof(Image));
                if (!ComponentAllowed(type, "visual") && type != typeof(CustomInventoryItemCollectableDisplay) && !liquid && !quest &&
                    type != typeof(TextMeshProContainerFitter) && type != typeof(TMProOld.TextContainer))
                    return "unadmitted native auxiliary component " + type.FullName;
                if (liquid) NeedLocal(component, root.transform, "liquidParent", "liquidSprite", "liquidOffset", "countText", "glandParent");
                if (component is IconCounterItem) NeedLocal(component, root.transform, "spriteRenderer");
                if (component is QuestItemDescription)
                {
                    var table = Get(component, "counterMaterials") as Array;
                    if (table == null || table.Length != 2) return "Quest counter material table shape changed";
                }
                if (component is Graphic && Get(component, "m_Material") == null) return "Quest extra UI material is not explicit";
                ValidateSerializedReferences(component, root.transform, root, root);
                if (component is PlayerDataTestResponse response)
                { ReadEvent(response.IsFullfilled, root.transform); ReadEvent(response.IsNotFulfilled, root.transform); }
                if (component is NestedFadeGroupBase fade && fade.ParentOverride != null && fade.ParentOverride.Value != null &&
                    !Within(fade.ParentOverride.Value.transform, root.transform)) throw new InvalidOperationException("Inventory auxiliary fade escaped scope");
            }
            ValidateBridgeCatalogue(root);
            return null;
        }
        public static void PrepareAuxiliary(GameObject root)
        {
            foreach (var response in root.GetComponentsInChildren<PlayerDataTestResponse>(true))
            { var field = Field(response, "runOn"); field.SetValue(response, Enum.Parse(field.FieldType, "JustStart")); response.enabled = false; }
            foreach (var clip in root.GetComponentsInChildren<TextMeshProClipRect>(true)) clip.enabled = false;
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = DsPresentation.UiCamera;
                canvas.targetDisplay = DsPresentation.DISPLAY; canvas.overrideSorting = false;
            }
            foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>(true)) renderer.cull = true;
            Suppress(root, true); RouteOwned(root);
        }
        static void CheckItem(CollectableItem item)
        {
            if (item == null || item.CustomInventoryDisplay == null) return;
            string problem = AuxiliaryProblem(item.CustomInventoryDisplay.gameObject);
            if (problem != null) throw new InvalidOperationException("Inventory custom icon asset " + item.name + ": " + problem);
        }
        static void ValidateInventoryReferences(Component component, Transform local, GameObject template, GameObject cursor)
        {
            if (!(component is InventoryItemCollectable)) { ValidateSerializedReferences(component, local, template, cursor); return; }
            for (Type type = component.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(Component); type = type.BaseType)
                foreach (var field in type.GetFields(Fields))
                {
                    if (field.IsStatic || field.IsNotSerialized || (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true))) continue;
                    var value = field.GetValue(component);
                    if (field.DeclaringType == typeof(InventoryItemCollectable) && field.Name == "audioPlayerPrefab")
                    { if (value is AudioSource audio) InspectConsumeAudio(audio); continue; }
                    CheckValue(field.Name, value, local);
                }
        }
        static void InspectConsumeAudio(AudioSource audio)
        {
            if (audio.spatialBlend != 0f) throw new InvalidOperationException("Inventory requires authored UI audio");
            foreach (var component in audio.GetComponentsInChildren<Component>(true))
                if (component == null || (component.GetType() != typeof(Transform) && component.GetType() != typeof(AudioSource)))
                    throw new InvalidOperationException("Inventory audio prefab has unadmitted pooled/lifecycle writer");
        }
        static void InspectConsumeVisual(GameObject root, CaptureAnimationEvent signal)
        {
            if (root.GetComponentsInChildren<Transform>(true).Length > 4096) throw new InvalidOperationException("Inventory consume visual exceeds owned bound");
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Inventory consume visual missing script");
                var type = component.GetType();
                if (type != typeof(Transform) && type != typeof(RectTransform) && type != typeof(SpriteRenderer) &&
                    type != typeof(MeshRenderer) && type != typeof(MeshFilter) && type != typeof(PaneText) && type != typeof(TMProOld.TMP_SubMesh) &&
                    type != typeof(TMProOld.TextContainer) && type != typeof(NestedFadeGroup) && type != typeof(NestedFadeGroupSpriteRenderer) &&
                    type != typeof(NestedFadeGroupTextMeshPro) && type != typeof(Animator) && type != typeof(CaptureAnimationEvent))
                    throw new InvalidOperationException("Inventory consume visual is not a closed owned island: " + type.FullName);
                ValidateSerializedReferences(component, root.transform, root, root);
                if (component is CaptureAnimationEvent capture)
                {
                    if (!ReferenceEquals(capture, signal)) throw new InvalidOperationException("Inventory consume event escaped exact signal");
                    var indexed = Get(capture, "indexedEvents") as Array;
                    if (indexed != null && indexed.Length != 0) throw new InvalidOperationException("Inventory consume indexed callbacks unadmitted");
                }
                if (component is Animator animator)
                {
                    if (animator.applyRootMotion || animator.GetBehaviours<StateMachineBehaviour>().Length != 0)
                        throw new InvalidOperationException("Inventory consume animator has input/root/state authority");
                    if (animator.runtimeAnimatorController != null)
                        foreach (var clip in animator.runtimeAnimatorController.animationClips)
                            if (clip != null) foreach (var callback in clip.events)
                                if (callback.functionName != "FireEvent" || signal == null ||
                                    !ReferenceEquals(animator.GetComponent<CaptureAnimationEvent>(), signal))
                                    throw new InvalidOperationException("Inventory consume animation event is not the exact local FireEvent");
                }
            }
            ValidateBridgeCatalogue(root);
        }
        static void PlaySound(Consumption consume, AudioEventRandom source)
        {
            if (!source.HasClips()) return;
            if (source.Clips.Length > 4096 || !DsJournalGeometry.Finite(source.Volume) ||
                !DsJournalGeometry.Finite(source.PitchMin) || !DsJournalGeometry.Finite(source.PitchMax))
                throw new InvalidOperationException("Inventory consume audio exceeds native finite input bound");
            var clip = source.GetClip();
            if (clip == null || source.Volume < Mathf.Epsilon) return;
            if (consume.Audio == null) throw new InvalidOperationException("Inventory authored consume audio source unavailable");
            // Native AudioEventManager frequency rule with owned cooldown writes.
            // Read an existing native budget only; never create its singleton or borrow its pooled source.
            var native = typeof(AudioEventManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as AudioEventManager;
            if (native != null && ((Dictionary<AudioClip, float>)Get(native, "clipReleaseTimesLeft")).TryGetValue(clip, out float left) && left > 0f) return;
            if (consume.AudioNext.TryGetValue(clip, out double next) && Time.unscaledTimeAsDouble < next) return;
            if (consume.AudioNext.Count >= 4096 && !consume.AudioNext.ContainsKey(clip)) throw new InvalidOperationException("Inventory audio cooldown bound exceeded");
            consume.AudioNext[clip] = Time.unscaledTimeAsDouble + GlobalSettings.Audio.AudioEventFrequencyLimit;
            consume.Audio.priority = AudioSourcePriority.SPAWNED_ACTOR_PRIORITY;
            consume.Audio.volume = source.Volume; consume.Audio.pitch = source.SelectPitch();
            consume.Audio.PlayOneShot(clip); source.PlayVibration(clip, consume.Audio);
        }
        bool ConsumeCurrent(InventoryPage page, Consumption consume) => !page.Released && _state.Ready &&
            consume.Entry != null && consume.Item != null && (consume.Blocked || !consume.Closing || CloseAuthority(consume)) &&
            ReferenceEquals(page.Consume, consume) && ReferenceEquals(page.Selected, consume.Entry) &&
            ReferenceEquals(consume.Entry.Item, consume.Item) && page.Visible.Contains(consume.Entry) &&
            Within(consume.Entry.transform, page.Grid.transform) && IsCurrent(page.Token) &&
            ReferenceEquals(Get(consume.Entry, "currentCustomDisplay"), consume.Display) &&
            ReferenceEquals(Get(consume.Entry, "consumeEffect"), consume.Signal) &&
            ReferenceEquals(Get(consume.Entry, "audioPlayerPrefab"), consume.AudioSource) &&
            ReferenceEquals(consume.Item.ExtraUseEffect, consume.ExtraSource);
        bool RefreshConsumePage(InventoryPage page, Consumption consume)
        {
            if (_actions.Pending || page.Released || !ReferenceEquals(page.Consume, consume) ||
                !ReferenceEquals(page.Selected, consume.Entry) || !ReferenceEquals(consume.Entry.Item, consume.Item)) return false;
            var before = page.Token; var next = Capture();
            if (!_state.RefreshAfterAction(page, before, next)) return false;
            page.Token = next; _current = next; _nextContentProbe = 0;
            return ConsumeCurrent(page, consume);
        }
        bool CloseAuthority(Consumption consume) => consume.Charge != null && consume.Game != null && consume.Hero != null &&
            ReferenceEquals(GameManager.SilentInstance, consume.Game) && ReferenceEquals(PlayerData.instance, consume.Data) &&
            ReferenceEquals(consume.Game.playerData, consume.Data) && ReferenceEquals(HeroController.instance, consume.Hero) &&
            ReferenceEquals(consume.Hero.playerData, consume.Data) && consume.Game.sceneName == consume.Scene &&
            ReferenceEquals(ManagerSingleton<HeroChargeEffects>.UnsafeInstance, consume.Charge) &&
            consume.Charge.gameObject.activeInHierarchy && !consume.Game.IsInSceneTransition;
        void FinishConsumeClose(Consumption consume, bool cancelled)
        {
            consume.Commit.FinishClose(() => CloseAuthority(consume),
                () => EventRegister.SendEvent(EventRegisterEvents.InventoryCancel),
                () => consume.Charge.DoUseBenchItem(consume.Item), cancelled);
        }
        static RandomAudioClipTable OwnFailedSound(Consumption consume, RandomAudioClipTable source)
        {
            if (source == null) return null;
            if (consume.FailedSounds.TryGetValue(source, out var found)) return found;
            if (consume.FailedSounds.Count >= 32 || Get(source, "type").ToString() != "Normal")
                throw new InvalidOperationException("Inventory denied audio requires bounded native UI tables");
            var copy = Object.Instantiate(source); consume.FailedSounds.Add(source, copy);
            var others = Get(source, "addCooldownTo") as RandomAudioClipTable[];
            if (others != null)
            {
                if (others.Length > 32) throw new InvalidOperationException("Inventory denied audio cooldown graph exceeds bound");
                var owned = new RandomAudioClipTable[others.Length];
                for (int i = 0; i < others.Length; i++) owned[i] = OwnFailedSound(consume, others[i]);
                Set(copy, "addCooldownTo", owned);
            }
            return copy;
        }
        static void ShowMemoryUseMsg(InventoryPage page)
        {
            var group = Get(page.Manager, "memoryUseMsg") as NestedFadeGroup;
            if (group == null || page.MemoryMessage) return;
            if (!Within(group.transform, page.Root.transform)) throw new InvalidOperationException("Inventory memory message escaped owned pane");
            float time = (float)Get(page.Manager, "msgFadeInTime");
            group.AlphaSelf = 0f; group.gameObject.SetActive(true); group.FadeTo(1f, time, null, isRealtime: true);
            page.MemoryMessage = true; page.MemoryHideAfter = Time.unscaledTimeAsDouble + time;
            // Native paneList.InSubMenu belongs to primary input. Only our message state changes.
        }
        static void HideMemoryUseMsg(InventoryPage page, bool force)
        {
            if (!page.MemoryMessage || (!force && Time.unscaledTimeAsDouble < page.MemoryHideAfter)) return;
            var group = Get(page.Manager, "memoryUseMsg") as NestedFadeGroup;
            if (group != null) group.FadeTo(0f, (float)Get(page.Manager, "msgFadeOutTime"), null, isRealtime: true);
            page.MemoryMessage = false;
        }
        void PrepareConsumeVisual(InventoryPage page, Consumption consume)
        {
            if (page.ConsumeVisuals.TryGetValue(consume.Entry, out var retained))
            {
                if (!ReferenceEquals(retained.Item, consume.Item) || !ReferenceEquals(retained.Signal, consume.Signal) ||
                    !ReferenceEquals(retained.ExtraSource, consume.ExtraSource))
                    throw new InvalidOperationException("Inventory retained consume visual source changed");
                consume.Visual = retained; return;
            }
            if (page.ConsumeVisuals.Count >= 4096) throw new InvalidOperationException("Inventory consume visual entry bound exceeded");
            var visual = new ConsumeVisual { Entry = consume.Entry, Item = consume.Item, Signal = consume.Signal, ExtraSource = consume.ExtraSource };
            page.ConsumeVisuals.Add(consume.Entry, visual); consume.Visual = visual;
            visual.Staging = new GameObject("DsPortInventoryCommittedVisuals"); visual.Staging.SetActive(false);
            visual.Staging.transform.SetParent(consume.Entry.transform.parent, false);
            if (visual.Signal != null)
            {
                InspectConsumeVisual(visual.Signal.gameObject, visual.Signal);
                if (page.PreparedSignals.Add(visual.Signal)) OwnExtraMaterials(visual.Signal.gameObject, page.ConsumeMaterials);
                foreach (var animator in visual.Signal.GetComponentsInChildren<Animator>(true)) animator.enabled = true;
            }
            if (visual.ExtraSource != null)
            {
                InspectConsumeVisual(visual.ExtraSource, null);
                visual.ExtraNodes = visual.ExtraSource.GetComponentsInChildren<Transform>(true).Length;
            }
        }
        static void CheckConsumeVisualBudget(InventoryPage page, ConsumeVisual visual)
        {
            if (visual.ExtraNodes < 0 || visual.ExtraNodes > 4096 - page.ConsumeExtraNodes)
                throw new InvalidOperationException("Inventory committed visuals exceed owned page node bound");
        }
        void ShowConsumeExtra(InventoryPage page, Consumption consume)
        {
            var visual = consume.Visual;
            if (visual.ExtraSource == null) return;
            CheckConsumeVisualBudget(page, visual);
            // Native creates a distinct extra only AFTER the signal wait. A subsequent commit
            // must not restart/deactivate the previous extra; all roots remain page-owned.
            var extra = Object.Instantiate(visual.ExtraSource, visual.Staging.transform, false);
            visual.Extras.Add(extra); page.ConsumeExtraNodes += visual.ExtraNodes;
            extra.SetActive(false); InspectConsumeVisual(extra, null);
            // Force traverses these reparented extras with page.Text, as it does custom icons.
            // The same owner must prepare their fonts and retain them until every page root retires.
            OwnExtraMaterials(extra, visual.Materials); page.Text.Prepare(extra, page.Token.Content);
            RouteOwned(extra);
            extra.transform.SetParent(consume.Entry.transform.parent, false);
            extra.transform.position = consume.Entry.transform.position + new Vector3(0, 0, -.0001f);
            extra.SetActive(true);
            if (!ConsumeCurrent(page, consume)) throw new InvalidOperationException("Inventory owner lost during committed extra activation");
        }
        void ReleaseConsumeVisual(InventoryPage page, ConsumeVisual visual)
        {
            // Never follow a replacement reference. These roots were registered before population.
            if (visual.Signal != null) visual.Signal.gameObject.SetActive(false);
            foreach (var extra in visual.Extras)
                if (extra != null) { extra.SetActive(false); Object.DestroyImmediate(extra); }
            if (visual.Staging != null) { Object.DestroyImmediate(visual.Staging); visual.Staging = null; }
            foreach (var material in visual.Materials) if (material != null) Object.DestroyImmediate(material);
            visual.Materials.Clear();
            page.ConsumeExtraNodes -= visual.Extras.Count * visual.ExtraNodes;
            visual.Extras.Clear(); page.ConsumeVisuals.Remove(visual.Entry);
        }
        void RetireUncommittedVisual(InventoryPage page, Consumption consume)
        {
            // Release/new Down ends the hold, not an earlier committed signal/extra.
            consume.Visual?.Lifetime.ReleaseHold(() => ReleaseConsumeVisual(page, consume.Visual));
        }
        void RetireConsumeVisuals(InventoryPage page)
        {
            foreach (var visual in new List<ConsumeVisual>(page.ConsumeVisuals.Values))
                visual.Lifetime.Retire(() => ReleaseConsumeVisual(page, visual));
        }
        bool BeginConsume(InventoryPage page, InventoryItemCollectable entry)
        {
            if (_actions.Active || _actions.Pending) return false;
            if (page.Consume != null && page.Consume.Blocked) CancelConsume(page);
            if (_actions.Active || _actions.Pending || page.Consume != null || !ReferenceEquals(page.Selected, entry) ||
                entry.Item == null || !entry.Item.IsConsumable() || entry.Item.CollectedAmount <= 0 || !IsCurrent(page.Token)) return false;
            if (page.MemoryMessage) { _actions.Run(() => HideMemoryUseMsg(page, false)); _suppressReleaseTap = true; return false; }
            bool blocked = !entry.Item.CanConsumeRightNow();
            long generation = _hold.Capture(); if (!TouchHeld(generation)) return false;
            var consume = new Consumption { Entry = entry, Item = entry.Item, Generation = generation,
                Game = GameManager.SilentInstance, Data = (PlayerData)page.Token.Data, Hero = HeroController.instance,
                Charge = ManagerSingleton<HeroChargeEffects>.UnsafeInstance, Scene = GameManager.SilentInstance.sceneName,
                Closing = entry.Item.ConsumeClosesInventory(extraCondition: true), Position = (Vector3)Get(entry, "initialPosition"),
                Scale = (Vector3)Get(entry, "initialScale"), Display = Get(entry, "currentCustomDisplay") as CustomInventoryItemCollectableDisplay,
                Signal = Get(entry, "consumeEffect") as CaptureAnimationEvent, AudioSource = Get(entry, "audioPlayerPrefab") as AudioSource,
                ExtraSource = entry.Item.ExtraUseEffect, Blocked = blocked };
            if (!blocked && consume.Closing && (!CloseAuthority(consume) || consume.Charge.IsCharging)) return false;
            page.Consume = consume; _suppressReleaseTap = true;
            _actions.Run(() =>
            {
                consume.Staging = new GameObject("DsPortInventoryConsume"); consume.Staging.SetActive(false);
                consume.Staging.transform.SetParent(page.Staging.transform, false);
                if (consume.Display != null && (consume.Display.GetType() != typeof(CustomInventoryItemCollectableDisplay) ||
                    !ReferenceEquals(consume.Display.Owner, entry) || !Within(consume.Display.transform, entry.transform)))
                    throw new InvalidOperationException("Inventory consume custom display is not exact owned source type");
                if (!consume.Blocked) PrepareConsumeVisual(page, consume);
                var audioPrefab = consume.Blocked ? GlobalSettings.Audio.DefaultUIAudioSourcePrefab :
                    consume.AudioSource != null ? consume.AudioSource : GlobalSettings.Audio.DefaultAudioSourcePrefab;
                if (audioPrefab != null)
                {
                    InspectConsumeAudio(audioPrefab);
                    consume.Audio = Object.Instantiate(audioPrefab, consume.Staging.transform, false);
                    consume.Audio.playOnAwake = false;
                }
                if (consume.Blocked)
                {
                    consume.FailedAnimator = Get(entry, "failedAnimator") as Animator;
                    if (consume.FailedAnimator == null || !Within(consume.FailedAnimator.transform, entry.transform))
                        throw new InvalidOperationException("Inventory denied animation lacks exact owned entry authority");
                    InspectConsumeVisual(consume.FailedAnimator.gameObject, null);
                    consume.FailedAnimatorEnabled = consume.FailedAnimator.enabled;
                    consume.FailedSound = OwnFailedSound(consume, Get(entry, "failedAudioTable") as RandomAudioClipTable);
                }
                consume.Staging.SetActive(true);
                if (!ConsumeCurrent(page, consume)) throw new InvalidOperationException("Inventory consume owner lost during owned preparation");
            });
            consume.Handle = entry.StartCoroutine(DriveConsume(page, consume));
            return true;
        }
        IEnumerator DriveConsume(InventoryPage page, Consumption consume)
        {
            var routine = ConsumeRoutine(page, consume);
            bool completed = false;
            try
            {
                while (true)
                {
                    bool more = false;
                    _actions.Run(() =>
                    {
                        if ((!consume.Blocked && !TouchHeld(consume.Generation)) || !ConsumeCurrent(page, consume)) return;
                        more = routine.MoveNext();
                    });
                    if (!more) break;
                    yield return routine.Current;
                }
                completed = true;
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                consume.Finished = true;
                if (!_actions.Active)
                {
                    bool current = ConsumeCurrent(page, consume);
                    bool retire = _actions.Pending || !completed || consume.Empty || (!consume.Blocked && consume.Closing) || !current;
                    var token = page.Token;
                    int section = consume.Entry.GridSectionIndex, index = consume.Entry.GridItemIndex;
                    CancelConsume(page);
                    bool fallback = consume.Empty && current && IsCurrent(token);
                    if (retire) Invalidate();
                    if (fallback)
                    { _fallback = token; _fallbackEpoch = _frame.SelectionEpoch; _fallbackSection = section; _fallbackItem = index; }
                }
            }
        }
        IEnumerator ConsumeRoutine(InventoryPage page, Consumption consume)
        {
            var entry = consume.Entry; var item = consume.Item;
            if (consume.Blocked)
            {
                // Native denied Submit presentation, excluding pooled audio and primary paneList writes.
                consume.FailedAnimator.enabled = true; consume.FailedAnimator.Play((int)Get(entry, "failedAnimId"));
                if (consume.FailedSound != null) consume.FailedSound.PlayOneShotUnsafe(consume.Audio, 0f);
                consume.Display?.OnConsumeBlocked();
                if (_actions.Pending || !ConsumeCurrent(page, consume)) yield break;
                if (consume.Game.IsMemoryScene()) ShowMemoryUseMsg(page);
                yield return null;
                while ((consume.Audio != null && consume.Audio.isPlaying) ||
                    (consume.FailedAnimator != null && consume.FailedAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)) yield return null;
                yield break;
            }
            while (TouchHeld(consume.Generation))
            {
                if (!item.CanConsumeRightNow() || item.CollectedAmount <= 0) yield break;
                consume.Commit = new DsPortConsumeCommit();
                page.Manager.IsActionsBlocked = false;
                PlaySound(consume, item.UseSounds); consume.Display?.OnConsumeStart();
                float jitter = consume.Display != null ? consume.Display.JitterMagnitudeMultiplier : 1f;
                var wait = new WaitForSecondsRealtime(1f / 60f);
                float duration = item.ConsumeClosesInventory(extraCondition: false) ? 1.5f : .5f;
                double before;
                for (float elapsed = 0; elapsed < duration; elapsed += (float)(Time.unscaledTimeAsDouble - before))
                {
                    if (!item.CanConsumeRightNow() || item.CollectedAmount <= 0) yield break;
                    entry.SetConsumeShakeAmount(elapsed / duration, jitter);
                    before = Time.unscaledTimeAsDouble; yield return wait;
                }
                if (!item.CanConsumeRightNow() || item.CollectedAmount <= 0 || !ConsumeCurrent(page, consume)) yield break;
                CheckConsumeVisualBudget(page, consume.Visual);
                page.Manager.IsActionsBlocked = true;
                if (item.AlwaysPlayInstantUse && item.InstantUseSounds.HasClips())
                { if (consume.Audio != null) consume.Audio.Stop(); PlaySound(consume, item.InstantUseSounds); }
                entry.StopConsumeRumble();
                typeof(InventoryItemCollectable).GetMethod("PlayConsumeFinalShake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, null);
                consume.Display?.OnConsumeComplete();
                entry.IconTransform.localPosition = consume.Position;
                consume.Commit.Commit(consume.Closing, item.TakeItemOnConsume, () => RefreshConsumePage(page, consume),
                    () => item.ConsumeItemResponse(), () => item.Take(1, showCounter: false));
                consume.Visual.Lifetime.Commit();
                RefreshConsumeDisplay(page, consume);
                if (consume.Signal != null)
                {
                    consume.HitSignal = false;
                    consume.SignalCallback = () => consume.HitSignal = true;
                    consume.Signal.EventFired += consume.SignalCallback;
                    consume.Signal.gameObject.SetActive(false); consume.Signal.gameObject.SetActive(true);
                    while (!consume.HitSignal) yield return null;
                    consume.Signal.EventFired -= consume.SignalCallback; consume.SignalCallback = null;
                }
                ShowConsumeExtra(page, consume);
                Set(entry, "consumeFadeUpDelay", 0f);
                var group = Get(entry, "group") as NestedFadeGroup;
                if (group != null) group.AlphaSelf = 0f;
                float scale = (float)Get(entry, "breakFadeUpScale");
                entry.IconTransform.localScale = new Vector3(scale, scale, consume.Scale.z);
                if (item.CollectedAmount <= 0 || consume.Closing)
                {
                    yield return new WaitForSecondsRealtime(.5f);
                    ClearPrompts(page);
                    if (consume.Closing) { FinishConsumeClose(consume, false); yield break; }
                    consume.Empty = true; yield break; // Existing page lifecycle owns native list rebuild after unwind.
                }
                // Native Update owns this fade timer independently of the cancelled hold routine.
                // EndConsume does not reset scale/alpha or stop that already-scheduled visual work.
                float fade = (float)Get(entry, "breakFadeUpTime");
                Set(entry, "consumeFadeUpDelay", .3f - fade);
                yield return new WaitForSecondsRealtime(.3f);
                if (item.PreventUseChaining || item.IsConsumeAtMax()) break;
            }
        }
        void RefreshConsumeDisplay(InventoryPage page, Consumption consume)
        {
            var entry = consume.Entry; var item = consume.Item;
            // Source UpdateItemDisplay scalar formatting only. Never its shared icon cache.
            ((SpriteRenderer)Get(entry, "spriteRenderer")).sprite = item.CustomInventoryDisplay != null ? null : item.GetIcon(CollectableItem.ReadSource.Inventory);
            var amount = (PaneText)Get(entry, "amountText");
            amount.text = (bool)Get(entry, "forceShowAmount") || item.DisplayAmount ? item.CollectedAmount.ToString() : string.Empty;
            amount.color = (Color)Get(entry, item.IsAtMax() ? "maxAmountTextColor" : "regularAmountTextColor");
            ClearPrompts(page);
            typeof(InventoryItemCollectable).GetMethod("DisplayPromptData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, null);
            if (!RefreshConsumePage(page, consume)) throw new InvalidOperationException("Inventory owner changed during consume display");
            Present(page.Token, page);
        }
        void CancelConsume(InventoryPage page)
        {
            var consume = page.Consume; if (consume == null || consume.Cancelling) return;
            if (_actions.DeferRetirement()) return;
            consume.Cancelling = true;
            try
            {
                _actions.Run(() =>
                {
                    // Native ResetConsume dispatches pending closing use even on release.
                    // Loss of that exact gameplay owner retains this bounded obligation for retry.
                    if (consume.Commit.PendingClose) FinishConsumeClose(consume, true);
                    if (consume.Signal != null && consume.SignalCallback != null)
                    { consume.Signal.EventFired -= consume.SignalCallback; consume.SignalCallback = null; }
                    if (consume.Handle != null && !consume.Finished) consume.Entry.StopCoroutine(consume.Handle);
                    consume.Handle = null;
                    consume.Entry.StopConsumeRumble(); consume.Display?.OnConsumeEnd();
                    consume.Entry.IconTransform.localPosition = consume.Position;
                    if (consume.Audio != null) consume.Audio.Stop();
                    RetireUncommittedVisual(page, consume);
                    if (consume.FailedAnimator != null) consume.FailedAnimator.enabled = consume.FailedAnimatorEnabled;
                    if (consume.Staging != null) { consume.Staging.SetActive(false); Object.DestroyImmediate(consume.Staging); consume.Staging = null; }
                    consume.AudioNext.Clear();
                    foreach (var sound in consume.FailedSounds.Values) if (sound != null) Object.DestroyImmediate(sound);
                    consume.FailedSounds.Clear(); page.Manager.IsActionsBlocked = false;
                });
                page.Consume = null; _nextContentProbe = 0;
            }
            finally { consume.Cancelling = false; }
        }

        List<InventoryItemGrid.GridSection> ReadOnlySections(InventoryPage page, List<InventoryItemCollectable> entries, List<CollectableItem> items)
        {
            if (entries.Count != items.Count) throw new InvalidOperationException("Inventory entry snapshot count changed");
            var regular = new List<InventoryItemSelectableDirectional>();
            var consumable = new List<InventoryItemSelectableDirectional>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; var item = items[i]; Current(page.Token);
                if (entry.gameObject.activeInHierarchy || !Within(entry.transform, page.Grid.transform))
                    throw new InvalidOperationException("Inventory entry must be owned and inactive before binding");
                // Source-adapted native UpdateItemDisplay formatting, without its shared-cache Item setter.
                Set(entry, "item", item); entry.gameObject.name = item.name;
                var sprite = (SpriteRenderer)Get(entry, "spriteRenderer");
                sprite.sprite = item.CustomInventoryDisplay != null ? null : item.GetIcon(CollectableItem.ReadSource.Inventory);
                var amount = (PaneText)Get(entry, "amountText");
                amount.text = (bool)Get(entry, "forceShowAmount") || item.DisplayAmount ? item.CollectedAmount.ToString() : string.Empty;
                amount.color = (Color)Get(entry, item.IsAtMax() ? "maxAmountTextColor" : "regularAmountTextColor");
                if (item.CustomInventoryDisplay != null)
                {
                    CheckItem(item);
                    var display = Object.Instantiate(item.CustomInventoryDisplay, entry.IconTransform);
                    string clonedProblem = AuxiliaryProblem(display.gameObject);
                    if (clonedProblem != null) throw new InvalidOperationException("Inventory cloned custom icon changed: " + clonedProblem);
                    display.transform.localPosition = Vector3.zero; display.transform.localRotation = Quaternion.identity;
                    display.transform.localScale = item.CustomInventoryDisplay.transform.localScale;
                    display.Owner = entry;
                    PrepareAuxiliary(display.gameObject);
                    page.Text.Prepare(display.gameObject, page.Token.Content);
                    Set(entry, "currentCustomDisplay", display);
                }
                entry.gameObject.SetActive(true); Current(page.Token);
                typeof(InventoryItemUpdateable).GetMethod("UpdateDisplay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, null);
                (item.IsConsumable() ? consumable : regular).Add(entry);
            }
            var relicHeader = Get(page.Manager, "relicHeader") as Transform;
            var consumableHeader = Get(page.Manager, "consumableHeader") as Transform;
            if (relicHeader != null) relicHeader.gameObject.SetActive(false);
            if (consumableHeader != null) consumableHeader.gameObject.SetActive(false);
            var result = new List<InventoryItemGrid.GridSection>();
            if (regular.Count > 0) result.Add(new InventoryItemGrid.GridSection { Items = regular, HideHeaderIfNoneBefore = true });
            if (consumable.Count > 0) result.Add(new InventoryItemGrid.GridSection { Items = consumable, Header = consumableHeader, HideHeaderIfNoneBefore = true });
            return result;
        }
        static List<CollectableItem> ReadItems(DsJournalToken token)
        {
            var records = (CollectableItemManager)token.Records;
            var all = records.GetAllCollectables();
            if (all == null) throw new InvalidOperationException("Inventory native master list missing");
            var result = new List<CollectableItem>();
            var known = new HashSet<string>();
            var hero = HeroController.instance;
            bool bare = hero != null && hero.Config != null && hero.Config.ForceBareInventory;
            foreach (var item in all)
            {
                if (item == null || !known.Add(item.name)) throw new InvalidOperationException("Inventory native master list missing/duplicate item");
                // Native non-inserting SaveData/GetData reads and native virtual visibility.
                // No reported-collection enumeration or invalid-item asset construction.
                if (bare && !item.IsVisibleWithBareInventory && item.SaveData.AmountWhileHidden <= 0) continue;
                if (!item.IsVisible) continue;
                CheckItem(item); result.Add(item);
            }
            foreach (string name in ((PlayerData)token.Data).Collectables.GetValidNames())
                if (!known.Contains(name)) throw new InvalidOperationException("Inventory unknown collected item: " + name);
            return result;
        }
        public string Inspect(DsJournalToken token)
        {
            var manager = (InventoryItemCollectableManager)token.List;
            var template = Get(manager, "templateItem") as InventoryItemCollectable;
            var cursor = Get(manager, "cursorPrefab") as InventoryCursor;
            if (template == null || cursor == null) return "Inventory native entry/cursor donor missing";
            foreach (var root in new[] { ((InventoryPane)token.Pane).gameObject, template.gameObject, cursor.gameObject })
                InspectScope(root, template.gameObject, cursor.gameObject);
            ReadItems(token);
            NeedLocal(manager, ((InventoryPane)token.Pane).transform, "itemList", "nameText", "descriptionText", "descriptionLayout");
            return null;
        }
        public object CloneInactive(DsJournalToken token)
        {
            var page = new InventoryPage { Token = token };
            try
            {
                page.Staging = new GameObject("DsPortInventoryStaging"); page.Staging.SetActive(false);
                page.Staging.transform.SetParent((Transform)token.Host, false);
                page.Root = Object.Instantiate(((InventoryPane)token.Pane).gameObject, page.Staging.transform, false);
                var source = (InventoryItemCollectableManager)token.List;
                page.Template = Object.Instantiate(((InventoryItemCollectable)Get(source, "templateItem")).gameObject, page.Staging.transform, false);
                page.CursorTemplate = Object.Instantiate(((InventoryCursor)Get(source, "cursorPrefab")).gameObject, page.Staging.transform, false);
                page.Template.SetActive(false); page.CursorTemplate.SetActive(false);
                return page;
            }
            catch (Exception error) { page.CreationError = error; return page; }
        }
        public void BindAndVerify(DsJournalToken token, object clone)
        {
            var page = (InventoryPage)clone;
            if (page.CreationError != null) throw new InvalidOperationException("Inventory native page creation failed", page.CreationError);
            var old = page.Root.GetComponentInChildren<InventoryItemCollectableManager>(true);
            if (old == null || old.GetType() != typeof(InventoryItemCollectableManager)) throw new InvalidOperationException("Inventory native manager identity changed");
            // Replace only the typed enumeration hook on the inactive clone, never the
            // native entry/grid/layout/detail graph or any primary manager instance.
            page.Manager = old.gameObject.AddComponent<DsPortCollectableManager>();
            for (Type type = old.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsStatic && !field.IsNotSerialized && (field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField))))
                        field.SetValue(page.Manager, field.GetValue(old));
            page.Manager.BindReadOnlyItems(ReadItems(token));
            page.Manager.BindReadOnlySections((entries, items) => ReadOnlySections(page, entries, items));
            var sourceTemplate = (InventoryItemCollectable)Get(token.List, "templateItem");
            page.DetailCondition = sourceTemplate.GetComponent<InventoryItemExtraDescription>();
            if (page.DetailCondition != null)
            {
                var sourceParent = Get(page.DetailCondition, "descSectionParent") as Transform;
                var sourcePane = ((InventoryPane)token.Pane).transform;
                if (sourceParent != null)
                {
                    if (!Within(sourceParent, sourcePane) || Within(sourceParent, ((InventoryItemGrid)Get(token.List, "itemList")).transform))
                        throw new InvalidOperationException("Inventory detail destination escaped fixed pane scope");
                    var indices = new Stack<int>();
                    for (var node = sourceParent; node != sourcePane; node = node.parent) indices.Push(node.GetSiblingIndex());
                    page.DetailParent = page.Root.transform;
                    while (indices.Count != 0) page.DetailParent = page.DetailParent.GetChild(indices.Pop());
                }
            }
            Object.DestroyImmediate(old);
            Set(page.Manager, "templateItem", page.Template.GetComponent<InventoryItemCollectable>());
            Set(page.Manager, "cursorPrefab", page.CursorTemplate.GetComponent<InventoryCursor>());
            foreach (var root in new[] { page.Root, page.Template, page.CursorTemplate })
            {
                page.Text.Prepare(root, token.Content);
                foreach (var input in root.GetComponentsInChildren<InventoryPaneInput>(true)) Object.DestroyImmediate(input);
                foreach (var extra in root.GetComponentsInChildren<InventoryItemExtraDescription>(true)) Object.DestroyImmediate(extra);
                // This helper's OnDestroy clears a primary static selection flag. Unity
                // invokes OnDestroy only for objects active at least once: remove it
                // while every clone is still born-inactive, before any activation.
                foreach (var helper in root.GetComponentsInChildren<InventoryCollectableItemSelectionHelper>(true)) Object.DestroyImmediate(helper);
                foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var response in root.GetComponentsInChildren<PlayerDataTestResponse>(true))
                { var field = Field(response, "runOn"); field.SetValue(response, Enum.Parse(field.FieldType, "JustStart")); response.enabled = false; }
                foreach (var cursor in root.GetComponentsInChildren<InventoryCursor>(true))
                { var sound = (AudioEvent)Get(cursor, "changeSelectionSound"); sound.Volume = 0; Set(cursor, "changeSelectionSound", sound); }
                foreach (var clip in root.GetComponentsInChildren<TextMeshProClipRect>(true)) clip.enabled = false;
                Suppress(root, true); RouteOwned(root); InspectScope(root, page.Template, page.CursorTemplate);
            }
            Set(page.Manager, "selectableHelper", null);
            page.Grid = (InventoryItemGrid)Get(page.Manager, "itemList");
            page.Scroll = (ScrollView)Get(page.Grid, "scrollView");
            if (page.Scroll == null || !Within(page.Scroll.transform, page.Root.transform)) throw new InvalidOperationException("Inventory owned scroll missing");
            Current(token);
        }
        public void ActivateForLayout(DsJournalToken token, object clone)
        {
            var page = (InventoryPage)clone; Current(token);
            var scale = page.Staging.transform.parent.lossyScale;
            if (Mathf.Abs(scale.x) < .0001f || Mathf.Abs(scale.y) < .0001f || Mathf.Abs(scale.z) < .0001f) throw new InvalidOperationException("Inventory host scale invalid");
            page.Staging.transform.rotation = Quaternion.identity;
            page.Staging.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            page.Root.transform.localPosition = Vector3.zero; page.Root.transform.localRotation = Quaternion.identity; page.Root.transform.localScale = Vector3.one;
            page.Root.SetActive(true); Current(token); page.Staging.SetActive(true); Current(token);
            page.Text.Validate(page.Root);
            page.Activated = Time.frameCount;
            page.Cursor = Get(page.Manager, "cursor") as InventoryCursor;
            if (page.Cursor == null) throw new InvalidOperationException("Inventory native cursor missing after Awake");
            page.Cursor.gameObject.SetActive(true); Current(token); page.Cursor.gameObject.SetActive(false);
            foreach (var response in page.Root.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                var item = response.GetComponentInParent<InventoryItemCollectable>(true);
                var local = item != null ? item.transform : page.Root.transform;
                response.IsFullfilled = ReconstructEvent(response.IsFullfilled, local);
                response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, local);
                typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null); Current(token);
            }
            Suppress(page.Root, true);
        }
        public bool TrySettle(DsJournalToken token, object clone)
        {
            var page = (InventoryPage)clone; Suppress(page.Root, true);
            if (Time.frameCount <= page.Activated + 2) return false;
            Current(token);
            if (page.Manager.CurrentSelected != null) throw new InvalidOperationException("Inventory native selection unexpectedly changed");
            page.Entries = page.Grid.GetComponentsInChildren<InventoryItemCollectable>(true);
            foreach (var entry in page.Entries)
                if (entry.gameObject.activeInHierarchy && (entry.Item == null || entry.GetComponent<BoxCollider2D>() == null)) throw new InvalidOperationException("Inventory generated item/collider missing");
            Force(page);
            page.Scroll.StopAllCoroutines(); page.Scroll.enabled = false; page.Grid.StopAllCoroutines(); page.Grid.enabled = false;
            page.View = page.Scroll.ViewBounds; page.Content = (Bounds)Get(page.Scroll, "contentBounds"); page.Origin = page.Scroll.transform.localPosition;
            if (page.View.size.x <= 0 || page.View.size.y <= 0) throw new InvalidOperationException("Inventory viewport missing");
            page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y, page.Origin.y);
            NeutralizeOwnedClips(page.Root); RouteOwned(page.Root); Current(token); return true;
        }
        static void Force(InventoryPage page)
        {
            page.Text.Validate(page.Root, page.DetailRoot);
            if (page.DetailRoot != null) page.DetailText.Validate(page.DetailRoot);
            ((LayoutGroup)Get(page.Manager, "descriptionLayout")).ForceUpdateLayoutNoCanvas();
            foreach (var text in page.Root.GetComponentsInChildren<PaneText>(true)) text.ForceMeshUpdate(true);
        }
        bool Inside(InventoryPage page, Bounds world)
        {
            var host = (RectTransform)page.Token.Host; var mask = _frame.ContentMask;
            return mask != null && Contains(host.rect, InSpace(world, null, host)) && Contains(mask.rect, InSpace(world, null, mask));
        }
        void Fit(InventoryPage page)
        {
            page.Fit = InSpace(page.View, page.Scroll.transform.parent, page.Root.transform);
            foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && !Within(renderer.transform, page.Scroll.transform) && !Within(renderer.transform, page.Cursor.transform))
                    page.Fit.Encapsulate(InSpace(renderer.bounds, null, page.Root.transform));
            var rect = ((RectTransform)page.Token.Host).rect; CheckValue("Inventory fit", page.Fit, page.Root.transform);
            if (page.Fit.size.x <= 0 || page.Fit.size.y <= 0 || rect.width <= 0 || rect.height <= 0) throw new InvalidOperationException("Inventory fit invalid");
            page.Scale = Mathf.Min(rect.width / page.Fit.size.x, rect.height / page.Fit.size.y) * .94f;
            page.Staging.transform.localRotation = Quaternion.identity; page.Staging.transform.localScale = Vector3.one * page.Scale;
            page.Staging.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0) - page.Fit.center * page.Scale;
        }
        public void Present(DsJournalToken token, object clone)
        {
            var page = (InventoryPage)clone; Current(token);
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
            if (page.Selected != null && !page.Visible.Contains(page.Selected)) ClearSelection(page);
            Contain(page); Current(token);
        }
        void Contain(InventoryPage page)
        {
            var view = InSpace(page.View, page.Scroll.transform.parent, null); bool cursorFits = page.Selected != null;
            foreach (var renderer in page.Root.GetComponentsInChildren<Renderer>(true))
                if (Within(renderer.transform, page.Cursor.transform))
                { if (renderer.enabled && renderer.gameObject.activeInHierarchy) cursorFits &= Inside(page, renderer.bounds) && Contains(view, renderer.bounds); }
                else if (!Within(renderer.transform, page.Scroll.transform)) renderer.forceRenderingOff = !Inside(page, renderer.bounds);
            Suppress(page.Cursor.gameObject, !cursorFits);
            if (page.DetailRoot != null)
                foreach (var graphic in page.DetailRoot.GetComponentsInChildren<Graphic>(true))
                {
                    var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
                    var bounds = new Bounds(corners[0], Vector3.zero);
                    for (int i = 1; i < corners.Length; i++) bounds.Encapsulate(corners[i]);
                    graphic.canvasRenderer.cull = !Inside(page, bounds);
                }
        }
        public bool OnGesture(DsGesture gesture)
        {
            var page = _state.Owned as InventoryPage;
            if (!_state.Ready || page == null) return false;
            var host = (RectTransform)page.Token.Host; Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, gesture.Position, DsPresentation.UiCamera, out point) || !host.rect.Contains(point)) return false;
            try
            {
                if (!IsCurrent(page.Token)) { _state.Clear(); return true; }
                if (gesture.Type == DsGestureType.Up) { if (page.Consume == null || !page.Consume.Blocked) CancelConsume(page); return true; }
                if (gesture.Type == DsGestureType.Tap && _suppressReleaseTap) { _suppressReleaseTap = false; return true; }
                if (gesture.Type == DsGestureType.Drag)
                {
                    _hold.Liveness(false); CancelConsume(page);
                    page.Offset = DsJournalGeometry.ClampScroll(page.Content.min.y, page.Content.max.y, page.View.min.y, page.View.max.y,
                        page.Offset + DsJournalGeometry.LocalDrag(gesture.Delta.y, page.Scale));
                    ClearSelection(page); Present(page.Token, page); return true;
                }
                if (gesture.Type != DsGestureType.Tap && gesture.Type != DsGestureType.Down) return false;
                foreach (var entry in page.Visible)
                {
                    var b = InSpace(entry.GetComponent<BoxCollider2D>().bounds, null, host);
                    if (point.x >= b.min.x && point.x <= b.max.x && point.y >= b.min.y && point.y <= b.max.y)
                    {
                        if (gesture.Type == DsGestureType.Down) return BeginConsume(page, entry);
                        _selection.Select(page, entry, page.Token.Data); return true;
                    }
                }
                return false;
            }
            catch (Exception e) { _failed = DsPortPageSnapshot.IsReadFailure(e) ? null : page.Token; _state.Clear(); Report(e.GetBaseException().Message); return true; }
        }
        bool IDsPortSelection.IsCurrent(object owner, object item, object data)
        {
            var page = owner as InventoryPage; var entry = item as InventoryItemCollectable;
            return page != null && !page.Released && entry != null && entry.Item != null && IsCurrent(page.Token) &&
                ReferenceEquals(data, page.Token.Data) && page.Visible.Contains(entry) && Within(entry.transform, page.Grid.transform);
        }
        void RequireExtraBinding(InventoryPage page, InventoryItemCollectable entry, CollectableItem item, GameObject prefab)
        {
            Current(page.Token);
            var template = Get(page.Token.List, "templateItem") as InventoryItemCollectable;
            var conditions = template != null ? template.GetComponents<InventoryItemExtraDescription>() : null;
            if (!ReferenceEquals(entry.Item, item) || !ReferenceEquals(item.ExtraDescriptionSection, prefab) ||
                conditions == null || conditions.Length != 1 || !ReferenceEquals(conditions[0], page.DetailCondition) ||
                page.DetailCondition == null || !page.DetailCondition.WillDisplay)
                throw new InvalidOperationException("Inventory selected native extra source replaced");
            var target = Get(page.DetailCondition, "descSectionParent") as Transform;
            var source = ((InventoryPane)page.Token.Pane).transform;
            if (!Within(target, source) || Within(target, ((InventoryItemGrid)Get(page.Token.List, "itemList")).transform))
                throw new InvalidOperationException("Inventory native extra destination escaped fixed pane");
            var indices = new Stack<int>();
            for (var node = target; node != source; node = node.parent) indices.Push(node.GetSiblingIndex());
            var destination = page.Root.transform;
            while (indices.Count != 0) destination = destination.GetChild(indices.Pop());
            if (!ReferenceEquals(destination, page.DetailParent)) throw new InvalidOperationException("Inventory native extra destination replaced");
        }
        bool TryDisplayExtra(InventoryPage page, InventoryItemCollectable entry)
        {
            ClearExtra(page);
            var item = entry.Item;
            var prefab = item.ExtraDescriptionSection;
            if (prefab == null || page.DetailCondition == null || !page.DetailCondition.WillDisplay) return true;
            RequireExtraBinding(page, entry, item, prefab);
            var setupOwner = ExtraSetupOwner(item);
            string problem = AuxiliaryProblem(prefab, setupOwner);
            if (problem != null) throw new InvalidOperationException(problem);
            page.DetailStaging = new GameObject("DsPortInventoryDetail");
            page.DetailStaging.SetActive(false); page.DetailStaging.transform.SetParent(page.Staging.transform, false);
            var detail = Object.Instantiate(prefab, page.DetailStaging.transform, false);
            page.DetailRoot = detail;
            detail.SetActive(false);
            PrepareQuestExtra(detail, item);
            string clonedProblem = AuxiliaryProblem(detail, setupOwner);
            if (clonedProblem != null) throw new InvalidOperationException("Inventory cloned auxiliary changed: " + clonedProblem);
            PrepareAuxiliary(detail);
            OwnExtraMaterials(detail, page.DetailMaterials);
            page.DetailText.Prepare(detail, page.Token.Content);
            RequireExtraBinding(page, entry, item, prefab);
            // Native layouts inspect direct children; staging is only an inactive construction boundary.
            detail.transform.SetParent(page.DetailParent, false);
            detail.SetActive(true); RequireExtraBinding(page, entry, item, prefab);
            foreach (var response in detail.GetComponentsInChildren<PlayerDataTestResponse>(true))
            {
                response.IsFullfilled = ReconstructEvent(response.IsFullfilled, detail.transform);
                response.IsNotFulfilled = ReconstructEvent(response.IsNotFulfilled, detail.transform);
                typeof(PlayerDataTestResponse).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(response, null);
                RequireExtraBinding(page, entry, item, prefab);
            }
            if (ExtraSetupOwner(item) != setupOwner) throw new InvalidOperationException("Inventory native extra override replaced");
            item.SetupExtraDescription(detail); // Exact source-proven SavedItem or CollectableItemQuestDisplay display only.
            RequireExtraBinding(page, entry, item, prefab);
            page.DetailText.Validate(detail);
            return true;
        }
        static void ClearExtra(InventoryPage page)
        {
            if (page.DetailRoot != null)
            {
                foreach (var driver in page.DetailRoot.GetComponentsInChildren<MonoBehaviour>(true)) driver.StopAllCoroutines();
                page.DetailRoot.SetActive(false);
                Object.DestroyImmediate(page.DetailRoot); page.DetailRoot = null;
            }
            if (page.DetailStaging != null) { Object.DestroyImmediate(page.DetailStaging); page.DetailStaging = null; }
            page.DetailText.Clear();
            while (page.DetailMaterials.Count != 0)
            {
                int last = page.DetailMaterials.Count - 1;
                if (page.DetailMaterials[last] != null) Object.DestroyImmediate(page.DetailMaterials[last]);
                page.DetailMaterials.RemoveAt(last);
            }
        }
        void IDsPortSelection.Display(object owner, object item)
        {
            var page = (InventoryPage)owner; var entry = (InventoryItemCollectable)item;
            if (page.Consume != null) CancelConsume(page);
            Current(page.Token); HideMemoryUseMsg(page, true); ClearPrompts(page);
            if (!DsPortDetailAttempt.Try(() => !page.Released && IsCurrent(page.Token),
                () => TryDisplayExtra(page, entry), () => ClearSelection(page),
                error => Report("Inventory detail unavailable for " + entry.Item.name + ": " + error.Message))) return;
            page.Manager.SetDisplay((InventoryItemSelectable)entry); Current(page.Token);
            // Native prompt formatting only here; consumption requires a fresh explicit hold.
            typeof(InventoryItemCollectable).GetMethod("DisplayPromptData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, null); Current(page.Token);
            Force(page); Fit(page); RouteOwned(page.Root); NeutralizeOwnedClips(page.Root);
            var world = InSpace(page.View, page.Scroll.transform.parent, null); page.Cursor.SetClampedPos(world.min, world.max);
            page.Cursor.Activate(); Current(page.Token); page.Cursor.SetTarget(entry.transform); Current(page.Token);
            page.Selected = entry; Contain(page);
        }
        bool IDsPortSelection.CanSubmit(object owner, object item)
        {
            var page = owner as InventoryPage; var entry = item as InventoryItemCollectable;
            return page != null && entry != null && ReferenceEquals(page.Selected, entry) && entry.Item != null &&
                IsCurrent(page.Token) && entry.Item.IsConsumable() && entry.Item.CollectedAmount > 0 && entry.Item.CanConsumeRightNow();
        }
        bool IDsPortSelection.Submit(object owner, object item) => BeginConsume((InventoryPage)owner, (InventoryItemCollectable)item);
        static void ClearPrompts(InventoryPage page)
        { if (page.Root != null) foreach (var prompts in page.Root.GetComponentsInChildren<InventoryItemButtonPromptDisplayList>(true)) prompts.Clear(); }
        public void ClearSelection(object clone)
        {
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Inventory selection teardown deferred until native callback unwind");
            var page = (InventoryPage)clone; CancelConsume(page); HideMemoryUseMsg(page, true); _selection.Clear(); page.Selected = null; ClearPrompts(page); ClearExtra(page);
            if (page.Cursor != null) { page.Cursor.StopAllCoroutines(); page.Cursor.SetTarget(null); page.Cursor.Deactivate(); }
            if (page.Manager != null) page.Manager.SetDisplay((GameObject)null);
        }
        public void DestroyOwned(object clone)
        {
            if (_actions.DeferRetirement()) throw new InvalidOperationException("Inventory page teardown deferred until native callback unwind");
            var page = (InventoryPage)clone; if (page.Destroyed) return; CancelConsume(page); page.Released = true;
            _actions.Run(() => RetireConsumeVisuals(page));
            Suppress(page.Root, true);
            ClearExtra(page);
            DsJournalAdmission.ReleaseOwned(
                () => { if (page.Staging != null) foreach (var driver in page.Staging.GetComponentsInChildren<MonoBehaviour>(true))
                    if (driver != null && !page.Stopped.Contains(driver)) { driver.StopAllCoroutines(); page.Stopped.Add(driver); } },
                () => { if (page.Staging != null && page.Staging.activeSelf) page.Staging.SetActive(false); },
                () => { if (page.Staging != null) Object.DestroyImmediate(page.Staging); page.Staging = null; });
            page.Text.Clear();
            foreach (var material in page.ConsumeMaterials) if (material != null) Object.DestroyImmediate(material);
            page.ConsumeMaterials.Clear(); page.PreparedSignals.Clear();
            page.Destroyed = true;
        }
    }
}
#endif
