// monojni — the .NET host, as a library loaded into a process that has a JVM.
//
// This began as monohost, an ordinary executable, and the reason it is not one
// any more is worth writing down.
//
// The launcher runs three .NET programs: il2cpp.dll, Roslyn's csc.dll, and the
// bundle surgery tool. Android has no .NET, and carrying Debian's glibc beside
// Microsoft's linux-arm64 runtime -- the obvious answer, and what this used to
// do -- cannot work on a current Android: every app runs under a seccomp
// filter generated from the syscalls bionic itself makes, and it answers
// anything else with SIGSYS rather than ENOSYS, so a modern glibc dies
// registering rseq before reaching main() and prints nothing.
//
// Microsoft's Mono build for android-arm64 has no such problem; it is bionic's
// syscalls all the way down. But it is built for Android in a stronger sense
// than "compiled against bionic": .NET on Android implements
// System.Security.Cryptography by calling Java's javax.crypto through JNI.
// libSystem.Security.Cryptography.Native.Android.so receives its JavaVM in
// JNI_OnLoad, which only runs when Java's System.loadLibrary loads it. In a
// plain exec'd process there is no JavaVM, nothing calls JNI_OnLoad, and the
// first hash lands on a null pointer -- which is where a subprocess host died,
// inside SHA256.Create, on the checksum Roslyn takes of every source file.
//
// So the runtime has to live in a process that has a JVM, which on Android
// means an app process. It gets one of its own -- see MonoService and the two
// builder processes in the manifest -- rather than the launcher's, because the
// isolation a subprocess gave away for free is worth keeping:
//
//   il2cpp calls Environment.Exit, which ends the process it is running in.
//   In a builder that is a process whose whole purpose is to be ended.
//
//   il2cpp resolves parts of its own installation relative to the working
//   directory, and chdir() is process-wide.
//
//   The runtime cannot be initialised twice in a process, and there are four
//   programs to run. The builder is killed after each one, so each starts
//   clean.
//
// What is left here is the part hostfxr and hostpolicy would do on a desktop:
// find the runtime, decide which assemblies it may load, and start it. There
// is no hostfxr for Android to defer to -- Microsoft ships no apphost for the
// platform, and the one host that does exist, libnet-android.so, is a JNI
// library with no main() and no way to be told to run an arbitrary assembly.

#include <dirent.h>
#include <dlfcn.h>
#include <fcntl.h>
#include <jni.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define LOG_TAG "monojni"
#include <android/log.h>
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)

typedef int (*coreclr_initialize_fn)(
    const char *exePath, const char *appDomainFriendlyName, int propertyCount,
    const char **propertyKeys, const char **propertyValues, void **hostHandle,
    unsigned int *domainId);

typedef int (*coreclr_execute_assembly_fn)(
    void *hostHandle, unsigned int domainId, int argc, const char **argv,
    const char *managedAssemblyPath, unsigned int *exitCode);

// A growable string, for the trusted assembly list: every assembly the runtime
// is allowed to load, ':'-separated, which for a whole class library is tens
// of kilobytes -- too long for a fixed buffer and too important to truncate.
typedef struct { char *data; size_t len, cap; } strbuf;

// Where the exit code is left for the launcher, and how it gets written even
// when nothing here is given the chance to write it.
//
// Environment.Exit does not return. Mono implements it by recording the code
// and calling exit(), which unwinds the process from inside
// coreclr_execute_assembly -- so the Kotlin that started this run never gets
// control back, and the finally block that would have written the result file
// never runs. il2cpp exits that way, and a conversion that ended perfectly
// well would otherwise be indistinguishable from one the low memory killer
// took.
//
// atexit handlers do run for exit(), so the file is written from one. The
// code comes from the runtime rather than from here: Environment.Exit went
// through mono_environment_exitcode_set on its way past, and that value is
// the one the program meant to return.
static char g_result_path[PATH_MAX];
static unsigned int g_exit_code = 120;
static int g_have_exit_code = 0;
static int g_result_written = 0;
static int (*g_exitcode_get)(void) = NULL;

