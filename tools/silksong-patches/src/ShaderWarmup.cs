// ShaderWarmup — eliminates the 0.5-1 s per-shader-on-first-use
// hitches that show up the first time a scene draws something with
// a new shader. On Adreno's Vulkan driver every shader pipeline
// gets compiled lazily on first draw: when a scene transition
// loads a new bundle with previously-unused shaders, the first
// frame that touches them blocks while the driver builds the
// pipeline state object (PSO), and the player sees a stutter
// (sometimes a long enough one for the framerate counter to dip
// into the teens).
//
// `Shader.WarmupAllShaders()` walks every Shader Unity currently
// has loaded and forces the driver to build PSOs for them up
// front. Doing it ONCE after each scene load front-loads the cost
// into the load transition — where a brief pause is already
// expected — and trades for smooth gameplay afterwards.
//
// We don't try to be clever about deduplication: subsequent
// WarmupAllShaders calls on already-compiled shaders are cheap
// (the Adreno driver checks its PSO cache and returns instantly).
// The expensive work only happens when a shader is genuinely new.
//
// Defers one frame via a coroutine so the warmup runs AFTER scene
// rendering has actually wired up — calling it inside the
// sceneLoaded callback can race with addressables that finish
// loading mid-frame and miss some shaders.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ShaderWarmup : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // The frame rate is ResolutionConfigurator's business; it runs earlier
        // (BeforeSceneLoad) and owns display settings generally.
        var go = new GameObject("__ShaderWarmup__");
        DontDestroyOnLoad(go);
        go.AddComponent<ShaderWarmup>();
        Debug.Log("[ShaderWarmup] registered");
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        StartCoroutine(WarmAfterFrame(s.name));
    }

    static IEnumerator WarmAfterFrame(string sceneName)
    {
        // Defer one frame so the scene's active renderers have
        // initialised and any "Activate on load = true"
        // addressable bundles have hit the engine.
        yield return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Shader.WarmupAllShaders();
        var ms = sw.ElapsedMilliseconds;
        // Only log when it took something noticeable — quiet
        // by default so the on-screen log panel + logcat aren't
        // spammed with "0ms" lines after the first warmup
        // pre-builds everything.
        if (ms > 20)
            Debug.Log($"[ShaderWarmup] {sceneName}: WarmupAllShaders {ms}ms");
    }
}
#endif
