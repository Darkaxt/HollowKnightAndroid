#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DualSouls.Mods
{
    /// <summary>
    /// #25 enemy health bars, boss bars and damage numbers, matching Nexus Silksong
    /// mod 28 (ShowDamage HealthBar): only the hero's hits count, an enemy bar appears
    /// on the first hit and hides 1.5 s after the last one, enemies above 105 HP get a
    /// boss bar along the bottom of the view, and damage numbers come from a fixed pool.
    /// Everything is drawn in world space in front of the game camera, so it stays on
    /// the game display. Each game's enemy-hit hook reports hero hits through
    /// <see cref="NoteHeroHit"/>; the game's per-frame tick calls <see cref="Tick"/>.
    /// </summary>
    internal static class CombatOverlay
    {
        public const string BossBarsId = "boss_health_bars";
        public const int BossThreshold = 105;
        public const int MaximumTracked = 3000;
        const float EnemyHideDelay = 1.5f;
        const float NumberDuration = 2f;
        const int NumberPool = 16;
        const int MaximumBossBars = 3;
        // Mod 28 defaults: 135x25 canvas units at 0.01 scale, fill inset 2 units each side.
        const float EnemyWidth = 1.35f, EnemyHeight = 0.25f, EnemyFillWidth = 1.31f, EnemyFillHeight = 0.21f;
        const float EnemyTextSize = 0.10f, EnemyTextAbove = 0.425f;
        // Mod 28's default bar colours: light grey fill with near-black numbers.
        static readonly Color BarFill = new Color(0.745f, 0.722f, 0.722f, 1f);
        static readonly Color BarText = new Color(0.055f, 0.016f, 0.016f, 1f);

        sealed class Tracked
        {
            public int Maximum, Last;
            public bool HeroHitPending;
            public float HideAt, Offset;
            public string Name;
            public Bar Bar;
        }

        sealed class Bar
        {
            public Transform Root, Fill;
            public TextMesh Label, Name;
        }

        sealed class Number
        {
            public Transform Transform;
            public TextMesh Text;
            public float Until;
        }

        static readonly Dictionary<HealthManager, Tracked> _tracked = new Dictionary<HealthManager, Tracked>();
        static readonly List<HealthManager> _gone = new List<HealthManager>();
        static readonly List<Tracked> _bosses = new List<Tracked>();
        static readonly Number[] _numbers = new Number[NumberPool];
        static int _nextNumber;
        static bool _bars, _bossBars, _damage;
        static Texture2D _pixelTexture;
        static Sprite _pixel;
        static Font _font;

        /// <summary>BOSS HEALTH BARS row (cheat-owned choice, so it is set here rather than passed in).</summary>
        public static bool BossBars;

        /// <summary>Game-specific display name for a boss bar; null falls back to the object name.</summary>
        public static Func<HealthManager, string> NameResolver;

        public static bool Active => _bars || _bossBars || _damage;

        /// <summary>The hero is about to hit <paramref name="target"/> (the HP owner after any redirect).</summary>
        // Device diagnostics: game hit-hook calls and hero hits recorded here.
        public static int HookCalls, HeroHits;

        public static void NoteHeroHit(HealthManager target)
        {
            HeroHits++;
            if (!Active || target == null || target.hp <= 0) return;
            Tracked tracked;
            if (!_tracked.TryGetValue(target, out tracked))
            {
                if (target.hp > MaximumTracked) return;
                tracked = new Tracked { Maximum = target.hp, Last = target.hp };
                _tracked.Add(target, tracked);
            }
            tracked.HeroHitPending = true;
            tracked.HideAt = Time.unscaledTime + EnemyHideDelay;
        }

        public static void Tick(bool enemyBars, bool damageNumbers, HeroController hero)
        {
            _bars = enemyBars; _bossBars = BossBars; _damage = damageNumbers;
            if (!Active) { Clear(); return; }

            float now = Time.unscaledTime;
            _gone.Clear();
            _bosses.Clear();
            foreach (KeyValuePair<HealthManager, Tracked> pair in _tracked)
            {
                HealthManager enemy = pair.Key;
                Tracked tracked = pair.Value;
                bool alive = enemy != null && enemy.gameObject != null && enemy.gameObject.activeInHierarchy && enemy.hp > 0;
                int hp = enemy != null ? Math.Max(enemy.hp, 0) : 0;
                if (tracked.HeroHitPending && hp < tracked.Last && _damage && enemy != null)
                    ShowNumber(enemy.transform.position, tracked.Last - hp);
                tracked.HeroHitPending = false;
                tracked.Last = hp;
                if (hp > tracked.Maximum) tracked.Maximum = hp;
                if (!alive || tracked.Maximum > MaximumTracked) { _gone.Add(enemy); continue; }

                bool boss = tracked.Maximum > BossThreshold;
                if (boss && _bossBars && _bosses.Count < MaximumBossBars)
                {
                    _bosses.Add(tracked);
                    if (tracked.Name == null) tracked.Name = DisplayName(enemy);
                }
                else if (!boss && _bars && now < tracked.HideAt)
                    ShowEnemyBar(enemy, tracked);
                else
                    Hide(tracked);
            }
            foreach (HealthManager enemy in _gone)
            {
                Tracked tracked;
                if (_tracked.TryGetValue(enemy, out tracked)) Destroy(tracked);
                _tracked.Remove(enemy);
            }
            LayoutBossBars(hero);
            TickNumbers(now);
        }

        public static void Clear()
        {
            foreach (Tracked tracked in _tracked.Values) Destroy(tracked);
            _tracked.Clear();
            _bosses.Clear();
            for (int index = 0; index < _numbers.Length; index++)
            {
                if (_numbers[index] != null && _numbers[index].Transform != null)
                    UnityEngine.Object.Destroy(_numbers[index].Transform.gameObject);
                _numbers[index] = null;
            }
        }

        public static void ReleaseResources()
        {
            Clear();
            if (_pixel != null) UnityEngine.Object.Destroy(_pixel);
            if (_pixelTexture != null) UnityEngine.Object.Destroy(_pixelTexture);
            _pixel = null; _pixelTexture = null; _font = null;
        }

        /// <summary>QA line: tracked enemies, visible boss bars and active numbers.</summary>
        public static string Describe()
        {
            int numbers = 0;
            for (int index = 0; index < _numbers.Length; index++)
                if (_numbers[index] != null && _numbers[index].Transform != null && _numbers[index].Transform.gameObject.activeSelf) numbers++;
            return "combat: bars=" + (_bars ? "on" : "off") + " bossBars=" + (_bossBars ? "on" : "off") + " numbers=" + (_damage ? "on" : "off")
                + " tracked=" + _tracked.Count + " bosses=" + _bosses.Count + " shownNumbers=" + numbers + " hooks=" + HookCalls + " heroHits=" + HeroHits;
        }

        static void ShowEnemyBar(HealthManager enemy, Tracked tracked)
        {
            if (tracked.Bar == null)
            {
                Renderer body = enemy.GetComponentInChildren<Renderer>();
                tracked.Offset = body != null ? Mathf.Clamp(body.bounds.extents.y + 0.5f, 1f, 6f) : 1.5f;
            }
            Bar bar = tracked.Bar ?? (tracked.Bar = NewBar(enemy.transform, false));
            if (bar.Root.parent != enemy.transform) bar.Root.SetParent(enemy.transform, false);
            bar.Root.position = enemy.transform.position + Vector3.up * tracked.Offset;
            Vector3 scale = enemy.transform.lossyScale;
            bar.Root.localScale = new Vector3(scale.x == 0f ? 1f : 1f / scale.x, scale.y == 0f ? 1f : 1f / scale.y, 1f);
            SetFill(bar, tracked, EnemyFillWidth, EnemyFillHeight);
            bar.Root.gameObject.SetActive(true);
        }

        static void LayoutBossBars(HeroController hero)
        {
            if (_bosses.Count == 0) return;
            GameCameras cameras = GameCameras.instance;
            Camera camera = cameras != null ? cameras.mainCamera : null;
            if (camera == null) return;
            float depth = (hero != null ? hero.transform.position.z : 0f) - camera.transform.position.z;
            if (depth <= 0.01f) depth = 20f;
            for (int index = 0; index < _bosses.Count; index++)
            {
                Tracked tracked = _bosses[index];
                Bar bar = tracked.Bar;
                if (bar == null || bar.Name == null) { Destroy(tracked); bar = tracked.Bar = NewBar(null, true); }
                if (bar.Root.parent != null) bar.Root.SetParent(null, false);
                float y = 0.06f + index * 0.075f;
                Vector3 left = camera.ViewportToWorldPoint(new Vector3(0.265f, y, depth));
                Vector3 right = camera.ViewportToWorldPoint(new Vector3(0.735f, y, depth));
                Vector3 top = camera.ViewportToWorldPoint(new Vector3(0.265f, y + 0.023f, depth));
                float width = Vector3.Distance(left, right);
                float height = Mathf.Max(0.01f, Vector3.Distance(left, top));
                bar.Root.position = (left + right) * 0.5f;
                bar.Root.rotation = camera.transform.rotation;
                bar.Root.localScale = Vector3.one;
                bar.Root.Find("Background").localScale = new Vector3(width, height, 1f);
                SetFill(bar, tracked, width * 0.99f, height * 0.84f);
                bar.Name.text = tracked.Name;
                bar.Name.transform.localPosition = new Vector3(0f, height * 1.6f, 0f);
                bar.Name.characterSize = height * 0.22f;
                bar.Label.characterSize = height * 0.18f;
                bar.Root.gameObject.SetActive(true);
            }
        }

        static void SetFill(Bar bar, Tracked tracked, float width, float height = 0.10f)
        {
            float fill = Mathf.Clamp01(tracked.Maximum > 0 ? (float)tracked.Last / tracked.Maximum : 0f);
            bar.Fill.localScale = new Vector3(width * fill, height, 1f);
            bar.Fill.localPosition = new Vector3(-width * 0.5f * (1f - fill), 0f, 0f);
            if (bar.Name != null) bar.Label.color = fill >= 0.5f ? BarText : BarFill;
            bar.Label.text = tracked.Last.ToString(CultureInfo.InvariantCulture) + "/" + tracked.Maximum.ToString(CultureInfo.InvariantCulture);
        }

        static Bar NewBar(Transform parent, bool boss)
        {
            var root = new GameObject(boss ? "DualSouls Boss Health" : "DualSouls Enemy Health");
            if (parent != null) root.transform.SetParent(parent, false);
            var bar = new Bar { Root = root.transform };
            Part(root.transform, "Background", new Color(0f, 0f, 0f, 0.62f), 3200, new Vector3(EnemyWidth, EnemyHeight, 1f));
            bar.Fill = Part(root.transform, "Fill", BarFill, 3201, new Vector3(EnemyFillWidth, EnemyFillHeight, 1f));
            bar.Label = Text(root.transform, "Health", boss ? 40 : 32, BarText, 3202);
            bar.Label.characterSize = EnemyTextSize;
            if (!boss) bar.Label.transform.localPosition = new Vector3(0f, EnemyTextAbove, 0f);
            if (boss) bar.Name = Text(root.transform, "Name", 40, Color.white, 3202);
            return bar;
        }

        static Transform Part(Transform parent, string name, Color color, int order, Vector3 scale)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            var renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = Pixel();
            renderer.color = color;
            renderer.sortingOrder = order;
            part.transform.localScale = scale;
            return part.transform;
        }

        static TextMesh Text(Transform parent, string name, int size, Color color, int order)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMesh>();
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.font = _font;
            text.fontSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = color;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && _font != null) { renderer.material = _font.material; renderer.sortingOrder = order; }
            return text;
        }

        static void ShowNumber(Vector3 position, int damage)
        {
            if (damage <= 0) return;
            Number number = _numbers[_nextNumber];
            if (number == null || number.Transform == null)
            {
                TextMesh text = Text(null, "DualSouls Damage Number", 44, Color.white, 3210);
                text.characterSize = 0.14f;
                number = _numbers[_nextNumber] = new Number { Transform = text.transform, Text = text };
            }
            _nextNumber = (_nextNumber + 1) % _numbers.Length;
            number.Transform.position = position + Vector3.up;
            number.Text.text = "-" + damage.ToString(CultureInfo.InvariantCulture);
            number.Text.color = new Color(0.86f, 0.08f, 0.24f, 1f);
            number.Until = Time.unscaledTime + NumberDuration;
            number.Transform.gameObject.SetActive(true);
        }

        static void TickNumbers(float now)
        {
            for (int index = 0; index < _numbers.Length; index++)
            {
                Number number = _numbers[index];
                if (number == null || number.Transform == null || !number.Transform.gameObject.activeSelf) continue;
                if (now >= number.Until) { number.Transform.gameObject.SetActive(false); continue; }
                number.Transform.position += Vector3.up * (Time.unscaledDeltaTime * 0.7f);
                Color color = number.Text.color;
                color.a = Mathf.Clamp01((number.Until - now) / 0.5f);
                number.Text.color = color;
            }
        }

        static void Hide(Tracked tracked)
        {
            if (tracked.Bar != null && tracked.Bar.Root != null) tracked.Bar.Root.gameObject.SetActive(false);
        }

        static void Destroy(Tracked tracked)
        {
            if (tracked.Bar != null && tracked.Bar.Root != null) UnityEngine.Object.Destroy(tracked.Bar.Root.gameObject);
            tracked.Bar = null;
        }

        static string DisplayName(HealthManager enemy)
        {
            string name = null;
            try { if (NameResolver != null) name = NameResolver(enemy); } catch { }
            if (string.IsNullOrEmpty(name) || name.StartsWith("#!#", StringComparison.Ordinal))
                name = enemy.gameObject.name.Replace("(Clone)", string.Empty).Replace('_', ' ').Trim();
            return name;
        }

        static Sprite Pixel()
        {
            if (_pixel != null) return _pixel;
            _pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixelTexture.SetPixel(0, 0, Color.white);
            _pixelTexture.Apply(false);
            _pixel = Sprite.Create(_pixelTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _pixel;
        }
    }
}
#endif
