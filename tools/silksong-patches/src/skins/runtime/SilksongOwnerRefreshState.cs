using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongOwnerIdentityStamp : IEquatable<SilksongOwnerIdentityStamp>
    {
        readonly IReadOnlyList<object> identities;

        public SilksongOwnerIdentityStamp(IEnumerable<object> identities)
        {
            if (identities == null) throw new ArgumentNullException(nameof(identities));
            var copy = identities.ToList();
            if (copy.Count == 0 || copy.Any(x => x == null))
                throw new ArgumentException("A complete owner identity is required.", nameof(identities));
            this.identities = copy.AsReadOnly();
        }

        public bool Equals(SilksongOwnerIdentityStamp other)
        {
            if (other == null || identities.Count != other.identities.Count) return false;
            for (int index = 0; index < identities.Count; index++)
                if (!ReferenceEquals(identities[index], other.identities[index])) return false;
            return true;
        }

        public override bool Equals(object value) => Equals(value as SilksongOwnerIdentityStamp);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (var identity in identities)
                    hash = hash * 31 + RuntimeHelpers.GetHashCode(identity);
                return hash;
            }
        }
    }

    public sealed class SilksongOwnerRefreshState
    {
        readonly float pollInterval;
        float nextPoll;
        SilksongOwnerIdentityStamp current;
        SilksongOwnerIdentityStamp visual;

        public int IdentityPollCount { get; private set; }
        public int VisualRefreshCount { get; private set; }
        public bool HasCurrentIdentity => current != null;
        public bool VisualCurrent => current != null && current.Equals(visual);

        public SilksongOwnerRefreshState(float pollInterval)
        {
            if (pollInterval <= 0 || float.IsNaN(pollInterval) || float.IsInfinity(pollInterval))
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
            this.pollInterval = pollInterval;
        }

        public bool ShouldPoll(float now)
        {
            if (float.IsNaN(now) || float.IsInfinity(now)) return false;
            if (IdentityPollCount > 0 && now < nextPoll) return false;
            nextPoll = now + pollInterval;
            IdentityPollCount++;
            return true;
        }

        public bool Update(SilksongOwnerIdentityStamp identity)
        {
            bool changed = identity == null ? current != null : !identity.Equals(current);
            current = identity;
            return changed;
        }

        public bool VisualRefreshRequired(bool normalRefreshAllowed) =>
            current != null && (normalRefreshAllowed || !VisualCurrent);

        public void MarkVisualRefresh()
        {
            if (current == null) throw new InvalidOperationException("No current owner identity is available.");
            visual = current;
            VisualRefreshCount++;
        }

        public void InvalidateVisuals()
        {
            visual = null;
            nextPoll = float.NegativeInfinity;
        }
    }
}
