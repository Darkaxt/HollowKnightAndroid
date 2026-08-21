// WormAnimatorFix — runtime fix for the off-camera "frozen sand worm" bug in
// Blasted Steps, added as a patch rather than by editing the game's own code.
//
// Two ambient props drive bundle-loaded Mecanim Animators that come up WITHOUT
// (or lose) their generic curve bindings on the Android/IL2CPP build once they
// have spent time off-camera:
//
//   • SandCentipede        — the background worms that pop up out of the sand on a
//                            timer (animator disabled between pop-ups).
//   • RangeAttacker        — the foreground "Sand Centipede Attacker" worms that
//                            appear when the hero is near, loop, then disappear
//                            (animator disabled while "away").
//
// When the prop re-enables + Plays, the clip's state machine advances but NO
// curves apply, so the worm reappears frozen. The committed AnimatorRebindFix only
// rebinds animators that are IDLE at scene-load / hazard-respawn, so these
// dynamically-(re)played props — which (re)play long after those passes, and whose
// GameObjects stay active — slip through it.
//
// This watcher catches the exact frame each tracked worm's animator transitions
// DISABLED → ENABLED (its (re)play moment) and rebuilds the binding THEN, while
// PRESERVING the state the game just set: it captures the current state + time,
// Rebind()s (which would otherwise snap the animator back to its entry state — the
// regression that, on a stateful animator like the bell-bench toll machine, would
// replay an already-paid animation), then re-Plays that exact state at that time
// and samples once. So a healthy animator is left visually untouched and a frozen
// one is restored.
//
// SandCentipede additionally gets cullingMode = AlwaysAnimate, because its pop-up
// can land just off the render frustum (within its spawn bounds buffer but off
// screen) where the stock CullCompletely would stop the animator evaluating at all
// (normalizedTime stuck, sprite null) even with a freshly-rebuilt binding.
// RangeAttacker is left on its own culling (it deliberately uses CullCompletely for
// its idle loop) — it appears on-camera, so only the rebind is needed.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WormAnimatorFix : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Bootstrap()
    {
        var go = new GameObject("WormAnimatorFix");
        DontDestroyOnLoad(go);
        go.AddComponent<WormAnimatorFix>();
    }

    sealed class Tracked
    {
        public Animator animator;
        public bool wasEnabled;
        public bool forceAlwaysAnimate;
    }

    readonly List<Tracked> _tracked = new List<Tracked>();
    readonly HashSet<Animator> _known = new HashSet<Animator>();
    float _nextScan;

    void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    void Start() { Rescan(); }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // New object set after a room change: forget the old animators.
        _tracked.Clear();
        _known.Clear();
        Rescan();
    }

    void Rescan()
    {
        Collect<SandCentipede>(forceAlwaysAnimate: true);
        Collect<RangeAttacker>(forceAlwaysAnimate: false);
    }

    void Collect<T>(bool forceAlwaysAnimate) where T : Component
    {
        var comps = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            var a = c.GetComponentInChildren<Animator>(true);
            if (a == null || a.runtimeAnimatorController == null) continue;
            if (!_known.Add(a)) continue;   // already tracked
            if (forceAlwaysAnimate) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _tracked.Add(new Tracked
            {
                animator = a,
                wasEnabled = a.enabled,
                forceAlwaysAnimate = forceAlwaysAnimate,
            });
        }
    }

    void LateUpdate()
    {
        // Cheap periodic rescan to pick up props streamed in after the initial load.
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 3f;
            Rescan();
        }

        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            var t = _tracked[i];
            var a = t.animator;
            if (a == null)
            {
                _tracked.RemoveAt(i);
                continue;
            }
            bool enabledNow = a.enabled;
            if (enabledNow && !t.wasEnabled)
            {
                // The animator was just (re)enabled this frame; the game has already
                // set its target state during Update. Rebuild the (possibly decayed)
                // bundle binding without disturbing that state.
                if (t.forceAlwaysAnimate)
                    a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                RebindPreservingState(a);
            }
            t.wasEnabled = enabledNow;
        }
    }

    // Rebind() rebuilds the generic curve bindings but resets the animator to its
    // default/entry state. Capture the live state on every layer first, Rebind, then
    // restore the captured state at its captured time and sample once so the correct
    // pose is applied immediately (no visible frame of the entry state).
    static void RebindPreservingState(Animator a)
    {
        int layers = a.layerCount;
        var stateHash = new int[layers];
        var normTime = new float[layers];
        for (int l = 0; l < layers; l++)
        {
            AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(l);
            stateHash[l] = st.fullPathHash;
            normTime[l] = st.normalizedTime;
        }

        a.Rebind();

        for (int l = 0; l < layers; l++)
        {
            if (stateHash[l] != 0)
                a.Play(stateHash[l], l, normTime[l]);
        }
        a.Update(0f);
    }
}
#endif
