// A way into a mod's settings on a device with no F1 key.
//
// BepInEx's configuration UIs are all opened with a keyboard shortcut, because
// on a PC there is always a keyboard. Here there usually is not, and without a
// way in, a settings mod that is installed and running is indistinguishable
// from one that is broken.
//
// So the way in is the sticks: click both (L3+R3), on the controller the
// player already has in their hands. A chord rather than a single button
// because every single button is already the game's.
//
// The contract on the other side is the one those UIs already publish for
// other mods: a plugin that draws settings exposes
// `public bool DisplayingWindow { get; set; }`, and toggling it opens and
// closes the window. ConfigurationManager is the plugin that defines it.
// Nothing here names that plugin, and no plugin has to know about this: if
// none of them has the property, this component is never created.

using System;
using System.Reflection;
using BepInEx.Configuration;

namespace BepInEx.Bootstrap
{
    internal class ModMenu : UnityEngine.MonoBehaviour
    {
        const string PROPERTY = "DisplayingWindow";

        object _plugin;
        PropertyInfo _window;
        ConfigEntry<KeyboardShortcut> _shortcut;
        Sticks _sticks;

        /// <summary>
        /// Adds the trigger if some plugin can be opened, and does nothing at
        /// all otherwise -- including not creating the component, so a build
        /// with no settings UI polls nothing every frame.
        /// </summary>
        internal static void Install(UnityEngine.GameObject host)
        {
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                var instance = info == null ? null : info.Instance;
                if (instance == null) continue;

                var property = instance.GetType().GetProperty(
                    PROPERTY, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.PropertyType != typeof(bool)) continue;
                if (!property.CanRead || !property.CanWrite) continue;

                var menu = host.AddComponent<ModMenu>();
                menu._plugin = instance;
                menu._window = property;
                menu._shortcut = Shortcut();
                menu._sticks = Sticks.Find();

                Logging.Logger.CreateLogSource("BepInEx").LogInfo(
                    info.Metadata.Name + " opens with " + menu._shortcut.Value +
                    (menu._sticks == null ? "" : ", or by clicking both sticks"));
                return;
            }
        }

        /// <summary>
        /// The chord, in the core config, so it can be changed without a
        /// rebuild -- and so the settings UI lists it, which means the way in
        /// can be rebound from inside the thing it opens.
        ///
        /// L3+R3 written as numbers because that is all the legacy input
        /// manager has: it exposes a pad as JoystickButtonN, numbered in the
        /// order Android reported the buttons.
        /// </summary>
        static ConfigEntry<KeyboardShortcut> Shortcut()
        {
            return ConfigFile.CoreConfig.Bind(
                "Mods", "Open settings",
                new KeyboardShortcut(UnityEngine.KeyCode.JoystickButton9, UnityEngine.KeyCode.JoystickButton8),
                new ConfigDescription(
                    "Opens and closes a mod's settings window. Click both sticks (L3+R3) on a " +
                    "controller, or set this to a key of your own -- F1, or any button. " +
                    "Set it to None to turn it off."));
        }

        void Update()
        {
            var shortcut = _shortcut.Value;
            if (shortcut.MainKey == UnityEngine.KeyCode.None) return;

            // The sticks are read twice, by number and by name, because the
            // two disagree on some pads and either reading is the same chord.
            // Rebinding the shortcut leaves the named reading in place; only
            // None turns the whole thing off.
            var pressed = shortcut.IsDown() || (_sticks != null && _sticks.Clicked());
            if (pressed) Open = !Open;
        }

        bool Open
        {
            get
            {
                try { return (bool)_window.GetValue(_plugin, null); }
                catch (Exception) { return false; }
            }
            set
            {
                try { _window.SetValue(_plugin, value, null); }
                catch (Exception e) { UnityEngine.Debug.LogWarning("[BepInEx] Could not open the settings: " + e); }
            }
        }
    }

    /// <summary>
    /// Both stick buttons, read through the input system package by name.
    ///
    /// The legacy manager numbers a pad's buttons in whatever order Android
    /// reported them, so JoystickButton8 is L3 on the pads this was tried with
    /// and need not be on the next one. The input system package names them
    /// instead, and this game already ships it.
    ///
    /// By reflection, so this assembly does not acquire a reference to a
    /// package that a game built the other way would not have.
    /// </summary>
    internal sealed class Sticks
    {
        PropertyInfo _current, _left, _right, _pressed;
        bool _wasDown;

        internal static Sticks Find()
        {
            try
            {
                var gamepad = Type.GetType("UnityEngine.InputSystem.Gamepad, Unity.InputSystem");
                if (gamepad == null) return null;

                var sticks = new Sticks
                {
                    _current = gamepad.GetProperty("current", BindingFlags.Public | BindingFlags.Static),
                    _left = gamepad.GetProperty("leftStickButton", BindingFlags.Public | BindingFlags.Instance),
                    _right = gamepad.GetProperty("rightStickButton", BindingFlags.Public | BindingFlags.Instance),
                };
                return sticks._current == null || sticks._left == null || sticks._right == null ? null : sticks;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True on the frame the chord closes, not while it is held.</summary>
        internal bool Clicked()
        {
            var down = Down();
            var clicked = down && !_wasDown;
            _wasDown = down;
            return clicked;
        }

        bool Down()
        {
            if (_current == null) return false;
            try
            {
                var pad = _current.GetValue(null, null);
                if (pad == null) return false;
                return IsPressed(_left.GetValue(pad, null)) && IsPressed(_right.GetValue(pad, null));
            }
            catch (Exception e)
            {
                // One failure is every failure: whatever is wrong with the
                // reflection is wrong on every frame, and a warning per frame
                // is worse than the missing chord.
                _current = null;
                UnityEngine.Debug.LogWarning("[BepInEx] Could not read the sticks: " + e.Message);
                return false;
            }
        }

        bool IsPressed(object button)
        {
            if (button == null) return false;
            if (_pressed == null)
            {
                _pressed = button.GetType().GetProperty("isPressed", BindingFlags.Public | BindingFlags.Instance);
                if (_pressed == null) return false;
            }
            return (bool)_pressed.GetValue(button, null);
        }
    }
}
