#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using DualSouls.Mods;
using DualSouls.Skins;
using GlobalEnums;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
// Silksong's native Options prefab uses UnityEngine.UI.Text. The lower HUD uses
// TMProOld, but cloned menu rows must keep the native component type.
using UiText = UnityEngine.UI.Text;
using UnityObject = UnityEngine.Object;

namespace DualSouls.Mods.Silksong
{
    /// <summary>
    /// Adds a paused-game-only entry to Silksong's resident Options screen and
    /// presents the process-owned typed Mods model with the game's own menu art,
    /// selection, audio, fade, and controller event paths.
    /// </summary>
    public sealed class SilksongNativeModsMenu : MonoBehaviour
    {
        const int VisibleRows = TweakMenuPresenterLayout.VisibleRows;
        const int MaximumSkinSnapshotBytes = 262144;
        const string SkinProfileId = "silksong";
        const float EntryY = -575f;
        const float FirstRowY = -25f;
        const float RowStep = TweakMenuPresenterLayout.FixedRowStep;
        const float ButtonTextHorizontalInset = 80f;
        const float DescriptionHorizontalInset = 220f;
        const int DescriptionMinimumFontSize = TweakMenuPresenterLayout.DescriptionMinimumFontSize;
        const int DescriptionFontSize = TweakMenuPresenterLayout.DescriptionFontSize;
        const float DescriptionHeight = TweakMenuPresenterLayout.DescriptionHeight;
        const float DescriptionVisualIndex = TweakMenuPresenterLayout.DescriptionVisualIndex;
        const float ResetVisualIndex = TweakMenuPresenterLayout.ResetVisualIndex;
        const float BackVisualIndex = TweakMenuPresenterLayout.BackVisualIndex;

        internal enum NativeMenuRoute { Mods, Skins }
        enum ButtonRole { Group, Row, Reset, Back }

        sealed class NativeMenuBinding
        {
            public NativeMenuBinding(UIManager ui, MenuScreen optionsScreen,
                                     MenuScreen modsScreen, MenuScreen skinsScreen,
                                     TweakSession session, TweakMenuModel menu,
                                     GameObject modsEntryRoot, GameObject skinsEntryRoot)
            {
                Ui = ui;
                OptionsScreen = optionsScreen;
                ModsScreen = modsScreen;
                SkinsScreen = skinsScreen;
                Session = session;
                Menu = menu;
                ModsEntryRoot = modsEntryRoot;
                SkinsEntryRoot = skinsEntryRoot;
            }

            public UIManager Ui { get; }
            public MenuScreen OptionsScreen { get; }
            public MenuScreen ModsScreen { get; }
            public MenuScreen SkinsScreen { get; }
            public TweakSession Session { get; }
            public TweakMenuModel Menu { get; }
            public GameObject ModsEntryRoot { get; }
            public GameObject SkinsEntryRoot { get; }
        }

        static SilksongNativeModsMenu _current;

        readonly List<SilksongNativeModsButton> _buttons =
            new List<SilksongNativeModsButton>();
        readonly List<GameObject> _buttonRoots = new List<GameObject>();
        readonly List<UiText> _labels = new List<UiText>();
        readonly List<UiText> _valueLabels = new List<UiText>();
        readonly List<SilksongNativeSkinButton> _skinButtons =
            new List<SilksongNativeSkinButton>();
        readonly List<GameObject> _skinButtonRoots = new List<GameObject>();
        readonly List<UiText> _skinLabels = new List<UiText>();
        readonly List<SilksongGameplayFeatures.BenchRecord> _benchRows =
            new List<SilksongGameplayFeatures.BenchRecord>();
        readonly SilksongNativeMenuLifecycle<NativeMenuBinding> _lifecycle =
            new SilksongNativeMenuLifecycle<NativeMenuBinding>();

        readonly List<GameObject> _ownedRoots = new List<GameObject>();
        readonly HashSet<GameObject> _retiredRoots = new HashSet<GameObject>();
        readonly Dictionary<MenuButton, Navigation> _originalNavigation = new Dictionary<MenuButton, Navigation>();
        GameObject _stagingRoot;
        GameObject _selectionBeforeOpen;
        int _bindingGeneration;
        SilksongNativeMenuTransition<NativeMenuBinding> _activeTransition;
        TweakSession _session;
        TweakMenuModel _menu;
        NativeSkinMenuModel _skinMenu;
        bool _skinSnapshotReady;
        readonly SkinNativeTransportWindow _skinTransport = new SkinNativeTransportWindow();
        AndroidJavaClass _skinBridge;
        UIManager _ui;
        MenuScreen _modsScreen;
        MenuScreen _skinsScreen;
        GameObject _entryRoot;
        GameObject _skinsEntryRoot;
        SilksongNativeModsEntryButton _entryButton;
        SilksongNativeModsEntryButton _skinsEntryButton;
        MenuButton _entrySelectable;
        MenuButton _skinsEntrySelectable;
        UiText _title;
        UiText _description;
        UiText _skinsTitle;
        UiText _skinsDescription;
        GameObject _descriptionRoot;
        GameObject _skinsDescriptionRoot;
        string _skinError = "";
        MenuButton _gameButton;
        MenuButton _audioButton;
        MenuButton _videoButton;
        MenuButton _controllerButton;
        MenuButton _keyboardButton;
        Coroutine _transitionCoroutine;
        NativeMenuRoute _openRoute;
        ButtonRole _focusedRole = ButtonRole.Group;
        long _benchOperationToken;
        int _benchSelected;
        int _benchWindowStart;
        bool _benchOpen;
        string _benchError = "";
        bool _nativeOpen;
        bool _topologyWarningLogged;
        float _nextBindAttempt;

        void Awake()
        {
            _current = this;
        }

        public static void OpenBenchTeleportRoute(long operationToken)
        {
            if (operationToken <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationToken));
            SilksongNativeModsMenu current = _current;
            if (current == null || !current._nativeOpen ||
                current._openRoute != NativeMenuRoute.Mods ||
                current._lifecycle.Transitioning)
                throw new InvalidOperationException(
                    "The native Silksong Mods menu is not ready for Bench Teleport.");
            current._benchOperationToken = operationToken;
            try
            {
                current.OpenBenchRoute();
            }
            catch
            {
                current._benchOperationToken = 0;
                throw;
            }
        }

        void Update()
        {
            if (_skinTransport.Due(Time.unscaledTime)) RefreshSkinMenu();
            if (!BindingIsAlive())
            {
                CancelAndClearBinding();
                if (Time.unscaledTime >= _nextBindAttempt)
                {
                    _nextBindAttempt = Time.unscaledTime + 0.5f;
                    TryBind();
                }
                return;
            }

            NativeMenuBinding binding = _lifecycle.Current;
            bool available = binding.Session.IsReady && binding.Ui.uiState == UIState.PAUSED;
            if (binding.ModsEntryRoot.activeSelf != available)
                binding.ModsEntryRoot.SetActive(available);
            if (binding.SkinsEntryRoot.activeSelf != available)
                binding.SkinsEntryRoot.SetActive(available);
            WireOptionsNavigation(available);
            if (!available && (_nativeOpen || _lifecycle.Transitioning)) {
                SuspendRoutes(binding);
                return;
            }

            if (_nativeOpen)
            {
                if (!available && !_lifecycle.Transitioning)
                    BeginClose(binding, showOptions: false);
                else if (_openRoute == NativeMenuRoute.Mods)
                    Paint();
                else
                    PaintSkins();
            }
        }

        bool BindingIsAlive()
        {
            return BindingIsAlive(_lifecycle.Current);
        }

        bool BindingIsAlive(NativeMenuBinding binding)
        {
            SilksongModsRuntime runtime = SilksongModsRuntime.Current;
            return binding != null && runtime != null && ReferenceEquals(runtime.Session, binding.Session) &&
                   _gameButton != null && ReferenceEquals(_lifecycle.Current, binding) &&
                   binding.Ui != null && binding.OptionsScreen != null &&
                   binding.ModsScreen != null && binding.SkinsScreen != null &&
                   binding.ModsEntryRoot != null && binding.SkinsEntryRoot != null &&
                   binding.Session != null && binding.Menu != null &&
                   ReferenceEquals(binding.Ui, _ui) &&
                   ReferenceEquals(binding.OptionsScreen, _ui.optionsMenuScreen) &&
                   ReferenceEquals(binding.ModsScreen, _modsScreen) &&
                   ReferenceEquals(binding.SkinsScreen, _skinsScreen) &&
                   ReferenceEquals(binding.ModsEntryRoot, _entryRoot) &&
                   ReferenceEquals(binding.SkinsEntryRoot, _skinsEntryRoot) &&
                   ReferenceEquals(binding.Session, _session) &&
                   ReferenceEquals(binding.Menu, _menu);
        }

