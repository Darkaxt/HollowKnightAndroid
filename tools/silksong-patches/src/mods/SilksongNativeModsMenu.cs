#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using DualSouls.Mods;
using DualSouls.Skins;
using GlobalEnums;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
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
        const int VisibleRows = 5;
        const int MaximumSkinSnapshotBytes = 262144;
        const string SkinProfileId = "silksong";
        const float EntryY = -575f;
        const float FirstRowY = -25f;
        const float RowStep = 78f;
        const float ButtonTextHorizontalInset = 80f;

        internal enum NativeMenuRoute { Mods, Skins }
        enum ButtonRole { Group, Master, Row, Reset, Back }

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

        readonly List<SilksongNativeModsButton> _buttons =
            new List<SilksongNativeModsButton>();
        readonly List<GameObject> _buttonRoots = new List<GameObject>();
        readonly List<TmpText> _labels = new List<TmpText>();
        readonly List<TmpText> _valueLabels = new List<TmpText>();
        readonly List<SilksongNativeSkinButton> _skinButtons =
            new List<SilksongNativeSkinButton>();
        readonly List<GameObject> _skinButtonRoots = new List<GameObject>();
        readonly List<TmpText> _skinLabels = new List<TmpText>();
        readonly SilksongNativeMenuLifecycle<NativeMenuBinding> _lifecycle =
            new SilksongNativeMenuLifecycle<NativeMenuBinding>();

        TweakSession _session;
        TweakMenuModel _menu;
        NativeSkinMenuModel _skinMenu;
        AndroidJavaClass _skinBridge;
        UIManager _ui;
        MenuScreen _modsScreen;
        MenuScreen _skinsScreen;
        GameObject _entryRoot;
        GameObject _skinsEntryRoot;
        SilksongNativeModsEntryButton _entryButton;
        SilksongNativeModsEntryButton _skinsEntryButton;
        TmpText _title;
        TmpText _description;
        TmpText _skinsTitle;
        TmpText _skinsDescription;
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
        bool _nativeOpen;
        bool _topologyWarningLogged;
        float _nextBindAttempt;

        void Update()
        {
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
            return binding != null && ReferenceEquals(_lifecycle.Current, binding) &&
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
            if (session == null) return;

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

                _gameButton = RequireMenuButton(game, "GameOptionsButton");
                _audioButton = RequireMenuButton(audio, "AudioOptionsButton");
                _videoButton = RequireMenuButton(video, "VideoOptionsButton");
                _controllerButton = RequireMenuButton(controller, "GamepadOptionsButton");
                _keyboardButton = RequireMenuButton(keyboard, "KeyboardOptionsButton");

                _session = session;
                _menu = session.Menu;
                _ui = manager;
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

        static MenuButton RequireMenuButton(Transform transform, string name)
        {
            MenuButton button = transform.GetComponent<MenuButton>();
            if (button == null)
                throw new InvalidOperationException(name + " is not a typed MenuButton.");
            return button;
        }

        void BuildRouteEntry(Transform content, GameObject template, float y,
                             NativeMenuRoute route, string label)
        {
            GameObject root = Instantiate(template, content, false);
            root.name = label;
            RectTransform wrapper = root.transform as RectTransform;
            wrapper.anchoredPosition = new Vector2(wrapper.anchoredPosition.x, y);

            MenuButton source = root.GetComponentInChildren<MenuButton>(true);
            if (source == null)
                throw new InvalidOperationException(label + " entry template has no MenuButton.");
            source.enabled = false;
            var button = source.gameObject.AddComponent<SilksongNativeModsEntryButton>();
            CopySelectable(source, button);
            button.Owner = this;
            button.Route = (int)route;
            button.FlashEffect = source.flashEffect;
            UnityObject.Destroy(source);
            DisableForeignDrivers(root);
            SetButtonText(root, label);
            root.SetActive(false);
            if (route == NativeMenuRoute.Mods)
            {
                _entryRoot = root;
                _entryButton = button;
            }
            else
            {
                _skinsEntryRoot = root;
                _skinsEntryButton = button;
            }
        }

        void BuildModsScreen(GameObject optionsScreen, GameObject rowTemplate)
        {
            GameObject root = Instantiate(optionsScreen, _ui.UICanvas.transform, false);
            root.name = "ModsMenuScreen";
            root.SetActive(false);

            _modsScreen = root.GetComponent<MenuScreen>();
            if (_modsScreen == null)
                throw new InvalidOperationException("Options screen clone has no typed MenuScreen.");
            MenuButtonList oldList = root.GetComponent<MenuButtonList>();
            if (oldList != null) oldList.enabled = false;
            _modsScreen.backButton = null;

            Transform title = root.transform.Find("Title");
            _title = title != null ? title.GetComponentInChildren<TmpText>(true) : null;
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

            CreateButton(content, rowTemplate, ButtonRole.Group, 0, "CATEGORY");
            CreateButton(content, rowTemplate, ButtonRole.Master, 1, "MASTER MODS");
            for (int i = 0; i < VisibleRows; i++)
                CreateButton(content, rowTemplate, ButtonRole.Row, i + 2, "MOD");
            CreateDescription(content, rowTemplate, 7);
            CreateButton(content, rowTemplate, ButtonRole.Reset, 8, "RESET ALL MODS");
            CreateButton(content, rowTemplate, ButtonRole.Back, 9, "BACK");

            DisableForeignDrivers(root);
            _modsScreen.defaultHighlight = _buttons[0];
            _title.text = "MODS";
            root.SetActive(false);
        }

        void BuildSkinsScreen(GameObject optionsScreen, GameObject rowTemplate)
        {
            GameObject root = Instantiate(optionsScreen, _ui.UICanvas.transform, false);
            root.name = "SkinsMenuScreen";
            root.SetActive(false);
            _skinsScreen = root.GetComponent<MenuScreen>();
            if (_skinsScreen == null)
                throw new InvalidOperationException("Options screen clone has no typed MenuScreen.");
            MenuButtonList oldList = root.GetComponent<MenuButtonList>();
            if (oldList != null) oldList.enabled = false;
            _skinsScreen.backButton = null;
            Transform title = root.transform.Find("Title");
            _skinsTitle = title != null ? title.GetComponentInChildren<TmpText>(true) : null;
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
            _skinsScreen.defaultHighlight = _skinButtons[0];
            _skinsTitle.text = "SKINS";
            root.SetActive(false);
        }

        void CreateSkinButton(Transform parent, GameObject template, int visualIndex)
        {
            GameObject wrapper = Instantiate(template, parent, false);
            wrapper.name = "SkinsRow" + visualIndex;
            RectTransform rect = wrapper.transform as RectTransform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                FirstRowY - RowStep * visualIndex);
            MenuButton source = wrapper.GetComponentInChildren<MenuButton>(true);
            if (source == null)
                throw new InvalidOperationException("Native Skins row has no MenuButton.");
            source.enabled = false;
            var button = source.gameObject.AddComponent<SilksongNativeSkinButton>();
            CopySelectable(source, button);
            button.Owner = this;
            button.FlashEffect = source.flashEffect;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            UnityObject.Destroy(source);
            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x, 70f);
            DisableForeignDrivers(wrapper);
            _skinButtonRoots.Add(wrapper);
            _skinButtons.Add(button);
            _skinLabels.Add(SetButtonText(wrapper, "SKIN"));
        }

        void CreateDescription(Transform parent, GameObject template, int visualIndex)
        {
            _descriptionRoot = CreateDescriptionRow(
                parent, template, "ModsDescription", visualIndex, out _description);
        }

        void CreateSkinDescription(Transform parent, GameObject template, int visualIndex)
        {
            _skinsDescriptionRoot = CreateDescriptionRow(
                parent, template, "SkinsDescription", visualIndex, out _skinsDescription);
        }

        static GameObject CreateDescriptionRow(Transform parent, GameObject template, string name,
                                               int visualIndex, out TmpText label)
        {
            GameObject wrapper = Instantiate(template, parent, false);
            wrapper.name = name;
            RectTransform rect = wrapper.transform as RectTransform;
            if (rect != null)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                    FirstRowY - RowStep * visualIndex);
            MenuButton source = wrapper.GetComponentInChildren<MenuButton>(true);
            if (source == null)
                throw new InvalidOperationException("Native description row has no MenuButton.");
            source.interactable = false;
            source.enabled = false;
            source.navigation = new Navigation { mode = Navigation.Mode.None };
            DisableForeignDrivers(wrapper);
            label = SetButtonText(wrapper, "Choose a category.");
            return wrapper;
        }

        void CreateButton(Transform parent, GameObject template, ButtonRole role,
                          int visualIndex, string initialText)
        {
            GameObject wrapper = Instantiate(template, parent, false);
            wrapper.name = "Mods" + role + visualIndex;
            RectTransform rect = wrapper.transform as RectTransform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                FirstRowY - RowStep * visualIndex);

            MenuButton source = wrapper.GetComponentInChildren<MenuButton>(true);
            if (source == null) throw new InvalidOperationException("Native Mods row has no MenuButton.");
            source.enabled = false;
            SilksongNativeModsButton button =
                source.gameObject.AddComponent<SilksongNativeModsButton>();
            CopySelectable(source, button);
            button.Owner = this;
            button.Role = (int)role;
            button.FlashEffect = source.flashEffect;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            UnityObject.Destroy(source);

            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x, 70f);
            DisableForeignDrivers(wrapper);
            TmpText label = SetButtonText(wrapper, initialText);
            TmpText valueLabel = null;
            if (role == ButtonRole.Group || role == ButtonRole.Master ||
                role == ButtonRole.Row)
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

        static void CopySelectable(MenuButton source, MenuSelectable target)
        {
            target.interactable = source.interactable;
            target.navigation = source.navigation;
            target.cancelAction = source.cancelAction;
            target.leftCursor = source.leftCursor;
            target.rightCursor = source.rightCursor;
            target.selectHighlight = source.selectHighlight;
            target.descriptionText = source.descriptionText;
            target.playSubmitSound = source.playSubmitSound;
            target.menuSubmitVibration = source.menuSubmitVibration;
            target.menuCancelVibration = source.menuCancelVibration;
        }

        static TmpText SetButtonText(GameObject root, string value)
        {
            Transform named = FindDescendant(root.transform, "Menu Button Text");
            TmpText text = named != null ? named.GetComponent<TmpText>() :
                root.GetComponentInChildren<TmpText>(true);
            if (text == null)
                throw new InvalidOperationException("Native button text is unavailable.");
            text.text = value;
            return text;
        }

        static TmpText CloneColumnText(TmpText source, string name)
        {
            GameObject clone = Instantiate(source.gameObject, source.transform.parent, false);
            clone.name = name;
            TmpText text = clone.GetComponent<TmpText>();
            if (text == null)
                throw new InvalidOperationException("Cloned native text is unavailable.");
            return text;
        }

        static void ConfigureColumn(TmpText text, bool rightAligned)
        {
            RectTransform rect = text.transform as RectTransform;
            if (rect != null)
                rect.sizeDelta = new Vector2(-ButtonTextHorizontalInset, rect.sizeDelta.y);
            text.alignment = rightAligned
                ? TMProOld.TextAlignmentOptions.Right
                : TMProOld.TextAlignmentOptions.Left;
            text.enableWordWrapping = false;
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
                    behaviour is SilksongNativeModsButton ||
                    behaviour is SilksongNativeSkinButton ||
                    behaviour is SilksongNativeModsEntryButton) continue;
                behaviour.enabled = false;
            }
        }

        void WireOptionsNavigation(bool includeMods)
        {
            if (_gameButton == null || _audioButton == null || _videoButton == null ||
                _controllerButton == null || _keyboardButton == null) return;
            SetVertical(_gameButton, includeMods ? (Selectable)_skinsEntryButton : _keyboardButton,
                        _audioButton);
            SetVertical(_audioButton, _gameButton, _videoButton);
            SetVertical(_videoButton, _audioButton, _controllerButton);
            SetVertical(_controllerButton, _videoButton, _keyboardButton);
            SetVertical(_keyboardButton, _controllerButton,
                        includeMods ? (Selectable)_entryButton : _gameButton);
            if (includeMods && _entryButton != null && _skinsEntryButton != null)
            {
                SetVertical(_entryButton, _keyboardButton, _skinsEntryButton);
                SetVertical(_skinsEntryButton, _entryButton, _gameButton);
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

        internal void Open(NativeMenuRoute route)
        {
            NativeMenuBinding binding = _lifecycle.Current;
            GameObject entry = route == NativeMenuRoute.Mods
                ? binding?.ModsEntryRoot
                : binding?.SkinsEntryRoot;
            if (_nativeOpen || !BindingIsAlive(binding) || entry == null ||
                !entry.activeInHierarchy ||
                !_lifecycle.TryBegin(out SilksongNativeMenuTransition<NativeMenuBinding> transition))
                return;
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
            _transitionCoroutine = StartCoroutine(
                CloseRoutine(binding, transition, showOptions));
        }

        IEnumerator CloseRoutine(NativeMenuBinding binding,
                                 SilksongNativeMenuTransition<NativeMenuBinding> transition,
                                 bool showOptions)
        {
            if (_openRoute == NativeMenuRoute.Mods) binding.Menu.Close();
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
            if (_nativeOpen && _openRoute == NativeMenuRoute.Mods) binding.Menu.Close();
            _nativeOpen = false;
            if (binding.ModsScreen != null)
                binding.ModsScreen.gameObject.SetActive(false);
            if (binding.SkinsScreen != null)
                binding.SkinsScreen.gameObject.SetActive(false);
            _transitionCoroutine = null;
        }

        bool TransitionStillCurrent(
            NativeMenuBinding binding,
            SilksongNativeMenuTransition<NativeMenuBinding> transition)
        {
            return _lifecycle.IsCurrent(transition, binding) && BindingIsAlive(binding);
        }

        internal void Select(SilksongNativeModsButton button)
        {
            if (button == null) return;
            _focusedRole = (ButtonRole)button.Role;
            if (_focusedRole == ButtonRole.Row && button.DataIndex >= 0 &&
                button.DataIndex < _menu.CurrentRows.Count)
                _menu.MoveRow(button.DataIndex - _menu.SelectedRowIndex);
            Paint();
        }

        internal void Submit(SilksongNativeModsButton button)
        {
            if (button == null || _lifecycle.Transitioning) return;
            Select(button);
            switch ((ButtonRole)button.Role)
            {
                case ButtonRole.Group: _menu.MoveGroup(1); break;
                case ButtonRole.Master: _menu.ToggleMaster(); break;
                case ButtonRole.Row: _menu.ActivateSelected(); break;
                case ButtonRole.Reset: _menu.Reset(); break;
                case ButtonRole.Back: Close(); return;
            }
            Paint();
        }

        internal void Move(SilksongNativeModsButton button, MoveDirection direction)
        {
            if (button == null || _lifecycle.Transitioning) return;
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

            bool down = direction == MoveDirection.Down;
            if (role == ButtonRole.Row)
            {
                Select(button);
                int selected = _menu.SelectedRowIndex;
                int count = _menu.CurrentRows.Count;
                if ((!down && selected > 0) || (down && selected + 1 < count))
                {
                    _menu.MoveRow(down ? 1 : -1);
                    Paint();
                    SelectCurrentRowButton();
                }
                else
                    Focus(down ? ButtonRole.Reset : ButtonRole.Master);
                return;
            }

            if (role == ButtonRole.Group) Focus(down ? ButtonRole.Master : ButtonRole.Back);
            else if (role == ButtonRole.Master)
            {
                if (down && _menu.CurrentRows.Count > 0) SelectCurrentRowButton();
                else Focus(ButtonRole.Group);
            }
            else if (role == ButtonRole.Reset)
            {
                if (!down && _menu.CurrentRows.Count > 0) SelectCurrentRowButton();
                else Focus(ButtonRole.Back);
            }
            else if (role == ButtonRole.Back) Focus(down ? ButtonRole.Group : ButtonRole.Reset);
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

        void SelectCurrentRowButton()
        {
            int slot = _menu.SelectedRowIndex - _menu.WindowStart;
            if (slot >= 0 && slot < VisibleRows && _buttons[slot + 2].gameObject.activeInHierarchy)
                _buttons[slot + 2].Select();
        }

        void Focus(ButtonRole role)
        {
            int index = role == ButtonRole.Group ? 0 :
                        role == ButtonRole.Master ? 1 :
                        role == ButtonRole.Reset ? 7 : 8;
            _focusedRole = role;
            _buttons[index].Select();
            Paint();
        }

        void Paint()
        {
            if (_menu == null || _labels.Count != 9 || _valueLabels.Count != 9 ||
                _title == null || _description == null) return;
            _labels[0].text = "< " + Friendly(_menu.Groups[_menu.SelectedGroupIndex]) + " >";
            _valueLabels[0].text = (_menu.SelectedGroupIndex + 1) + "/" + _menu.Groups.Count;
            _labels[1].text = "MASTER MODS";
            _valueLabels[1].text = _session.Controller.MasterEnabled ? "ON" : "OFF";

            IReadOnlyList<TweakDescriptor> rows = _menu.CurrentRows;
            for (int slot = 0; slot < VisibleRows; slot++)
            {
                int dataIndex = _menu.WindowStart + slot;
                SilksongNativeModsButton button = _buttons[slot + 2];
                GameObject root = _buttonRoots[slot + 2];
                bool shown = dataIndex >= 0 && dataIndex < rows.Count;
                if (root.activeSelf != shown) root.SetActive(shown);
                button.DataIndex = shown ? dataIndex : -1;
                if (!shown) continue;

                TweakDescriptor descriptor = rows[dataIndex];
                string value;
                if (!descriptor.IsAvailable) value = "UNAVAILABLE";
                else if (descriptor.ControlKind == TweakControlKind.Command) value = "RUN";
                else if (descriptor.ControlKind == TweakControlKind.Route) value = "OPEN";
                else value = Friendly(_session.Controller.Value(descriptor.Id));
                _labels[slot + 2].text = descriptor.Title.ToUpperInvariant();
                _valueLabels[slot + 2].text = value;
            }

            _labels[7].text = "RESET ALL MODS";
            _labels[8].text = "BACK";
            _title.text = "MODS";
            _description.text = _menu.MessageIsError
                ? _menu.Message.ToUpperInvariant()
                : FocusDescription();
        }

        string FocusDescription()
        {
            if (_focusedRole == ButtonRole.Group)
                return "Choose a mod category.";
            if (_focusedRole == ButtonRole.Master)
                return "Enable or disable all built-in mods.";
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
            if (button == null || _skinMenu == null) return;
            _skinMenu.SelectRow(button.DataIndex);
            PaintSkins();
        }

        internal void SubmitSkin(SilksongNativeSkinButton button)
        {
            if (button == null || _skinMenu == null || _lifecycle.Transitioning) return;
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
            if (button == null || _skinMenu == null || _lifecycle.Transitioning) return;
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
                    _skinButtons[index].Select();
                    return;
                }
        }

        void ApplySkinMutation(NativeSkinMutation mutation)
        {
            if (_skinMenu == null || mutation.Kind == NativeSkinMutationKind.None) return;
            string expected = _skinMenu.Snapshot.ConfigSha256;
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
            try
            {
                string json = SkinBridge.CallStatic<string>("readMenuSnapshot", SkinProfileId);
                if (string.IsNullOrEmpty(json) || json.Length > MaximumSkinSnapshotBytes)
                    throw new InvalidOperationException("Invalid native skin snapshot length.");
                WireSkinSnapshot wire = JsonUtility.FromJson<WireSkinSnapshot>(json);
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
                var snapshot = new NativeSkinMenuSnapshot(wire.profileId, wire.configSha256,
                    wire.mode, wire.spriteScope, wire.selectedPackId,
                    wire.eligiblePackIds ?? Array.Empty<string>(), packs);
                if (_skinMenu == null) _skinMenu = new NativeSkinMenuModel(snapshot, VisibleRows);
                else _skinMenu.Replace(snapshot);
                if (_skinError.StartsWith("SKINS UNAVAILABLE", StringComparison.Ordinal))
                    _skinError = "";
            }
            catch (Exception error)
            {
                _skinError = "SKINS UNAVAILABLE · " + error.GetBaseException().Message;
                Debug.LogWarning("[Silksong Skins] " + _skinError);
            }
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
                for (int index = 0; index < _skinButtonRoots.Count; index++)
                    _skinButtonRoots[index].SetActive(false);
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
                _skinButtons[slot].interactable = row.IsActionable;
                _skinLabels[slot].text = row.Label.ToUpperInvariant() +
                    (string.IsNullOrEmpty(row.Value) ? "" : "     " + row.Value);
            }
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
        [Serializable] sealed class WireSkinSnapshot
        {
            public bool ok;
            public string code, detail, profileId, configSha256, mode, spriteScope, selectedPackId;
            public string[] eligiblePackIds;
            public WireSkinPack[] packs;
        }
#pragma warning restore CS0649

        void CancelAndClearBinding()
        {
            NativeMenuBinding binding = _lifecycle.Current;
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            _lifecycle.Clear();
            if (binding != null && _nativeOpen && _openRoute == NativeMenuRoute.Mods)
                binding.Menu.Close();
            _nativeOpen = false;
            ClearBinding();
        }

        void ClearBinding()
        {
            WireOptionsNavigation(false);
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
        }

        void OnDestroy()
        {
            CancelAndClearBinding();
            AndroidJavaClass bridge = _skinBridge;
            _skinBridge = null;
            if (bridge != null) bridge.Dispose();
        }
    }

    public sealed class SilksongNativeModsEntryButton : MenuSelectable,
        ISubmitHandler, IPointerClickHandler
    {
        internal SilksongNativeModsMenu Owner;
        internal Animator FlashEffect;
        internal int Route;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (!interactable || Owner == null) return;
            ForceDeselect();
            Flash();
            PlaySubmitSound();
            Owner.OpenEntry(Route);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            ((ISubmitHandler)this).OnSubmit(eventData);
        }

        void Flash()
        {
            if (FlashEffect == null) return;
            FlashEffect.ResetTrigger("Flash");
            FlashEffect.SetTrigger("Flash");
        }
    }

    public sealed class SilksongNativeModsButton : MenuSelectable,
        ISubmitHandler, IPointerClickHandler, IMoveHandler, ICancelHandler, ISelectHandler
    {
        internal SilksongNativeModsMenu Owner;
        internal Animator FlashEffect;
        internal int Role;
        internal int DataIndex = -1;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (!interactable || Owner == null) return;
            Flash();
            PlaySubmitSound();
            Owner.Submit(this);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            Owner?.Select(this);
            ((ISubmitHandler)this).OnSubmit(eventData);
        }

        void IMoveHandler.OnMove(AxisEventData eventData)
        {
            if (!interactable || Owner == null) return;
            Owner.Move(this, eventData.moveDir);
            eventData.Use();
        }

        void ICancelHandler.OnCancel(BaseEventData eventData)
        {
            if (!interactable || Owner == null) return;
            ForceDeselect();
            PlayCancelSound();
            Owner.Close();
            eventData.Use();
        }

        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            Owner?.Select(this);
        }

        void Flash()
        {
            if (FlashEffect == null) return;
            FlashEffect.ResetTrigger("Flash");
            FlashEffect.SetTrigger("Flash");
        }
    }

    public sealed class SilksongNativeSkinButton : MenuSelectable,
        ISubmitHandler, IPointerClickHandler, IMoveHandler, ICancelHandler, ISelectHandler
    {
        internal SilksongNativeModsMenu Owner;
        internal Animator FlashEffect;
        internal int DataIndex = -1;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (!interactable || Owner == null) return;
            Flash();
            PlaySubmitSound();
            Owner.SubmitSkin(this);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            Owner?.SelectSkin(this);
            ((ISubmitHandler)this).OnSubmit(eventData);
        }

        void IMoveHandler.OnMove(AxisEventData eventData)
        {
            if (!interactable || Owner == null) return;
            Owner.MoveSkin(this, eventData.moveDir);
            eventData.Use();
        }

        void ICancelHandler.OnCancel(BaseEventData eventData)
        {
            if (!interactable || Owner == null) return;
            ForceDeselect();
            PlayCancelSound();
            Owner.Close();
            eventData.Use();
        }

        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            Owner?.SelectSkin(this);
        }

        void Flash()
        {
            if (FlashEffect == null) return;
            FlashEffect.ResetTrigger("Flash");
            FlashEffect.SetTrigger("Flash");
        }
    }
}
#endif
