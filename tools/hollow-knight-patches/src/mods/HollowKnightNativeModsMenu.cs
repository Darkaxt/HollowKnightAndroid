#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using DualSouls.Mods;
using GlobalEnums;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityObject = UnityEngine.Object;

namespace DualSouls.Mods.HollowKnight
{
    /// <summary>
    /// Adds the complete process-owned Mods model to Hollow Knight's paused Options
    /// menu without changing the direct-display transport or polling gameplay input.
    /// </summary>
    public sealed class HollowKnightNativeModsMenu : MonoBehaviour
    {
        const int VisibleRows = 5;
        const float MaximumRowStep = 78f;
        const float MinimumRowStep = 58f;

        enum ButtonRole { Group, Master, Row, Reset, Back }

        sealed class NativeMenuBinding
        {
            public NativeMenuBinding(UIManager ui, MenuScreen optionsScreen,
                                     MenuScreen modsScreen, HollowKnightModsSession session,
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
            public HollowKnightModsSession Session { get; }
            public TweakMenuModel Menu { get; }
            public GameObject EntryRoot { get; }
        }

        sealed class OptionButton
        {
            public OptionButton(MenuButton button, GameObject wrapper)
            {
                Button = button;
                Wrapper = wrapper;
                OriginalNavigation = button.navigation;
                RectTransform rect = wrapper.transform as RectTransform;
                Y = rect != null ? rect.anchoredPosition.y : 0f;
            }

            public MenuButton Button { get; }
            public GameObject Wrapper { get; }
            public Navigation OriginalNavigation { get; }
            public float Y { get; }
        }

        readonly List<OptionButton> _optionButtons = new List<OptionButton>();
        readonly List<HollowKnightNativeModsButton> _buttons =
            new List<HollowKnightNativeModsButton>();
        readonly List<GameObject> _buttonRoots = new List<GameObject>();
        readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();

        NativeMenuBinding _binding;
        HollowKnightModsSession _session;
        TweakMenuModel _menu;
        UIManager _ui;
        MenuScreen _modsScreen;
        GameObject _entryRoot;
        MenuButton _entrySelectable;
        HollowKnightNativeModsEntryButton _entryButton;
        TextMeshProUGUI _title;
        Coroutine _transitionCoroutine;
        int _generation;
        bool _transitioning;
        bool _nativeOpen;
        bool _optionsNavigationIncludesEntry;
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

            NativeMenuBinding binding = _binding;
            if (!binding.Session.IsReady)
            {
                CancelAndClearBinding();
                return;
            }
            bool available = binding.Ui.uiState == UIState.PAUSED;
            if (binding.EntryRoot.activeSelf != available)
                binding.EntryRoot.SetActive(available);
            WireOptionsNavigation(available);

            if (_nativeOpen)
            {
                if (!available && !_transitioning)
                    BeginClose(binding, showOptions: false);
                else
                    Paint();
            }
        }

        bool BindingIsAlive()
        {
            return BindingIsAlive(_binding, _generation);
        }

        bool BindingIsAlive(NativeMenuBinding binding, int generation)
        {
            return generation == _generation && binding != null &&
                   ReferenceEquals(_binding, binding) && binding.Ui != null &&
                   binding.OptionsScreen != null && binding.ModsScreen != null &&
                   binding.EntryRoot != null && binding.Session != null &&
                   binding.Menu != null && ReferenceEquals(binding.Ui, _ui) &&
                   ReferenceEquals(binding.OptionsScreen, _ui.optionsMenuScreen) &&
                   ReferenceEquals(binding.ModsScreen, _modsScreen) &&
                   ReferenceEquals(binding.EntryRoot, _entryRoot) &&
                   ReferenceEquals(binding.Session, _session) &&
                   ReferenceEquals(binding.Menu, _menu);
        }

