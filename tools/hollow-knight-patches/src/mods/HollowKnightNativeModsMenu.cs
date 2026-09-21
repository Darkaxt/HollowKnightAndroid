#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DualSouls.Mods;
using DualSouls.Skins;
using GlobalEnums;
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
        const int MaximumSkinSnapshotBytes = 262144;
        const string SkinProfileId = "hollow-knight";
        const float ButtonTextHorizontalInset = 80f;
        const float MaximumRowStep = 78f;
        const float MinimumRowStep = 58f;

        internal enum NativeMenuRoute { Mods, Skins }
        enum ButtonRole { Group, Master, Row, Reset, Back }

        sealed class NativeMenuBinding
        {
            public NativeMenuBinding(UIManager ui, MenuScreen optionsScreen,
                                     MenuScreen modsScreen, MenuScreen skinsScreen,
                                     HollowKnightModsSession session,
                                     TweakMenuModel menu, GameObject modsEntryRoot,
                                     GameObject skinsEntryRoot)
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
            public HollowKnightModsSession Session { get; }
            public TweakMenuModel Menu { get; }
            public GameObject ModsEntryRoot { get; }
            public GameObject SkinsEntryRoot { get; }
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

        sealed class NativeText
        {
            readonly Component _component;
            readonly PropertyInfo _textProperty;

            NativeText(Component component, PropertyInfo textProperty)
            {
                _component = component;
                _textProperty = textProperty;
            }

            public string Text
            {
                set { _textProperty.SetValue(_component, value, null); }
            }

            public NativeText CloneSibling(string name)
            {
                GameObject clone = UnityObject.Instantiate(
                    _component.gameObject, _component.transform.parent, false);
                clone.name = name;
                NativeText text = Find(clone);
                if (text == null)
                    throw new InvalidOperationException("Cloned native text is unavailable.");
                return text;
            }

            public void ConfigureColumn(bool rightAligned)
            {
                RectTransform rect = _component.transform as RectTransform;
                if (rect != null)
                    rect.sizeDelta = new Vector2(-ButtonTextHorizontalInset, rect.sizeDelta.y);

                Text uiText = _component as Text;
                if (uiText != null)
                {
                    uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    uiText.verticalOverflow = VerticalWrapMode.Overflow;
                    uiText.alignment = rightAligned
                        ? TextAnchor.MiddleRight
                        : TextAnchor.MiddleLeft;
                    return;
                }

                PropertyInfo alignmentProperty = _component.GetType().GetProperty("alignment");
                if (alignmentProperty == null || !alignmentProperty.CanWrite ||
                    !alignmentProperty.PropertyType.IsEnum) return;
                object alignment = Enum.Parse(alignmentProperty.PropertyType,
                                              rightAligned ? "Right" : "Left");
                alignmentProperty.SetValue(_component, alignment, null);
            }

            public void ConfigureSingleLine(float horizontalInset)
            {
                RectTransform rect = _component.transform as RectTransform;
                if (rect != null)
                    rect.sizeDelta = new Vector2(-horizontalInset, rect.sizeDelta.y);

                Text uiText = _component as Text;
                if (uiText == null) return;
                uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
                uiText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            public static NativeText Find(GameObject root)
            {
                if (root == null) return null;
                Component[] components = root.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null) continue;
                    Type type = component.GetType();
                    bool knownTextMeshPro =
                        (type.Namespace == "TMProOld" || type.Namespace == "TMPro") &&
                        (type.Name == "TextMeshPro" || type.Name == "TextMeshProUGUI");
                    if (!(component is Text) && !knownTextMeshPro) continue;
                    PropertyInfo textProperty = type.GetProperty("text");
                    if (textProperty != null && textProperty.CanWrite)
                        return new NativeText(component, textProperty);
                }
                return null;
            }
        }

        readonly List<OptionButton> _optionButtons = new List<OptionButton>();
        readonly List<HollowKnightNativeModsButton> _buttons =
            new List<HollowKnightNativeModsButton>();
        readonly List<GameObject> _buttonRoots = new List<GameObject>();
        readonly List<NativeText> _labels = new List<NativeText>();
        readonly List<NativeText> _valueLabels = new List<NativeText>();
        readonly List<HollowKnightNativeSkinButton> _skinButtons =
            new List<HollowKnightNativeSkinButton>();
        readonly List<GameObject> _skinButtonRoots = new List<GameObject>();
        readonly List<NativeText> _skinLabels = new List<NativeText>();

        NativeMenuBinding _binding;
        HollowKnightModsSession _session;
        TweakMenuModel _menu;
        NativeSkinMenuModel _skinMenu;
        AndroidJavaClass _skinBridge;
        UIManager _ui;
        MenuScreen _modsScreen;
        MenuScreen _skinsScreen;
        GameObject _entryRoot;
        GameObject _skinsEntryRoot;
        MenuButton _entrySelectable;
        MenuButton _skinsEntrySelectable;
        HollowKnightNativeModsEntryButton _entryButton;
        HollowKnightNativeModsEntryButton _skinsEntryButton;
        NativeText _title;
        NativeText _description;
        NativeText _skinsTitle;
        NativeText _skinsDescription;
        GameObject _descriptionRoot;
        GameObject _skinsDescriptionRoot;
        string _skinError = "";
        Coroutine _transitionCoroutine;
        NativeMenuRoute _openRoute;
        ButtonRole _focusedRole = ButtonRole.Group;
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
            if (binding.ModsEntryRoot.activeSelf != available)
                binding.ModsEntryRoot.SetActive(available);
            if (binding.SkinsEntryRoot.activeSelf != available)
                binding.SkinsEntryRoot.SetActive(available);
            WireOptionsNavigation(available);

            if (_nativeOpen)
            {
                if (!available && !_transitioning)
                    BeginClose(binding, showOptions: false);
                else if (_openRoute == NativeMenuRoute.Mods)
                    Paint();
                else
                    PaintSkins();
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
                   binding.SkinsScreen != null && binding.ModsEntryRoot != null &&
                   binding.SkinsEntryRoot != null && binding.Session != null &&
                   binding.Menu != null && ReferenceEquals(binding.Ui, _ui) &&
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
            HollowKnightModsRuntime runtime = HollowKnightModsRuntime.Current;
            HollowKnightModsSession session = runtime != null ? runtime.Session : null;
            if (session == null || !session.IsReady) return;

            UIManager manager = FindResidentUiManager();
            if (manager == null || manager.UICanvas == null ||
                manager.optionsMenuScreen == null || manager.optionsMenuScreen.content == null)
                return;
            if (_binding != null || _ui != null || _entryRoot != null ||
                _skinsEntryRoot != null || _modsScreen != null || _skinsScreen != null)
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
                BuildEntry(content, template.Wrapper, entryY,
                           NativeMenuRoute.Mods, "MODS");
                BuildEntry(content, template.Wrapper, entryY - optionStep,
                           NativeMenuRoute.Skins, "SKINS");
                BuildModsScreen(manager.optionsMenuScreen.gameObject,
                                template.Wrapper, template.Y, rowStep);
                BuildSkinsScreen(manager.optionsMenuScreen.gameObject,
                                 template.Wrapper, template.Y, rowStep);

                _generation++;
                _binding = new NativeMenuBinding(
                    manager, manager.optionsMenuScreen, _modsScreen, _skinsScreen,
                    _session, _menu, _entryRoot, _skinsEntryRoot);
                _topologyWarningLogged = false;
                Debug.Log("[HK Mods] bound native paused Options -> Mods/Skins routes.");
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

        void BuildEntry(Transform content, GameObject template, float y,
                             NativeMenuRoute route, string label)
        {
            GameObject root = Instantiate(template, content, false);
            root.name = label;
            RectTransform wrapper = root.transform as RectTransform;
            if (wrapper != null)
                wrapper.anchoredPosition = new Vector2(wrapper.anchoredPosition.x, y);

            MenuButton source = root.GetComponentInChildren<MenuButton>(true);
            if (source == null)
                throw new InvalidOperationException(label + " entry template has no MenuButton.");
            HollowKnightNativeModsEntryButton button =
                source.gameObject.AddComponent<HollowKnightNativeModsEntryButton>();
            button.Owner = this;
            button.Selectable = source;
            button.Route = (int)route;
            DisableForeignDrivers(root);
            SetButtonText(root, label);
            root.SetActive(false);
            if (route == NativeMenuRoute.Mods)
            {
                _entryRoot = root;
                _entrySelectable = source;
                _entryButton = button;
            }
            else
            {
                _skinsEntryRoot = root;
                _skinsEntrySelectable = source;
                _skinsEntryButton = button;
            }
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
                ? NativeText.Find(_modsScreen.title.gameObject)
                : null;
            if (_title == null)
                throw new InvalidOperationException("Options title text is unavailable.");

            if (_modsScreen.controls != null)
                _modsScreen.controls.gameObject.SetActive(false);
            if (_modsScreen.content == null)
                throw new InvalidOperationException("Options content root is unavailable.");
            Transform content = _modsScreen.content.transform;
            DisableInheritedButtonsOutsideContent(root, content);
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                UnityObject.Destroy(child);
            }

            CreateButton(content, rowTemplate, ButtonRole.Group, 0, firstY, rowStep, "CATEGORY");
            CreateButton(content, rowTemplate, ButtonRole.Master, 1, firstY, rowStep,
                         "MASTER MODS");
            for (int i = 0; i < VisibleRows; i++)
                CreateButton(content, rowTemplate, ButtonRole.Row, i + 2, firstY, rowStep, "MOD");
            CreateDescription(content, rowTemplate, 7, firstY, rowStep);
            CreateButton(content, rowTemplate, ButtonRole.Reset, 8, firstY, rowStep,
                         "RESET ALL MODS");
            CreateButton(content, rowTemplate, ButtonRole.Back, 9, firstY, rowStep, "BACK");

            DisableForeignDrivers(root);
            _modsScreen.defaultHighlight = _buttons[0].Selectable;
            _title.Text = "MODS";
            root.SetActive(false);
        }

        void BuildSkinsScreen(GameObject optionsScreen, GameObject rowTemplate,
                              float firstY, float rowStep)
        {
            GameObject root = Instantiate(optionsScreen, _ui.UICanvas.transform, false);
            root.name = "SkinsMenuScreen";
            root.SetActive(false);
            _skinsScreen = root.GetComponent<MenuScreen>();
            if (_skinsScreen == null)
                throw new InvalidOperationException("Options screen clone has no typed MenuScreen.");
            MenuButtonList oldList = root.GetComponent<MenuButtonList>();
            if (oldList != null) UnityObject.Destroy(oldList);
            _skinsTitle = _skinsScreen.title != null
                ? NativeText.Find(_skinsScreen.title.gameObject)
                : null;
            if (_skinsTitle == null)
                throw new InvalidOperationException("Skins title text is unavailable.");
            if (_skinsScreen.controls != null) _skinsScreen.controls.gameObject.SetActive(false);
            if (_skinsScreen.content == null)
                throw new InvalidOperationException("Skins content root is unavailable.");
            Transform content = _skinsScreen.content.transform;
            DisableInheritedButtonsOutsideContent(root, content);
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                UnityObject.Destroy(child);
            }
            for (int index = 0; index < VisibleRows + 3; index++)
                CreateSkinButton(content, rowTemplate, index, firstY, rowStep);
            CreateSkinDescription(content, rowTemplate, VisibleRows + 3, firstY, rowStep);
            DisableForeignDrivers(root);
            _skinsScreen.defaultHighlight = _skinButtons[0].Selectable;
            _skinsTitle.Text = "SKINS";
            root.SetActive(false);
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

        void CreateDescription(Transform parent, GameObject template, int visualIndex,
                               float firstY, float rowStep)
        {
            _descriptionRoot = CreateDescriptionRow(
                parent, template, "ModsDescription", visualIndex,
                firstY, rowStep, out _description);
        }

        void CreateSkinDescription(Transform parent, GameObject template, int visualIndex,
                                   float firstY, float rowStep)
        {
            _skinsDescriptionRoot = CreateDescriptionRow(
                parent, template, "SkinsDescription", visualIndex,
                firstY, rowStep, out _skinsDescription);
        }

        static GameObject CreateDescriptionRow(Transform parent, GameObject template, string name,
                                               int visualIndex, float firstY, float rowStep,
                                               out NativeText label)
        {
            GameObject wrapper = Instantiate(template, parent, false);
            wrapper.name = name;
            RectTransform rect = wrapper.transform as RectTransform;
            if (rect != null)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                                                    firstY - rowStep * visualIndex);
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

        void CreateSkinButton(Transform parent, GameObject template, int visualIndex,
                              float firstY, float rowStep)
        {
            GameObject wrapper = Instantiate(template, parent, false);
            wrapper.name = "SkinsRow" + visualIndex;
            RectTransform rect = wrapper.transform as RectTransform;
            if (rect != null)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                                                    firstY - rowStep * visualIndex);
            MenuButton source = wrapper.GetComponentInChildren<MenuButton>(true);
            if (source == null)
                throw new InvalidOperationException("Native Skins row has no MenuButton.");
            var button = source.gameObject.AddComponent<HollowKnightNativeSkinButton>();
            button.Owner = this;
            button.Selectable = source;
            source.buttonType = MenuButton.MenuButtonType.Activate;
            source.cancelAction = CancelAction.DoNothing;
            source.navigation = new Navigation { mode = Navigation.Mode.None };
            RectTransform buttonRect = source.transform as RectTransform;
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x,
                                                   Math.Min(buttonRect.sizeDelta.y, rowStep - 4f));
            DisableForeignDrivers(wrapper);
            _skinButtonRoots.Add(wrapper);
            _skinButtons.Add(button);
            _skinLabels.Add(SetButtonText(wrapper, "SKIN", fullRow: true));
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
            source.buttonType = MenuButton.MenuButtonType.Activate;
            source.cancelAction = CancelAction.DoNothing;
            source.navigation = new Navigation { mode = Navigation.Mode.None };

            RectTransform buttonRect = source.transform as RectTransform;
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x,
                                                   Math.Min(buttonRect.sizeDelta.y, rowStep - 4f));
            DisableForeignDrivers(wrapper);
            NativeText label = SetButtonText(wrapper, initialText, fullRow: true);
            NativeText valueLabel = null;
            if (role == ButtonRole.Group || role == ButtonRole.Master ||
                role == ButtonRole.Row)
            {
                valueLabel = label.CloneSibling("ModsValue");
                label.ConfigureColumn(rightAligned: false);
                valueLabel.ConfigureColumn(rightAligned: true);
                valueLabel.Text = "";
            }
            _buttonRoots.Add(wrapper);
            _buttons.Add(button);
            _labels.Add(label);
            _valueLabels.Add(valueLabel);
        }

        static NativeText SetButtonText(GameObject root, string value, bool fullRow = false)
        {
            NativeText text = NativeText.Find(root);
            if (text == null)
                throw new InvalidOperationException("Native button text is unavailable.");
            if (fullRow)
                text.ConfigureSingleLine(ButtonTextHorizontalInset);
            text.Text = value;
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
                    behaviour is HollowKnightNativeSkinButton ||
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
                Selectable up = i == 0 ? (Selectable)_skinsEntrySelectable : _optionButtons[i - 1].Button;
                Selectable down = i + 1 == _optionButtons.Count
                    ? (Selectable)_entrySelectable
                    : _optionButtons[i + 1].Button;
                SetVertical(_optionButtons[i].Button, up, down);
            }
            SetVertical(_entrySelectable,
                        _optionButtons[_optionButtons.Count - 1].Button,
                        _skinsEntrySelectable);
            SetVertical(_skinsEntrySelectable, _entrySelectable, _optionButtons[0].Button);
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

        internal void Open(NativeMenuRoute route)
        {
            NativeMenuBinding binding = _binding;
            GameObject entry = route == NativeMenuRoute.Mods
                ? binding?.ModsEntryRoot
                : binding?.SkinsEntryRoot;
            if (_nativeOpen || !BindingIsAlive(binding, _generation) ||
                entry == null || !entry.activeInHierarchy || _transitioning)
                return;
            _transitioning = true;
            _openRoute = route;
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

            if (_openRoute == NativeMenuRoute.Mods)
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
            if (_openRoute == NativeMenuRoute.Mods)
                yield return binding.Ui.ShowMenu(binding.ModsScreen);
            else
                yield return binding.Ui.ShowMenu(binding.SkinsScreen);
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
            if (_openRoute == NativeMenuRoute.Mods) binding.Menu.Close();
            MenuScreen screen = _openRoute == NativeMenuRoute.Mods
                ? binding.ModsScreen
                : binding.SkinsScreen;
            _nativeOpen = false;
            yield return binding.Ui.HideMenu(screen);
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
            if (_nativeOpen && _openRoute == NativeMenuRoute.Mods) binding.Menu.Close();
            _nativeOpen = false;
            if (binding.ModsScreen != null)
                binding.ModsScreen.gameObject.SetActive(false);
            if (binding.SkinsScreen != null)
                binding.SkinsScreen.gameObject.SetActive(false);
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
            if (button == null) return;
            _focusedRole = (ButtonRole)button.Role;
            if (_focusedRole == ButtonRole.Row && button.DataIndex >= 0 &&
                button.DataIndex < _menu.CurrentRows.Count)
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
            _focusedRole = role;
            _buttons[index].Selectable.Select();
            Paint();
        }

        void Paint()
        {
            if (_menu == null || _labels.Count != 9 || _valueLabels.Count != 9) return;
            _labels[0].Text = "< " + Friendly(_menu.Groups[_menu.SelectedGroupIndex]) + " >";
            _valueLabels[0].Text = (_menu.SelectedGroupIndex + 1) + "/" + _menu.Groups.Count;
            _labels[1].Text = "MASTER MODS";
            _valueLabels[1].Text = _session.Controller.MasterEnabled ? "ON" : "OFF";

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
                _labels[slot + 2].Text = descriptor.Title.ToUpperInvariant();
                _valueLabels[slot + 2].Text = value;
            }

            _labels[7].Text = "RESET ALL MODS";
            _labels[8].Text = "BACK";
            _title.Text = "MODS";
            _description.Text = _menu.MessageIsError
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

        internal void SelectSkin(HollowKnightNativeSkinButton button)
        {
            if (button == null || _skinMenu == null) return;
            _skinMenu.SelectRow(button.DataIndex);
            PaintSkins();
        }

        internal void SubmitSkin(HollowKnightNativeSkinButton button)
        {
            if (button == null || _skinMenu == null || _transitioning) return;
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

        internal void MoveSkin(HollowKnightNativeSkinButton button, MoveDirection direction)
        {
            if (button == null || _skinMenu == null || _transitioning) return;
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
                HollowKnightModsRuntime runtime = HollowKnightModsRuntime.Current;
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
                Debug.LogWarning("[HK Skins] " + _skinError);
            }
        }

        void PaintSkins()
        {
            if (_skinLabels.Count != VisibleRows + 3 || _skinsTitle == null ||
                _skinsDescription == null) return;
            _skinsTitle.Text = "SKINS";
            _skinsDescription.Text = string.IsNullOrEmpty(_skinError)
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
                _skinButtons[slot].Selectable.interactable = row.IsActionable;
                _skinLabels[slot].Text = row.Label.ToUpperInvariant() +
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
            NativeMenuBinding binding = _binding;
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            _generation++;
            _transitioning = false;
            if (binding != null && _nativeOpen && _openRoute == NativeMenuRoute.Mods)
                binding.Menu.Close();
            _nativeOpen = false;
            ClearBinding();
        }

        void ClearBinding()
        {
            RestoreOptionsNavigation();
            if (_entryRoot != null) UnityObject.Destroy(_entryRoot);
            if (_skinsEntryRoot != null) UnityObject.Destroy(_skinsEntryRoot);
            if (_modsScreen != null) UnityObject.Destroy(_modsScreen.gameObject);
            if (_skinsScreen != null) UnityObject.Destroy(_skinsScreen.gameObject);
            _binding = null;
            _optionButtons.Clear();
            _buttons.Clear();
            _buttonRoots.Clear();
            _labels.Clear();
            _valueLabels.Clear();
            _skinButtons.Clear();
            _skinButtonRoots.Clear();
            _skinLabels.Clear();
            _entryRoot = null;
            _skinsEntryRoot = null;
            _entrySelectable = null;
            _skinsEntrySelectable = null;
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

    public sealed class HollowKnightNativeModsEntryButton : MonoBehaviour,
        ISubmitHandler, IPointerClickHandler
    {
        internal HollowKnightNativeModsMenu Owner;
        internal MenuButton Selectable;
        internal int Route;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null) return;
            Owner.OpenEntry(Route);
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

    public sealed class HollowKnightNativeSkinButton : MonoBehaviour,
        ISubmitHandler, IPointerClickHandler, IMoveHandler, ICancelHandler, ISelectHandler
    {
        internal HollowKnightNativeModsMenu Owner;
        internal MenuButton Selectable;
        internal int DataIndex = -1;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null) return;
            Owner.SubmitSkin(this);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            Owner?.SelectSkin(this);
            ((ISubmitHandler)this).OnSubmit(eventData);
        }

        void IMoveHandler.OnMove(AxisEventData eventData)
        {
            if (Selectable == null || !Selectable.interactable || Owner == null) return;
            Owner.MoveSkin(this, eventData.moveDir);
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
            Owner?.SelectSkin(this);
        }
    }
}
#endif
