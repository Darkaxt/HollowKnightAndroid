using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongCollectionInstance
    {
        public string CollectionName { get; }
        public object OwnerIdentity { get; }
        public object CollectionIdentity { get; }
        public string Failure { get; }

        public SilksongCollectionInstance(string collectionName, object ownerIdentity,
            object collectionIdentity, string failure)
        {
            CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            OwnerIdentity = ownerIdentity ?? throw new ArgumentNullException(nameof(ownerIdentity));
            CollectionIdentity = collectionIdentity ?? throw new ArgumentNullException(nameof(collectionIdentity));
            Failure = failure;
        }
    }

    public sealed class SilksongCollectionOwnerStamp : IEquatable<SilksongCollectionOwnerStamp>
    {
        readonly IReadOnlyList<(object Owner, object Collection)> identities;

        internal SilksongCollectionOwnerStamp(IEnumerable<SilksongCollectionInstance> instances)
        {
            identities = instances.Select(x => (x.OwnerIdentity, x.CollectionIdentity)).ToList().AsReadOnly();
        }

        public bool Equals(SilksongCollectionOwnerStamp other)
        {
            if (other == null || identities.Count != other.identities.Count) return false;
            for (int i = 0; i < identities.Count; i++)
                if (!ReferenceEquals(identities[i].Owner, other.identities[i].Owner) ||
                    !ReferenceEquals(identities[i].Collection, other.identities[i].Collection)) return false;
            return true;
        }
        public override bool Equals(object value) => Equals(value as SilksongCollectionOwnerStamp);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (var identity in identities)
                    hash = hash * 31 + RuntimeHelpers.GetHashCode(identity.Owner) * 397 +
                        RuntimeHelpers.GetHashCode(identity.Collection);
                return hash;
            }
        }
    }

    public sealed class SilksongCollectionPlan
    {
        public IReadOnlyDictionary<string, IReadOnlyList<SilksongCollectionInstance>> Bindings { get; }
        public IReadOnlyList<string> Omissions { get; }
        public SilksongCollectionOwnerStamp OwnerStamp { get; }

        SilksongCollectionPlan(Dictionary<string, IReadOnlyList<SilksongCollectionInstance>> bindings,
            List<string> omissions, IEnumerable<SilksongCollectionInstance> admitted)
        {
            Bindings = new ReadOnlyDictionary<string, IReadOnlyList<SilksongCollectionInstance>>(bindings);
            Omissions = omissions.AsReadOnly();
            OwnerStamp = new SilksongCollectionOwnerStamp(admitted);
        }

        public static SilksongCollectionPlan Build(IEnumerable<SilksongSkinTarget> targets,
            IEnumerable<string> requestedTargets, IEnumerable<SilksongCollectionInstance> candidates)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (requestedTargets == null) throw new ArgumentNullException(nameof(requestedTargets));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var requested = new HashSet<string>(requestedTargets, StringComparer.Ordinal);
            var source = candidates.ToList();
            var bindings = new Dictionary<string, IReadOnlyList<SilksongCollectionInstance>>(StringComparer.Ordinal);
            var omissions = new List<string>();
            var admitted = new List<SilksongCollectionInstance>();

            foreach (var target in targets)
            {
                if (!requested.Contains(target.CanonicalPath))
                {
                    omissions.Add(target.CanonicalPath + " (pack texture missing)");
                    continue;
                }
                var matching = source.Where(x => string.Equals(x.CollectionName, target.CollectionName,
                        StringComparison.Ordinal))
                    .GroupBy(x => x.CollectionIdentity, ReferenceIdentityComparer.Instance)
                    .Select(group => group.ToList()).ToList();
                if (matching.Count == 0)
                {
                    omissions.Add(target.CanonicalPath + " (collection missing)");
                    continue;
                }
                if (matching.Any(group =>
                    group.Select(x => x.Failure ?? "").Distinct(StringComparer.Ordinal).Count() != 1))
                {
                    omissions.Add(target.CanonicalPath + " (conflicting collection ownership)");
                    continue;
                }
                var unique = matching.Select(x => x[0]).ToList();
                var failures = unique.Where(x => !string.IsNullOrEmpty(x.Failure)).Select(x => x.Failure)
                    .Distinct(StringComparer.Ordinal).ToList();
                if (failures.Count > 0)
                {
                    omissions.Add(target.CanonicalPath + " (" + string.Join(", ", failures) + ")");
                    continue;
                }
                bindings.Add(target.CanonicalPath, unique.AsReadOnly());
                admitted.AddRange(matching.SelectMany(x => x));
            }
            return new SilksongCollectionPlan(bindings, omissions, admitted);
        }

        sealed class ReferenceIdentityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();
            public new bool Equals(object left, object right) => ReferenceEquals(left, right);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