        void TryBind()
        {
            HollowKnightModsRuntime runtime = HollowKnightModsRuntime.Current;
            HollowKnightModsSession session = runtime != null ? runtime.Session : null;
            if (session == null || !session.IsReady) return;

            UIManager manager = FindResidentUiManager();
            if (manager == null || manager.UICanvas == null ||
                manager.optionsMenuScreen == null || manager.optionsMenuScreen.content == null)
                return;
            if (_binding != null || _ui != null || _entryRoot != null || _modsScreen != null)
            {
                if (BindingIsAlive() && ReferenceEquals(_ui, manager)) return;
                CancelAndClearBinding();
            }

            try
            {
                Transform content = manager.optionsMenuScreen.content.transform;
                CollectOptionButtons(content);
                if (_optionButtons.Count < 2)
                    throw new InvalidOperationException(
                        "Hollow Knight Options menu has fewer than two typed route buttons.");

                _session = session;
                _menu = session.Menu;
                _ui = manager;

                OptionButton template = _optionButtons[0];
                float optionStep = ResolveOptionStep();
                float rowStep = Mathf.Clamp(optionStep, MinimumRowStep, MaximumRowStep);
                float entryY = _optionButtons[_optionButtons.Count - 1].Y - optionStep;
                BuildEntry(content, template.Wrapper, entryY);
                BuildModsScreen(manager.optionsMenuScreen.gameObject,
                                template.Wrapper, template.Y, rowStep);

                _generation++;
                _binding = new NativeMenuBinding(
                    manager, manager.optionsMenuScreen, _modsScreen,
                    _session, _menu, _entryRoot);
                _topologyWarningLogged = false;
                Debug.Log("[HK Mods] bound native paused Options -> Mods route.");
            }
            catch (Exception error)
            {
                CancelAndClearBinding();
                if (!_topologyWarningLogged)
                {
                    _topologyWarningLogged = true;
                    Debug.LogError("[HK Mods] native menu capability unavailable: " +
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

        void CollectOptionButtons(Transform content)
        {
            _optionButtons.Clear();
            var wrappers = new HashSet<GameObject>();
            MenuButton[] buttons = content.GetComponentsInChildren<MenuButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                MenuButton button = buttons[i];
                if (button == null || !ActiveBelow(button.transform, content)) continue;
                GameObject wrapper = FindDirectChild(content, button.transform);
                if (wrapper == null || !wrappers.Add(wrapper)) continue;
                _optionButtons.Add(new OptionButton(button, wrapper));
            }
            _optionButtons.Sort((left, right) => right.Y.CompareTo(left.Y));
        }

        static bool ActiveBelow(Transform child, Transform ancestor)
        {
            Transform current = child;
            while (current != null && current != ancestor)
            {
                if (!current.gameObject.activeSelf) return false;
                current = current.parent;
            }
            return current == ancestor;
        }

        static GameObject FindDirectChild(Transform parent, Transform descendant)
        {
            Transform current = descendant;
            while (current != null && current.parent != parent)
                current = current.parent;
            return current != null && current.parent == parent ? current.gameObject : null;
        }

        float ResolveOptionStep()
        {
            float total = 0f;
            int samples = 0;
            for (int i = 1; i < _optionButtons.Count; i++)
            {
                float distance = _optionButtons[i - 1].Y - _optionButtons[i].Y;
                if (distance <= 1f) continue;
                total += distance;
                samples++;
            }
            return samples > 0 ? total / samples : MaximumRowStep;
        }

        void BuildEntry(Transform content, GameObject template, float y)
        {
            _entryRoot = Instantiate(template, content, false);
            _entryRoot.name = "Mods";
            RectTransform wrapper = _entryRoot.transform as RectTransform;
            if (wrapper != null)
                wrapper.anchoredPosition = new Vector2(wrapper.anchoredPosition.x, y);

            MenuButton source = _entryRoot.GetComponentInChildren<MenuButton>(true);
            if (source == null)
                throw new InvalidOperationException("Mods entry template has no MenuButton.");
            _entrySelectable = source;
            _entryButton = source.gameObject.AddComponent<HollowKnightNativeModsEntryButton>();
            _entryButton.Owner = this;
            _entryButton.Selectable = source;
            DisableForeignDrivers(_entryRoot);
            SetButtonText(_entryRoot, "MODS");
            _entryRoot.SetActive(false);
        }

        void BuildModsScreen(GameObject optionsScreen, GameObject rowTemplate,
                             float firstY, float rowStep)
        {
            GameObject root = Instantiate(optionsScreen, _ui.UICanvas.transform, false);
            root.name = "ModsMenuScreen";
            root.SetActive(false);

            _modsScreen = root.GetComponent<MenuScreen>();
            if (_modsScreen == null)
                throw new InvalidOperationException("Options screen clone has no typed MenuScreen.");
            MenuButtonList oldList = root.GetComponent<MenuButtonList>();
            if (oldList != null) UnityObject.Destroy(oldList);

            _title = _modsScreen.title != null
                ? _modsScreen.title.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            if (_title == null)
                throw new InvalidOperationException("Options title text is unavailable.");

            if (_modsScreen.controls != null)
                _modsScreen.controls.gameObject.SetActive(false);
            if (_modsScreen.content == null)
                throw new InvalidOperationException("Options content root is unavailable.");
            Transform content = _modsScreen.content.transform;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                UnityObject.Destroy(child);
            }

            CreateButton(content, rowTemplate, ButtonRole.Group, 0, firstY, rowStep, "GROUP");
            CreateButton(content, rowTemplate, ButtonRole.Master, 1, firstY, rowStep,
                         "MASTER MODS");
            for (int i = 0; i < VisibleRows; i++)
                CreateButton(content, rowTemplate, ButtonRole.Row, i + 2, firstY, rowStep, "MOD");
            CreateButton(content, rowTemplate, ButtonRole.Reset, 7, firstY, rowStep,
                         "RESET ALL MODS");
            CreateButton(content, rowTemplate, ButtonRole.Back, 8, firstY, rowStep, "BACK");

            DisableForeignDrivers(root);
            _modsScreen.defaultHighlight = _buttons[0].Selectable;
            _title.text = "MODS";
            root.SetActive(false);
        }

        void CreateButton(Transform parent, GameObject template, ButtonRole role,
                          int visualIndex, float firstY, float rowStep, string initialText)
        {
            GameObject wrapper = Instantiate(template, parent, false);
            wrapper.name = "Mods" + role + visualIndex;
            RectTransform rect = wrapper.transform as RectTransform;
            if (rect != null)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                                                    firstY - rowStep * visualIndex);

            MenuButton source = wrapper.GetComponentInChildren<MenuButton>(true);
            if (source == null)
                throw new InvalidOperationException("Native Mods row has no MenuButton.");
            HollowKnightNativeModsButton button =
                source.gameObject.AddComponent<HollowKnightNativeModsButton>();
            button.Owner = this;
            button.Selectable = source;
            button.Role = (int)role;
            source.cancelAction = CancelAction.DoNothing;
            source.navigation = new Navigation { mode = Navigation.Mode.None };

            RectTransform buttonRect = source.transform as RectTransform;
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x,
                                                   Math.Min(buttonRect.sizeDelta.y, rowStep - 4f));
            DisableForeignDrivers(wrapper);
            TextMeshProUGUI label = SetButtonText(wrapper, initialText);
            _buttonRoots.Add(wrapper);
            _buttons.Add(button);
            _labels.Add(label);
        }

