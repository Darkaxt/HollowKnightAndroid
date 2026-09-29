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
        long _lastOperationEvidenceSequence;

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
            RowsForGroup(SelectedGroupIndex);
        public IReadOnlyList<TweakDescriptor> RowsForGroup(int groupIndex) =>
            groupIndex < 0 || groupIndex >= _rowsByGroup.Length
                ? EmptyRows
                : _rowsByGroup[groupIndex];
        public TweakDescriptor Selected =>
            CurrentRows.Count == 0 ? null : CurrentRows[SelectedRowIndex];

        public void Open()
        {
            IsOpen = true;
            TweakOperationEvidence evidence;
            if (_controller.TryGetLatestOperationEvidence(out evidence))
                _lastOperationEvidenceSequence = evidence.Sequence;
        }

        public void Close()
        {
            IsOpen = false;
            DismissMessage();
        }

        public void DismissMessage()
        {
            ClearMessage();
        }

        public void MoveGroup(int delta)
        {
            if (_rowsByGroup.Length == 0) return;

            int next = Wrap(SelectedGroupIndex, delta, _rowsByGroup.Length);
            if (next == SelectedGroupIndex) return;

            SelectedGroupIndex = next;
            SelectedRowIndex = 0;
            WindowStart = 0;
            ClearMessage();
        }

        public void MoveRow(int delta)
        {
            int count = CurrentRows.Count;
            if (count == 0) return;

            int next = Wrap(SelectedRowIndex, delta, count);
            if (next == SelectedRowIndex) return;

            SelectedRowIndex = next;
            KeepSelectionVisible();
            ClearMessage();
        }

        public TweakActionResult CycleSelected()
        {
            TweakDescriptor selected = Selected;
            TweakActionResult result = selected == null
                ? TweakActionResult.Fail("No tweak is selected.")
                : _controller.Cycle(selected.Id);
            return Record(result, "Value saved.");
        }

        public TweakActionResult ActivateSelected()
        {
            TweakDescriptor selected = Selected;
            if (selected == null)
                return Record(TweakActionResult.Fail("No tweak is selected."), "Action completed.");
            if (selected.ControlKind == TweakControlKind.Choice)
                return CycleSelected();
            return Record(
                _controller.Set(selected.Id, selected.DefaultValue),
                "Action completed.");
        }

        public TweakActionResult SetSelected(string value)
        {
            TweakDescriptor selected = Selected;
            TweakActionResult result = selected == null
                ? TweakActionResult.Fail("No tweak is selected.")
                : _controller.Set(selected.Id, value);
            return Record(result, "Value saved.");
        }

        public TweakActionResult Reset()
        {
            return Record(_controller.Reset(), "All values reset.");
        }

        public bool RefreshOperationMessage()
        {
            TweakOperationEvidence evidence;
            if (!_controller.TryGetLatestOperationEvidence(out evidence) ||
                evidence.Sequence <= _lastOperationEvidenceSequence)
                return false;

            _lastOperationEvidenceSequence = evidence.Sequence;
            MessageIsError = !evidence.Success;
            Message = evidence.Success
                ? SuccessMessage(evidence)
                : evidence.Error;
            return true;
        }

        void ClearMessage()
        {
            Message = "";
            MessageIsError = false;
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
            Message = result.Pending
                ? "Runtime operation pending."
                : result.Success
                    ? successMessage
                    : result.Error;
            MessageIsError = !result.Success;
            if (result.Pending) return result;

            TweakOperationEvidence evidence;
            if (_controller.TryGetLatestOperationEvidence(out evidence) &&
                evidence.Sequence > _lastOperationEvidenceSequence)
            {
                _lastOperationEvidenceSequence = evidence.Sequence;
                if (result.Success)
                {
                    MessageIsError = !evidence.Success;
                    Message = evidence.Success
                        ? SuccessMessage(evidence)
                        : evidence.Error;
                }
            }
            return result;
        }

        string SuccessMessage(TweakOperationEvidence evidence)
        {
            string row = "Mods operation";
            if (!string.IsNullOrEmpty(evidence.RowId))
            {
                TweakDescriptor descriptor = null;
                for (int index = 0; index < _controller.Descriptors.Count; index++)
                {
                    TweakDescriptor candidate = _controller.Descriptors[index];
                    if (!string.Equals(candidate.Id, evidence.RowId, StringComparison.Ordinal))
                        continue;
                    descriptor = candidate;
                    break;
                }
                row = descriptor != null
                    ? descriptor.Title
                    : evidence.RowId.Replace('_', ' ').Replace('-', ' ');
                row = row.ToUpperInvariant();
            }
            if (evidence.Readback.HasValue)
                return row + ": " + evidence.Readback.Value.Replace('_', ' ').ToUpperInvariant() + ".";
            return row + " completed; runtime readback unavailable.";
        }

        static int Wrap(int index, int delta, int count)
        {
            long wrapped = ((long)index + delta) % count;
            return (int)(wrapped < 0 ? wrapped + count : wrapped);
        }
    }
}