static void write_result(void) {
    if (g_result_written || g_result_path[0] == '\0') return;
    g_result_written = 1;

    // Before the result file, never after. The launcher stops reading the
    // output the pass after this file appears, so anything still sitting in
    // stdio's buffer at that moment is never read and is deleted with the
    // file. On the exit() path that is all of it: bionic runs atexit
    // handlers first and flushes the streams afterwards, so a program that
    // ends through Environment.Exit -- which is how il2cpp ends, including
    // when it ends badly -- had its entire output thrown away. That is why a
    // 375-second conversion could leave a convert.log of nothing at all.
    fflush(NULL);

    unsigned int code = g_exit_code;
    // A normal return has already put the real answer in g_exit_code; only
    // ask the runtime when this is the exit() path.
    if (!g_have_exit_code && g_exitcode_get) code = (unsigned int)g_exitcode_get();

    // Written to one side and renamed into place. The launcher treats the
    // result file existing as "the run is over and the code is readable", and
    // open-then-write leaves a moment where it exists and is empty; a rename
    // within a directory is atomic, so it never sees a half-written answer.
    char tmp[PATH_MAX];
    snprintf(tmp, sizeof(tmp), "%s.tmp", g_result_path);
    FILE *f = fopen(tmp, "w");
    if (!f) return;
    fprintf(f, "%u", code);
    fflush(f);
    fclose(f);
    rename(tmp, g_result_path);
}

static int sb_add(strbuf *b, const char *s) {
    size_t n = strlen(s);
    if (b->len + n + 1 > b->cap) {
        size_t cap = b->cap ? b->cap : 8192;
        while (cap < b->len + n + 1) cap *= 2;
        char *p = realloc(b->data, cap);
        if (!p) return 0;
        b->data = p;
        b->cap = cap;
    }
    memcpy(b->data + b->len, s, n);
    b->len += n;
    b->data[b->len] = '\0';
    return 1;
}

/**
 * Appends every .dll in [dir] to the trusted assembly list.
 *
 * Only .dll: the runtime pack keeps native libraries in the same directory as
 * System.Private.CoreLib, and offering it a .so as an assembly is not
 * something it recovers from. Duplicates are harmless -- the first occurrence
 * of a simple name wins, which is why the class library is added before the
 * program's own directory.
 */
static void add_dlls(strbuf *tpa, const char *dir) {
    DIR *d = opendir(dir);
    if (!d) return;
    struct dirent *e;
    while ((e = readdir(d)) != NULL) {
        const char *dot = strrchr(e->d_name, '.');
        if (!dot || strcmp(dot, ".dll") != 0) continue;
        char path[PATH_MAX];
        snprintf(path, sizeof(path), "%s/%s", dir, e->d_name);
        if (tpa->len) sb_add(tpa, ":");
        sb_add(tpa, path);
    }
    closedir(d);
}

static char *dup_jstring(JNIEnv *env, jstring s) {
    if (!s) return NULL;
    const char *c = (*env)->GetStringUTFChars(env, s, NULL);
    char *out = c ? strdup(c) : NULL;
    if (c) (*env)->ReleaseStringUTFChars(env, s, c);
    return out;
}

