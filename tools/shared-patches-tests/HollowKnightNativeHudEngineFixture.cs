#pragma warning disable CS0649, CS0414
namespace HkPauseContracts;

// Native advancement tokens are engine inputs, not a second HUD fit policy.
internal sealed class ParticleSystem:MonoBehaviour
{
    internal float time;
    internal int particleCount;
    internal bool isPlaying;
}
internal sealed class ParticleSystemRenderer:Renderer { }
internal struct AnimatorStateInfo { internal float normalizedTime; internal int fullPathHash; }
internal struct AnimatorTransitionInfo { internal float normalizedTime; }
internal sealed class Animator:MonoBehaviour
{
    internal int layerCount=1;
    internal AnimatorStateInfo State,Next;
    internal AnimatorTransitionInfo Transition;
    internal bool Transitioning, Retired;
    internal int CurrentReads, NextReads, TransitionReads, MovingReads;
    internal AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) { CurrentReads++; return State; }
    internal AnimatorStateInfo GetNextAnimatorStateInfo(int layer) { NextReads++; return Next; }
    internal AnimatorTransitionInfo GetAnimatorTransitionInfo(int layer) { TransitionReads++; return Transition; }
    internal bool IsInTransition(int layer) { MovingReads++; return Transitioning; }
    public static bool operator ==(Animator a,Animator b)=>
        (ReferenceEquals(a,null)||a.Retired)?ReferenceEquals(b,null)||b.Retired:ReferenceEquals(a,b);
    public static bool operator !=(Animator a,Animator b)=>!(a==b);
    public override bool Equals(object o)=>ReferenceEquals(this,o);
    public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
internal sealed class tk2dBaseSprite:MonoBehaviour
{
    internal int spriteId;
    internal object Collection;
    internal Vector3 scale=Vector3.one;
}
