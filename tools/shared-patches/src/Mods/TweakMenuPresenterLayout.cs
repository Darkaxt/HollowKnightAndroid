using System;

namespace DualSouls.Mods
{
    public enum TweakMenuCancelTarget
    {
        CloseMenu,
        CloseRoute,
    }

    /// <summary>
    /// Executable native Mods presenter contract shared by both game profiles.
    /// It owns the physical button/window mapping, cancel behavior, and the
    /// conservative native-scale description geometry used by each presenter.
    /// </summary>
    public static class TweakMenuPresenterLayout
    {
        public const int VisibleRows = 5;
        public const int GroupButtonIndex = 0;
        public const int FirstRowButtonIndex = 1;
        public const int ResetButtonIndex = FirstRowButtonIndex + VisibleRows;
        public const int BackButtonIndex = ResetButtonIndex + 1;
        public const int ButtonCount = BackButtonIndex + 1;

        public const float DescriptionVisualIndex = 6.75f;
        public const float ResetVisualIndex = 8.35f;
        public const float BackVisualIndex = 9.45f;
        public const float DescriptionHeight = 96f;
        public const int DescriptionFontSize = 24;
        public const int DescriptionMinimumFontSize = 16;
        public const int DescriptionCharactersPerLine = 48;
        public const int DescriptionMaximumLines = 3;
        public const int DescriptionCharactersPerLineAtMinimumFontSize = 72;
        public const int DescriptionMaximumLinesAtMinimumFontSize = 5;
        public const float DescriptionLineHeight = 28f;
        public const float MinimumAdaptiveRowStep = 58f;
        public const float MaximumAdaptiveRowStep = 78f;
        public const float RowVerticalPadding = 4f;
        public const float FixedRowStep = 78f;
        public const float FixedRowButtonHeight = 70f;

        public static int ButtonIndex(
            TweakMenuFocusTarget target,
            int windowStart,
            int rowCount)
        {
            if (windowStart < 0) throw new ArgumentOutOfRangeException(nameof(windowStart));
            if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));

            switch (target.Kind)
            {
                case TweakMenuFocusKind.Group:
                    return GroupButtonIndex;
                case TweakMenuFocusKind.Reset:
                    return ResetButtonIndex;
                case TweakMenuFocusKind.Back:
                    return BackButtonIndex;
                case TweakMenuFocusKind.Row:
                    if (target.RowIndex < 0 || target.RowIndex >= rowCount)
                        return -1;
                    int slot = target.RowIndex - windowStart;
                    return slot >= 0 && slot < VisibleRows
                        ? FirstRowButtonIndex + slot
                        : -1;
                default:
                    return -1;
            }
        }

        public static int DataIndexForRowButton(
            int buttonIndex,
            int windowStart,
            int rowCount)
        {
            if (windowStart < 0) throw new ArgumentOutOfRangeException(nameof(windowStart));
            if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
            int slot = buttonIndex - FirstRowButtonIndex;
            if (slot < 0 || slot >= VisibleRows) return -1;
            int dataIndex = windowStart + slot;
            return dataIndex < rowCount ? dataIndex : -1;
        }

        public static TweakMenuCancelTarget CancelTarget(bool nestedRouteOpen) =>
            nestedRouteOpen ? TweakMenuCancelTarget.CloseRoute : TweakMenuCancelTarget.CloseMenu;

        public static bool DescriptionFits(string description) =>
            DescriptionLineCount(description) <= DescriptionMaximumLines;

        public static bool OperationDescriptionFits(string description) =>
            DescriptionLineCount(
                description,
                DescriptionCharactersPerLineAtMinimumFontSize) <=
            DescriptionMaximumLinesAtMinimumFontSize;

        public static int DescriptionLineCount(string description) =>
            DescriptionLineCount(description, DescriptionCharactersPerLine);

        static int DescriptionLineCount(string description, int charactersPerLine)
        {
            if (string.IsNullOrWhiteSpace(description)) return 0;
            string[] words = description.Split(
                (char[])null,
                StringSplitOptions.RemoveEmptyEntries);
            int lines = 1;
            int used = 0;
            for (int index = 0; index < words.Length; index++)
            {
                int remaining = words[index].Length;
                while (remaining > 0)
                {
                    int separator = used == 0 ? 0 : 1;
                    int available = charactersPerLine - used - separator;
                    if (available <= 0)
                    {
                        lines++;
                        used = 0;
                        continue;
                    }

                    if (remaining <= available)
                    {
                        used += separator + remaining;
                        remaining = 0;
                    }
                    else if (used > 0)
                    {
                        lines++;
                        used = 0;
                    }
                    else
                    {
                        remaining -= charactersPerLine;
                        used = charactersPerLine;
                        if (remaining > 0)
                        {
                            lines++;
                            used = 0;
                        }
                    }
                }
            }
            return lines;
        }

        public static bool ControlsDoNotOverlap(float rowStep, float rowButtonHeight)
        {
            if (rowStep <= 0f || rowButtonHeight <= 0f) return false;
            float buttonHalf = rowButtonHeight / 2f;
            float descriptionHalf = DescriptionHeight / 2f;
            float lastRowToDescription =
                (DescriptionVisualIndex - VisibleRows) * rowStep;
            float descriptionToReset =
                (ResetVisualIndex - DescriptionVisualIndex) * rowStep;
            float resetToBack = (BackVisualIndex - ResetVisualIndex) * rowStep;
            return lastRowToDescription >= buttonHalf + descriptionHalf &&
                   descriptionToReset >= descriptionHalf + buttonHalf &&
                   resetToBack >= rowButtonHeight;
        }
    }
}