JNIEXPORT jint JNICALL
Java_dev_silksong_launcher_MonoBridge_nativeRun(
    JNIEnv *env, jclass clazz, jstring j_assembly, jobjectArray j_args,
    jstring j_cwd, jstring j_bcl, jstring j_runtime, jobjectArray j_env,
    jstring j_out, jstring j_result) {
    (void)clazz;

    char *assembly = dup_jstring(env, j_assembly);
    char *cwd = dup_jstring(env, j_cwd);
    char *bcl = dup_jstring(env, j_bcl);
    char *runtime_dir = dup_jstring(env, j_runtime);
    char *out_path = dup_jstring(env, j_out);
    char *result_path = dup_jstring(env, j_result);

    // Registered before anything can fail, so that every path out of this
    // function -- including the ones that do not come back through it --
    // leaves the launcher an answer rather than a process that vanished.
    if (result_path) {
        snprintf(g_result_path, sizeof(g_result_path), "%s", result_path);
        atexit(write_result);
    }

    if (!assembly || !bcl || !runtime_dir) return 120;

    // Everything the runtime writes to stdout or stderr goes to a file the
    // launcher is tailing. A pipe would be the obvious choice and was the
    // first one; it cannot be used, because the descriptor has to reach this
    // process through the Intent that starts the service and the activity
    // manager refuses to carry file descriptors ("Not allowed to write file
    // descriptors here"). A path crosses that boundary as a string, and both
    // processes are the same application, so they share the directory.
    //
    // Redirected here rather than in the caller because the runtime captures
    // both descriptors when it starts and never consults them again.
    if (out_path) {
        int fd = open(out_path, O_WRONLY | O_CREAT | O_TRUNC, 0600);
        if (fd >= 0) {
            dup2(fd, STDOUT_FILENO);
            dup2(fd, STDERR_FILENO);
            if (fd > STDERR_FILENO) close(fd);
            // Line buffered, so the launcher sees progress rather than one
            // block of output when the program finally exits.
            setvbuf(stdout, NULL, _IOLBF, 0);
            setvbuf(stderr, NULL, _IONBF, 0);
        }
    }

    if (j_env) {
        jsize n = (*env)->GetArrayLength(env, j_env);
        for (jsize i = 0; i + 1 < n; i += 2) {
            char *k = dup_jstring(env, (jstring)(*env)->GetObjectArrayElement(env, j_env, i));
            char *v = dup_jstring(env, (jstring)(*env)->GetObjectArrayElement(env, j_env, i + 1));
            if (k && v) setenv(k, v, 1);
            free(k);
            free(v);
        }
    }

    // il2cpp resolves parts of its own installation relative to here.
    if (cwd && chdir(cwd) != 0) {
        fprintf(stderr, "monojni: cannot enter %s\n", cwd);
        return 120;
    }

    char app_dir[PATH_MAX];
    snprintf(app_dir, sizeof(app_dir), "%s", assembly);
    char *slash = strrchr(app_dir, '/');
    if (slash) *slash = '\0';

    strbuf tpa = {0};
    // The class library first, and the program's own directory second.
    //
    // The opposite is the usual convention -- an application that ships a
    // copy of a framework assembly generally means the one it was built
    // against -- and it is wrong here. il2cpp carries the Linux flavour of
    // System.Security.Cryptography, whose Interop.Crypto P/Invokes
    // libSystem.Security.Cryptography.Native.OpenSsl. That library does not
    // exist on Android and is not what the runtime pack ships; letting it
    // win produced a DllNotFoundException the moment Mono.Cecil hashed a
    // public key, which it does for the first assembly it reads.
    //
    // The class library shipped with the runtime is the one built for this
    // platform, and it is the one the native libraries beside it answer to.
    // Assemblies that are genuinely the program's own -- Mono.Cecil,
    // Unity.IL2CPP.* and the rest -- are not in the class library, so they
    // still resolve from the program's directory.
    add_dlls(&tpa, bcl);
    add_dlls(&tpa, app_dir);
    if (!tpa.len) {
        fprintf(stderr, "monojni: no managed assemblies in %s or %s\n", app_dir, bcl);
        return 120;
    }

    // By absolute path: this is loaded into an app process whose linker
    // namespace does search nativeLibraryDir, but the runtime is named
    // explicitly anyway so that the copy beside this library is the one that
    // answers, whatever else is loaded.
    char so_path[PATH_MAX];
    snprintf(so_path, sizeof(so_path), "%s/libmonosgen-2.0.so", runtime_dir);
    void *rt = dlopen(so_path, RTLD_NOW | RTLD_GLOBAL);
    if (!rt) {
        fprintf(stderr, "monojni: cannot load %s: %s\n", so_path, dlerror());
        return 120;
    }

    coreclr_initialize_fn cc_init = (coreclr_initialize_fn)dlsym(rt, "coreclr_initialize");
    coreclr_execute_assembly_fn cc_exec =
        (coreclr_execute_assembly_fn)dlsym(rt, "coreclr_execute_assembly");
    if (!cc_init || !cc_exec) {
        fprintf(stderr, "monojni: the runtime does not export the CoreCLR hosting API\n");
        return 120;
    }

    // Mono uses SIGSEGV as a working part of the runtime -- null checks and
    // the collector's write barrier fault on a guard page and recover in the
    // handler -- so its handlers have to coexist with the ones Android's own
    // runtime installs in every process. Without chaining the two fight over
    // the same signals, an ordinary internal fault is reported as a crash,
    // and the crash reporter faults in turn: "Exiting early due to double
    // fault", with no stack trace to show for it.
    void (*set_signal_chaining)(int) = (void (*)(int))dlsym(rt, "mono_set_signal_chaining");
    void (*set_crash_chaining)(int) = (void (*)(int))dlsym(rt, "mono_set_crash_chaining");
    if (set_signal_chaining) set_signal_chaining(1);
    if (set_crash_chaining) set_crash_chaining(1);

    // Where the atexit handler reads the code from when the program leaves
    // through Environment.Exit rather than by returning.
    g_exitcode_get = (int (*)(void))dlsym(rt, "mono_environment_exitcode_get");

    // Invariant globalization because the runtime pack carries no ICU for
    // Android, and il2cpp's own runtimeconfig asks for it regardless.
    //
    // Server GC off explicitly. Roslyn's csc.runtimeconfig.json asks for it,
    // and a real host would honour that -- but this runtime collects with
    // SGen, which has no server collector, and nothing here reads that file
    // anyway. Saying false leaves no room for it to arrive by another route.
    const char *keys[] = {
        "TRUSTED_PLATFORM_ASSEMBLIES",
        "APP_PATHS",
        "APP_NI_PATHS",
        "APP_CONTEXT_BASE_DIRECTORY",
        "NATIVE_DLL_SEARCH_DIRECTORIES",
        "System.Globalization.Invariant",
        "System.GC.Server",
        "System.GC.Concurrent",
    };
    const char *values[] = {
        tpa.data, app_dir, app_dir, app_dir, runtime_dir, "true", "false", "false",
    };

    void *host = NULL;
    unsigned int domain = 0;
    int hr = cc_init(assembly, "silksong-builder",
                     (int)(sizeof(keys) / sizeof(keys[0])), keys, values, &host, &domain);
    if (hr < 0) {
        fprintf(stderr, "monojni: the runtime failed to start: 0x%08x\n", (unsigned)hr);
        LOGE("coreclr_initialize failed: 0x%08x", (unsigned)hr);
        return 120;
    }

    int argc = j_args ? (int)(*env)->GetArrayLength(env, j_args) : 0;
    const char **argv = argc ? calloc((size_t)argc, sizeof(char *)) : NULL;
    for (int i = 0; i < argc; i++) {
        argv[i] = dup_jstring(env, (jstring)(*env)->GetObjectArrayElement(env, j_args, i));
    }

    LOGI("running %s with %d argument(s)", assembly, argc);
    unsigned int exit_code = 0;
    hr = cc_exec(host, domain, argc, argv, assembly, &exit_code);
    if (hr < 0) {
        fprintf(stderr, "monojni: could not run %s: 0x%08x\n", assembly, (unsigned)hr);
        return 120;
    }

    // The program returned rather than exiting, so this is the real code and
    // the runtime's own copy should not be consulted.
    g_exit_code = exit_code;
    g_have_exit_code = 1;

    // Deliberately no coreclr_shutdown. The process is killed after this
    // returns, which is both faster and safer than asking a runtime that may
    // have just failed to unwind itself politely.
    fflush(stdout);
    fflush(stderr);
    write_result();
    return (jint)exit_code;
}
