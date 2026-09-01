using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    /// <summary>Pure interaction state for a grouped tweak menu.</summary>
    public sealed class TweakMenuModel
    {
        static readonly IReadOnlyList<TweakDescriptor> EmptyRows = Array.Empty<TweakDescriptor>();

        readonly TweakController _controller;
        readonly IReadOnlyList<TweakDescriptor>[] _rowsByGroup;

        public TweakMenuModel(TweakController controller, int visibleRows)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            if (visibleRows <= 0) throw new ArgumentOutOfRangeException(nameof(visibleRows));

            VisibleRows = visibleRows;

            var groups = new List<string>();
            var groupIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowsByGroup = new List<List<TweakDescriptor>>();
            for (int i = 0; i < controller.Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = controller.Descriptors[i];
                int groupIndex;
                if (!groupIndexes.TryGetValue(descriptor.Group, out groupIndex))
                {
                    groupIndex = groups.Count;
                    groups.Add(descriptor.Group);
                    groupIndexes.Add(descriptor.Group, groupIndex);
                    rowsByGroup.Add(new List<TweakDescriptor>());
                }
                rowsByGroup[groupIndex].Add(descriptor);
            }

            Groups = groups.ToArray();
            _rowsByGroup = new IReadOnlyList<TweakDescriptor>[rowsByGroup.Count];
            for (int i = 0; i < rowsByGroup.Count; i++)
                _rowsByGroup[i] = rowsByGroup[i].ToArray();
        }

        public bool IsOpen { get; private set; }
        public int SelectedGroupIndex { get; private set; }
        public int SelectedRowIndex { get; private set; }
        public int WindowStart { get; private set; }
        public int VisibleRows { get; }
        public string Message { get; private set; } = "";
        public bool MessageIsError { get; private set; }
        public IReadOnlyList<string> Groups { get; }
        public IReadOnlyList<TweakDescriptor> CurrentRows =>
            _rowsByGroup.Length == 0 ? EmptyRows : _rowsByGroup[SelectedGroupIndex];
        public TweakDescriptor Selected =>
            CurrentRows.Count == 0 ? null : CurrentRows[SelectedRowIndex];

        public void Open()
        {
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        public void MoveGroup(int delta)
        {
            if (_rowsByGroup.Length == 0) return;

            int next = Wrap(SelectedGroupIndex, delta, _rowsByGroup.Length);
            if (next == SelectedGroupIndex) return;

            SelectedGroupIndex = next;
            SelectedRowIndex = 0;
            WindowStart = 0;
        }

        public void MoveRow(int delta)
        {
            int count = CurrentRows.Count;
            if (count == 0) return;

            SelectedRowIndex = Wrap(SelectedRowIndex, delta, count);
            KeepSelectionVisible();
        }

        public TweakActionResult ToggleMaster()
        {
            TweakActionResult result = _controller.SetMaster(!_controller.MasterEnabled);
            string success = _controller.MasterEnabled
                ? "Mods enabled."
                : "Mods disabled; game baseline restored.";
            return Record(result, success);
        }

        public TweakActionResult CycleSelected()
        {
            TweakDescriptor selected = Selected;
            TweakActionResult result = selected == null
                ? TweakActionResult.Fail("No tweak is selected.")
                : _controller.Cycle(selected.Id);
            return Record(result, "Value saved.");
        }

        public TweakActionResult Reset()
        {
            return Record(_controller.Reset(), "All values reset.");
        }

        void KeepSelectionVisible()
        {
            if (SelectedRowIndex < WindowStart)
                WindowStart = SelectedRowIndex;
            else if ((long)SelectedRowIndex >= (long)WindowStart + VisibleRows)
                WindowStart = SelectedRowIndex - VisibleRows + 1;
        }

        TweakActionResult Record(TweakActionResult result, string successMessage)
        {
            Message = result.Success ? successMessage : result.Error;
            MessageIsError = !result.Success;
            return result;
        }

        static int Wrap(int index, int delta, int count)
        {
            long wrapped = ((long)index + delta) % count;
            return (int)(wrapped < 0 ? wrapped + count : wrapped);
        }
    }
}
