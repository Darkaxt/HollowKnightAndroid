using System;
using System.Collections.Generic;
using System.Linq;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class Task101SpecRegressionTests
{
    [Fact]
    public void Collection_plan_admits_every_distinct_valid_owned_instance()
    {
        var target = SilksongSkinTargets.All[0];
        var ownerA = new object();
        var ownerB = new object();
        var collectionA = new object();
        var collectionB = new object();
        var plan = SilksongCollectionPlan.Build(
            new[] { target },
            new[] { target.CanonicalPath },
            new[] {
                new SilksongCollectionInstance(target.CollectionName, ownerA, collectionA, null),
                new SilksongCollectionInstance(target.CollectionName, ownerB, collectionB, null),
                new SilksongCollectionInstance(target.CollectionName, ownerA, collectionA, null),
            });

        Assert.Empty(plan.Omissions);
        Assert.Equal(new[] { collectionA, collectionB },
            plan.Bindings[target.CanonicalPath].Select(x => x.CollectionIdentity));
    }

    [Fact]
    public void Collection_plan_reports_every_sparse_target_and_rejects_invalid_owned_ambiguity()
    {
        var supplied = SilksongSkinTargets.All[0];
        var invalid = SilksongSkinTargets.All[1];
        var plan = SilksongCollectionPlan.Build(
            SilksongSkinTargets.All,
            new[] { supplied.CanonicalPath, invalid.CanonicalPath },
            new[] {
                new SilksongCollectionInstance(supplied.CollectionName, new object(), new object(), null),
                new SilksongCollectionInstance(invalid.CollectionName, new object(), new object(), null),
                new SilksongCollectionInstance(invalid.CollectionName, new object(), new object(), "wrong dimensions"),
            });

        Assert.Single(plan.Bindings[supplied.CanonicalPath]);
        Assert.False(plan.Bindings.ContainsKey(invalid.CanonicalPath));
        Assert.Contains(plan.Omissions, x => x == invalid.CanonicalPath + " (wrong dimensions)");
        foreach (var missing in SilksongSkinTargets.All.Skip(2))
            Assert.Contains(plan.Omissions, x => x == missing.CanonicalPath + " (pack texture missing)");
        Assert.Equal(10, plan.Omissions.Count);
    }

    [Fact]
    public void Collection_plan_identity_changes_on_same_hero_library_swap()
    {
        var target = SilksongSkinTargets.All[0];
        var hero = new object();
        var firstCollection = new object();
        var secondCollection = new object();
        var first = SilksongCollectionPlan.Build(new[] { target }, new[] { target.CanonicalPath },
            new[] { new SilksongCollectionInstance(target.CollectionName, hero, firstCollection, null) });
        var second = SilksongCollectionPlan.Build(new[] { target }, new[] { target.CanonicalPath },
            new[] { new SilksongCollectionInstance(target.CollectionName, hero, secondCollection, null) });

        Assert.NotEqual(first.OwnerStamp, second.OwnerStamp);
    }

    [Fact]
    public void Pending_skin_teardown_retries_and_blocks_replacement_without_losing_owner()
    {
        var session = new FailingSkinTeardown();
        var pending = new PendingSkinTeardown();

        Assert.True(pending.TryRetain(session));
        Assert.True(pending.BlocksReplacement);
        Assert.Same(session, pending.Session);
        var unrelated = new FailingSkinTeardown();
        Assert.False(pending.TryRetain(unrelated));
        Assert.Same(session, pending.Session);
        Assert.Equal(0, unrelated.Attempts);
        Assert.False(pending.Tick());
        Assert.True(pending.BlocksReplacement);
        Assert.Same(session, pending.Session);

        session.Fail = false;
        Assert.True(pending.Tick());
        Assert.False(pending.BlocksReplacement);
        Assert.Null(pending.Session);
        Assert.Equal(2, session.Attempts);
    }

    [Fact]
    public void Process_startup_runs_skin_owner_before_disabled_dual_screen_return()
    {
        var calls = new List<string>();
        bool started = SilksongProcessStartup.Run(
            () => calls.Add("skins"),
            () => { calls.Add("enabled"); return false; },
            () => calls.Add("dual"));

        Assert.False(started);
        Assert.Equal(new[] { "skins", "enabled" }, calls);
    }

    sealed class FailingSkinTeardown : ISkinTeardownSession
    {
        public bool Fail = true;
        public int Attempts;
        public bool TeardownComplete { get; private set; }
        public string LastError { get; private set; } = "not attempted";
        public void TickTeardown()
        {
            Attempts++;
            if (Fail) { LastError = "restore failed"; return; }
            LastError = "";
            TeardownComplete = true;
        }
    }
}
