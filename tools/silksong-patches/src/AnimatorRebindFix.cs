// AnimatorRebindFix — general fix for the bundle-loaded Animator binding bug.
//
// On the Android/IL2CPP build, Animators that ship DISABLED inside an asset
// bundle (the common "pay to open / appear" props: bellway floor gates, toll
// machines, benches, doors, ...) come up WITHOUT their generic curve bindings
// built. When a script later does `animator.enabled = true; animator.Play(...)`
// the state machine advances but NO curves apply, so the object never moves —
// it only "snaps" to its saved end-state on the next room reload. Calling
// Animator.Rebind() rebuilds those bindings and the animation plays correctly.
//
// The committed PlayMaker `SetAnimator` patch fixes this for animators driven
// through that one PlayMaker action, but ~160 of the game's own classes (Gate,
// BellBench, ...) enable+play their animators DIRECTLY in C#, bypassing it. Rather
// than patch every call site, we fix it once, generically, here:
//
//   At every scene load we find all Animators that are NOT currently displaying
//   (component disabled, or sitting on an inactive GameObject) — exactly the
//   disabled-at-load bug population — and Rebind() them while they are still idle,
//   so their bindings are ready BEFORE any prop enables + plays them.
//
//   We ALSO re-run after a hazard respawn. A hazard respawn does NOT reload the
//   scene (no sceneLoaded), but it routes through the scene-entry flow
//   (GameManager.OnFinishedEnteringScene) which re-activates scene objects. Props
//   that gate their own one-time init on a flag (e.g. SandCentipede's `warmup`)
//   skip it on re-enable and come back up UNBOUND — the "Blasted Steps sand
//   centipedes freeze after you get hit and respawn" bug. Re-binding the idle
//   animators on OnFinishedEnteringScene restores them (the centipede disables
//   its animator between pop-ups, so an idle pass catches it). We clear the
//   per-instance dedupe first because, unlike a real scene load, the objects keep
//   their instance ids across a respawn.
//
// Safety: we deliberately SKIP animators that are already enabled AND active
// (i.e. currently on screen), so we never reset a running animation to its default
// pose. Rebinding an idle / hidden animator has no visible effect. There is no
// per-frame cost: the only work is a short scan on each sceneLoaded / scene-entry,
// plus a few delayed re-scans to catch props that disable their animator in
// Start() (which runs after sceneLoaded) or that spawn a moment after the room
// loads.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AnimatorRebindFix : MonoBehaviour
{
    // Rebind each animator at most once (tracked by instance id). Scene animators
    // get fresh ids per load, so this only suppresses redundant work within a
    // single loaded set, never across genuinely-new objects.
    static readonly HashSet<int> _rebound = new HashSet<int>();

    // The GameManager whose OnFinishedEnteringScene we're currently subscribed to
    // (re-subscribed if the singleton is ever recreated, e.g. after a menu trip).
    GameManager _subscribedGm;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Bootstrap()
    {
        var go = new GameObject("AnimatorRebindFix");
        DontDestroyOnLoad(go);
        go.AddComponent<AnimatorRebindFix>();
    }

    void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_subscribedGm != null)
        {
            _subscribedGm.OnFinishedEnteringScene -= OnFinishedEnteringScene;
            _subscribedGm = null;
        }
    }

    // Whatever is already loaded when we come up.
    void Start()
    {
        StartCoroutine(RebindPasses());
        StartCoroutine(KeepSceneEntrySubscription());
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) { StartCoroutine(RebindPasses()); }

    // Keep ourselves subscribed to the current GameManager's scene-entry event.
    // GameManager is a DontDestroyOnLoad singleton, but can be recreated across a
    // return-to-menu, so we re-check and re-bind to the live instance.
    IEnumerator KeepSceneEntrySubscription()
    {
        var wait = new WaitForSeconds(2f);
        while (true)
        {
            var gm = GameManager.UnsafeInstance;
            if (gm != null && gm != _subscribedGm)
            {
                if (_subscribedGm != null)
                    _subscribedGm.OnFinishedEnteringScene -= OnFinishedEnteringScene;
                gm.OnFinishedEnteringScene += OnFinishedEnteringScene;
                _subscribedGm = gm;
            }
            yield return wait;
        }
    }

    // Fires on every scene entry INCLUDING hazard respawn (which doesn't raise
    // sceneLoaded). Clear the dedupe so animators that kept their instance ids
    // across the respawn get re-bound, then run a longer burst of idle passes to
    // catch props during whatever idle window they re-init into.
    void OnFinishedEnteringScene()
    {
        _rebound.Clear();
        StartCoroutine(EntryRebindPasses());
    }

    // A burst of passes: immediately, next frame (after scene objects' Start() has
    // run, in case a prop disables its animator there), then a couple of delayed
    // ones to catch animators spawned just after the room loads.
    IEnumerator RebindPasses()
    {
        RebindIdleAnimators();
        yield return null;
        RebindIdleAnimators();
        yield return new WaitForSeconds(0.5f);
        RebindIdleAnimators();
        yield return new WaitForSeconds(1.5f);
        RebindIdleAnimators();
    }

    // Longer-tailed burst for scene entry / hazard respawn: props like the sand
    // centipedes only sit idle (animator disabled) between intermittent pop-ups,
    // so we keep re-scanning for several seconds to catch each one while idle and
    // rebind it before its next pop-up.
    IEnumerator EntryRebindPasses()
    {
        RebindIdleAnimators();
        yield return null;
        RebindIdleAnimators();
        float[] delays = { 0.3f, 0.6f, 1.0f, 1.5f, 2.5f, 4.0f };
        for (int i = 0; i < delays.Length; i++)
        {
            yield return new WaitForSeconds(delays[i] - (i == 0 ? 0f : delays[i - 1]));
            RebindIdleAnimators();
        }
    }

    static void RebindIdleAnimators()
    {
        var anims = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int n = 0;
        for (int i = 0; i < anims.Length; i++)
        {
            var a = anims[i];
            if (a == null || a.runtimeAnimatorController == null) continue;
            // Only touch animators that are NOT currently displaying — the
            // disabled-at-load bug population. Never reset a running animation.
            if (a.enabled && a.gameObject.activeInHierarchy) continue;
            if (!_rebound.Add(a.GetInstanceID())) continue;   // once per animator
            a.Rebind();
            n++;
        }
        if (n > 0) Debug.Log("[AnimatorRebindFix] rebound " + n + " idle bundle animators");
    }
}
#endif
