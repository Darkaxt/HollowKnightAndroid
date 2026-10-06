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
    internal bool Transitioning;
    internal AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) => State;
    internal AnimatorStateInfo GetNextAnimatorStateInfo(int layer) => Next;
    internal AnimatorTransitionInfo GetAnimatorTransitionInfo(int layer) => Transition;
    internal bool IsInTransition(int layer) => Transitioning;
}
internal sealed class tk2dBaseSprite:MonoBehaviour
{
    internal int spriteId;
    internal object Collection;
    internal Vector3 scale=Vector3.one;
}
