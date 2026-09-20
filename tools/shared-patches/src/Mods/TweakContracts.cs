using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    public enum TweakControlKind
    {
        Choice,
        Command,
        Route,
    }

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
            : this(id, id, TweakControlKind.Choice, group, title, description, defaultValue, values)
        {
        }

        public TweakDescriptor(
            string id,
            string contractId,
            TweakControlKind controlKind,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values)
            : this(id, contractId, controlKind, group, title, description, defaultValue, values, true, "")
        {
        }

        TweakDescriptor(
            string id,
            string contractId,
            TweakControlKind controlKind,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values,
            bool isAvailable,
            string unavailableReason)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A persistence id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(contractId)) throw new ArgumentException("A contract id is required.", nameof(contractId));
            if (!Enum.IsDefined(typeof(TweakControlKind), controlKind)) throw new ArgumentOutOfRangeException(nameof(controlKind));
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("A tweak group is required.", nameof(group));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A tweak title is required.", nameof(title));
            if (!isAvailable && string.IsNullOrWhiteSpace(unavailableReason)) throw new ArgumentException("An unavailable reason is required for unavailable tweaks.", nameof(unavailableReason));
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
            ContractId = contractId;
            ControlKind = controlKind;
            Group = group;
            Title = title;
            Description = description ?? "";
            DefaultValue = defaultValue;
            Values = Array.AsReadOnly(copy);
            IsAvailable = isAvailable;
            UnavailableReason = unavailableReason ?? "";
        }

        public static TweakDescriptor Unavailable(
            string id,
            string contractId,
            TweakControlKind controlKind,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values,
            string unavailableReason)
        {
            return new TweakDescriptor(
                id, contractId, controlKind, group, title, description,
                defaultValue, values, false, unavailableReason);
        }

        /// <summary>Stable persistence key used by existing profile state.</summary>
        public string Id { get; }
        /// <summary>Canonical identity shared by both game catalogs.</summary>
        public string ContractId { get; }
        public TweakControlKind ControlKind { get; }
        public string Group { get; }
        public string Title { get; }
        public string Description { get; }
        public string DefaultValue { get; }
        public IReadOnlyList<string> Values { get; }
        public bool IsAvailable { get; }
        public string UnavailableReason { get; }

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

        void CaptureBaseline();
        TweakActionResult Apply(string id, string value);
        void RestoreBaseline();
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