        static TextMeshProUGUI SetButtonText(GameObject root, string value)
        {
            TextMeshProUGUI text = root.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null)
                throw new InvalidOperationException("Native button text is unavailable.");
            text.text = value;
            return text;
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
                    behaviour is HollowKnightNativeModsButton ||
                    behaviour is HollowKnightNativeModsEntryButton) continue;
                behaviour.enabled = false;
            }
        }

        void WireOptionsNavigation(bool includeMods)
        {
            if (_optionButtons.Count == 0) return;
            if (!includeMods)
            {
                if (!_optionsNavigationIncludesEntry) return;
                RestoreOptionsNavigation();
                return;
            }

            // Hollow Knight's MenuButtonList rewrites this screen each time it is
            // shown. Reapply the Mods edge while paused instead of trusting stale
            // local state after a native submenu transition.
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                Selectable up = i == 0 ? (Selectable)_entrySelectable : _optionButtons[i - 1].Button;
                Selectable down = i + 1 == _optionButtons.Count
                    ? (Selectable)_entrySelectable
                    : _optionButtons[i + 1].Button;
                SetVertical(_optionButtons[i].Button, up, down);
            }
            SetVertical(_entrySelectable,
                        _optionButtons[_optionButtons.Count - 1].Button,
                        _optionButtons[0].Button);
            _optionsNavigationIncludesEntry = true;
        }

        void RestoreOptionsNavigation()
        {
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                OptionButton option = _optionButtons[i];
                if (option.Button != null)
                    option.Button.navigation = option.OriginalNavigation;
            }
            _optionsNavigationIncludesEntry = false;
        }

        static void SetVertical(Selectable selectable, Selectable up, Selectable down)
        {
            if (selectable == null) return;
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            selectable.navigation = navigation;
        }

        internal void Open()
        {
            NativeMenuBinding binding = _binding;
            if (_nativeOpen || !BindingIsAlive(binding, _generation) ||
                !binding.EntryRoot.activeInHierarchy || _transitioning)
                return;
            _transitioning = true;
            int generation = _generation;
            _transitionCoroutine = StartCoroutine(OpenRoutine(binding, generation));
        }

        IEnumerator OpenRoutine(NativeMenuBinding binding, int generation)
        {
            yield return binding.Ui.HideMenu(binding.OptionsScreen);
            if (!OpenTransitionStillAvailable(binding, generation))
            {
                CancelOpenTransition(binding, generation);
                yield break;
            }

            binding.Menu.Open();
            _nativeOpen = true;
            Paint();
            yield return binding.Ui.ShowMenu(binding.ModsScreen);
            if (!OpenTransitionStillAvailable(binding, generation))
            {
                CancelOpenTransition(binding, generation);
                yield break;
            }

            CompleteTransition(binding, generation);
        }

        internal void Close()
        {
            BeginClose(_binding, showOptions: true);
        }

        void BeginClose(NativeMenuBinding binding, bool showOptions)
        {
            if (!_nativeOpen || !BindingIsAlive(binding, _generation) || _transitioning)
                return;
            _transitioning = true;
            int generation = _generation;
            _transitionCoroutine = StartCoroutine(CloseRoutine(binding, generation, showOptions));
        }

        IEnumerator CloseRoutine(NativeMenuBinding binding, int generation, bool showOptions)
        {
            binding.Menu.Close();
            _nativeOpen = false;
            yield return binding.Ui.HideMenu(binding.ModsScreen);
            if (!BindingIsAlive(binding, generation)) yield break;

            if (showOptions && binding.Ui.uiState == UIState.PAUSED)
            {
                yield return binding.Ui.ShowMenu(binding.OptionsScreen);
                if (!BindingIsAlive(binding, generation)) yield break;
            }

            CompleteTransition(binding, generation);
        }

        bool OpenTransitionStillAvailable(NativeMenuBinding binding, int generation)
        {
            return BindingIsAlive(binding, generation) && binding.Session.IsReady &&
                   binding.Ui.uiState == UIState.PAUSED;
        }

        void CancelOpenTransition(NativeMenuBinding binding, int generation)
        {
            if (!BindingIsAlive(binding, generation)) return;
            if (_nativeOpen) binding.Menu.Close();
            _nativeOpen = false;
            if (binding.ModsScreen != null)
                binding.ModsScreen.gameObject.SetActive(false);
            CompleteTransition(binding, generation);
        }

        void CompleteTransition(NativeMenuBinding binding, int generation)
        {
            if (!BindingIsAlive(binding, generation)) return;
            _transitioning = false;
            _transitionCoroutine = null;
        }

        internal void Select(HollowKnightNativeModsButton button)
        {
            if (button == null || (ButtonRole)button.Role != ButtonRole.Row ||
                button.DataIndex < 0 || button.DataIndex >= _menu.CurrentRows.Count) return;
            _menu.MoveRow(button.DataIndex - _menu.SelectedRowIndex);
            Paint();
        }

        internal void Submit(HollowKnightNativeModsButton button)
        {
            if (button == null || _transitioning) return;
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

        internal void Move(HollowKnightNativeModsButton button, MoveDirection direction)
        {
            if (button == null || _transitioning) return;
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
            {
                if (!string.Equals(selected.Values[i], current, StringComparison.Ordinal)) continue;
                index = i;
                break;
            }
            int next = (index + delta) % selected.Values.Count;
            if (next < 0) next += selected.Values.Count;
            _menu.SetSelected(selected.Values[next]);
        }

        void SelectCurrentRowButton()
        {
            int slot = _menu.SelectedRowIndex - _menu.WindowStart;
            if (slot >= 0 && slot < VisibleRows &&
                _buttons[slot + 2].gameObject.activeInHierarchy)
                _buttons[slot + 2].Selectable.Select();
        }

        void Focus(ButtonRole role)
        {
            int index = role == ButtonRole.Group ? 0 :
                        role == ButtonRole.Master ? 1 :
                        role == ButtonRole.Reset ? 7 : 8;
            _buttons[index].Selectable.Select();
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
                HollowKnightNativeModsButton button = _buttons[slot + 2];
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
            NativeMenuBinding binding = _binding;
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            _generation++;
            _transitioning = false;
            if (binding != null && _nativeOpen) binding.Menu.Close();
            _nativeOpen = false;
            ClearBinding();
        }

        void ClearBinding()
        {
            RestoreOptionsNavigation();
            if (_entryRoot != null) UnityObject.Destroy(_entryRoot);
            if (_modsScreen != null) UnityObject.Destroy(_modsScreen.gameObject);
            _binding = null;
            _optionButtons.Clear();
            _buttons.Clear();
            _buttonRoots.Clear();
            _labels.Clear();
            _entryRoot = null;
            _entrySelectable = null;
            _entryButton = null;
            _modsScreen = null;
            _title = null;
            _ui = null;
            _session = null;
            _menu = null;
        }

        void OnDestroy()
        {
            CancelAndClearBinding();
        }
    }

    public sealed class HollowKnightNativeModsEntryButton : MonoBehaviour,
        ISubmitHandler, IPointerClickHandler
    {
        internal HollowKnightNativeModsMenu Owner;
        internal MenuButton Selectable;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null) return;
            Owner.Open();
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            ((ISubmitHandler)this).OnSubmit(eventData);
        }
    }

    public sealed class HollowKnightNativeModsButton : MonoBehaviour,
        ISubmitHandler, IPointerClickHandler, IMoveHandler, ICancelHandler, ISelectHandler
    {
        internal HollowKnightNativeModsMenu Owner;
        internal MenuButton Selectable;
        internal int Role;
        internal int DataIndex = -1;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null) return;
            Owner.Submit(this);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            Owner?.Select(this);
            ((ISubmitHandler)this).OnSubmit(eventData);
        }

        void IMoveHandler.OnMove(AxisEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null) return;
            Owner.Move(this, eventData.moveDir);
            eventData.Use();
        }

        void ICancelHandler.OnCancel(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null) return;
            Selectable.ForceDeselect();
            Owner.Close();
            eventData.Use();
        }

        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            Owner?.Select(this);
        }
    }
}
#endif
