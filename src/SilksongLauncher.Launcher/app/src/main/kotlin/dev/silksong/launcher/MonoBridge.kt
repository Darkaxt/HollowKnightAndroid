// MonoBridge — the JNI door into the .NET runtime.
//
// Everything interesting is on the other side, in monojni.c. What matters
// here is which libraries are loaded, in what order, and by whom.
//
// System.loadLibrary is not interchangeable with the runtime's own dlopen.
// Android calls JNI_OnLoad on a library it loads this way and hands it the
// process's JavaVM; nothing calls JNI_OnLoad on a library that arrives
// through dlopen or through a P/Invoke. That distinction is the whole reason
// this class exists: .NET implements cryptography on Android by calling
// Java's javax.crypto through JNI, so libSystem.Security.Cryptography.Native
// .Android.so has to be loaded the Java way, before any managed code asks for
// a hash. Roslyn asks on the first source file it parses.
//
// Loaded from MonoService, which runs in a builder process. Loading it
// anywhere else would put the runtime in the launcher's own process.

package dev.silksong.launcher

object MonoBridge {

    @Volatile private var loaded = false

    /**
     * Loads the runtime's native halves into this process.
     *
     * The crypto shim is the one that must come through here rather than be
     * left to the runtime, and it is loaded eagerly for that reason -- the
     * alternative is a null JavaVM inside a P/Invoke and a SIGSEGV with a
     * managed stack trace that points at SHA256.Create.
     *
     * The others are ordinary dependencies; the linker would find them, and
     * naming them only makes a missing one fail here, with a name, instead of
     * somewhere further in.
     */
    @Synchronized
    fun load() {
        if (loaded) return
        System.loadLibrary("System.Security.Cryptography.Native.Android")
        System.loadLibrary("monojni")
        loaded = true
    }

    /**
     * Runs a .NET assembly to completion and returns its exit code.
     *
     * Blocks the calling thread for as long as the program runs, which for
     * il2cpp is minutes. Everything the program writes to stdout and stderr
     * is redirected to [outPath], which the launcher tails; a pipe would be
     * the natural choice, but the descriptor cannot reach this process --
     * the Intent that starts the service is refused by the activity manager
     * if it carries one.
     *
     * [env] is flattened to alternating keys and values: a Map crossing JNI
     * is a great deal of GetObjectClass for something the C side wants as a
     * pair of strings.
     */
    external fun nativeRun(
        assembly: String,
        args: Array<String>,
        cwd: String?,
        bcl: String,
        runtimeDir: String,
        env: Array<String>,
        outPath: String,
        resultPath: String,
    ): Int
}
