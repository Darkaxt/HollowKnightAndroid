using System;
using System.Collections.Generic;
using Xunit;

public sealed class SilksongPortJournalTests
{
    sealed class Native : IDsPortJournalNative
    {
        public bool Current=true, Settled=true;
        public string Problem;
        public string InterruptAt;
        public int Inspections, Clones, Binds, Activations, Settles, Presents, Clears, Destroys;
        public int DestroyFailures;
        public Exception BindFailure;
        public Action DestroyCallback;
        public Action<string> Callback;
        public Func<DsJournalToken, bool> CurrentToken;
        public readonly List<string> Order = new List<string>();
        public object Clone=new object();
        void Step(string name) { Order.Add(name); if (InterruptAt==name) Current=false; Callback?.Invoke(name); }
        public bool IsCurrent(DsJournalToken token) => Current && (CurrentToken == null || CurrentToken(token));
        public string Inspect(DsJournalToken token) { Inspections++; Step("inspect"); return Problem; }
        public object CloneInactive(DsJournalToken token) { Clones++; Clone = new object(); Step("clone"); return Clone; }
        public void BindAndVerify(DsJournalToken token, object clone)
        { Assert.Same(Clone,clone); Binds++; Step("bind"); if (BindFailure != null) throw BindFailure; }
        public void ActivateForLayout(DsJournalToken token, object clone) { Activations++; Step("activate"); }
        public bool TrySettle(DsJournalToken token, object clone) { Settles++; Step("settle"); return Settled; }
        public void Present(DsJournalToken token, object clone) { Presents++; Step("present"); }
        public void ClearSelection(object clone) { Clears++; }
        public void DestroyOwned(object clone)
        {
            Assert.Same(Clone,clone); Destroys++; DestroyCallback?.Invoke();
            if (Destroys <= DestroyFailures) throw new InvalidOperationException("native detail retirement");
        }
    }
    static DsJournalToken Token(long epoch=1) => new DsJournalToken(new object(),new object(),new object(),new object(),new object(),epoch);

