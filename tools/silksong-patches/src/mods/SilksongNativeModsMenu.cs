#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using DualSouls.Mods;
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
        const float EntryY = -575f;
        const float FirstRowY = -25f;
        const float RowStep = 78f;

        enum ButtonRole { Group, Master, Row, Reset, Back }

        sealed class NativeMenuBinding
        {
            public NativeMenuBinding(UIManager ui, MenuScreen optionsScreen,
                                     MenuScreen modsScreen, TweakSession session,
                                     TweakMenuModel menu, GameObject entryRoot)
            {
                Ui = ui;
                OptionsScreen = optionsScreen;
                ModsScreen = modsScreen;
                Session = session;
                Menu = menu;
                EntryRoot = entryRoot;
            }

            public UIManager Ui { get; }
            public MenuScreen OptionsScreen { get; }
            public MenuScreen ModsScreen { get; }
            public TweakSession Session { get; }
            public TweakMenuModel Menu { get; }
            public GameObject EntryRoot { get; }
        }

        readonly List<SilksongNativeModsButton> _buttons =
            new List<SilksongNativeModsButton>();
        readonly List<GameObject> _buttonRoots = new List<GameObject>();
        readonly List<TmpText> _labels = new List<TmpText>();
        readonly SilksongNativeMenuLifecycle<NativeMenuBinding> _lifecycle =
            new SilksongNativeMenuLifecycle<NativeMenuBinding>();

        TweakSession _session;
        TweakMenuModel _menu;
        UIManager _ui;
        MenuScreen _modsScreen;
        GameObject _entryRoot;
        SilksongNativeModsEntryButton _entryButton;
        TmpText _title;
        MenuButton _gameButton;
        MenuButton _audioButton;
        MenuButton _videoButton;
        MenuButton _controllerButton;
        MenuButton _keyboardButton;
        Coroutine _transitionCoroutine;
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
            if (binding.EntryRoot.activeSelf != available)
                binding.EntryRoot.SetActive(available);
            WireOptionsNavigation(available);

            if (_nativeOpen)
            {
                if (!available && !_lifecycle.Transitioning)
                    BeginClose(binding, showOptions: false);
                else
                    Paint();
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
                   binding.ModsScreen != null && binding.EntryRoot != null &&
                   binding.Session != null && binding.Menu != null &&
                   ReferenceEquals(binding.Ui, _ui) &&
                   ReferenceEquals(binding.OptionsScreen, _ui.optionsMenuScreen) &&
                   ReferenceEquals(binding.ModsScreen, _modsScreen) &&
                   ReferenceEquals(binding.EntryRoot, _entryRoot) &&
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
                _modsScreen != null)
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
                BuildEntry(content, template.gameObject);
                BuildModsScreen(options.gameObject, template.gameObject);
                _lifecycle.Bind(new NativeMenuBinding(
                    manager, manager.optionsMenuScreen, _modsScreen,
                    _session, _menu, _entryRoot));
                _topologyWarningLogged = false;
                Debug.Log("[Silksong Mods] bound native paused Options -> Mods route.");
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

        void BuildEntry(Transform content, GameObject template)
        {
            _entryRoot = Instantiate(template, content, false);
            _entryRoot.name = "Mods";
            RectTransform wrapper = _entryRoot.transform as RectTransform;
            wrapper.anchoredPosition = new Vector2(wrapper.anchoredPosition.x, EntryY);

            MenuButton source = _entryRoot.GetComponentInChildren<MenuButton>(true);
            if (source == null) throw new InvalidOperationException("Mods entry template has no MenuButton.");
            source.enabled = false;
            _entryButton = source.gameObject.AddComponent<SilksongNativeModsEntryButton>();
            CopySelectable(source, _entryButton);
            _entryButton.Owner = this;
            _entryButton.FlashEffect = source.flashEffect;
            UnityObject.Destroy(source);
            DisableForeignDrivers(_entryRoot);
            SetButtonText(_entryRoot, "MODS");
            _entryRoot.SetActive(false);
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
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                UnityObject.Destroy(child);
            }

            CreateButton(content, rowTemplate, ButtonRole.Group, 0, "GROUP");
            CreateButton(content, rowTemplate, ButtonRole.Master, 1, "MASTER MODS");
            for (int i = 0; i < VisibleRows; i++)
                CreateButton(content, rowTemplate, ButtonRole.Row, i + 2, "MOD");
            CreateButton(content, rowTemplate, ButtonRole.Reset, 7, "RESET ALL MODS");
            CreateButton(content, rowTemplate, ButtonRole.Back, 8, "BACK");

            DisableForeignDrivers(root);
            _modsScreen.defaultHighlight = _buttons[0];
            _title.text = "MODS";
            root.SetActive(false);
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
            _buttonRoots.Add(wrapper);
            _buttons.Add(button);
            _labels.Add(label);
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

        static void DisableForeignDrivers(GameObject root)
        {
            Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour is Animator || behaviour is Graphic ||
                    behaviour is CanvasGroup || behaviour is MenuScreen ||
                    behaviour is SilksongNativeModsButton ||
                    behaviour is SilksongNativeModsEntryButton) continue;
                behaviour.enabled = false;
            }
        }

        void WireOptionsNavigation(bool includeMods)
        {
            if (_gameButton == null || _audioButton == null || _videoButton == null ||
                _controllerButton == null || _keyboardButton == null) return;
            SetVertical(_gameButton, includeMods ? (Selectable)_entryButton : _keyboardButton,
                        _audioButton);
            SetVertical(_audioButton, _gameButton, _videoButton);
            SetVertical(_videoButton, _audioButton, _controllerButton);
            SetVertical(_controllerButton, _videoButton, _keyboardButton);
            SetVertical(_keyboardButton, _controllerButton,
                        includeMods ? (Selectable)_entryButton : _gameButton);
            if (includeMods && _entryButton != null)
                SetVertical(_entryButton, _keyboardButton, _gameButton);
        }

        static void SetVertical(Selectable selectable, Selectable up, Selectable down)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            selectable.navigation = navigation;
        }

        internal void Open()
        {
            NativeMenuBinding binding = _lifecycle.Current;
            if (_nativeOpen || !BindingIsAlive(binding) ||
                !binding.EntryRoot.activeInHierarchy ||
                !_lifecycle.TryBegin(out SilksongNativeMenuTransition<NativeMenuBinding> transition))
                return;
            _transitionCoroutine = StartCoroutine(OpenRoutine(binding, transition));
        }

        IEnumerator OpenRoutine(NativeMenuBinding binding,
                                SilksongNativeMenuTransition<NativeMenuBinding> transition)
        {
            yield return binding.Ui.HideMenu(binding.OptionsScreen);
            if (!OpenTransitionStillAvailable(binding, transition))
            {
                CancelOpenTransition(binding, transition);
                yield break;
            }

            binding.Menu.Open();
            _nativeOpen = true;
            Paint();
            yield return binding.Ui.ShowMenu(binding.ModsScreen);
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
            binding.Menu.Close();
            _nativeOpen = false;
            yield return binding.Ui.HideMenu(binding.ModsScreen);
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
            if (_nativeOpen) binding.Menu.Close();
            _nativeOpen = false;
            if (binding.ModsScreen != null)
                binding.ModsScreen.gameObject.SetActive(false);
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
            if (button == null || (ButtonRole)button.Role != ButtonRole.Row ||
                button.DataIndex < 0 || button.DataIndex >= _menu.CurrentRows.Count) return;
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
            _buttons[index].Select();
        }

        void Paint()
        {
            if (_menu == null || _labels.Count != 9) return;
            _labels[0].text = "GROUP  <  " + Friendly(_menu.Groups[_menu.SelectedGroupIndex]) +
                              "  >  " + (_menu.SelectedGroupIndex + 1) + "/" + _menu.Groups.Count;
            _labels[1].text = "MASTER MODS     " +
                              (_session.Controller.MasterEnabled ? "ON" : "OFF");

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
                _labels[slot + 2].text = descriptor.Title.ToUpperInvariant() + "     " + value;
            }

            _labels[7].text = "RESET ALL MODS";
            _labels[8].text = "BACK";
            string status = _menu.Message;
            TweakDescriptor selected = _menu.Selected;
            _title.text = string.IsNullOrEmpty(status)
                ? "MODS  ·  " + (selected != null ? selected.Title.ToUpperInvariant() :
                                  Friendly(_menu.Groups[_menu.SelectedGroupIndex]))
                : "MODS  ·  " + status.ToUpperInvariant();
        }

        static string Friendly(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "UNKNOWN"
                : value.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
        }

        void CancelAndClearBinding()
        {
            NativeMenuBinding binding = _lifecycle.Current;
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            _lifecycle.Clear();
            if (binding != null && _nativeOpen) binding.Menu.Close();
            _nativeOpen = false;
            ClearBinding();
        }

        void ClearBinding()
        {
            WireOptionsNavigation(false);
            if (_entryRoot != null) UnityObject.Destroy(_entryRoot);
            if (_modsScreen != null) UnityObject.Destroy(_modsScreen.gameObject);
            _buttons.Clear();
            _buttonRoots.Clear();
            _labels.Clear();
            _entryRoot = null;
            _entryButton = null;
            _modsScreen = null;
            _title = null;
            _gameButton = _audioButton = _videoButton = null;
            _controllerButton = _keyboardButton = null;
            _ui = null;
            _session = null;
            _menu = null;
        }

        void OnDestroy()
        {
            CancelAndClearBinding();
        }
    }

    public sealed class SilksongNativeModsEntryButton : MenuSelectable,
        ISubmitHandler, IPointerClickHandler
    {
        internal SilksongNativeModsMenu Owner;
        internal Animator FlashEffect;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (!interactable || Owner == null) return;
            ForceDeselect();
            Flash();
            PlaySubmitSound();
            Owner.Open();
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
}
#endif
