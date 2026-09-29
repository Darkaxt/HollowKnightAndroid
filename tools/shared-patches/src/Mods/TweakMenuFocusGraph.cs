using System;

namespace DualSouls.Mods
{
    public enum TweakMenuFocusKind
    {
        Group,
        Row,
        Reset,
        Back,
    }

    public readonly struct TweakMenuFocusTarget : IEquatable<TweakMenuFocusTarget>
    {
        TweakMenuFocusTarget(TweakMenuFocusKind kind, int rowIndex)
        {
            Kind = kind;
            RowIndex = rowIndex;
        }

        public TweakMenuFocusKind Kind { get; }
        public int RowIndex { get; }

        public static TweakMenuFocusTarget Group =>
            new TweakMenuFocusTarget(TweakMenuFocusKind.Group, -1);
        public static TweakMenuFocusTarget Reset =>
            new TweakMenuFocusTarget(TweakMenuFocusKind.Reset, -1);
        public static TweakMenuFocusTarget Back =>
            new TweakMenuFocusTarget(TweakMenuFocusKind.Back, -1);
        public static TweakMenuFocusTarget Row(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            return new TweakMenuFocusTarget(TweakMenuFocusKind.Row, index);
        }

        public bool Equals(TweakMenuFocusTarget other) =>
            Kind == other.Kind && RowIndex == other.RowIndex;
        public override bool Equals(object obj) =>
            obj is TweakMenuFocusTarget other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((int)Kind * 397) ^ RowIndex; }
        }
        public static bool operator ==(
            TweakMenuFocusTarget left,
            TweakMenuFocusTarget right) => left.Equals(right);
        public static bool operator !=(
            TweakMenuFocusTarget left,
            TweakMenuFocusTarget right) => !left.Equals(right);
    }

    /// <summary>
    /// Circular native-menu focus order: category, each descriptor row, reset, back.
    /// Presenters map row targets through their current scrolling window.
    /// </summary>
    public static class TweakMenuFocusGraph
    {
        public static TweakMenuFocusTarget Move(
            TweakMenuFocusTarget current,
            int delta,
            int rowCount)
        {
            if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
            if (delta == 0) return Normalize(current, rowCount);

            int count = rowCount + 3;
            int position = Position(Normalize(current, rowCount), rowCount);
            int step = delta > 0 ? 1 : -1;
            int next = (position + step) % count;
            if (next < 0) next += count;
            return Target(next, rowCount);
        }

        static TweakMenuFocusTarget Normalize(
            TweakMenuFocusTarget target,
            int rowCount)
        {
            if (target.Kind != TweakMenuFocusKind.Row) return target;
            if (rowCount == 0) return TweakMenuFocusTarget.Reset;
            if (target.RowIndex < rowCount) return target;
            return TweakMenuFocusTarget.Row(rowCount - 1);
        }

        static int Position(TweakMenuFocusTarget target, int rowCount)
        {
            switch (target.Kind)
            {
                case TweakMenuFocusKind.Row:
                    return target.RowIndex + 1;
                case TweakMenuFocusKind.Reset:
                    return rowCount + 1;
                case TweakMenuFocusKind.Back:
                    return rowCount + 2;
                default:
                    return 0;
            }
        }

        static TweakMenuFocusTarget Target(int position, int rowCount)
        {
            if (position == 0) return TweakMenuFocusTarget.Group;
            if (position <= rowCount) return TweakMenuFocusTarget.Row(position - 1);
            if (position == rowCount + 1) return TweakMenuFocusTarget.Reset;
            return TweakMenuFocusTarget.Back;
        }
    }
}
