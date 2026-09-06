using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Pure composition only. TransactionCore remains the sole execution reducer.
    public sealed class SkinRotationTransactionCore
    {
        private readonly SkinRotationCore rotation = new();
        private readonly SkinTransactionCore transaction = new();

        public RotationTransactionDecision Decide(RotationTransactionState s, RotationTransactionEvent signal)
        {
            if (!IsStateValid(s)) return D(s,"invalid-state");
            if (signal == null) return D(s,"invalid-event");
            if (signal is RotationTransactionEvent.Recover recovery) return Recover(s,recovery.Evidence);
            var operation=s.Operation;
            if (signal is RotationTransactionEvent.Mode mode)
            {
                if (mode.Event is RotationModeEvent.Selector { Event: RotationEvent.Rebind })
                {
                    var rebound=rotation.Decide(s.Mode,mode.Event);
                    if (rebound.State==s.Mode) return D(s,rebound.Diagnosis);
                    return D(s with {Mode=rebound.State,Operation=operation == null ? null : operation with {ReboundBlocked=true},
                        ReadinessReboundBlocked=s.ReadinessReboundBlocked || operation==null && s.Mode.Rotation.Pending?.Phase==RotationPendingPhase.INTENT_ISSUED},rebound.Diagnosis);
                }
                if (operation!=null)
                {
                    if (mode.Event is RotationModeEvent.AdvanceMode && s.Mode.Rotation.Activation.Mode==SkinMode.ROTATE)
                        return D(s with {Mode=s.Mode with {OffRequested=true}},"transaction-disarmed-restoration-needed");
                    return D(s,"transaction-busy");
                }
                var result=rotation.Decide(s.Mode,mode.Event);
                var next=s with {Mode=result.State};
                if (mode.Event is RotationModeEvent.AdvanceMode && result.Diagnosis=="transaction-required") return Begin(next,null);
                return new(next,result.Diagnosis,result.Commands.Select(c=>(RotationTransactionCommand)new RotationTransactionCommand.Mode(c)));
            }
            if (signal is RotationTransactionEvent.ConsumeReady ready)
            {
                if (s.ReadinessReboundBlocked) return D(s,"task67-readiness-rebound-resolution-required");
                if (operation!=null || s.Mode.Operation!=null) return D(s,"operation-busy");
                if (!Ready(s.Mode,ready.Intent)) return D(s,"stale-readiness");
                return Begin(s,ready.Intent);
            }
            if (!(signal is RotationTransactionEvent.Transaction callback)) return D(s,"invalid-event");
            if (operation==null) return D(s,"no-transaction");
            if (operation.ReboundBlocked) return D(s,"task67-rebound-resolution-required");
            if (callback.Hero!=operation.Origin.Rotation.CurrentHero || callback.Hero!=s.Mode.Rotation.CurrentHero)
                return D(s,"stale-hero");
            if (callback.Event is TransactionEvent.Begin) return D(s,"transaction-busy");
            var decision=transaction.Decide(operation.Transaction,callback.Event);
            if (decision.State==operation.Transaction) return D(s,decision.Diagnosis);
            if (decision.State.Phase==TransactionPhase.COMMITTED)
            {
                // Only an accepted, exact verified closure event can enter this branch.
                // A caller-provided terminal state is not a publication event.
                var complete=(TransactionEvent.CompletionCommitted)callback.Event;
                var m=s.Mode;
                var published=m with {Head=complete.VerifiedHead,LiveProof=null,
                    Rotation=m.Rotation with {Activation=decision.State.Activation,Pending=null},
                    OffRequested=decision.State.Activation.Mode==SkinMode.ROTATE && m.OffRequested,
                    CanceledPending=null};
                return D(new(published,LastRecovery:s.LastRecovery),"transaction-committed");
            }
            return new(s with {Operation=operation with {Transaction=decision.State}},decision.Diagnosis,
                decision.Commands.Select(c=>(RotationTransactionCommand)new RotationTransactionCommand.Transaction(callback.Hero,c)));
        }

        // Recovery only retires wrapper ownership after exact durable authority. It never
        // retokens an issued intent, executes commands, or rewrites the historical core phase.
        private RotationTransactionDecision Recover(RotationTransactionState s, RotationRecoveryEvidence evidence)
        {
            var authority=evidence?.Authority;
            if(authority==null || authority.Hero==null || authority.Skin==null ||
                authority.Hero!=s.Mode.Rotation.CurrentHero || authority.Skin!=s.Mode.Rotation.CurrentSkin)
                return D(s,"recovery-current-authority-required");
            var p=s.Operation;VerifiedRegistryHead head;
            switch(evidence)
            {
                case RotationRecoveryEvidence.IssuedClosureOutcome issued:
                    if(p==null || issued.Operation!=p)return D(s,"recovery-original-operation-required");
                    if(p.Transaction.Phase!=TransactionPhase.BLOCKED || p.Transaction.PendingClosure==null)
                        return D(s,"recovery-issued-outcome-required");
                    if(!OutcomeChild(p,issued.Fence,issued.Receipt,issued.Head,p.Transaction.PendingClosure))
                        return D(s,"recovery-exact-serialized-outcome-required");
                    head=issued.Head;
                    break;
                case RotationRecoveryEvidence.PriorRestoredAndFenced restored:
                    if(p==null || restored.Operation!=p)return D(s,"recovery-original-operation-required");
                    var priorEnvelope=p.Transaction.Envelope;
                    if(p.Transaction.Phase!=TransactionPhase.ROLLBACK_PENDING && p.Transaction.Phase!=TransactionPhase.BLOCKED)
                        return D(s,"recovery-prior-outcome-required");
                    // Exact TransactionCore prior-closure rule; no historical phase reduction.
                    var priorTarget=priorEnvelope.Prior;
                    if(!priorEnvelope.PriorEstablishedOnBinding && priorTarget.Active is ActiveVisual.Pack)
                    {
                        if(priorTarget.SkinStamp==long.MaxValue)return D(s,"skin-stamp-exhausted");
                        priorTarget=priorTarget with {SkinStamp=priorTarget.SkinStamp+1};
                    }
                    if(!ProofMatches(restored.Restoration,p.Origin.Rotation.CurrentHero,priorEnvelope.Binding,priorTarget.Active) ||
                        !OutcomeChild(p,restored.Fence,restored.Receipt,restored.Head,priorTarget))
                        return D(s,"recovery-exact-serialized-prior-required");
                    head=restored.Head;
                    break;
                case RotationRecoveryEvidence.VisualClosure closure:
                    if(p==null || !p.ReboundBlocked || closure.Operation!=p || closure.Completion==null)
                        return D(s,"recovery-original-operation-required");
                    if(p.Transaction.Phase!=TransactionPhase.APPLIED && p.Transaction.Phase!=TransactionPhase.ROLLED_BACK)
                        return D(s,"task68-qualified-prior-or-terminal-resolution-required");
                    // Only validate an already-issued closure; the returned terminal state is
                    // not installed. The audit retains the exact original transaction unchanged.
                    var checkedClosure=transaction.Decide(p.Transaction,closure.Completion);
                    if(checkedClosure.State.Phase!=TransactionPhase.COMMITTED)return D(s,"recovery-exact-closure-required");
                    head=closure.Completion.VerifiedHead;
                    break;
                case RotationRecoveryEvidence.VisualFencedNotExecuted fenced:
                    if(p==null || !p.ReboundBlocked || fenced.Operation!=p)return D(s,"recovery-original-operation-required");
                    var t=p.Transaction;var envelope=t.Envelope;
                    if(t.Phase==TransactionPhase.PREPARING || t.Phase==TransactionPhase.PREPARED)
                    {
                        if(fenced.Receipt!=null || fenced.Head!=p.Origin.Head)return D(s,"recovery-unchanged-base-required");
                    }
                    else if(t.Phase==TransactionPhase.ARMED)
                    {
                        var arm=t.ArmCommitReceipt;
                        var prior=!envelope.PriorEstablishedOnBinding && envelope.Prior.Active is ActiveVisual.Pack
                            ? envelope.Prior with {SkinStamp=envelope.Prior.SkinStamp+1} : envelope.Prior;
                        if(!ExactChild(fenced.Receipt,fenced.Head,arm.NewGenerationId,arm.NewGenerationSha256,prior) ||
                            fenced.Head.GenerationId==envelope.BaseGenerationId || fenced.Head.GenerationSha256==envelope.BaseGenerationSha256)
                            return D(s,"recovery-exact-armed-prior-closure-required");
                    }
                    else return D(s,"task68-qualified-prior-or-terminal-resolution-required");
                    head=fenced.Head;
                    break;
                case RotationRecoveryEvidence.ModeClosure mode:
                    var operation=s.Mode.Operation;
                    if(p!=null || operation==null || !operation.ReboundBlocked || mode.Operation!=operation ||
                        mode.Completion==null || mode.Completion.Correlation!=operation.Correlation)
                        return D(s,"recovery-original-operation-required");
                    if(!ExactChild(mode.Completion.Receipt,mode.Completion.Head,operation.BaseHead.GenerationId,
                        operation.BaseHead.GenerationSha256,operation.Target))return D(s,"recovery-exact-closure-required");
                    // H1's OFF CAS was issued with original-binding Vanilla authority. A
                    // current Vanilla fact is required before publishing OFF on this binding.
                    if(operation.Target.Mode==SkinMode.OFF && !(authority.LiveProof?.Proof is VerifiedLiveVisualProof.Vanilla))
                        return D(s,"recovery-current-vanilla-required");
                    if(operation.SelectedProof!=null && !ProofMatches(authority.LiveProof,authority.Hero,authority.Skin,operation.Target.Active))
                        return D(s,"recovery-current-selected-proof-required");
                    head=mode.Completion.Head;
                    break;
                case RotationRecoveryEvidence.ReadinessFencedNotExecuted ready:
                    if(p!=null || s.Mode.Operation!=null || !(s.ReadinessReboundBlocked || s.Mode.OffRequested) || ready.Intent==null ||
                        ready.Intent!=s.Mode.Rotation.Pending?.IssuedIntent)return D(s,"recovery-original-readiness-required");
                    if(ready.Head!=s.Mode.Head)return D(s,"recovery-unchanged-base-required");
                    head=ready.Head;
                    break;
                default:return D(s,"invalid-recovery-evidence");
            }
            var m=s.Mode with {Head=head,Operation=null,LiveProof=authority.LiveProof,
                Rotation=s.Mode.Rotation with {Activation=head.Activation,Pending=null},
                OffRequested=head.Activation.Mode==SkinMode.ROTATE && s.Mode.OffRequested,CanceledPending=null};
            if(!rotation.IsModeStateValid(m))return D(s,"recovery-current-proof-required");
            return D(new(m,LastRecovery:new(s with {LastRecovery=null},evidence)),"recovery-resolved");
        }
        private static bool OutcomeChild(RotationTransactionOperation p,RotationOutcomeFence fence,RegistryCommitReceipt receipt,
            VerifiedRegistryHead head,ActivationSnapshot target)
        {
            if(fence==null)return false;
            var t=p.Transaction;var e=t.Envelope;var arm=t.ArmCommitReceipt;var parentReceipt=t.FailureReceipt??arm;
            var parent=new VerifiedRegistryHead(parentReceipt.NewGenerationId,parentReceipt.NewGenerationSha256,e.Prior,t.Interlock);
            var late=fence.LateFailureReceipt;
            if(late!=null)
            {
                // One bounded learned hop; never replace or contradict a stored failure.
                if(t.Phase!=TransactionPhase.BLOCKED || t.FailureReceipt!=null || t.PendingClosure!=null ||
                    t.OriginalFailure==null || t.RollbackFailure==null || !ExactReceipt(late,arm.NewGenerationId,arm.NewGenerationSha256) ||
                    late.NewGenerationId==e.BaseGenerationId || late.NewGenerationSha256==e.BaseGenerationSha256)return false;
                parent=new(late.NewGenerationId,late.NewGenerationSha256,e.Prior,
                    t.Interlock with {State=InterlockState.ROLLBACK_FAILED,OriginalFailure=t.OriginalFailure,RollbackFailure=t.RollbackFailure});
            }
            return fence.Correlation==new TransactionCorrelation(e.TransactionId,e.Binding) && fence.OriginalHero==p.Origin.Rotation.CurrentHero &&
                fence.AuthoritativeParent==parent && ExactChild(receipt,head,parent.GenerationId,parent.GenerationSha256,target) &&
                head.GenerationId!=e.BaseGenerationId && head.GenerationSha256!=e.BaseGenerationSha256 &&
                head.GenerationId!=arm.NewGenerationId && head.GenerationSha256!=arm.NewGenerationSha256;
        }
        private static bool ProofMatches(HeroVerifiedVisual p,HeroBindingToken hero,SkinBindingToken skin,ActiveVisual visual)
        {
            if(p==null || p.Hero!=hero || p.Proof==null || p.Proof.Binding!=skin ||
                !RotationValues.Text(p.Hero.Value,256) || !RotationValues.Text(p.Proof.Binding.Value,256))return false;
            return p.Proof is VerifiedLiveVisualProof.Vanilla && visual is ActiveVisual.Vanilla ||
                p.Proof is VerifiedLiveVisualProof.Pack v && visual is ActiveVisual.Pack a && RotationValues.Pack(v.Visual) &&
                v.Visual.Id==a.Id && v.Visual.TreeSha256==a.TreeSha256 && v.Visual.ContentSha256==a.ContentSha256;
        }
        private static bool ExactReceipt(RegistryCommitReceipt r,string parentId,string parentHash)=>
            r!=null && Uuid(r.NewGenerationId) && RotationValues.Digest(r.NewGenerationSha256) &&
            r.ExpectedGenerationId==parentId && r.ExpectedGenerationSha256==parentHash &&
            r.NewGenerationId!=parentId && r.NewGenerationSha256!=parentHash;
        private static bool ExactChild(RegistryCommitReceipt r,VerifiedRegistryHead h,string parentId,string parentHash,ActivationSnapshot target)=>
            h!=null && ExactReceipt(r,parentId,parentHash) && h.GenerationId==r.NewGenerationId &&
            h.GenerationSha256==r.NewGenerationSha256 && h.Activation==target && h.Interlock==RotationInterlock.Clear();
        private static bool Uuid(string value)=>value!=null && System.Text.RegularExpressions.Regex.IsMatch(value,"\\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z");

        private RotationTransactionDecision Begin(RotationTransactionState s, RotationReadyIntent readiness)
        {
            if (s.Mode.OperationHighWater==long.MaxValue) return D(s,"operation-id-exhausted");
            if (s.Mode.Rotation.Activation.SkinStamp==long.MaxValue) return D(s,"skin-stamp-exhausted");
            var envelope=Envelope(s.Mode,readiness);
            if (envelope==null) return D(s,"transaction-target-unavailable");
            var decision=transaction.Decide(new TransactionState(Binding:envelope.Binding,Activation:envelope.Prior),new TransactionEvent.Begin(envelope));
            if (decision.State.Phase!=TransactionPhase.PREPARING) return D(s,decision.Diagnosis);
            var next=new RotationTransactionState(s.Mode with {OperationHighWater=s.Mode.OperationHighWater+1,LiveProof=null},
                new(s.Mode,readiness,decision.State),LastRecovery:s.LastRecovery);
            return new(next,decision.Diagnosis,decision.Commands.Select(c=>(RotationTransactionCommand)new RotationTransactionCommand.Transaction(s.Mode.Rotation.CurrentHero,c)));
        }

        public bool IsStateValid(RotationTransactionState s)
        {
            if (s==null || !rotation.IsModeStateValid(s.Mode)) return false;
            var audit=s.LastRecovery;
            // Check nesting before recursion: only one audit-free prior state is retained.
            if(audit!=null && (audit.Before==null || audit.Before.LastRecovery!=null || !IsStateValid(audit.Before) ||
                Recover(audit.Before,audit.Evidence).Diagnosis!="recovery-resolved" ||
                s.Mode.OperationHighWater<audit.Before.Mode.OperationHighWater ||
                s.Mode.Rotation.EpochHighWater.Value<audit.Before.Mode.Rotation.EpochHighWater.Value))return false;
            if (s.ReadinessReboundBlocked && (s.Operation!=null || s.Mode.Operation!=null ||
                s.Mode.Rotation.Pending?.Phase!=RotationPendingPhase.INTENT_ISSUED)) return false;
            var issued=s.Mode.Rotation.Pending?.IssuedIntent;
            if (s.Operation==null && issued!=null && !s.ReadinessReboundBlocked &&
                (issued.Hero!=s.Mode.Rotation.CurrentHero || issued.Skin!=s.Mode.Rotation.CurrentSkin)) return false;
            var p=s.Operation;if(p==null)return true;
            if (s.Mode.Rotation.CurrentHero==null || !rotation.IsModeStateValid(p.Origin) || p.Origin.Operation!=null || s.Mode.Operation!=null ||
                p.Origin.OperationHighWater==long.MaxValue || s.Mode.OperationHighWater!=p.Origin.OperationHighWater+1 ||
                !transaction.IsStateValid(p.Transaction) || p.Transaction.Phase==TransactionPhase.IDLE || p.Transaction.Phase==TransactionPhase.COMMITTED) return false;
            var envelope=Envelope(p.Origin,p.Readiness);
            if(envelope==null || envelope!=p.Transaction.Envelope)return false;
            var current=s.Mode.Rotation;
            if(!p.ReboundBlocked && (current.CurrentHero!=p.Origin.Rotation.CurrentHero || current.CurrentSkin!=p.Origin.Rotation.CurrentSkin))return false;
            // The only mutable H1 fields during a transaction are authoritative rebind,
            // proof invalidation, and a one-way OFF/disarm request. Parent/head never move.
            var expected=p.Origin with {OperationHighWater=s.Mode.OperationHighWater,LiveProof=null,
                OffRequested=p.Origin.OffRequested || s.Mode.OffRequested,
                Rotation=p.Origin.Rotation with {CurrentHero=current.CurrentHero,CurrentSkin=current.CurrentSkin,
                    Pending=p.Origin.Rotation.Pending == null ? null : p.Origin.Rotation.Pending with {Hero=current.CurrentHero,Skin=current.CurrentSkin}}};
            return expected==s.Mode;
        }

        private TransactionEnvelope Envelope(RotationModeState m,RotationReadyIntent readiness)
        {
            if(m.Rotation.CurrentHero==null || m.Operation!=null || m.OperationHighWater==long.MaxValue || m.Rotation.Activation.SkinStamp==long.MaxValue)return null;
            var a=m.Rotation.Activation;ActivationSnapshot target;SkinOperationKind kind;
            if(readiness!=null)
            {
                if(!Ready(m,readiness))return null;
                kind=SkinOperationKind.DEATH_ROTATION;
                target=a with {SelectedPackId=readiness.Candidate.CurrentObject.Id,Active=readiness.Candidate.CurrentObject,SkinStamp=a.SkinStamp+1};
            }
            else if(a.Mode==SkinMode.OFF)
            {
                var selected=m.Rotation.Ring.Entries.SingleOrDefault(x=>x.CurrentObject.Id==a.SelectedPackId);
                if(selected==null)return null;
                kind=SkinOperationKind.MODE_ON;
                target=a with {Mode=SkinMode.ON,Active=selected.CurrentObject,SkinStamp=a.SkinStamp+1};
            }
            else if(a.Mode==SkinMode.ROTATE && m.OffRequested && m.Rotation.Pending==null && !(m.LiveProof?.Proof is VerifiedLiveVisualProof.Vanilla))
            {
                kind=SkinOperationKind.MODE_OFF;
                target=a with {Mode=SkinMode.OFF,Active=new ActiveVisual.Vanilla(),SkinStamp=a.SkinStamp+1};
            }
            else return null;
            return new(OperationId(m.OperationHighWater+1),kind,m.Head.GenerationId,m.Head.GenerationSha256,a,target,m.Rotation.CurrentSkin,rotation.IsPriorEstablished(m));
        }
        private static bool Ready(RotationModeState m,RotationReadyIntent r)=>r!=null && !m.OffRequested && m.Operation==null &&
            m.Rotation.Pending?.Phase==RotationPendingPhase.INTENT_ISSUED && m.Rotation.Pending.IssuedIntent==r &&
            r.Hero==m.Rotation.CurrentHero && r.Skin==m.Rotation.CurrentSkin && r.Prior==m.Rotation.Activation;
        // Shares H1's monotonically increasing counter; prefix also distinguishes visual IDs.
        private static string OperationId(long n)
        {
            var hex=n.ToString("x16",System.Globalization.CultureInfo.InvariantCulture);
            return "10000000-0000-"+hex.Substring(0,4)+"-"+hex.Substring(4,4)+"-0000"+hex.Substring(8);
        }
        private static RotationTransactionDecision D(RotationTransactionState s,string diagnosis)=>new(s,diagnosis);
    }
}
