#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DualSouls.Mods
{
    /// <summary>
    /// #25 hero recolouring (Nexus Silksong mod 19 colour presets). The tint multiplies
    /// the hero's tk2d sprite colour, so it layers over any skin texture. A colour the
    /// game sets itself (PlayMaker fades) becomes the new base and is tinted in turn.
    /// </summary>
    internal sealed class HeroTint
    {
        public const string Id = "hero_colour";
        public static readonly string[] Values =
        {
            "off", "red", "blue", "green", "purple", "gold", "fire", "ice",
            "shadow", "red_shadow", "gold_shadow", "rainbow_1", "rainbow_2",
        };

        sealed class Owned
        {
            public tk2dSprite Sprite;
            public Color Base;
            public Color Written;
        }

        readonly List<Owned> _owned = new List<Owned>();
        readonly List<tk2dSprite> _scan = new List<tk2dSprite>();
        HeroController _hero;
        string _mode = "off";
        float _nextScan;

        public string Mode => _mode;

        public void Set(string value)
        {
            if (Array.IndexOf(Values, value) < 0) throw new ArgumentException("Unknown hero colour: " + value);
            _mode = value;
            if (value == "off") Restore();
        }

        public void Tick()
        {
            if (_mode == "off") return;
            HeroController hero = HeroController.instance;
            if (hero == null) { Restore(); return; }
            if (!ReferenceEquals(hero, _hero))
            {
                Restore();
                _hero = hero;
                _nextScan = 0f;
            }
            // Effects attach children after spawn; a slow rescan picks them up.
            if (Time.unscaledTime >= _nextScan)
            {
                Scan(hero);
                _nextScan = Time.unscaledTime + 2f;
            }
            Color tint = TintFor(_mode, Time.unscaledTime);
            for (int index = _owned.Count - 1; index >= 0; index--)
            {
                Owned owned = _owned[index];
                if (owned.Sprite == null) { _owned.RemoveAt(index); continue; }
                Color current = owned.Sprite.color;
                if (current != owned.Written) owned.Base = current;
                Color next = new Color(owned.Base.r * tint.r, owned.Base.g * tint.g, owned.Base.b * tint.b, owned.Base.a);
                if (next != current) owned.Sprite.color = next;
                owned.Written = next;
            }
        }

        public void Restore()
        {
            for (int index = 0; index < _owned.Count; index++)
            {
                Owned owned = _owned[index];
                if (owned.Sprite != null && owned.Sprite.color == owned.Written) owned.Sprite.color = owned.Base;
            }
            _owned.Clear();
            _hero = null;
        }

        void Scan(HeroController hero)
        {
            hero.GetComponentsInChildren(true, _scan);
            for (int index = 0; index < _scan.Count; index++)
            {
                tk2dSprite sprite = _scan[index];
                bool known = false;
                for (int item = 0; item < _owned.Count; item++)
                    if (ReferenceEquals(_owned[item].Sprite, sprite)) { known = true; break; }
                if (known) continue;
                Color color = sprite.color;
                _owned.Add(new Owned { Sprite = sprite, Base = color, Written = color });
            }
            _scan.Clear();
        }

        // Solid presets use mod 19's colours. Its multi-colour presets paint quad corners
        // that tk2d rebuilds on every animation frame, so they become their blended tint
        // here, and the two rainbows cycle the hue instead.
        static Color TintFor(string mode, float time)
        {
            switch (mode)
            {
                case "red": return new Color(1f, 0.2f, 0.2f);
                case "blue": return new Color(0.2f, 0.2f, 1f);
                case "green": return new Color(0.2f, 1f, 0.2f);
                case "purple": return new Color(1f, 0.2f, 1f);
                case "gold": return new Color(1f, 0.8f, 0.2f);
                case "fire": return new Color(1f, 0.55f, 0.1f);
                case "ice": return new Color(0.45f, 0.75f, 1f);
                case "shadow": return new Color(0.3f, 0.25f, 0.4f);
                case "red_shadow": return new Color(0.75f, 0.3f, 0.1f);
                case "gold_shadow": return new Color(0.8f, 0.65f, 0.15f);
                case "rainbow_1": return Color.HSVToRGB(Mathf.Repeat(time * 0.25f, 1f), 0.8f, 1f);
                case "rainbow_2": return Color.HSVToRGB(Mathf.Repeat(time * 0.15f, 1f), 0.35f, 1f);
                default: return Color.white;
            }
        }
    }
}
#endif