        void TryBind()
        {
            SilksongModsRuntime runtime = SilksongModsRuntime.Current;
            TweakSession session = runtime != null ? runtime.Session : null;
            if (session == null || !session.IsReady) return;

            UIManager manager = FindResidentUiManager();
            if (manager == null || manager.UICanvas == null ||
                manager.optionsMenuScreen == null) return;
            if (_lifecycle.Current != null || _ui != null || _entryRoot != null ||
                _skinsEntryRoot != null || _modsScreen != null || _skinsScreen != null)
            {
                if (BindingIsAlive() && ReferenceEquals(_ui, manager)) return;
                CancelAndClearBinding();
            }

            try
            {
                _bindingGeneration++;
                _retiredRoots.RemoveWhere(root => root == null);
                Transform options = manager.optionsMenuScreen.transform;
                Transform content = options.Find("Content");
                Transform template = options.Find("Content/GameOptions");
                Transform templateButton = options.Find("Content/GameOptions/GameOptionsButton");
                Transform game = templateButton;
                Transform audio = options.Find("Content/AudioOptions/AudioOptionsButton");
                Transform video = options.Find("Content/VideoOptions/VideoOptionsButton");
                Transform controller = options.Find("Content/ControllerOptions/GamepadOptionsButton");
                Transform keyboard = options.Find("Content/KeyboardOptions/KeyboardOptionsButton");
                if (content == null || template == null || game == null || audio == null ||
                    video == null || controller == null || keyboard == null)
                    throw new InvalidOperationException(
                        "Silksong 1.0.29980 Options menu topology is unavailable.");

                ValidateOptionsTopology(content, manager, game);
                RequireNativeButton(template.gameObject);
                _gameButton = RequireMenuButton(game, "GameOptionsButton");
                _audioButton = RequireMenuButton(audio, "AudioOptionsButton");
                _videoButton = RequireMenuButton(video, "VideoOptionsButton");
                _controllerButton = RequireMenuButton(controller, "GamepadOptionsButton");
                _keyboardButton = RequireMenuButton(keyboard, "KeyboardOptionsButton");

                RememberNavigation(_gameButton);
                RememberNavigation(_audioButton);
                RememberNavigation(_videoButton);
                RememberNavigation(_controllerButton);
                RememberNavigation(_keyboardButton);
                _session = session;
                _menu = session.Menu;
                _ui = manager;
                _stagingRoot = new GameObject("NativeModsMenuStaging");
                _ownedRoots.Add(_stagingRoot);
                _stagingRoot.SetActive(false);
                BuildRouteEntry(content, template.gameObject, EntryY,
                                NativeMenuRoute.Mods, "MODS");
                BuildRouteEntry(content, template.gameObject, EntryY - RowStep,
                                NativeMenuRoute.Skins, "SKINS");
                BuildModsScreen(options.gameObject, template.gameObject);
                BuildSkinsScreen(options.gameObject, template.gameObject);
                _lifecycle.Bind(new NativeMenuBinding(
                    manager, manager.optionsMenuScreen, _modsScreen, _skinsScreen,
                    _session, _menu, _entryRoot, _skinsEntryRoot));
                _topologyWarningLogged = false;
                Debug.Log("[Silksong Mods] bound native paused Options -> Mods/Skins routes.");
            }
            catch (Exception error)
            {
                CancelAndClearBinding();
                if (!_topologyWarningLogged)
                {
                    _topologyWarningLogged = true;
                    Debug.LogError("[Silksong Mods] native menu capability unavailable: " +
                                   error.GetBaseException().Message);
                }
            }
        }

        static UIManager FindResidentUiManager()
        {
            UIManager[] managers = Resources.FindObjectsOfTypeAll<UIManager>();
            UIManager match = null;
            for (int i = 0; i < managers.Length; i++)
            {
                UIManager candidate = managers[i];
                if (candidate == null || candidate.gameObject == null) continue;
                if (!candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded)
                    continue;
                if (candidate.optionsMenuScreen == null || candidate.UICanvas == null) continue;
                if (match != null && !ReferenceEquals(match, candidate)) return null;
                match = candidate;
            }
            return match;
        }

        void ValidateOptionsTopology(Transform content, UIManager manager, Transform game)
        {
            string[] nativeRoutes = { "GameOptions", "AudioOptions", "VideoOptions", "ControllerOptions", "KeyboardOptions" };
            int[] counts = new int[nativeRoutes.Length];
            for (int i = 0; i < content.childCount; i++) {
                GameObject direct = content.GetChild(i).gameObject;
                if (_retiredRoots.Contains(direct)) continue;
                if (direct.name == "MODS" || direct.name == "SKINS")
                    throw new InvalidOperationException("Options already contains a foreign MODS or SKINS route.");
                for (int route = 0; route < nativeRoutes.Length; route++)
                    if (direct.name == nativeRoutes[route]) counts[route]++;
            }
            for (int route = 0; route < counts.Length; route++)
                if (counts[route] != 1)
                    throw new InvalidOperationException("Options needs exactly one native " + nativeRoutes[route] + " route.");
            MenuButton authority = null;
            MenuButton[] buttons = content.GetComponentsInChildren<MenuButton>(true);
            for (int i = 0; i < buttons.Length; i++) {
                MenuButton button = buttons[i];
                if (button == null || !HasGameOptionsAuthority(button, manager)) continue;
                bool retired = false;
                foreach (GameObject root in _retiredRoots)
                    if (root != null && button.transform.IsChildOf(root.transform)) { retired = true; break; }
                if (retired) continue;
                if (authority != null)
                    throw new InvalidOperationException("Options has ambiguous native GAMEOPTIONS authority.");
                authority = button;
            }
            if (authority == null || !ReferenceEquals(authority.transform, game))
                throw new InvalidOperationException("Options has no exact native GAMEOPTIONS route.");
        }

