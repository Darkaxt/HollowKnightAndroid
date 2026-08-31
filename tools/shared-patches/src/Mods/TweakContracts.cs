using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    /// <summary>A stable, game-neutral row in the built-in Mods menu.</summary>
    public sealed class TweakDescriptor
    {
        public TweakDescriptor(
            string id,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A tweak id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("A tweak group is required.", nameof(group));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A tweak title is required.", nameof(title));
            if (values == null || values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));

            var copy = new string[values.Count];
            bool foundDefault = false;
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Tweak values cannot be blank.", nameof(values));
                for (int j = 0; j < i; j++)
                    if (string.Equals(copy[j], value, StringComparison.Ordinal))
                        throw new ArgumentException("Tweak values must be unique.", nameof(values));
                copy[i] = value;
                if (string.Equals(value, defaultValue, StringComparison.Ordinal)) foundDefault = true;
            }
            if (!foundDefault) throw new ArgumentException("The default must be one of the allowed values.", nameof(defaultValue));

            Id = id;
            Group = group;
            Title = title;
            Description = description ?? "";
            DefaultValue = defaultValue;
            Values = copy;
        }

        public string Id { get; }
        public string Group { get; }
        public string Title { get; }
        public string Description { get; }
        public string DefaultValue { get; }
        public IReadOnlyList<string> Values { get; }

        public bool Allows(string value)
        {
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        public string Next(string current)
        {
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i], current, StringComparison.Ordinal))
                    return Values[(i + 1) % Values.Count];
            return DefaultValue;
        }
    }

    public readonly struct TweakActionResult
    {
        TweakActionResult(bool success, string error)
        {
            Success = success;
            Error = error ?? "";
        }

        public bool Success { get; }
        public string Error { get; }

        public static TweakActionResult Ok() => new TweakActionResult(true, "");
        public static TweakActionResult Fail(string error) =>
            new TweakActionResult(false, string.IsNullOrWhiteSpace(error) ? "The tweak could not be applied." : error);
    }

    /// <summary>The game-specific side of the shared controller.</summary>
    public interface ITweakAdapter
    {
        string GameId { get; }
        IReadOnlyList<TweakDescriptor> Descriptors { get; }

        /// <summary>Capture the values which master OFF must restore.</summary>
        void CaptureBaseline();

        /// <summary>Apply one value. The descriptor default restores this capability's baseline.</summary>
        TweakActionResult Apply(string id, string value);

        /// <summary>Restore every captured value after disable, reset, or failure.</summary>
        void RestoreBaseline();

        /// <summary>Maintain effects which use the game's own periodic path.</summary>
        void Tick();
    }

    /// <summary>Small persistence boundary; implementations supply the game-qualified backing store.</summary>
    public interface ITweakStore
    {
        string Read(string key);
        void Write(string key, string value);
        void Flush();
    }
}
