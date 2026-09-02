using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    public readonly struct TweakPresenterPoint
    {
        public TweakPresenterPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
    }

    public readonly struct TweakPresenterRect
    {
        public TweakPresenterRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public bool Contains(TweakPresenterPoint point)
        {
            return Width > 0f && Height > 0f &&
                   point.X >= X && point.X <= X + Width &&
                   point.Y >= Y && point.Y <= Y + Height;
        }
    }

    public static class TweakPresenterGeometryPaintStamp
    {
        const long Offset = 1469598103934665603L;
        const long Prime = 1099511628211L;

        public static long Compute(
            float left,
            float right,
            float bottom,
            float top,
            float scale,
            float viewportX,
            float viewportY,
            float viewportWidth,
            float viewportHeight,
            float cameraX,
            float cameraY,
            float cameraZ,
            float orthographicSize,
            float aspect)
        {
            long stamp = Offset;
            stamp = Hash(stamp, left);
            stamp = Hash(stamp, right);
            stamp = Hash(stamp, bottom);
            stamp = Hash(stamp, top);
            stamp = Hash(stamp, scale);
            stamp = Hash(stamp, viewportX);
            stamp = Hash(stamp, viewportY);
            stamp = Hash(stamp, viewportWidth);
            stamp = Hash(stamp, viewportHeight);
            stamp = Hash(stamp, cameraX);
            stamp = Hash(stamp, cameraY);
            stamp = Hash(stamp, cameraZ);
            stamp = Hash(stamp, orthographicSize);
            return Hash(stamp, aspect);
        }

        static long Hash(long hash, float value)
        {
            return unchecked((hash ^ (uint)value.GetHashCode()) * Prime);
        }
    }

    public sealed class TweakPresenterHitMap
    {
        static readonly IReadOnlyList<TweakPresenterRect> EmptyRows =
            Array.Empty<TweakPresenterRect>();

        public TweakPresenterHitMap(
            TweakPresenterRect close,
            TweakPresenterRect master,
            TweakPresenterRect previousGroup,
            TweakPresenterRect nextGroup,
            TweakPresenterRect reset,
            IReadOnlyList<TweakPresenterRect> rows)
        {
            Close = close;
            Master = master;
            PreviousGroup = previousGroup;
            NextGroup = nextGroup;
            Reset = reset;
            Rows = rows ?? EmptyRows;
        }

        public TweakPresenterRect Close { get; }
        public TweakPresenterRect Master { get; }
        public TweakPresenterRect PreviousGroup { get; }
        public TweakPresenterRect NextGroup { get; }
        public TweakPresenterRect Reset { get; }
        public IReadOnlyList<TweakPresenterRect> Rows { get; }
    }

    public enum TweakPresenterActionKind
    {
        None,
        Close,
        ToggleMaster,
        PreviousGroup,
        NextGroup,
        SelectRow,
        CycleSelected,
        Reset,
    }

    public readonly struct TweakPresenterAction
    {
        public TweakPresenterAction(TweakPresenterActionKind kind, int rowIndex = -1)
        {
            Kind = kind;
            RowIndex = rowIndex;
        }

        public TweakPresenterActionKind Kind { get; }
        public int RowIndex { get; }
    }

    /// <summary>
    /// Pure touch and semantic-action policy for built-in tweak presenters.
    /// It never mutates a menu; the presenter forwards resolved actions to the
    /// process-owned <see cref="TweakMenuModel"/>.
    /// </summary>
    public sealed class TweakPresenterInteraction
    {
        int _lastCleanTapSequence = int.MinValue;

        public bool IsNewCleanTap(int sequence)
        {
            return sequence != _lastCleanTapSequence;
        }

        public bool TryAcceptCleanTap(int sequence)
        {
            if (!IsNewCleanTap(sequence)) return false;
            _lastCleanTapSequence = sequence;
            return true;
        }

        public void ResetCleanTap(int sequence)
        {
            _lastCleanTapSequence = sequence;
        }

        public static bool TryMapNormalizedTopLeft(
            float x,
            float y,
            TweakPresenterRect viewport,
            out TweakPresenterPoint viewportLocal)
        {
            viewportLocal = default;
            if (x < 0f || x > 1f || y < 0f || y > 1f ||
                viewport.Width <= 0f || viewport.Height <= 0f)
                return false;

            var panelBottomLeft = new TweakPresenterPoint(x, 1f - y);
            if (!viewport.Contains(panelBottomLeft)) return false;
            viewportLocal = new TweakPresenterPoint(
                (panelBottomLeft.X - viewport.X) / viewport.Width,
                (panelBottomLeft.Y - viewport.Y) / viewport.Height);
            return true;
        }

        public static TweakPresenterAction ResolveAction(
            TweakPresenterPoint point,
            TweakPresenterHitMap hits,
            TweakMenuModel menu)
        {
            if (hits == null || menu == null) return default;
            if (hits.Close.Contains(point))
                return new TweakPresenterAction(TweakPresenterActionKind.Close);
            if (hits.Master.Contains(point))
                return new TweakPresenterAction(TweakPresenterActionKind.ToggleMaster);
            if (hits.PreviousGroup.Contains(point))
                return new TweakPresenterAction(TweakPresenterActionKind.PreviousGroup);
            if (hits.NextGroup.Contains(point))
                return new TweakPresenterAction(TweakPresenterActionKind.NextGroup);

            int visible = Math.Min(menu.VisibleRows, hits.Rows.Count);
            for (int i = 0; i < visible; i++)
            {
                if (!hits.Rows[i].Contains(point)) continue;
                int rowIndex = menu.WindowStart + i;
                if (rowIndex < 0 || rowIndex >= menu.CurrentRows.Count)
                    return default;
                if (rowIndex != menu.SelectedRowIndex)
                    return new TweakPresenterAction(
                        TweakPresenterActionKind.SelectRow, rowIndex);
                return menu.CurrentRows[rowIndex].IsAvailable
                    ? new TweakPresenterAction(
                        TweakPresenterActionKind.CycleSelected, rowIndex)
                    : default;
            }

            return hits.Reset.Contains(point)
                ? new TweakPresenterAction(TweakPresenterActionKind.Reset)
                : default;
        }
    }

    public readonly struct TweakPresenterRebindDecision
    {
        internal TweakPresenterRebindDecision(
            bool changed,
            bool closePreviousMenu,
            bool detachPreviousPresenter,
            bool restoreCoveredContent)
        {
            Changed = changed;
            ClosePreviousMenu = closePreviousMenu;
            DetachPreviousPresenter = detachPreviousPresenter;
            RestoreCoveredContent = restoreCoveredContent;
        }

        public bool Changed { get; }
        public bool ClosePreviousMenu { get; }
        public bool DetachPreviousPresenter { get; }
        public bool RestoreCoveredContent { get; }
    }

    /// <summary>Tracks presentation lifecycle decisions, never behavior state.</summary>
    public sealed class TweakPresenterLifecycle
    {
        readonly TweakPresenterPaintInvalidation _paint;
        object _owner;
        object _menu;

        public TweakPresenterLifecycle(TweakPresenterPaintInvalidation paint)
        {
            _paint = paint ?? throw new ArgumentNullException(nameof(paint));
        }

        public bool IsOpen { get; private set; }
        public bool ViewValid { get; private set; }
        public bool PresenterAttached { get; private set; }
        public bool CoveredContentStowed { get; private set; }

        public TweakPresenterRebindDecision Rebind(
            object owner,
            object menu,
            bool menuIsOpen)
        {
            bool changed = !ReferenceEquals(_owner, owner) ||
                           !ReferenceEquals(_menu, menu);
            if (!changed)
            {
                SynchronizeOpen(menuIsOpen);
                return default;
            }

            var decision = new TweakPresenterRebindDecision(
                true,
                _menu != null,
                PresenterAttached,
                CoveredContentStowed);
            _owner = owner;
            _menu = menu;
            IsOpen = menuIsOpen;
            ViewValid = false;
            PresenterAttached = false;
            CoveredContentStowed = false;
            _paint.Invalidate();
            return decision;
        }

        public void SynchronizeOpen(bool menuIsOpen)
        {
            if (IsOpen == menuIsOpen) return;
            IsOpen = menuIsOpen;
            _paint.Invalidate();
        }

        public void MarkViewBuilt()
        {
            ViewValid = true;
            _paint.Invalidate();
        }

        public void MarkPresenterAttached()
        {
            PresenterAttached = true;
        }

        public bool RequestCoveredContentStow()
        {
            if (CoveredContentStowed) return false;
            CoveredContentStowed = true;
            return true;
        }

        public bool RequestCoveredContentRestore()
        {
            if (!CoveredContentStowed) return false;
            CoveredContentStowed = false;
            return true;
        }

        public bool Detach()
        {
            bool changed = _owner != null || _menu != null || IsOpen ||
                           ViewValid || PresenterAttached || CoveredContentStowed;
            if (!changed) return false;
            _owner = null;
            _menu = null;
            IsOpen = false;
            ViewValid = false;
            PresenterAttached = false;
            CoveredContentStowed = false;
            _paint.Invalidate();
            return true;
        }
    }

    /// <summary>Allocation-free paint dirty/stamp gate.</summary>
    public sealed class TweakPresenterPaintInvalidation
    {
        bool _dirty = true;
        bool _acknowledged;
        long _modelStamp;
        long _geometryStamp;

        public bool IsDirty => _dirty;

        public void Invalidate()
        {
            _dirty = true;
        }

        public bool ShouldPaint(long modelStamp, long geometryStamp)
        {
            return _dirty || !_acknowledged ||
                   _modelStamp != modelStamp ||
                   _geometryStamp != geometryStamp;
        }

        public bool HasCurrentGeometry(long geometryStamp)
        {
            return !_dirty && _acknowledged && _geometryStamp == geometryStamp;
        }

        public void Acknowledge(long modelStamp, long geometryStamp)
        {
            _modelStamp = modelStamp;
            _geometryStamp = geometryStamp;
            _acknowledged = true;
            _dirty = false;
        }
    }
}