    [Theory]
    [InlineData("UnityEngine.SpriteRenderer","TeamCherry.NestedFadeGroup.NestedFadeGroupSpriteRenderer",true,true,true)]
    [InlineData("TMProOld.TextMeshPro","NestedFadeGroupTextMeshPro",true,true,true)]
    [InlineData("UnityEngine.SpriteRenderer","UnreviewedBridge",true,true,false)]
    [InlineData("UnityEngine.SpriteRenderer","TeamCherry.NestedFadeGroup.NestedFadeGroupSpriteRenderer",false,true,false)]
    [InlineData("TMProOld.TextMeshPro","NestedFadeGroupTextMeshPro",true,false,false)]
    public void ExactBridgeNeedsOwnedParentAndLocalRequiredComponents(string source,string destination,bool parent,bool required,bool expected)
        => Assert.Equal(expected,DsJournalAdmission.BridgeAllowed(source,destination,parent,required));
    [Theory] [InlineData(false)] [InlineData(true)]
    public void TeardownDeactivatesForNativeHookUnsubscriptionBeforeDestroyEvenWhenClearThrows(bool fail)
    {
        var order=new List<string>();
        Action release=()=>DsJournalAdmission.ReleaseOwned(
            ()=> { order.Add("clear"); if (fail) throw new InvalidOperationException(); },
            ()=>order.Add("deactivate-unsubscribe"),()=>order.Add("destroy"));
        if (fail) Assert.Throws<InvalidOperationException>(release); else release();
        Assert.Equal(new[]{"clear","deactivate-unsubscribe","destroy"},order);
    }
    [Fact] public void NativePageActivatesOnceThenSettlesInLaterFrame()
    {
        var n=new Native(); var state=new DsPortJournalState(n); var token=Token();
        state.Tick(token,10); state.Tick(token,10);
        Assert.Equal(1,n.Activations); Assert.Equal(0,n.Settles); Assert.False(state.Ready);
        state.Tick(token,11); Assert.True(state.Ready); Assert.Equal(1,n.Clones); Assert.Equal(1,n.Binds);
        state.Tick(token,12); Assert.Equal(1,n.Activations); Assert.Equal(1,n.Settles);
    }
    [Theory] [InlineData("unknown component")] [InlineData("external template")] [InlineData("unknown callback")]
    public void RejectedShapeNeverClonesActivatesOrRetriesWithoutNewToken(string problem)
    {
        var n=new Native { Problem=problem }; var state=new DsPortJournalState(n); var token=Token();
        state.Tick(token,1); state.Tick(token,2);
        Assert.Equal(problem,state.Problem); Assert.Equal(1,n.Inspections); Assert.Equal(0,n.Clones); Assert.Equal(0,n.Activations);
        n.Problem=null; state.Tick(Token(2),3); Assert.Equal(1,n.Activations);
    }
    [Theory] [InlineData("inspect")] [InlineData("clone")] [InlineData("bind")] [InlineData("activate")] [InlineData("settle")] [InlineData("present")]
    public void InterruptedNativeDelegateCannotPublishStalePage(string step)
    {
        var n=new Native { InterruptAt=step }; var state=new DsPortJournalState(n); var token=Token();
        state.Tick(token,1); state.Tick(token,2);
        Assert.False(state.Ready); Assert.Null(state.Owned);
        Assert.Equal(1,n.Inspections); Assert.Equal(step=="inspect" ? 0 : 1,n.Clones);
        Assert.Equal(n.Clones,n.Destroys);
        if (step=="inspect" || step=="clone" || step=="bind") Assert.Equal(0,n.Activations);
    }
    [Fact] public void OwnerLossImmediatelyClearsAndNamedReentryCanRebuild()
    {
        var n=new Native(); var state=new DsPortJournalState(n); var token=Token();
        state.Tick(token,1); state.Tick(token,2); n.Current=false; state.Tick(token,3);
        Assert.False(state.Ready); Assert.Equal(1,n.Clears); Assert.Equal(1,n.Destroys);
        n.Current=true; var reentry=Token(2); state.Tick(reentry,4); state.Tick(reentry,5);
        Assert.Equal(2,n.Clones); Assert.True(state.Ready);
    }
    [Fact] public void OutgoingActiveHostCannotSettleAfterSelectionEpochChanges()
    {
        var n=new Native(); var state=new DsPortJournalState(n); var token=Token();
        state.Tick(token,1); state.Tick(null,2);
        Assert.Equal(0,n.Settles); Assert.Equal(1,n.Destroys); Assert.False(state.Ready);
        state.Tick(Token(2),3); Assert.Equal(2,n.Clones);
    }
    [Fact] public void FailedPageRetirementRetainsNoninteractiveOwnerAndBlocksReplacementUntilTickRetry()
    {
        var n = new Native { DestroyFailures = 2 }; var state = new DsPortJournalState(n); var first = Token();
        state.Tick(first, 1); state.Tick(first, 2); Assert.True(state.Ready);
        Assert.Throws<InvalidOperationException>(() => state.Clear());
        Assert.Same(n.Clone, state.Owned); Assert.False(state.Ready);
        var next = Token(2);
        Assert.Throws<InvalidOperationException>(() => state.Tick(next, 3));
        Assert.Equal(1, n.Clones); Assert.Equal(1, n.Presents); Assert.Same(n.Clone, state.Owned);
        state.Tick(next, 4); Assert.Equal(3, n.Destroys); Assert.Equal(1, n.Clears); Assert.Equal(2, n.Clones);
        state.Tick(next, 5); Assert.True(state.Ready);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void FailedPageRetirementIsRetriedByExistingFullOffDisposalPump(bool failInRestore)
    {
        var n = new Native { DestroyFailures = 2 }; var state = new DsPortJournalState(n); var token = Token();
        state.Tick(token, 1); state.Tick(token, 2); int parentReleases = 0;
        var pump = new DsHudReleaseState(() => { if (failInRestore) state.Clear(); },
            () => state.Clear(), () => parentReleases++, () => { });
        pump.RequestShutdown(pump.ReleasePresentation);
        Assert.True(pump.Pending); Assert.False(pump.CanRoute); Assert.False(state.Ready); Assert.Same(n.Clone, state.Owned);
        Assert.False(pump.Retry()); Assert.Same(n.Clone, state.Owned); Assert.Equal(0, parentReleases);
        Assert.True(pump.Retry()); Assert.Null(state.Owned); Assert.Equal(1, parentReleases); Assert.Equal(3, n.Destroys);
    }
    [Fact] public void PageRetirementReentrancyCannotPublishOrAcquireReplacement()
    {
        var n = new Native(); var state = new DsPortJournalState(n); var token = Token(); state.Tick(token, 1);
        n.DestroyCallback = () => state.Tick(Token(2), 10);
        state.Clear(); Assert.Null(state.Owned); Assert.Equal(1, n.Clones); Assert.Equal(0, n.Presents);
    }
    [Fact] public void InterruptedCloneRetirementFailureRemainsOwnedUntilInactiveTickRetry()
    {
        var n = new Native { InterruptAt = "clone", DestroyFailures = 1 }; var state = new DsPortJournalState(n);
        Assert.Throws<InvalidOperationException>(() => state.Tick(Token(), 1));
        Assert.Same(n.Clone, state.Owned); Assert.False(state.Ready); Assert.Equal(0, n.Binds);
        state.Tick(null, 2); Assert.Null(state.Owned); Assert.Equal(2, n.Destroys); Assert.Equal(1, n.Clones);
    }

    static DsJournalToken ContentToken(DsJournalToken owners, params object[] values)
    {
        var type = typeof(DsJournalToken).Assembly.GetType("DsPortPageSnapshot"); Assert.NotNull(type);
        var snapshot = Activator.CreateInstance(type, new object[] { values });
        return (DsJournalToken)Activator.CreateInstance(typeof(DsJournalToken), owners.Pane, owners.List,
            owners.Data, owners.Records, owners.Host, owners.Epoch, snapshot);
    }
    [Theory] [InlineData("Journal")] [InlineData("Tasks")] [InlineData("Inventory")] [InlineData("Loadout")]
    public void SameOwnerSameCountMembershipAndValueChangesReenterHiddenNativeLayout(string page)
    {
        var owners = Token(); var a = new object(); var b = new object(); var c = new object();
        var first = ContentToken(owners, page, a, 1, b, 2);
        var latest = first; var n = new Native { CurrentToken = t => t.Same(latest) };
        var state = new DsPortJournalState(n); state.Tick(first, 1); state.Tick(first, 2);
        Assert.True(state.Ready);
        latest = ContentToken(owners, page, a, 1, c, 2); // Same count, different exact native member.
        Assert.False(first.Same(latest)); state.Tick(latest, 3);
        Assert.False(state.Ready); Assert.Equal(2, n.Activations); Assert.Equal(1, n.Clears); Assert.Equal(1, n.Destroys);
        Assert.Equal(new[] { "inspect", "clone", "bind", "activate", "settle", "present", "inspect", "clone", "bind", "activate" }, n.Order);
        state.Tick(latest, 4); Assert.True(state.Ready);
        var previous = latest; latest = ContentToken(owners, page, a, 1, c, 3); // Same members, amount/counter/equip value changed.
        Assert.False(previous.Same(latest)); state.Tick(latest, 5);
        Assert.False(state.Ready); Assert.Equal(3, n.Activations); Assert.Equal(2, n.Destroys);
        state.Tick(latest, 6); Assert.True(state.Ready);
        state.Tick(ContentToken(owners, page, a, 1, c, 3), 7); Assert.Equal(3, n.Clones);
    }
    [Fact] public void SnapshotCopiesValuesAndDoesNotMistakeReferenceEqualsOverridesForNativeIdentity()
    {
        var owners = Token(); var source = new object[] { new EqualObject(), 1, "name" };
        var frozen = ContentToken(owners, source); source[1] = 2;
        Assert.False(frozen.Same(ContentToken(owners, source)));
        Assert.False(frozen.Same(ContentToken(owners, new EqualObject(), 1, "name")));
        Assert.True(frozen.Same(ContentToken(owners, source[0], 1, new string("name".ToCharArray()))));
    }
    sealed class EqualObject
    {
        public override bool Equals(object value) => value is EqualObject;
        public override int GetHashCode() => 0;
    }
    [Theory] [InlineData("inspect")] [InlineData("bind")] [InlineData("activate")]
    [InlineData("settle")] [InlineData("present")]
    public void ReentrantContentRefreshCancelsOldCallbackWithoutConstructingInsideNativeCall(string step)
    {
        var first = Token(); var latest = first; var next = new DsJournalToken(first.Pane, first.List, first.Data, first.Records, first.Host, 2);
        var n = new Native { CurrentToken = t => t.Same(latest) }; var state = new DsPortJournalState(n);
        bool entered = false;
        n.Callback = phase =>
        {
            if (phase != step || entered) return;
            entered = true; latest = next; int clones = n.Clones;
            state.Tick(next, 10);
            Assert.Equal(clones, n.Clones); Assert.False(state.Ready);
        };
        state.Tick(first, 1); if (!entered) state.Tick(first, 2);
        Assert.True(entered); Assert.False(state.Ready); Assert.Null(state.Owned);
        state.Tick(next, 11); Assert.False(state.Ready); state.Tick(next, 12); Assert.True(state.Ready);
    }
    [Fact] public void ContentRefreshWaitsForRetainedRetirementBeforeAnyReplacementLayout()
    {
        var owners = Token(); var first = ContentToken(owners, "equipped-a"); var latest = first;
        var n = new Native { DestroyFailures = 1, CurrentToken = t => t.Same(latest) }; var state = new DsPortJournalState(n);
        state.Tick(first, 1); state.Tick(first, 2); latest = ContentToken(owners, "equipped-b");
        Assert.Throws<InvalidOperationException>(() => state.Tick(latest, 3));
        Assert.False(state.Ready); Assert.Same(n.Clone, state.Owned); Assert.Equal(1, n.Activations);
        state.Tick(latest, 4); Assert.Equal(2, n.Activations); Assert.Equal(1, n.Clears);
        state.Tick(latest, 5); Assert.True(state.Ready);
    }

    [Fact] public void SnapshotRejectsOversizedReadWithoutPublishingATruncatedToken()
    {
        var owners = Token(); int reads = 0;
        var type = typeof(DsJournalToken).Assembly.GetType("DsPortPageSnapshot"); Assert.NotNull(type);
        int maximum = (int)type.GetField("MaximumValues").GetValue(null);
        IEnumerable<object> Values() { while (true) { reads++; yield return 1; } }
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Activator.CreateInstance(type, new object[] { Values() }));
        Assert.Equal("DsPortPageReadException", error.InnerException.GetType().Name);
        Assert.Equal(maximum + 1, reads);
    }
    [Fact] public void SnapshotReadFailureClearsPublishedPageAndCanRetryUnchangedContents()
    {
        var owners = Token(); var current = ContentToken(owners, "same-content"); bool fail = false;
        var type = typeof(DsJournalToken).Assembly.GetType("DsPortPageSnapshot");
        IEnumerable<object> Broken() { yield return "first"; throw new InvalidOperationException("native getter failed"); }
        var n = new Native { CurrentToken = token =>
        {
            if (fail) Activator.CreateInstance(type, new object[] { Broken() });
            return token.Same(current);
        } };
        var state = new DsPortJournalState(n); state.Tick(current, 1); state.Tick(current, 2); Assert.True(state.Ready);
        fail = true; Assert.Throws<System.Reflection.TargetInvocationException>(() => state.Tick(current, 3));
        Assert.False(state.Ready); Assert.Null(state.Owned);
        fail = false; state.Tick(current, 4); state.Tick(current, 5); Assert.True(state.Ready); Assert.Equal(2, n.Clones);
    }
    sealed class SelectionNative : IDsPortSelection
    {
        public Func<bool> Current;
        public Action Prompt;
        public int Submits;
        public bool IsCurrent(object owner, object item, object data) => Current();
        public void Display(object owner, object item) { }
        public bool CanSubmit(object owner, object item) { Prompt?.Invoke(); return true; }
        public bool Submit(object owner, object item) { Submits++; return true; }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void SameOwnerChangedContentCannotAuthorizeStaleSelectionOrDeferredEquip(bool duringPrompt)
    {
        var owners = Token(); var first = ContentToken(owners, "same-slot", "tool-a"); var current = first;
        var changed = ContentToken(owners, "same-slot", "tool-b");
        var native = new SelectionNative { Current = () => first.Same(current) };
        var selection = new DsPortSelectState(native); Assert.True(selection.Select(owners.Pane, owners.List, owners.Data));
        if (duringPrompt) native.Prompt = () => current = changed; else current = changed;
        Assert.False(selection.TrySubmit()); Assert.False(selection.HasSelection); Assert.Equal(0, native.Submits);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AdoptedPartialCreationFailureRetainsExactResourcesUntilNativeRetirementRetry(bool interrupted)
    {
        var original = new InvalidOperationException("retained partial native allocation");
        var n = new Native { BindFailure = original, DestroyFailures = 1, InterruptAt = interrupted ? "clone" : null };
        var state = new DsPortJournalState(n); var next = Token(2);
        n.DestroyCallback = () => { state.Tick(next, 10); Assert.Equal(1, n.Clones); };
        if (interrupted) Assert.Throws<InvalidOperationException>(() => state.Tick(Token(), 1));
        else
        {
            var error = Assert.Throws<AggregateException>(() => state.Tick(Token(), 1));
            Assert.Same(original, error.InnerExceptions[0]);
            Assert.Contains("native detail retirement", error.InnerExceptions[1].Message);
        }
        var partial = n.Clone;
        Assert.Same(partial, state.Owned); Assert.False(state.Ready); Assert.Equal(0, n.Activations); Assert.Equal(0, n.Presents);
        Assert.Equal(interrupted ? 0 : 1, n.Binds);
        n.Current = true; n.InterruptAt = null; n.BindFailure = null;
        state.Tick(next, 2); Assert.Equal(2, n.Destroys); Assert.Equal(1, n.Clears);
        Assert.NotSame(partial, state.Owned); Assert.Same(n.Clone, state.Owned); Assert.False(state.Ready);
        state.Tick(next, 3); Assert.True(state.Ready); Assert.Equal(1, n.Activations); Assert.Equal(1, n.Presents);
    }

    [Theory] [InlineData("Awake",false,false)] [InlineData("JustStart",true,false)] [InlineData("JustStart",false,true)]
    public void ResponseNeedsNamedNonAwakeModeAndDisabledLifecycle(string mode,bool enabled,bool safe)
        => Assert.Equal(safe,DsJournalAdmission.ResponseIsPrepared(mode,enabled));
    [Theory] [InlineData(false,false,true)] [InlineData(true,false,false)] [InlineData(true,true,true)]
    public void NativeCursorAcceptsAbsentButNotPresentDisabledInput(bool present,bool enabled,bool allowed)
        => Assert.Equal(allowed,DsJournalAdmission.CursorAcceptsInput(present,enabled));
    [Theory] [InlineData("Pane","Pane",null)] [InlineData("Entry","Pane","escaped")] [InlineData("Cursor",null,"missing")]
    public void MutableReferencesStayInTheirRequiredOwnedScope(string expected,string actual,string problem)
    {
        string result=DsJournalAdmission.ReferenceProblem("iconSprite",expected,actual,false);
        if (problem==null) Assert.Null(result); else { Assert.Contains("iconSprite",result); Assert.Contains(problem,result); }
    }
    [Theory] [InlineData("SetActive","Bool","RuntimeOnly",true,true)] [InlineData("Equip","Bool","RuntimeOnly",true,false)]
    [InlineData("SetActive","Void","RuntimeOnly",true,false)] [InlineData("SetActive","Bool","RuntimeOnly",false,false)]
    [InlineData("SetActive","Bool","Unknown",true,false)]
    public void OnlyExactLocalDisplayCallbacksAreAdmitted(string method,string mode,string state,bool local,bool allowed)
        => Assert.Equal(allowed,DsJournalAdmission.CallbackAllowed(method,mode,state,local));
    [Theory] [InlineData(-30,0,0,10,0,10)] [InlineData(-30,0,0,10,99,30)] [InlineData(0,4,0,10,0,6)]
    public void NativeLocalScrollClampsLargeAndTopAlignsShortContent(float cmin,float cmax,float vmin,float vmax,float desired,float expected)
        => Assert.Equal(expected,DsJournalGeometry.ClampScroll(cmin,cmax,vmin,vmax,desired));
    [Theory] [InlineData(20,2,10)] [InlineData(-20,.5f,-40)]
    public void ScaledDragIsConvertedBackToNativePaneLocalUnits(float pixels,float scale,float local)
        => Assert.Equal(local,DsJournalGeometry.LocalDrag(pixels,scale));
    [Theory] [InlineData(0,10,true)] [InlineData(-.01f,10,false)] [InlineData(0,10.01f,false)] [InlineData(float.NaN,5,false)]
    public void WholeRowsCursorAndExpandedDetailsMustFitCompletely(float bottom,float top,bool fits)
        => Assert.Equal(fits,DsJournalGeometry.Contains(0,10,bottom,top));
}
