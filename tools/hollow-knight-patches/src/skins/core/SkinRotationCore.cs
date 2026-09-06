using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Task64 selector only. Task65 owns mode/proof/transaction composition and correlated retirement.
    // INTENT_ISSUED is deliberately parked: no new death, second delivery or outcome authority here.
    public sealed class SkinRotationCore
    {
        // H1 emits mode-only CAS values. Task66 delegates visual execution to TransactionCore.
        public RotationModeDecision Decide(RotationModeState s, RotationModeEvent signal)
        {
            if (!IsModeStateValid(s)) return Mode(s,"invalid-state");
            if (signal == null) return Mode(s,"invalid-event");
            if (signal is RotationModeEvent.Selector selector)
            {
                if (!ValidEvent(selector.Event)) return Mode(s,"invalid-event");
                if (selector.Event is RotationEvent.Rebind)
                {
                    var next=Decide(s.Rotation,selector.Event).State;
                    if(next==s.Rotation)return Mode(s,"same-binding");
                    return Mode(s with {Rotation=next,LiveProof=null,Operation=s.Operation == null ? null : s.Operation with {ReboundBlocked=true}},"rebound");
                }
                if(s.OffRequested || s.Operation!=null)return Mode(s,"mode-busy");
                var result=Decide(s.Rotation,selector.Event);
                return Mode(s with {Rotation=result.State},result.Diagnosis);
            }
            if(signal is RotationModeEvent.VerifiedVisual verified)
            {
                if(!ProofValid(verified.Value) || verified.Value.Hero!=s.Rotation.CurrentHero || verified.Value.Proof.Binding!=s.Rotation.CurrentSkin)return Mode(s,"stale-proof");
                if(s.Operation!=null)return Mode(s,"mode-busy");
                return Mode(s with {LiveProof=verified.Value},"visual-verified");
            }
            if(signal is RotationModeEvent.AdvanceMode)return Advance(s);
            var p=s.Operation;if(p==null)return Mode(s,"no-mode-operation");
            var correlation=signal switch {
                RotationModeEvent.ModeCommitted e=>e.Correlation,
                RotationModeEvent.ModeIndeterminate e=>e.Correlation,
                RotationModeEvent.ModeFailed e=>e.Correlation,
                RotationModeEvent.RetryModeCommit e=>e.Correlation,
                _=>null
            };
            if(correlation==null || correlation!=p.Correlation)return Mode(s,"stale-correlation");
            if(p.ReboundBlocked)return Mode(s,"rebound-resolution-required");
            switch(signal)
            {
                case RotationModeEvent.ModeCommitted complete:
                    var r=complete.Receipt;var h=complete.Head;
                    if(r==null || !Uuid(r.NewGenerationId) || !RotationValues.Digest(r.NewGenerationSha256) ||
                        r.ExpectedGenerationId!=p.BaseHead.GenerationId || r.ExpectedGenerationSha256!=p.BaseHead.GenerationSha256 ||
                        r.NewGenerationId==r.ExpectedGenerationId || r.NewGenerationSha256==r.ExpectedGenerationSha256 ||
                        !HeadValid(h) || h.GenerationId!=r.NewGenerationId || h.GenerationSha256!=r.NewGenerationSha256 || h.Activation!=p.Target)
                        return Mode(s,"stale-receipt");
                    return Mode(s with {Rotation=s.Rotation with {Activation=p.Target},Head=h,Operation=null,OffRequested=false,CanceledPending=null},"mode-committed");
                case RotationModeEvent.ModeIndeterminate _:
                    return Mode(s with {Operation=p with {Phase=ModeCommitPhase.INDETERMINATE}},"mode-indeterminate");
                case RotationModeEvent.ModeFailed _:
                    return Mode(s with {Operation=p with {Phase=ModeCommitPhase.DEFINITIVE_FAILURE}},"mode-failed");
                case RotationModeEvent.RetryModeCommit _:
                    return Mode(s,"retry-exact-cas",Command(p));
                default:return Mode(s,"invalid-event");
            }
        }
        private static RotationModeDecision Advance(RotationModeState initial)
        {
            if(initial.Operation!=null)return Mode(initial,"mode-busy");
            if(initial.Rotation.CurrentHero==null)return Mode(initial,"unbound");
            var a=initial.Rotation.Activation;
            if(a.Mode==SkinMode.OFF)
            {
                if(a.SelectedPackId==null)return Mode(initial,"NO_SELECTED_SKIN");
                var selected=initial.Rotation.Ring.Entries.SingleOrDefault(x=>x.CurrentObject.Id==a.SelectedPackId);
                if(selected==null)return Mode(initial,"selected-object-unavailable");
                if(!SelectedEstablished(initial.LiveProof,a.Active,selected.CurrentObject))return Mode(initial,"transaction-required");
                if(initial.OperationHighWater==long.MaxValue)return Mode(initial,"operation-id-exhausted");
                var onId=initial.OperationHighWater+1;
                // No visual write: preserve stamp, but publish the exact CURRENT import identity.
                var on=new ModeOperation(new(OperationId(onId),initial.Rotation.CurrentHero,initial.Rotation.CurrentSkin),
                    initial.Head,a with {Mode=SkinMode.ON,Active=selected.CurrentObject},SelectedProof:initial.LiveProof);
                return Mode(initial with {OperationHighWater=onId,Operation=on},"commit-mode",Command(on));
            }
            var s=initial;
            if(a.Mode==SkinMode.ROTATE)
            {
                var pending=s.Rotation.Pending;s=s with {OffRequested=true};
                if(pending?.Phase==RotationPendingPhase.INTENT_ISSUED)return Mode(s,"issued-rotation-busy");
                if(pending!=null)s=s with {Rotation=s.Rotation with {Pending=null},CanceledPending=pending};
                if(!(s.LiveProof?.Proof is VerifiedLiveVisualProof.Vanilla))return Mode(s,"transaction-required");
            }
            if(s.OperationHighWater==long.MaxValue)return Mode(s,"operation-id-exhausted");
            var next=s.OperationHighWater+1;
            var p=new ModeOperation(new(OperationId(next),s.Rotation.CurrentHero,s.Rotation.CurrentSkin),s.Head,
                a with {Mode=a.Mode==SkinMode.ON ? SkinMode.ROTATE : SkinMode.OFF},a.Mode==SkinMode.ROTATE ? s.LiveProof : null);
            return Mode(s with {OperationHighWater=next,Operation=p},"commit-mode",Command(p));
        }
        // Validates the entire composed state; this is NOT an Apply permission query.
        public bool IsModeStateValid(RotationModeState s)
        {
            if(s==null || !Valid(s.Rotation) || !HeadValid(s.Head) || s.Head.Activation!=s.Rotation.Activation || s.OperationHighWater<0)return false;
            if(s.LiveProof!=null && (!ProofValid(s.LiveProof) || s.LiveProof.Hero!=s.Rotation.CurrentHero || s.LiveProof.Proof.Binding!=s.Rotation.CurrentSkin))return false;
            if(s.OffRequested && s.Rotation.Activation.Mode!=SkinMode.ROTATE)return false;
            var canceled=s.CanceledPending;
            if(canceled!=null && (!s.OffRequested || s.Rotation.Pending!=null || canceled.Phase!=RotationPendingPhase.AWAITING_STABILITY ||
                !Valid(s.Rotation with {CurrentHero=canceled.Hero,CurrentSkin=canceled.Skin,Pending=canceled})))return false;
            if(s.OffRequested && s.Rotation.Pending?.Phase==RotationPendingPhase.AWAITING_STABILITY)return false;
            var p=s.Operation;if(p==null)return true;
            if(s.Rotation.CurrentHero==null || s.Rotation.Pending!=null || s.OperationHighWater==0 || p.Correlation==null ||
                p.Correlation.OperationId!=OperationId(s.OperationHighWater) || !Hero(p.Correlation.Hero) || !Skin(p.Correlation.Skin) ||
                p.BaseHead!=s.Head || !RotationValues.Activation(p.Target) ||
                (p.Phase!=ModeCommitPhase.ISSUED && p.Phase!=ModeCommitPhase.INDETERMINATE && p.Phase!=ModeCommitPhase.DEFINITIVE_FAILURE))return false;
            if(!p.ReboundBlocked && (p.Correlation.Hero!=s.Rotation.CurrentHero || p.Correlation.Skin!=s.Rotation.CurrentSkin))return false;
            if(p.ReboundBlocked && s.LiveProof!=null)return false;
            if(!p.ReboundBlocked && p.VanillaProof!=null && s.LiveProof!=p.VanillaProof)return false;
            if(!p.ReboundBlocked && p.SelectedProof!=null && s.LiveProof!=p.SelectedProof)return false;
            var a=s.Rotation.Activation;
            if(a.Mode!=SkinMode.OFF && p.SelectedProof!=null)return false;
            var selected=s.Rotation.Ring.Entries.SingleOrDefault(x=>x.CurrentObject.Id==a.SelectedPackId);
            return a.Mode switch {
                SkinMode.OFF => selected!=null && !s.OffRequested && p.VanillaProof==null &&
                    p.Target==(a with {Mode=SkinMode.ON,Active=selected.CurrentObject}) &&
                    SelectedEstablished(p.SelectedProof,a.Active,selected.CurrentObject) &&
                    p.SelectedProof.Hero==p.Correlation.Hero && p.SelectedProof.Proof.Binding==p.Correlation.Skin,
                SkinMode.ON => !s.OffRequested && p.VanillaProof==null && p.Target==(a with {Mode=SkinMode.ROTATE}),
                SkinMode.ROTATE => s.OffRequested && p.Target==(a with {Mode=SkinMode.OFF}) && ProofValid(p.VanillaProof) &&
                    p.VanillaProof.Proof is VerifiedLiveVisualProof.Vanilla && p.VanillaProof.Hero==p.Correlation.Hero && p.VanillaProof.Proof.Binding==p.Correlation.Skin,
                _=>false
            };
        }
        public bool IsPriorEstablished(RotationModeState s)
        {
            if(!IsModeStateValid(s))return false;
            var proof=s.LiveProof?.Proof;var active=s.Rotation.Activation.Active;
            return proof is VerifiedLiveVisualProof.Vanilla && active is ActiveVisual.Vanilla ||
                proof is VerifiedLiveVisualProof.Pack p && active is ActiveVisual.Pack a &&
                p.Visual.Id==a.Id && p.Visual.TreeSha256==a.TreeSha256 && p.Visual.ContentSha256==a.ContentSha256;
        }
        private static bool SelectedEstablished(HeroVerifiedVisual p,ActiveVisual prior,ActiveVisual.Pack target)=>
            ProofValid(p) && p.Proof is VerifiedLiveVisualProof.Pack proof && prior is ActiveVisual.Pack a &&
            SamePackVisual(proof.Visual,a) && SamePackVisual(proof.Visual,target);
        private static bool SamePackVisual(ActiveVisual.Pack a,ActiveVisual.Pack b)=>
            a.Id==b.Id && a.TreeSha256==b.TreeSha256 && a.ContentSha256==b.ContentSha256;
        private static bool ProofValid(HeroVerifiedVisual p)=>p!=null && Hero(p.Hero) && p.Proof!=null && Skin(p.Proof.Binding) &&
            (p.Proof is VerifiedLiveVisualProof.Vanilla || p.Proof is VerifiedLiveVisualProof.Pack v && RotationValues.Pack(v.Visual));
        private static bool HeadValid(VerifiedRegistryHead h)=>h!=null && Uuid(h.GenerationId) && RotationValues.Digest(h.GenerationSha256) &&
            RotationValues.Activation(h.Activation) && h.Interlock==RotationInterlock.Clear();
        private static bool Uuid(string s)=>s!=null && System.Text.RegularExpressions.Regex.IsMatch(s,"\\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z");
        private static string OperationId(long n)
        {
            var hex=n.ToString("x16",System.Globalization.CultureInfo.InvariantCulture);
            return "00000000-0000-"+hex.Substring(0,4)+"-"+hex.Substring(4,4)+"-0000"+hex.Substring(8);
        }
        private static RotationModeCommand Command(ModeOperation p)
        {
            var issued=p with {Phase=ModeCommitPhase.ISSUED};
            return p.Target.Mode==SkinMode.ON ? new RotationModeCommand.CommitVerifiedSelectedOn(issued) :
                p.Target.Mode==SkinMode.ROTATE ? new RotationModeCommand.CommitOnToRotate(issued) : new RotationModeCommand.CommitVerifiedVanillaOff(issued);
        }
        private static RotationModeDecision Mode(RotationModeState s,string diagnosis,params RotationModeCommand[] commands)=>new(s,diagnosis,commands);

        public RotationDecision Decide(RotationState state, RotationEvent signal)
        {
            if (!Valid(state)) return D(state,"invalid-state");
            if (!ValidEvent(signal)) return D(state,"invalid-event");
            if (signal is RotationEvent.Rebind binding)
            {
                if(binding.Hero==state.CurrentHero && binding.Skin==state.CurrentSkin) return D(state,"same-binding");
                return D(state with {CurrentHero=binding.Hero,CurrentSkin=binding.Skin,
                    Pending=state.Pending == null ? null : state.Pending with {Hero=binding.Hero,Skin=binding.Skin}},"rebound");
            }
            if(state.CurrentHero==null) return D(state,"unbound");
            return signal switch
            {
                RotationEvent.ConfirmDeath e => Confirm(state,e),
                RotationEvent.StableRespawn e => Stable(state,e.Token),
                _ => D(state,"invalid-event")
            };
        }
        private static RotationDecision Confirm(RotationState s, RotationEvent.ConfirmDeath e)
        {
            if(e.Hero!=s.CurrentHero || e.Skin!=s.CurrentSkin) return D(s,"stale-binding");
            if(s.Activation.Mode!=SkinMode.ROTATE) return D(s,"rotation-inactive");
            if(e.Epoch.Value==0 || e.Epoch.Value<=s.EpochHighWater.Value) return D(s,"consumed-epoch");
            if(s.Pending!=null) return D(s,"pending-rotation");
            var candidate=Successor(s);if(candidate==null) return D(s,"insufficient-eligible");
            return D(s with {EpochHighWater=e.Epoch,Pending=new PendingRotation(candidate,e.Epoch,e.Hero,e.Skin)},"candidate-confirmed");
        }
        private static RotationDecision Stable(RotationState s,StableRespawnToken token)
        {
            if(token.Hero!=s.CurrentHero || token.Skin!=s.CurrentSkin) return D(s,"stale-binding");
            var p=s.Pending;if(p==null) return D(s,"no-pending");
            if(token.DeathEpoch!=p.Epoch) return D(s,"stale-epoch");
            if(p.Phase==RotationPendingPhase.INTENT_ISSUED) return D(s,"intent-already-issued");
            var intent=new RotationReadyIntent(p.Epoch,p.Hero,p.Skin,p.Candidate,s.Activation);
            return new RotationDecision(s with {Pending=p with {Phase=RotationPendingPhase.INTENT_ISSUED,IssuedIntent=intent}},"rotation-ready",intent);
        }
        private static RotationDescriptor Successor(RotationState s)
        {
            var eligible=s.Ring.Entries.Where(x=>x.Eligible).ToList();if(eligible.Count<=1)return null;
            var active=(s.Activation.Active as ActiveVisual.Pack)?.Id;
            var anchor=eligible.FindIndex(x=>x.CurrentObject.Id==active);
            if(anchor<0)anchor=eligible.FindIndex(x=>x.CurrentObject.Id==s.Activation.SelectedPackId);
            return eligible[(anchor+1)%eligible.Count];
        }
        private static bool Valid(RotationState s)
        {
            if(s==null || s.Ring==null || s.EpochHighWater==null || !RotationValues.Activation(s.Activation) ||
                (s.CurrentHero==null)!=(s.CurrentSkin==null))return false;
            if(s.CurrentHero==null)return s.Pending==null && s.EpochHighWater.Value==0;
            if(!Hero(s.CurrentHero)||!Skin(s.CurrentSkin))return false;
            var p=s.Pending;if(p==null)return true;
            if(s.Activation.Mode!=SkinMode.ROTATE || p.Epoch==null || p.Epoch.Value==0 || p.Epoch!=s.EpochHighWater ||
                p.Hero!=s.CurrentHero || p.Skin!=s.CurrentSkin || p.Candidate==null || p.Candidate!=Successor(s))return false;
            return p.Phase switch
            {
                RotationPendingPhase.AWAITING_STABILITY => p.IssuedIntent==null,
                RotationPendingPhase.INTENT_ISSUED => p.IssuedIntent!=null && p.IssuedIntent.Epoch==p.Epoch &&
                    p.IssuedIntent.Candidate==p.Candidate && p.IssuedIntent.Prior==s.Activation &&
                    Hero(p.IssuedIntent.Hero) && Skin(p.IssuedIntent.Skin),
                _ => false
            };
        }
        private static bool Hero(HeroBindingToken h)=>h!=null && RotationValues.Text(h.Value,256);
        private static bool Skin(SkinBindingToken s)=>s!=null && RotationValues.Text(s.Value,256);
        private static bool ValidEvent(RotationEvent e)=>e switch
        {
            RotationEvent.Rebind b=>Hero(b.Hero)&&Skin(b.Skin),
            RotationEvent.ConfirmDeath d=>d.Epoch!=null&&Hero(d.Hero)&&Skin(d.Skin),
            RotationEvent.StableRespawn r=>r.Token!=null&&r.Token.DeathEpoch!=null&&Hero(r.Token.Hero)&&Skin(r.Token.Skin),
            _=>false
        };
        private static RotationDecision D(RotationState s,string diagnosis)=>new(s,diagnosis);
    }
}