        static bool HasGameOptionsAuthority(MenuButton button, UIManager manager)
        {
            UnityEvent submit = button.OnSubmitPressed;
            if (submit != null)
                for (int i = 0; i < submit.GetPersistentEventCount(); i++)
                    if (ReferenceEquals(submit.GetPersistentTarget(i), manager) &&
                        submit.GetPersistentMethodName(i) == nameof(UIManager.UIGoToGameOptionsMenu)) return true;
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null) return false;
            for (int i = 0; i < trigger.triggers.Count; i++) {
                EventTrigger.Entry entry = trigger.triggers[i];
                if (entry == null || (entry.eventID != EventTriggerType.Submit &&
                    entry.eventID != EventTriggerType.PointerClick) || entry.callback == null) continue;
                for (int j = 0; j < entry.callback.GetPersistentEventCount(); j++)
                    if (ReferenceEquals(entry.callback.GetPersistentTarget(j), manager) &&
                        entry.callback.GetPersistentMethodName(j) == nameof(UIManager.UIGoToGameOptionsMenu)) return true;
            }
            return false;
        }

        static MenuButton RequireNativeButton(GameObject root)
        {
            Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);
            if (selectables.Length != 1 || !(selectables[0] is MenuButton))
                throw new InvalidOperationException("Native control must contain exactly one MenuButton-derived Selectable.");
            return (MenuButton)selectables[0];
        }

        GameObject CloneOwned(GameObject template, Transform parent, bool inactive = false)
        {
            GameObject root = Instantiate(template, _stagingRoot.transform, false);
            _ownedRoots.Add(root); // own before fallible work, with OnEnable suppressed by staging
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            if (!inactive) root.SetActive(true); // submenu parent is still inactive
            return root;
        }

        void RememberNavigation(MenuButton button)
        {
            _originalNavigation.Add(button, button.navigation);
        }

        void RestoreOptionsNavigation()
        {
            foreach (KeyValuePair<MenuButton, Navigation> item in _originalNavigation)
                if (item.Key != null) item.Key.navigation = item.Value;
        }

        static MenuButton RequireMenuButton(Transform transform, string name)
        {
            MenuButton button = transform.GetComponent<MenuButton>();
            RequireNativeButton(transform.gameObject);
            if (button == null)
                throw new InvalidOperationException(name + " is not a typed MenuButton.");
            return button;
        }

        void BuildRouteEntry(Transform content, GameObject template, float y,
                             NativeMenuRoute route, string label)
        {
            GameObject root = CloneOwned(template, content, inactive: true);
            root.name = label;
            root.SetActive(false);
            if (route == NativeMenuRoute.Mods)
                _entryRoot = root;
            else
                _skinsEntryRoot = root;
            RectTransform wrapper = root.transform as RectTransform;
            wrapper.anchoredPosition = new Vector2(wrapper.anchoredPosition.x, y);

            MenuButton source = RequireNativeButton(root);
            if (source == null)
                throw new InvalidOperationException(label + " entry template has no MenuButton.");
            PrepareNativeButton(source);
            var button = source.gameObject.AddComponent<SilksongNativeModsEntryButton>();
            button.Generation = _bindingGeneration;
            button.Owner = this;
            button.Selectable = source;
            button.Route = (int)route;
            DisableForeignDrivers(root);
            SetButtonText(root, label);
            if (route == NativeMenuRoute.Mods)
            {
                _entryButton = button;
                _entrySelectable = source;
            }
            else
            {
                _skinsEntryButton = button;
                _skinsEntrySelectable = source;
            }
        }

        void BuildModsScreen(GameObject optionsScreen, GameObject rowTemplate)
        {
            GameObject root = CloneOwned(optionsScreen, _ui.UICanvas.transform, inactive: true);
            root.name = "ModsMenuScreen";
            root.SetActive(false);

            _modsScreen = root.GetComponent<MenuScreen>();
            if (_modsScreen == null)
                throw new InvalidOperationException("Options screen clone has no typed MenuScreen.");
            MenuButtonList oldList = root.GetComponent<MenuButtonList>();
            if (oldList != null) oldList.enabled = false;
            _modsScreen.backButton = null;

            Transform title = root.transform.Find("Title");
            _title = title != null ? title.GetComponentInChildren<UiText>(true) : null;
            if (_title == null) throw new InvalidOperationException("Options title text is unavailable.");

            Transform controls = root.transform.Find("Controls");
            if (controls != null) controls.gameObject.SetActive(false);
            Transform content = root.transform.Find("Content");
            if (content == null) throw new InvalidOperationException("Options content root is unavailable.");
            DisableInheritedButtonsOutsideContent(root, content);
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                UnityObject.Destroy(child);
            }

            CreateButton(content, rowTemplate, ButtonRole.Group,
                         TweakMenuPresenterLayout.GroupButtonIndex, "CATEGORY");
            for (int i = 0; i < VisibleRows; i++)
                CreateButton(content, rowTemplate, ButtonRole.Row,
                             TweakMenuPresenterLayout.FirstRowButtonIndex + i, "MOD");
            CreateDescription(content, rowTemplate, DescriptionVisualIndex);
            CreateButton(content, rowTemplate, ButtonRole.Reset, ResetVisualIndex,
                         "RESET ALL MODS");
            CreateButton(content, rowTemplate, ButtonRole.Back, BackVisualIndex, "BACK");

            DisableForeignDrivers(root);
            _modsScreen.defaultHighlight =
                _buttons[TweakMenuPresenterLayout.GroupButtonIndex].Selectable;
            _title.text = "MODS";
            root.SetActive(false);
        }

        void BuildSkinsScreen(GameObject optionsScreen, GameObject rowTemplate)
        {
            GameObject root = CloneOwned(optionsScreen, _ui.UICanvas.transform, inactive: true);
            root.name = "SkinsMenuScreen";
            root.SetActive(false);
            _skinsScreen = root.GetComponent<MenuScreen>();
            if (_skinsScreen == null)
                throw new InvalidOperationException("Options screen clone has no typed MenuScreen.");
            MenuButtonList oldList = root.GetComponent<MenuButtonList>();
            if (oldList != null) oldList.enabled = false;
            _skinsScreen.backButton = null;
            Transform title = root.transform.Find("Title");
            _skinsTitle = title != null ? title.GetComponentInChildren<UiText>(true) : null;
            if (_skinsTitle == null)
                throw new InvalidOperationException("Skins title text is unavailable.");
            Transform controls = root.transform.Find("Controls");
            if (controls != null) controls.gameObject.SetActive(false);
            Transform content = root.transform.Find("Content");
            if (content == null)
                throw new InvalidOperationException("Skins content root is unavailable.");
            DisableInheritedButtonsOutsideContent(root, content);
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                UnityObject.Destroy(child);
            }
            for (int index = 0; index < VisibleRows + 3; index++)
                CreateSkinButton(content, rowTemplate, index);
            CreateSkinDescription(content, rowTemplate, VisibleRows + 3);
            DisableForeignDrivers(root);
            _skinsScreen.defaultHighlight = _skinButtons[0].Selectable;
            _skinsTitle.text = "SKINS";
            root.SetActive(false);
        }

        void CreateSkinButton(Transform parent, GameObject template, int visualIndex)
        {
            GameObject wrapper = CloneOwned(template, parent);
            wrapper.name = "SkinsRow" + visualIndex;
            RectTransform rect = wrapper.transform as RectTransform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                FirstRowY - RowStep * visualIndex);
            MenuButton source = RequireNativeButton(wrapper);
            if (source == null)
                throw new InvalidOperationException("Native Skins row has no MenuButton.");
            PrepareNativeButton(source);
            var button = source.gameObject.AddComponent<SilksongNativeSkinButton>();
            button.Generation = _bindingGeneration;
            button.Owner = this;
            button.Selectable = source;
            source.navigation = new Navigation { mode = Navigation.Mode.None };
            RectTransform buttonRect = source.transform as RectTransform;
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(
                    buttonRect.sizeDelta.x,
                    TweakMenuPresenterLayout.FixedRowButtonHeight);
            DisableForeignDrivers(wrapper);
            _skinButtonRoots.Add(wrapper);
            _skinButtons.Add(button);
            _skinLabels.Add(SetButtonText(wrapper, "SKIN"));
        }

        void CreateDescription(Transform parent, GameObject template, float visualIndex)
        {
            _descriptionRoot = CreateDescriptionRow(
                this, parent, template, "ModsDescription", visualIndex, out _description);
        }

        void CreateSkinDescription(Transform parent, GameObject template, int visualIndex)
        {
            _skinsDescriptionRoot = CreateDescriptionRow(
                this, parent, template, "SkinsDescription", visualIndex, out _skinsDescription);
        }

        static GameObject CreateDescriptionRow(SilksongNativeModsMenu owner,
                                               Transform parent, GameObject template, string name,
                                               float visualIndex, out UiText label)
        {
            GameObject wrapper = owner.CloneOwned(template, parent);
            wrapper.name = name;
            RectTransform rect = wrapper.transform as RectTransform;
            if (rect != null)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                    FirstRowY - RowStep * visualIndex);
            MenuButton source = RequireNativeButton(wrapper);
            if (source == null)
                throw new InvalidOperationException("Native description row has no MenuButton.");
            source.interactable = false;
            source.enabled = false;
            source.navigation = new Navigation { mode = Navigation.Mode.None };
            DisableForeignDrivers(wrapper);
            label = SetButtonText(wrapper, "Choose a category.");
            ConfigureDescription(label);
            return wrapper;
        }

        void CreateButton(Transform parent, GameObject template, ButtonRole role,
                          float visualIndex, string initialText)
        {
            GameObject wrapper = CloneOwned(template, parent);
            wrapper.name = "Mods" + role + visualIndex;
            RectTransform rect = wrapper.transform as RectTransform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                FirstRowY - RowStep * visualIndex);

            MenuButton source = RequireNativeButton(wrapper);
            if (source == null) throw new InvalidOperationException("Native Mods row has no MenuButton.");
            PrepareNativeButton(source);
            SilksongNativeModsButton button =
                source.gameObject.AddComponent<SilksongNativeModsButton>();
            button.Generation = _bindingGeneration;
            button.Owner = this;
            button.Selectable = source;
            button.Role = (int)role;
            source.navigation = new Navigation { mode = Navigation.Mode.None };

            RectTransform buttonRect = source.transform as RectTransform;
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(
                    buttonRect.sizeDelta.x,
                    TweakMenuPresenterLayout.FixedRowButtonHeight);
            DisableForeignDrivers(wrapper);
            UiText label = SetButtonText(wrapper, initialText);
            UiText valueLabel = null;
            if (role == ButtonRole.Group || role == ButtonRole.Row)
            {
                valueLabel = CloneColumnText(label, "ModsValue");
                ConfigureColumn(label, rightAligned: false);
                ConfigureColumn(valueLabel, rightAligned: true);
                valueLabel.text = "";
            }
            _buttonRoots.Add(wrapper);
            _buttons.Add(button);
            _labels.Add(label);
            _valueLabels.Add(valueLabel);
        }

        static void PrepareNativeButton(MenuButton button)
        {
            button.enabled = false;
            button.buttonType = MenuButton.MenuButtonType.Activate;
            button.cancelAction = CancelAction.DoNothing;
            button.OnSubmitPressed = new UnityEvent();
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger != null) trigger.triggers = new List<EventTrigger.Entry>();
            button.enabled = true;
        }

        static UiText SetButtonText(GameObject root, string value)
        {
            Transform named = FindDescendant(root.transform, "Menu Button Text");
            UiText text = named != null
                ? named.GetComponentInChildren<UiText>(true)
                : null;
            if (text == null) text = root.GetComponentInChildren<UiText>(true);
            if (text == null)
                throw new InvalidOperationException("Native button text is unavailable.");
            text.text = value;
            return text;
        }

        UiText CloneColumnText(UiText source, string name)
        {
            GameObject clone = CloneOwned(source.gameObject, source.transform.parent);
            clone.name = name;
            UiText text = clone.GetComponent<UiText>();
            if (text == null)
                throw new InvalidOperationException("Cloned native text is unavailable.");
            return text;
        }

        static void ConfigureColumn(UiText text, bool rightAligned)
        {
            RectTransform rect = text.transform as RectTransform;
            if (rect != null)
                rect.sizeDelta = new Vector2(-ButtonTextHorizontalInset, rect.sizeDelta.y);
            text.alignment = rightAligned
                ? TextAnchor.MiddleRight
                : TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        static void ConfigureDescription(UiText text)
        {
            RectTransform rect = text.transform as RectTransform;
            if (rect != null)
                rect.sizeDelta = new Vector2(-DescriptionHorizontalInset, DescriptionHeight);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = DescriptionMinimumFontSize;
            text.resizeTextMaxSize = DescriptionFontSize;
            text.fontSize = DescriptionFontSize;
        }

        static Transform FindDescendant(Transform parent, string exactName)
        {
            if (parent == null) return null;
            if (string.Equals(parent.name, exactName, StringComparison.Ordinal)) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform match = FindDescendant(parent.GetChild(i), exactName);
                if (match != null) return match;
            }
            return null;
        }

        static void DisableInheritedButtonsOutsideContent(GameObject root, Transform content)
        {
            MenuButton[] buttons = root.GetComponentsInChildren<MenuButton>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                MenuButton button = buttons[index];
                if (button != null && !button.transform.IsChildOf(content))
                    button.gameObject.SetActive(false);
            }
        }

        static void DisableForeignDrivers(GameObject root)
        {
            Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour is Animator || behaviour is Graphic ||
                    behaviour is CanvasGroup || behaviour is MenuScreen ||
                    behaviour is MenuButton ||
                    behaviour is SilksongNativeModsButton ||
                    behaviour is SilksongNativeSkinButton ||
                    behaviour is SilksongNativeModsEntryButton) continue;
                EventTrigger trigger = behaviour as EventTrigger;
                if (trigger != null) trigger.triggers = new List<EventTrigger.Entry>();
                behaviour.enabled = false;
            }
        }

        void WireOptionsNavigation(bool includeMods)
        {
            if (!includeMods) { RestoreOptionsNavigation(); return; }
            if (_gameButton == null || _audioButton == null || _videoButton == null ||
                _controllerButton == null || _keyboardButton == null) return;
            SetVertical(_gameButton, includeMods ? (Selectable)_skinsEntrySelectable : _keyboardButton,
                        _audioButton);
            SetVertical(_audioButton, _gameButton, _videoButton);
            SetVertical(_videoButton, _audioButton, _controllerButton);
            SetVertical(_controllerButton, _videoButton, _keyboardButton);
            SetVertical(_keyboardButton, _controllerButton,
                        includeMods ? (Selectable)_entrySelectable : _gameButton);
            if (includeMods && _entrySelectable != null && _skinsEntrySelectable != null)
            {
                SetVertical(_entrySelectable, _keyboardButton, _skinsEntrySelectable);
                SetVertical(_skinsEntrySelectable, _entrySelectable, _gameButton);
            }
        }

        static void SetVertical(Selectable selectable, Selectable up, Selectable down)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            selectable.navigation = navigation;
        }

        internal bool DriverIsCurrent(MonoBehaviour driver, int generation)
        {
            NativeMenuBinding binding = _lifecycle.Current;
            if (driver == null || generation != _bindingGeneration || !BindingIsAlive(binding) ||
                !binding.Session.IsReady || binding.Ui.uiState != UIState.PAUSED ||
                !driver.gameObject.activeInHierarchy) return false;
            SilksongNativeModsEntryButton entry = driver as SilksongNativeModsEntryButton;
            if (entry != null) return !_nativeOpen &&
                (ReferenceEquals(entry, _entryButton) || ReferenceEquals(entry, _skinsEntryButton));
            SilksongNativeModsButton mod = driver as SilksongNativeModsButton;
            if (mod != null) return _nativeOpen && _openRoute == NativeMenuRoute.Mods && _buttons.Contains(mod);
            SilksongNativeSkinButton skin = driver as SilksongNativeSkinButton;
            return skin != null && _nativeOpen && _openRoute == NativeMenuRoute.Skins && _skinButtons.Contains(skin);
        }

        internal void Open(NativeMenuRoute route)
        {
            NativeMenuBinding binding = _lifecycle.Current;
            GameObject entry = route == NativeMenuRoute.Mods
                ? binding?.ModsEntryRoot
                : binding?.SkinsEntryRoot;
            if (_nativeOpen || !BindingIsAlive(binding) || entry == null ||
                !entry.activeInHierarchy || !binding.Session.IsReady ||
                binding.Ui.uiState != UIState.PAUSED ||
                !_lifecycle.TryBegin(out SilksongNativeMenuTransition<NativeMenuBinding> transition))
                return;
            _selectionBeforeOpen = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            if (IsOwnedSelection(_selectionBeforeOpen)) _selectionBeforeOpen = null;
            _activeTransition = transition;
            _transitionCoroutine = StartCoroutine(OpenRoutine(binding, transition, route));
        }

        IEnumerator OpenRoutine(NativeMenuBinding binding,
                                SilksongNativeMenuTransition<NativeMenuBinding> transition,
                                NativeMenuRoute route)
        {
            yield return binding.Ui.HideMenu(binding.OptionsScreen);
            if (!OpenTransitionStillAvailable(binding, transition))
            {
                CancelOpenTransition(binding, transition);
                yield break;
            }

            _openRoute = route;
            if (route == NativeMenuRoute.Mods)
            {
                binding.Menu.Open();
                Paint();
            }
            else
            {
                _skinError = "";
                _skinTransport.Start(Time.unscaledTime);
                RefreshSkinMenu();
                PaintSkins();
            }
            _nativeOpen = true;
            if (route == NativeMenuRoute.Mods)
                yield return binding.Ui.ShowMenu(binding.ModsScreen);
            else
                yield return binding.Ui.ShowMenu(binding.SkinsScreen);
            if (!OpenTransitionStillAvailable(binding, transition))
            {
                CancelOpenTransition(binding, transition);
                yield break;
            }

            if (_lifecycle.Complete(transition, binding))
                _transitionCoroutine = null;
        }

        internal void Close()
        {
            BeginClose(_lifecycle.Current, showOptions: true);
        }

        void BeginClose(NativeMenuBinding binding, bool showOptions)
        {
            if (!_nativeOpen || !BindingIsAlive(binding) ||
                !_lifecycle.TryBegin(out SilksongNativeMenuTransition<NativeMenuBinding> transition))
                return;
            _activeTransition = transition;
            _skinTransport.Cancel();
            _transitionCoroutine = StartCoroutine(
                CloseRoutine(binding, transition, showOptions));
        }

        IEnumerator CloseRoutine(NativeMenuBinding binding,
                                 SilksongNativeMenuTransition<NativeMenuBinding> transition,
                                 bool showOptions)
        {
            if (_openRoute == NativeMenuRoute.Mods)
            {
                if (_benchOpen)
                    CompleteBenchOperation(TweakActionResult.Fail(
                        "Bench Teleport closed before a destination completed."));
                _benchOpen = false;
                _benchRows.Clear();
                binding.Menu.Close();
            }
            _nativeOpen = false;
            if (_openRoute == NativeMenuRoute.Mods)
                yield return binding.Ui.HideMenu(binding.ModsScreen);
            else
                yield return binding.Ui.HideMenu(binding.SkinsScreen);
            if (!TransitionStillCurrent(binding, transition)) yield break;

            if (showOptions && binding.Ui.uiState == UIState.PAUSED)
            {
                yield return binding.Ui.ShowMenu(binding.OptionsScreen);
                if (!TransitionStillCurrent(binding, transition)) yield break;
            }

            if (_lifecycle.Complete(transition, binding))
                _transitionCoroutine = null;
        }

        bool OpenTransitionStillAvailable(
            NativeMenuBinding binding,
            SilksongNativeMenuTransition<NativeMenuBinding> transition)
        {
            return TransitionStillCurrent(binding, transition) &&
                   binding.Session.IsReady &&
                   binding.Ui.uiState == UIState.PAUSED;
        }

        void CancelOpenTransition(
            NativeMenuBinding binding,
            SilksongNativeMenuTransition<NativeMenuBinding> transition)
        {
            if (!_lifecycle.Cancel(transition, binding)) return;
            SuspendRoutes(binding);
        }

        void SuspendRoutes(NativeMenuBinding binding)
        {
            _skinTransport.Cancel();
            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = null;
            _lifecycle.Cancel(_activeTransition, binding);
            if (_benchOpen) CompleteBenchOperation(TweakActionResult.Fail(
                "Bench Teleport lost paused menu authority."));
            _benchOpen = false;
            _benchRows.Clear();
            if (_openRoute == NativeMenuRoute.Mods && binding.Menu.IsOpen) binding.Menu.Close();
            _nativeOpen = false;
            binding.ModsScreen.gameObject.SetActive(false);
            binding.SkinsScreen.gameObject.SetActive(false);
            RestoreOptionsNavigation();
            RestoreSelection();
        }

        bool TransitionStillCurrent(
            NativeMenuBinding binding,
            SilksongNativeMenuTransition<NativeMenuBinding> transition)
        {
            return _lifecycle.IsCurrent(transition, binding) && BindingIsAlive(binding);
        }

        internal void Select(SilksongNativeModsButton button)
        {
            if (button == null || !DriverIsCurrent(button, button.Generation)) return;
            _focusedRole = (ButtonRole)button.Role;
            if (_focusedRole == ButtonRole.Row && button.DataIndex >= 0)
            {
                if (_benchOpen)
                {
                    if (button.DataIndex < _benchRows.Count)
                        _benchSelected = button.DataIndex;
                }
                else if (button.DataIndex < _menu.CurrentRows.Count)
                    _menu.MoveRow(button.DataIndex - _menu.SelectedRowIndex);
            }
            Paint();
        }

        internal void Submit(SilksongNativeModsButton button)
        {
            if (button == null || _lifecycle.Transitioning || !DriverIsCurrent(button, button.Generation)) return;
            Select(button);
            if (_benchOpen)
            {
                SubmitBench(button);
                return;
            }
            switch ((ButtonRole)button.Role)
            {
                case ButtonRole.Group: _menu.MoveGroup(1); break;
                case ButtonRole.Row: _menu.ActivateSelected(); break;
                case ButtonRole.Reset: _menu.Reset(); break;
                case ButtonRole.Back: Close(); return;
            }
            Paint();
        }

        internal void Move(SilksongNativeModsButton button, MoveDirection direction)
        {
            if (button == null || _lifecycle.Transitioning || !DriverIsCurrent(button, button.Generation)) return;
            if (_benchOpen)
            {
                MoveBench(button, direction);
                return;
            }
            ButtonRole role = (ButtonRole)button.Role;
            if (direction == MoveDirection.Left || direction == MoveDirection.Right)
            {
                int delta = direction == MoveDirection.Left ? -1 : 1;
                if (role == ButtonRole.Group) _menu.MoveGroup(delta);
                else if (role == ButtonRole.Row) MoveChoice(delta);
                Paint();
                return;
            }
            if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;

            Select(button);
            TweakMenuFocusTarget current = FocusTarget((ButtonRole)button.Role);
            TweakMenuFocusTarget next = TweakMenuFocusGraph.Move(
                current,
                direction == MoveDirection.Down ? 1 : -1,
                _menu.CurrentRows.Count);
            Focus(next);
        }

        void OpenBenchRoute()
        {
            _benchRows.Clear();
            _benchRows.AddRange(SilksongGameplayFeatures.BenchDestinations());
            _benchSelected = 0;
            _benchWindowStart = 0;
            _benchError = "";
            _benchOpen = true;
            PaintBench();
            if (_benchRows.Count > 0) FocusBenchRow();
            else Focus(ButtonRole.Back);
        }

        void SubmitBench(SilksongNativeModsButton button)
        {
            ButtonRole role = (ButtonRole)button.Role;
            if (role == ButtonRole.Back)
            {
                CloseBenchRoute();
                return;
            }
            if (role != ButtonRole.Row || _benchSelected < 0 ||
                _benchSelected >= _benchRows.Count) return;
            SilksongGameplayFeatures.BenchRecord destination =
                _benchRows[_benchSelected];
            try
            {
                SilksongGameplayFeatures.WarpToBench(destination.scene);
                CompleteBenchOperation(TweakActionResult.Ok(
                    TweakReadback.Text(destination.scene)));
                _benchError = "";
                BeginClose(_lifecycle.Current, showOptions: false);
            }
            catch (Exception error)
            {
                _benchError = error.GetBaseException().Message;
                CompleteBenchOperation(TweakActionResult.Fail(_benchError));
                _benchOpen = false;
                _benchRows.Clear();
                _benchSelected = 0;
                _benchWindowStart = 0;
                Paint();
                SelectCurrentRowButton();
            }
        }

        void MoveBench(SilksongNativeModsButton button, MoveDirection direction)
        {
            if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
            ButtonRole role = (ButtonRole)button.Role;
            if (_benchRows.Count == 0)
            {
                Focus(ButtonRole.Back);
                return;
            }
            if (role == ButtonRole.Back)
            {
                if (direction == MoveDirection.Up) FocusBenchRow();
                return;
            }
            if (role != ButtonRole.Row) return;

            Select(button);
            int next = _benchSelected + (direction == MoveDirection.Down ? 1 : -1);
            if (next >= _benchRows.Count)
            {
                Focus(ButtonRole.Back);
                return;
            }
            if (next < 0) return;
            _benchSelected = next;
            if (_benchSelected < _benchWindowStart)
                _benchWindowStart = _benchSelected;
            else if (_benchSelected >= _benchWindowStart + VisibleRows)
                _benchWindowStart = _benchSelected - VisibleRows + 1;
            PaintBench();
            FocusBenchRow();
        }

        TweakActionResult CompleteBenchOperation(TweakActionResult result)
        {
            if (_session == null)
                return TweakActionResult.Fail(
                    "The Silksong Mods session is unavailable.");
            long operationToken = _benchOperationToken;
            _benchOperationToken = 0;
            return _session.Controller.CompletePending(
                "bench_teleport",
                operationToken,
                result);
        }

        void CloseBenchRoute()
        {
            CompleteBenchOperation(TweakActionResult.Fail(
                "Bench Teleport was canceled."));
            _benchOpen = false;
            _benchRows.Clear();
            _benchSelected = 0;
            _benchWindowStart = 0;
            _benchError = "";
            Paint();
            SelectCurrentRowButton();
        }

        void FocusBenchRow()
        {
            int buttonIndex = TweakMenuPresenterLayout.ButtonIndex(
                TweakMenuFocusTarget.Row(_benchSelected),
                _benchWindowStart,
                _benchRows.Count);
            if (buttonIndex >= TweakMenuPresenterLayout.FirstRowButtonIndex &&
                buttonIndex < TweakMenuPresenterLayout.ResetButtonIndex)
            {
                SilksongNativeModsButton button = _buttons[buttonIndex];
                if (button.Selectable != null && button.Selectable.gameObject.activeInHierarchy)
                    button.Selectable.Select();
            }
        }

        internal void Cancel()
        {
            TweakMenuCancelTarget target =
                TweakMenuPresenterLayout.CancelTarget(_benchOpen);
            if (target == TweakMenuCancelTarget.CloseRoute)
                CloseBenchRoute();
            else
                Close();
        }

        void MoveChoice(int delta)
        {
            TweakDescriptor selected = _menu.Selected;
            if (selected == null || selected.ControlKind != TweakControlKind.Choice ||
                !selected.IsAvailable || selected.Values.Count == 0) return;
            string current = _session.Controller.Value(selected.Id);
            int index = 0;
            for (int i = 0; i < selected.Values.Count; i++)
                if (string.Equals(selected.Values[i], current, StringComparison.Ordinal))
                { index = i; break; }
            int next = (index + delta) % selected.Values.Count;
            if (next < 0) next += selected.Values.Count;
            _menu.SetSelected(selected.Values[next]);
        }

        TweakMenuFocusTarget FocusTarget(ButtonRole role)
        {
            if (role == ButtonRole.Row)
                return TweakMenuFocusTarget.Row(_menu.SelectedRowIndex);
            if (role == ButtonRole.Reset) return TweakMenuFocusTarget.Reset;
            if (role == ButtonRole.Back) return TweakMenuFocusTarget.Back;
            return TweakMenuFocusTarget.Group;
        }

        void SelectCurrentRowButton()
        {
            int buttonIndex = TweakMenuPresenterLayout.ButtonIndex(
                TweakMenuFocusTarget.Row(_menu.SelectedRowIndex),
                _menu.WindowStart,
                _menu.CurrentRows.Count);
            if (buttonIndex >= TweakMenuPresenterLayout.FirstRowButtonIndex &&
                buttonIndex < TweakMenuPresenterLayout.ResetButtonIndex &&
                _buttons[buttonIndex].Selectable.gameObject.activeInHierarchy)
                _buttons[buttonIndex].Selectable.Select();
        }

        void Focus(TweakMenuFocusTarget target)
        {
            _menu.DismissMessage();
            if (target.Kind == TweakMenuFocusKind.Row)
            {
                _menu.MoveRow(target.RowIndex - _menu.SelectedRowIndex);
                _focusedRole = ButtonRole.Row;
                Paint();
                SelectCurrentRowButton();
                return;
            }

            ButtonRole role = target.Kind == TweakMenuFocusKind.Reset
                ? ButtonRole.Reset
                : target.Kind == TweakMenuFocusKind.Back
                    ? ButtonRole.Back
                    : ButtonRole.Group;
            int index = TweakMenuPresenterLayout.ButtonIndex(
                target, _menu.WindowStart, _menu.CurrentRows.Count);
            if (index < 0 || index >= _buttons.Count) return;
            _focusedRole = role;
            _buttons[index].Selectable.Select();
            Paint();
        }

        void Focus(ButtonRole role)
        {
            Focus(role == ButtonRole.Back
                ? TweakMenuFocusTarget.Back
                : role == ButtonRole.Reset
                    ? TweakMenuFocusTarget.Reset
                    : TweakMenuFocusTarget.Group);
        }

        void Paint()
        {
            if (_benchOpen)
            {
                PaintBench();
                return;
            }
            if (_menu == null ||
                _labels.Count != TweakMenuPresenterLayout.ButtonCount ||
                _valueLabels.Count != TweakMenuPresenterLayout.ButtonCount ||
                _title == null || _description == null) return;
            _menu.RefreshOperationMessage();
            if (!_buttonRoots[TweakMenuPresenterLayout.GroupButtonIndex].activeSelf)
                _buttonRoots[TweakMenuPresenterLayout.GroupButtonIndex].SetActive(true);
            if (!_buttonRoots[TweakMenuPresenterLayout.ResetButtonIndex].activeSelf)
                _buttonRoots[TweakMenuPresenterLayout.ResetButtonIndex].SetActive(true);
            if (!_buttonRoots[TweakMenuPresenterLayout.BackButtonIndex].activeSelf)
                _buttonRoots[TweakMenuPresenterLayout.BackButtonIndex].SetActive(true);
            _labels[TweakMenuPresenterLayout.GroupButtonIndex].text =
                "< " + Friendly(_menu.Groups[_menu.SelectedGroupIndex]) + " >";
            _valueLabels[TweakMenuPresenterLayout.GroupButtonIndex].text =
                (_menu.SelectedGroupIndex + 1) + "/" + _menu.Groups.Count;

            IReadOnlyList<TweakDescriptor> rows = _menu.CurrentRows;
            for (int slot = 0; slot < VisibleRows; slot++)
            {
                int buttonIndex = TweakMenuPresenterLayout.FirstRowButtonIndex + slot;
                int dataIndex = TweakMenuPresenterLayout.DataIndexForRowButton(
                    buttonIndex, _menu.WindowStart, rows.Count);
                SilksongNativeModsButton button = _buttons[buttonIndex];
                GameObject root = _buttonRoots[buttonIndex];
                bool shown = dataIndex >= 0;
                if (root.activeSelf != shown) root.SetActive(shown);
                button.DataIndex = shown ? dataIndex : -1;
                if (!shown) continue;

                TweakDescriptor descriptor = rows[dataIndex];
                string value;
                if (!descriptor.IsAvailable) value = "UNAVAILABLE";
                else if (descriptor.ControlKind == TweakControlKind.Command) value = "RUN";
                else if (descriptor.ControlKind == TweakControlKind.Route) value = "OPEN";
                else value = Friendly(_session.Controller.Value(descriptor.Id));
                _labels[buttonIndex].text = descriptor.Title.ToUpperInvariant();
                _valueLabels[buttonIndex].text = value;
            }

            _labels[TweakMenuPresenterLayout.ResetButtonIndex].text = "RESET ALL MODS";
            _labels[TweakMenuPresenterLayout.BackButtonIndex].text = "BACK";
            _title.text = "MODS";
            _description.text = string.IsNullOrEmpty(_menu.Message)
                ? FocusDescription()
                : _menu.Message.ToUpperInvariant();
        }

        void PaintBench()
        {
            if (_labels.Count != TweakMenuPresenterLayout.ButtonCount ||
                _valueLabels.Count != TweakMenuPresenterLayout.ButtonCount ||
                _title == null || _description == null) return;
            _buttonRoots[TweakMenuPresenterLayout.GroupButtonIndex].SetActive(false);
            _buttonRoots[TweakMenuPresenterLayout.ResetButtonIndex].SetActive(false);
            _buttonRoots[TweakMenuPresenterLayout.BackButtonIndex].SetActive(true);

            for (int slot = 0; slot < VisibleRows; slot++)
            {
                int buttonIndex = TweakMenuPresenterLayout.FirstRowButtonIndex + slot;
                int dataIndex = TweakMenuPresenterLayout.DataIndexForRowButton(
                    buttonIndex, _benchWindowStart, _benchRows.Count);
                SilksongNativeModsButton button = _buttons[buttonIndex];
                GameObject root = _buttonRoots[buttonIndex];
                bool shown = dataIndex >= 0;
                if (root.activeSelf != shown) root.SetActive(shown);
                button.DataIndex = shown ? dataIndex : -1;
                if (!shown) continue;
                _labels[buttonIndex].text = Friendly(_benchRows[dataIndex].scene);
                _valueLabels[buttonIndex].text = dataIndex == _benchSelected ? ">" : "";
            }

            _labels[TweakMenuPresenterLayout.BackButtonIndex].text = "BACK";
            _title.text = "BENCH TELEPORT";
            if (!string.IsNullOrEmpty(_benchError))
                _description.text = _benchError.ToUpperInvariant();
            else if (_benchRows.Count == 0)
                _description.text = "NO RECORDED BENCHES ARE AVAILABLE.";
            else
                _description.text = "TRAVEL TO " + Friendly(_benchRows[_benchSelected].scene) + ".";
        }

        string FocusDescription()
        {
            if (_focusedRole == ButtonRole.Group)
                return "Choose a mod category.";
            if (_focusedRole == ButtonRole.Reset)
                return "Restore every mod setting to its default.";
            if (_focusedRole == ButtonRole.Back)
                return "Return to Options.";

            TweakDescriptor descriptor = _menu.Selected;
            if (descriptor == null) return "Choose a mod setting.";
            string description = descriptor.Description;
            if (!descriptor.IsAvailable && !string.IsNullOrEmpty(descriptor.UnavailableReason))
                description += " Unavailable: " + descriptor.UnavailableReason;
            return description;
        }

        static string Friendly(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "UNKNOWN"
                : value.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
        }

        internal void OpenEntry(int route)
        {
            Open((NativeMenuRoute)route);
        }

        internal void SelectSkin(SilksongNativeSkinButton button)
        {
            if (button == null || _skinMenu == null || !DriverIsCurrent(button, button.Generation)) return;
            _skinMenu.SelectRow(button.DataIndex);
            PaintSkins();
        }

        internal void SubmitSkin(SilksongNativeSkinButton button)
        {
            if (button == null || _lifecycle.Transitioning ||
                !DriverIsCurrent(button, button.Generation)) return;
            if (_skinMenu == null) {
                if (ReferenceEquals(button, _skinButtons[0])) Close();
                return;
            }
            SelectSkin(button);
            NativeSkinMenuRow row = _skinMenu.Selected;
            if (row.Kind == NativeSkinMenuRowKind.Back) { Close(); return; }
            NativeSkinMutation mutation = row.Kind == NativeSkinMenuRowKind.Mode
                ? _skinMenu.CycleMode(1)
                : row.Kind == NativeSkinMenuRowKind.Sprites
                    ? _skinMenu.CycleSprites(1)
                    : _skinMenu.ConfirmSelected();
            ApplySkinMutation(mutation);
        }

        internal void MoveSkin(SilksongNativeSkinButton button, MoveDirection direction)
        {
            if (button == null || !_skinSnapshotReady || _skinMenu == null || _lifecycle.Transitioning ||
                !DriverIsCurrent(button, button.Generation)) return;
            SelectSkin(button);
            if (direction == MoveDirection.Left || direction == MoveDirection.Right)
            {
                int delta = direction == MoveDirection.Left ? -1 : 1;
                NativeSkinMenuRow row = _skinMenu.Selected;
                if (row.Kind == NativeSkinMenuRowKind.Mode)
                    ApplySkinMutation(_skinMenu.CycleMode(delta));
                else if (row.Kind == NativeSkinMenuRowKind.Sprites)
                    ApplySkinMutation(_skinMenu.CycleSprites(delta));
                return;
            }
            if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
            _skinMenu.Move(direction == MoveDirection.Up ? -1 : 1);
            PaintSkins();
            FocusSkin();
        }

        void FocusSkin()
        {
            if (_skinMenu == null) return;
            int dataIndex = _skinMenu.SelectedRowIndex;
            for (int index = 0; index < _skinButtons.Count; index++)
                if (_skinButtonRoots[index].activeInHierarchy &&
                    _skinButtons[index].DataIndex == dataIndex)
                {
                    _skinButtons[index].Selectable.Select();
                    return;
                }
        }

        void ApplySkinMutation(NativeSkinMutation mutation)
        {
            if (!_skinSnapshotReady || _skinMenu == null || mutation.Kind == NativeSkinMutationKind.None) return;
            string expected = _skinMenu.Snapshot.ConfigSha256;
            _skinTransport.Start(Time.unscaledTime);
            SilksongModsRuntime currentRuntime = SilksongModsRuntime.Current;
            if (currentRuntime != null) currentRuntime.InvalidateSkinLibrary(); // queued intent is never counted as apply
            bool accepted = false;
            try
            {
                accepted = mutation.Kind == NativeSkinMutationKind.SetMode
                    ? SkinBridge.CallStatic<bool>("setMode", SkinProfileId, expected, mutation.Value)
                    : mutation.Kind == NativeSkinMutationKind.SetSpriteScope
                        ? SkinBridge.CallStatic<bool>("setSpriteScope", SkinProfileId, expected, mutation.Value)
                        : SkinBridge.CallStatic<bool>("confirmPack", SkinProfileId, expected, mutation.Value);
            }
            catch (Exception error)
            {
                _skinError = "CHANGE FAILED · " + error.GetBaseException().Message;
            }
            if (accepted)
            {
                SilksongModsRuntime runtime = SilksongModsRuntime.Current;
                if (runtime != null) runtime.InvalidateSkinLibrary();
                _skinError = "";
            }
            else if (string.IsNullOrEmpty(_skinError))
                _skinError = "CHANGE NOT APPLIED · REFRESHED";
            // A false result is stale/busy authority: repaint only from a new checked snapshot.
            RefreshSkinMenu();
            PaintSkins();
            FocusSkin();
        }

        AndroidJavaClass SkinBridge => _skinBridge ?? (_skinBridge = new AndroidJavaClass(
            "dev.silksong.launcher.runtime.SkinLibraryRuntimeBridge"));

        void RefreshSkinMenu()
        {
            _skinSnapshotReady = false;
            try
            {
                string json = SkinBridge.CallStatic<string>("readMenuSnapshot", SkinProfileId);
                if (string.IsNullOrEmpty(json) || json.Length > MaximumSkinSnapshotBytes)
                    throw new InvalidOperationException("Invalid native skin snapshot length.");
                WireSkinSnapshot wire = JsonUtility.FromJson<WireSkinSnapshot>(json);
                if (wire != null && !wire.ok && wire.code == "LIFECYCLE_BLOCKED") {
                    _skinError = "SKIN EVENT PENDING · BOUNDED RETRY";
                    return;
                }
                if (wire == null || !wire.ok)
                    throw new InvalidOperationException((wire != null ? wire.code : "MISSING") +
                                                        ": " + (wire != null ? wire.detail : "snapshot"));
                if (!string.Equals(wire.profileId, SkinProfileId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Native skin snapshot belongs to another profile.");
                WireSkinPack[] sourcePacks = wire.packs ?? Array.Empty<WireSkinPack>();
                if (sourcePacks.Length > 2048)
                    throw new InvalidOperationException("Native skin pack count exceeds bound.");
                var packs = new NativeSkinPackDescriptor[sourcePacks.Length];
                for (int index = 0; index < sourcePacks.Length; index++)
                    packs[index] = new NativeSkinPackDescriptor(sourcePacks[index].id,
                        sourcePacks[index].name, sourcePacks[index].author);
                bool changedConfiguration = _skinMenu == null || _skinMenu.Snapshot.ConfigSha256 != wire.configSha256;
                _skinTransport.CompleteEvidence(wire.evidenceState);
                if (changedConfiguration) {
                    _skinError = "";
                    SilksongModsRuntime current = SilksongModsRuntime.Current;
                    if (current != null) current.InvalidateSkinLibrary();
                }
                var snapshot = new NativeSkinMenuSnapshot(wire.profileId, wire.configSha256,
                    wire.mode, wire.spriteScope, wire.selectedPackId,
                    wire.eligiblePackIds ?? Array.Empty<string>(), packs);
                if (_skinMenu == null) _skinMenu = new NativeSkinMenuModel(snapshot, VisibleRows);
                else _skinMenu.Replace(snapshot);
                bool preserveMutationError =
                    _skinError.StartsWith("CHANGE FAILED", StringComparison.Ordinal) ||
                    _skinError.StartsWith("CHANGE NOT APPLIED", StringComparison.Ordinal);
                if (!preserveMutationError)
                    _skinError = FormatSkinEvidence(wire);
                _skinSnapshotReady = true;
            }
            catch (Exception error)
            {
                _skinTransport.Cancel();
                _skinError = "SKINS UNAVAILABLE · " + error.GetBaseException().Message;
                Debug.LogWarning("[Silksong Skins] " + _skinError);
            }
            finally
            {
                if (!_skinSnapshotReady && _skinMenu != null && BindingIsAlive()) {
                    _skinMenu.SelectRow(_skinMenu.Rows.Count - 1);
                    PaintSkins();
                    if (_nativeOpen && _openRoute == NativeMenuRoute.Skins) FocusSkin();
                }
            }
        }

        static string FormatSkinEvidence(WireSkinSnapshot wire)
        {
            string correlation = (wire.featureId ?? "") + " · " +
                (wire.operationId ?? "") + "/" + wire.operationGeneration;
            if (string.Equals(wire.evidenceState, "TERMINAL", StringComparison.Ordinal) &&
                wire.observation != null)
            {
                string terminalCorrelation = (wire.observation.featureId ?? "") + " · " +
                    (wire.observation.operationId ?? "") + "/" + wire.observation.operationGeneration;
                string value = wire.observation.status + " · " + terminalCorrelation;
                if (!string.IsNullOrEmpty(wire.observation.detail))
                    value += " · " + wire.observation.detail;
                return value;
            }
            if (string.Equals(wire.evidenceState, "STALE", StringComparison.Ordinal))
                return "PENDING · " + correlation + " · PRIOR EVIDENCE STALE";
            if (string.Equals(wire.evidenceState, "UNREADABLE", StringComparison.Ordinal))
                return "PENDING · " + correlation + " · EVIDENCE UNREADABLE";
            string pending = "PENDING · " + correlation;
            if (wire.observation != null && !string.IsNullOrEmpty(wire.observation.status))
                pending += " · " + wire.observation.status;
            return pending;
        }

        void PaintSkins()
        {
            if (_skinLabels.Count != VisibleRows + 3 || _skinsTitle == null ||
                _skinsDescription == null) return;
            _skinsTitle.text = "SKINS";
            _skinsDescription.text = string.IsNullOrEmpty(_skinError)
                ? SkinFocusDescription()
                : _skinError.ToUpperInvariant();
            if (_skinMenu == null)
            {
                int back = 0;
                for (int index = 0; index < _skinButtonRoots.Count; index++) {
                    bool shown = index == back;
                    _skinButtonRoots[index].SetActive(shown);
                    _skinButtons[index].DataIndex = -1;
                    _skinButtons[index].Selectable.interactable = shown;
                }
                _skinLabels[back].text = "BACK";
                _skinsScreen.defaultHighlight = _skinButtons[back].Selectable;
                return;
            }
            IReadOnlyList<NativeSkinMenuRow> visible = _skinMenu.VisibleRows;
            for (int slot = 0; slot < _skinButtons.Count; slot++)
            {
                bool shown = slot < visible.Count;
                GameObject root = _skinButtonRoots[slot];
                if (root.activeSelf != shown) root.SetActive(shown);
                if (!shown) { _skinButtons[slot].DataIndex = -1; continue; }
                NativeSkinMenuRow row = visible[slot];
                int dataIndex = -1;
                for (int index = 0; index < _skinMenu.Rows.Count; index++)
                    if (ReferenceEquals(_skinMenu.Rows[index], row)) { dataIndex = index; break; }
                _skinButtons[slot].DataIndex = dataIndex;
                _skinButtons[slot].Selectable.interactable = row.IsActionable &&
                    (_skinSnapshotReady || row.Kind == NativeSkinMenuRowKind.Back);
                _skinLabels[slot].text = row.Label.ToUpperInvariant() +
                    (string.IsNullOrEmpty(row.Value) ? "" : "     " + row.Value);
            }
            _skinsScreen.defaultHighlight = _skinSnapshotReady
                ? _skinButtons[0].Selectable : _skinButtons[visible.Count - 1].Selectable;
        }

        string SkinFocusDescription()
        {
            if (_skinMenu == null) return "The installed skin library is unavailable.";
            switch (_skinMenu.Selected.Kind)
            {
                case NativeSkinMenuRowKind.Mode:
                    return "Choose whether skins are off, fixed, or rotating.";
                case NativeSkinMenuRowKind.Sprites:
                    return "Choose which sprites use installed skins.";
                case NativeSkinMenuRowKind.Skin:
                    return "Select this installed skin for the current mode.";
                case NativeSkinMenuRowKind.Back:
                    return "Return to Options.";
                default:
                    return "No compatible installed skins were found.";
            }
        }

#pragma warning disable CS0649
        [Serializable] sealed class WireSkinPack { public string id, name, author; }
        [Serializable] sealed class WireSkinObservation
        {
            public long operationGeneration;
            public string profileId, featureId, operationId, operationKind, rotationRun,
                requestConfigSha256, resultingConfigSha256, resolvedKind, resolvedPackId,
                resolvedTreeSha256, resolvedReceiptSha256, activeKind, activePackId,
                activeTreeSha256, activeReceiptSha256, status, detail;
        }
        [Serializable] sealed class WireSkinSnapshot
        {
            public bool ok;
            public long operationGeneration;
            public string code, detail, profileId, configSha256, mode, spriteScope, selectedPackId,
                operationId, featureId, operationKind, evidenceState;
            public string[] eligiblePackIds;
            public WireSkinPack[] packs;
            public WireSkinObservation observation;
        }
#pragma warning restore CS0649

        void CancelAndClearBinding()
        {
            _bindingGeneration++;
            _skinTransport.Cancel();
            NativeMenuBinding binding = _lifecycle.Current;
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            if (_benchOpen)
                CompleteBenchOperation(TweakActionResult.Fail(
                    "Bench Teleport binding closed before a destination completed."));
            _lifecycle.Clear();
            if (binding != null && _nativeOpen && _openRoute == NativeMenuRoute.Mods)
                binding.Menu.Close();
            _nativeOpen = false;
            ClearBinding();
        }

        bool IsOwnedSelection(GameObject selection)
        {
            if (selection == null) return false;
            for (int i = 0; i < _ownedRoots.Count; i++)
                if (_ownedRoots[i] != null && selection.transform.IsChildOf(_ownedRoots[i].transform)) return true;
            return false;
        }

        void RestoreSelection()
        {
            EventSystem events = EventSystem.current;
            if (events == null || !IsOwnedSelection(events.currentSelectedGameObject)) return;
            GameObject target = _selectionBeforeOpen;
            if (target == null || !target.activeInHierarchy || IsOwnedSelection(target))
                target = _gameButton != null ? _gameButton.gameObject : null;
            events.SetSelectedGameObject(target != null && target.activeInHierarchy ? target : null);
        }

        void ClearBinding()
        {
            if (_stagingRoot != null) _stagingRoot.SetActive(false);
            for (int i = 0; i < _ownedRoots.Count; i++)
                if (_ownedRoots[i] != null) _ownedRoots[i].SetActive(false);
            WireOptionsNavigation(false);
            RestoreSelection();
            for (int i = 0; i < _ownedRoots.Count; i++) {
                GameObject root = _ownedRoots[i];
                if (root == null) continue;
                _retiredRoots.Add(root);
                UnityObject.Destroy(root);
            }
            if (_stagingRoot != null) UnityObject.Destroy(_stagingRoot);
            _ownedRoots.Clear();
            _originalNavigation.Clear();
            _stagingRoot = null;
            _selectionBeforeOpen = null;
            _activeTransition = default;
            if (_entryRoot != null) UnityObject.Destroy(_entryRoot);
            if (_skinsEntryRoot != null) UnityObject.Destroy(_skinsEntryRoot);
            if (_modsScreen != null) UnityObject.Destroy(_modsScreen.gameObject);
            if (_skinsScreen != null) UnityObject.Destroy(_skinsScreen.gameObject);
            _buttons.Clear();
            _buttonRoots.Clear();
            _labels.Clear();
            _valueLabels.Clear();
            _skinButtons.Clear();
            _skinButtonRoots.Clear();
            _skinLabels.Clear();
            _entryRoot = null;
            _skinsEntryRoot = null;
            _entryButton = null;
            _skinsEntryButton = null;
            _entrySelectable = null;
            _skinsEntrySelectable = null;
            _modsScreen = null;
            _skinsScreen = null;
            _title = null;
            _description = null;
            _skinsTitle = null;
            _skinsDescription = null;
            _descriptionRoot = null;
            _skinsDescriptionRoot = null;
            _gameButton = _audioButton = _videoButton = null;
            _controllerButton = _keyboardButton = null;
            _ui = null;
            _session = null;
            _menu = null;
            _skinMenu = null;
            _skinSnapshotReady = false;
            _benchOpen = false;
            _benchRows.Clear();
            _benchSelected = 0;
            _benchWindowStart = 0;
            _benchError = "";
        }

        void OnDestroy()
        {
            if (ReferenceEquals(_current, this)) _current = null;
            CancelAndClearBinding();
            AndroidJavaClass bridge = _skinBridge;
            _skinBridge = null;
            if (bridge != null) bridge.Dispose();
        }
    }

    public sealed class SilksongNativeModsEntryButton : MonoBehaviour,
        ISubmitHandler, IPointerClickHandler
    {
        internal SilksongNativeModsMenu Owner;
        internal MenuButton Selectable;
        internal int Generation;
        internal int Route;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null ||
                !Owner.DriverIsCurrent(this, Generation)) return;
            Owner.OpenEntry(Route);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            ((ISubmitHandler)this).OnSubmit(eventData);
        }
    }

    public sealed class SilksongNativeModsButton : MonoBehaviour,
        ISubmitHandler, IPointerClickHandler, IMoveHandler, ICancelHandler, ISelectHandler
    {
        internal SilksongNativeModsMenu Owner;
        internal MenuButton Selectable;
        internal int Generation;
        internal int Role;
        internal int DataIndex = -1;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null ||
                !Owner.DriverIsCurrent(this, Generation)) return;
            Owner.Submit(this);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (Owner != null && Owner.DriverIsCurrent(this, Generation)) Owner.Select(this);
            ((ISubmitHandler)this).OnSubmit(eventData);
        }

        void IMoveHandler.OnMove(AxisEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null ||
                !Owner.DriverIsCurrent(this, Generation)) return;
            Owner.Move(this, eventData.moveDir);
            eventData.Use();
        }

        void ICancelHandler.OnCancel(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null ||
                !Owner.DriverIsCurrent(this, Generation)) return;
            Selectable.ForceDeselect();
            Owner.Cancel();
            eventData.Use();
        }

        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            if (Owner != null && Owner.DriverIsCurrent(this, Generation)) Owner.Select(this);
        }
    }

    public sealed class SilksongNativeSkinButton : MonoBehaviour,
        ISubmitHandler, IPointerClickHandler, IMoveHandler, ICancelHandler, ISelectHandler
    {
        internal SilksongNativeModsMenu Owner;
        internal MenuButton Selectable;
        internal int Generation;
        internal int DataIndex = -1;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null ||
                !Owner.DriverIsCurrent(this, Generation)) return;
            Owner.SubmitSkin(this);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (Owner != null && Owner.DriverIsCurrent(this, Generation)) Owner.SelectSkin(this);
            ((ISubmitHandler)this).OnSubmit(eventData);
        }

        void IMoveHandler.OnMove(AxisEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null ||
                !Owner.DriverIsCurrent(this, Generation)) return;
            Owner.MoveSkin(this, eventData.moveDir);
            eventData.Use();
        }

        void ICancelHandler.OnCancel(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null ||
                !Owner.DriverIsCurrent(this, Generation)) return;
            Selectable.ForceDeselect();
            Owner.Close();
            eventData.Use();
        }

        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            if (Owner != null && Owner.DriverIsCurrent(this, Generation)) Owner.SelectSkin(this);
        }
    }
}
#endif
