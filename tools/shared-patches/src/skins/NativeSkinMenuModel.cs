using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSouls.Skins
{
    public enum NativeSkinMenuRowKind { Mode, Sprites, Skin, Empty, Back }
    public enum NativeSkinMutationKind { None, SetMode, SetSpriteScope, ConfirmPack }
    public enum NativeSkinConfirmationIntent { None, SelectForLater, EnableSole, ToggleRotationMembership }

    public sealed class NativeSkinPackDescriptor
    {
        public NativeSkinPackDescriptor(string id, string name, string author)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Skin id is required.", nameof(id));
            Id = id;
            Name = string.IsNullOrEmpty(name) ? id : name;
            Author = author ?? "";
        }

        public string Id { get; }
        public string Name { get; }
        public string Author { get; }
    }

    public sealed class NativeSkinMenuSnapshot
    {
        public NativeSkinMenuSnapshot(string profileId, string configSha256, string mode,
            string spriteScope, string selectedPackId, IEnumerable<string> eligiblePackIds,
            IEnumerable<NativeSkinPackDescriptor> packs)
        {
            ProfileId = profileId ?? "";
            ConfigSha256 = configSha256 ?? "";
            Mode = mode ?? "";
            SpriteScope = spriteScope ?? "";
            SelectedPackId = selectedPackId;
            EligiblePackIds = (eligiblePackIds ?? Array.Empty<string>()).ToArray();
            Packs = (packs ?? Array.Empty<NativeSkinPackDescriptor>()).ToArray();
        }

        public string ProfileId { get; }
        public string ConfigSha256 { get; }
        public string Mode { get; }
        public string SpriteScope { get; }
        public string SelectedPackId { get; }
        public IReadOnlyList<string> EligiblePackIds { get; }
        public IReadOnlyList<NativeSkinPackDescriptor> Packs { get; }
    }

    public sealed class NativeSkinMenuRow
    {
        internal NativeSkinMenuRow(NativeSkinMenuRowKind kind, string key, string label,
            string value, string packId, bool isActionable)
        {
            Kind = kind;
            Key = key;
            Label = label;
            Value = value;
            PackId = packId;
            IsActionable = isActionable;
        }

        public NativeSkinMenuRowKind Kind { get; }
        public string Key { get; }
        public string Label { get; }
        public string Value { get; }
        public string PackId { get; }
        public bool IsActionable { get; }
    }

    public readonly struct NativeSkinMutation
    {
        public NativeSkinMutation(NativeSkinMutationKind kind, string value,
            NativeSkinConfirmationIntent confirmationIntent = NativeSkinConfirmationIntent.None)
        {
            Kind = kind;
            Value = value ?? "";
            ConfirmationIntent = confirmationIntent;
        }

        public NativeSkinMutationKind Kind { get; }
        public string Value { get; }
        public NativeSkinConfirmationIntent ConfirmationIntent { get; }
    }

    /// <summary>Pure focus, viewport, labels, and mutation intent for both native Skins screens.</summary>
    public sealed class NativeSkinMenuModel
    {
        static readonly string[] Modes = { "OFF", "ON", "ROTATE" };
        static readonly string[] ScopeValues = { "ALL", "CHARACTER_HUD", "CHARACTER" };
        static readonly string[] ScopeLabels = { "ALL", "CHARACTER + HUD", "CHARACTER" };

        NativeSkinMenuSnapshot snapshot;
        IReadOnlyList<NativeSkinMenuRow> rows;
        int selectedIndex;

        public NativeSkinMenuModel(NativeSkinMenuSnapshot snapshot, int visibleSkinRows)
        {
            if (visibleSkinRows <= 0) throw new ArgumentOutOfRangeException(nameof(visibleSkinRows));
            VisibleSkinRows = visibleSkinRows;
            Replace(snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
        }

        public static IReadOnlyList<string> ModeChoices => Modes;
        public static IReadOnlyList<string> SpriteChoices => ScopeLabels;
        public int VisibleSkinRows { get; }
        public int SkinWindowStart { get; private set; }
        public NativeSkinMenuSnapshot Snapshot => snapshot;
        public IReadOnlyList<NativeSkinMenuRow> Rows => rows;
        public int SelectedRowIndex => selectedIndex;
        public NativeSkinMenuRow Selected => rows[selectedIndex];

        public IReadOnlyList<NativeSkinMenuRow> VisibleRows
        {
            get
            {
                var result = new List<NativeSkinMenuRow>(VisibleSkinRows + 3)
                {
                    rows[0], rows[1]
                };
                int skinCount = snapshot.Packs.Count;
                if (skinCount == 0) result.Add(rows[2]);
                else
                    for (int index = SkinWindowStart;
                         index < skinCount && index < SkinWindowStart + VisibleSkinRows; index++)
                        result.Add(rows[index + 2]);
                result.Add(rows[rows.Count - 1]);
                return result;
            }
        }

        public void Replace(NativeSkinMenuSnapshot value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            string selectedKey = rows != null ? Selected.Key : "mode";
            snapshot = value;
            rows = BuildRows(value);
            selectedIndex = IndexOfKey(selectedKey);
            if (selectedIndex < 0) selectedIndex = 0;
            KeepSelectedSkinVisible();
        }

        public void SelectRow(int index)
        {
            if (index < 0 || index >= rows.Count || !rows[index].IsActionable) return;
            selectedIndex = index;
            KeepSelectedSkinVisible();
        }

        public void SelectPack(int packIndex)
        {
            if (packIndex < 0 || packIndex >= snapshot.Packs.Count) return;
            SelectRow(packIndex + 2);
        }

        public void Move(int delta)
        {
            if (delta == 0) return;
            int direction = delta < 0 ? -1 : 1;
            int remaining = Math.Abs(delta);
            while (remaining-- > 0)
            {
                do selectedIndex = Wrap(selectedIndex + direction, rows.Count);
                while (!rows[selectedIndex].IsActionable);
            }
            KeepSelectedSkinVisible();
        }

        public NativeSkinMutation CycleMode(int delta) =>
            new NativeSkinMutation(NativeSkinMutationKind.SetMode,
                Cycle(Modes, snapshot.Mode, delta));

        public NativeSkinMutation CycleSprites(int delta) =>
            new NativeSkinMutation(NativeSkinMutationKind.SetSpriteScope,
                Cycle(ScopeValues, snapshot.SpriteScope, delta));

        public NativeSkinMutation ConfirmSelected()
        {
            NativeSkinMenuRow row = Selected;
            if (row.Kind != NativeSkinMenuRowKind.Skin)
                return new NativeSkinMutation(NativeSkinMutationKind.None, "");
            NativeSkinConfirmationIntent intent = snapshot.Mode == "OFF"
                ? NativeSkinConfirmationIntent.SelectForLater
                : snapshot.Mode == "ON"
                    ? NativeSkinConfirmationIntent.EnableSole
                    : NativeSkinConfirmationIntent.ToggleRotationMembership;
            return new NativeSkinMutation(NativeSkinMutationKind.ConfirmPack, row.PackId, intent);
        }

        int IndexOfKey(string key)
        {
            for (int index = 0; index < rows.Count; index++)
                if (string.Equals(rows[index].Key, key, StringComparison.Ordinal)) return index;
            return -1;
        }

        void KeepSelectedSkinVisible()
        {
            if (Selected.Kind != NativeSkinMenuRowKind.Skin)
            {
                int maximum = Math.Max(0, snapshot.Packs.Count - VisibleSkinRows);
                SkinWindowStart = Math.Min(SkinWindowStart, maximum);
                return;
            }
            int packIndex = selectedIndex - 2;
            if (packIndex < SkinWindowStart) SkinWindowStart = packIndex;
            else if (packIndex >= SkinWindowStart + VisibleSkinRows)
                SkinWindowStart = packIndex - VisibleSkinRows + 1;
        }

        static IReadOnlyList<NativeSkinMenuRow> BuildRows(NativeSkinMenuSnapshot value)
        {
            var result = new List<NativeSkinMenuRow>(value.Packs.Count + 3)
            {
                new NativeSkinMenuRow(NativeSkinMenuRowKind.Mode, "mode", "MODE", value.Mode, null, true),
                new NativeSkinMenuRow(NativeSkinMenuRowKind.Sprites, "sprites", "SPRITES",
                    ScopeLabel(value.SpriteScope), null, true)
            };
            if (value.Packs.Count == 0)
                result.Add(new NativeSkinMenuRow(NativeSkinMenuRowKind.Empty, "empty",
                    "NO COMPATIBLE SKINS", "", null, false));
            else
            {
                var eligible = new HashSet<string>(value.EligiblePackIds, StringComparer.Ordinal);
                foreach (NativeSkinPackDescriptor pack in value.Packs)
                {
                    string status = value.Mode == "OFF"
                        ? (pack.Id == value.SelectedPackId ? "SELECTED" : "DISABLED")
                        : value.Mode == "ON"
                            ? (pack.Id == value.SelectedPackId ? "ENABLED" : "DISABLED")
                            : (eligible.Contains(pack.Id) ? "ENABLED" : "DISABLED");
                    result.Add(new NativeSkinMenuRow(NativeSkinMenuRowKind.Skin,
                        "skin:" + pack.Id, pack.Name, status, pack.Id, true));
                }
            }
            result.Add(new NativeSkinMenuRow(NativeSkinMenuRowKind.Back, "back", "BACK", "", null, true));
            return result;
        }

        static string ScopeLabel(string value)
        {
            int index = Array.IndexOf(ScopeValues, value);
            return index >= 0 ? ScopeLabels[index] : value;
        }

        static string Cycle(string[] values, string current, int delta)
        {
            int index = Array.IndexOf(values, current);
            if (index < 0) index = 0;
            return values[Wrap(index + delta, values.Length)];
        }

        static int Wrap(int value, int count)
        {
            int wrapped = value % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }
    }
}
