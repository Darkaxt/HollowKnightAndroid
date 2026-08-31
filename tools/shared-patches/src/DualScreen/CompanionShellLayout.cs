using System;
using System.Collections.Generic;

namespace DualSouls.DualScreen
{
    /// <summary>Integer panel rectangle measured from the top-left.</summary>
    public readonly struct CompanionRect
    {
        public CompanionRect(int left, int top, int width, int height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => Left + Width;
        public int Bottom => Top + Height;
        public int CenterX => Left + Width / 2;
        public int CenterY => Top + Height / 2;

        public bool Contains(float x, float y) =>
            x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    public sealed class CompanionTabLayout
    {
        internal CompanionTabLayout(string id, CompanionRect bounds)
        {
            Id = id;
            Bounds = bounds;
        }

        public string Id { get; }
        public CompanionRect Bounds { get; }

        // Selection is expressed only by label colour and paired ornaments.
        // A filled tab is the flat toolbar design this layout replaces.
        public bool DrawFilledPanel => false;
    }

    public readonly struct CompanionSelectionLayout
    {
        public CompanionSelectionLayout(CompanionRect top, CompanionRect bottom)
        {
            Top = top;
            Bottom = bottom;
        }

        public CompanionRect Top { get; }
        public CompanionRect Bottom { get; }
    }

    public enum CompanionHitTarget
    {
        None,
        Tab,
        Mods,
    }

    public readonly struct CompanionHit
    {
        public CompanionHit(CompanionHitTarget target, int tabIndex = -1)
        {
            Target = target;
            TabIndex = tabIndex;
        }

        public CompanionHitTarget Target { get; }
        public int TabIndex { get; }
    }

    public sealed class CompanionControlLayout
    {
        internal CompanionControlLayout(CompanionRect bounds) { Bounds = bounds; }
        public CompanionRect Bounds { get; }
    }

    /// <summary>
    /// Game-neutral geometry for the Dual Souls HUD/frame rendered by either
    /// game's adapter. No Unity or game type crosses this boundary.
    /// </summary>
    public sealed class CompanionShellLayout
    {
        public const int MinimumTouchTarget = 72;

        const int ReferenceWidth = 1240;
        const int ReferenceHeight = 1080;

        readonly List<CompanionTabLayout> _tabs;

        CompanionShellLayout(
            CompanionRect hud,
            CompanionRect topOrnament,
            CompanionRect content,
            CompanionRect bottomOrnament,
            CompanionRect navigation,
            CompanionControlLayout modsGear,
            CompanionControlLayout status,
            CompanionControlLayout battery,
            List<CompanionTabLayout> tabs)
        {
            Hud = hud;
            TopOrnament = topOrnament;
            Content = content;
            BottomOrnament = bottomOrnament;
            Navigation = navigation;
            ModsGear = modsGear;
            Status = status;
            Battery = battery;
            _tabs = tabs;
        }

        public CompanionRect Hud { get; }
        public CompanionRect TopOrnament { get; }
        public CompanionRect Content { get; }
        public CompanionRect BottomOrnament { get; }
        public CompanionRect Navigation { get; }
        public CompanionControlLayout ModsGear { get; }
        public CompanionControlLayout Status { get; }
        public CompanionControlLayout Battery { get; }
        public IReadOnlyList<CompanionTabLayout> Tabs => _tabs;

        public static CompanionShellLayout Create(int width, int height, IReadOnlyList<string> pageIds)
        {
            if (width < 640) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 640) throw new ArgumentOutOfRangeException(nameof(height));
            if (pageIds == null || pageIds.Count == 0) throw new ArgumentException("At least one page is required.", nameof(pageIds));

            int Sx(int value) => Math.Max(1, (int)Math.Round(value * width / (double)ReferenceWidth));
            int Sy(int value) => Math.Max(1, (int)Math.Round(value * height / (double)ReferenceHeight));

            int hudH = Sy(154);
            int ornamentH = Sy(20);
            int navH = Sy(150);
            int navTop = height - navH;
            int contentTop = hudH + ornamentH;
            int contentBottom = navTop - ornamentH;

            var hud = new CompanionRect(0, 0, width, hudH);
            var topOrnament = new CompanionRect(0, hud.Bottom, width, ornamentH);
            var content = new CompanionRect(0, contentTop, width, Math.Max(1, contentBottom - contentTop));
            var bottomOrnament = new CompanionRect(0, content.Bottom, width, ornamentH);
            var navigation = new CompanionRect(0, bottomOrnament.Bottom, width, height - bottomOrnament.Bottom);

            int gutter = Sx(160);
            int tabsWidth = width - gutter * 2;
            int tabW = tabsWidth / pageIds.Count;
            int tabTop = navigation.Top + Sy(20);
            int tabH = Math.Max(MinimumTouchTarget, navigation.Height - Sy(30));
            var tabs = new List<CompanionTabLayout>(pageIds.Count);
            for (int i = 0; i < pageIds.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(pageIds[i])) throw new ArgumentException("Page IDs cannot be blank.", nameof(pageIds));
                int left = gutter + tabW * i;
                int right = i == pageIds.Count - 1 ? width - gutter : left + tabW;
                tabs.Add(new CompanionTabLayout(pageIds[i], new CompanionRect(left, tabTop, right - left, tabH)));
            }

            int statusW = Sx(126);
            int statusH = Math.Max(MinimumTouchTarget, Sy(72));
            int statusTop = height - statusH - Sy(10);
            var status = new CompanionControlLayout(new CompanionRect(Sx(18), statusTop, statusW, statusH));
            var battery = new CompanionControlLayout(new CompanionRect(width - Sx(18) - statusW, statusTop, statusW, statusH));
            int gearSize = Math.Max(MinimumTouchTarget, Sx(78));
            var gear = new CompanionControlLayout(new CompanionRect(status.Bounds.CenterX - gearSize / 2,
                                                                     status.Bounds.Top - gearSize,
                                                                     gearSize,
                                                                     gearSize));

            return new CompanionShellLayout(hud, topOrnament, content, bottomOrnament,
                                            navigation, gear, status, battery, tabs);
        }

        public CompanionSelectionLayout SelectionFor(int index)
        {
            if (index < 0 || index >= _tabs.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var tab = _tabs[index].Bounds;
            int width = Math.Max(MinimumTouchTarget, (int)Math.Round(tab.Width * 0.72));
            int height = Math.Max(6, Navigation.Height / 14);
            int left = tab.CenterX - width / 2;
            return new CompanionSelectionLayout(
                new CompanionRect(left, tab.Top - height + 8, width, height),
                new CompanionRect(left, tab.Bottom - 8, width, height));
        }

        public CompanionHit HitTest(float x, float y)
        {
            if (ModsGear.Bounds.Contains(x, y)) return new CompanionHit(CompanionHitTarget.Mods);
            for (int i = 0; i < _tabs.Count; i++)
                if (_tabs[i].Bounds.Contains(x, y)) return new CompanionHit(CompanionHitTarget.Tab, i);
            return new CompanionHit(CompanionHitTarget.None);
        }
    }
}
