"""Execute the real Activity/observer with Android boundary stand-ins, never an engine.

Catches missing registration, wrong public surface identity/epoch, changed lifecycle
forwarding and diagnostic exceptions escaping into gameplay. This is NOT a real
SurfaceView, Android scheduling, native acknowledgment or Vulkan recovery test.
"""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
SHELL = ROOT / "tools/depot-to-apk/shell"

# Android framework and Unity cannot execute in a host JVM. Keep our Activity and
# observer intact; substitute only those external boundaries. Holders retain real
# callback registration order rather than replacing the observer under test.
STUBS = {
    "android/content/Intent.java": "package android.content; public class Intent {}",
    "android/content/res/Configuration.java": "package android.content.res; public class Configuration {}",
    "android/os/Bundle.java": "package android.os; public class Bundle {}",
    "android/os/SystemClock.java": "package android.os; public class SystemClock { public static long elapsedRealtimeNanos(){return 9000000001L;} }",
    "android/graphics/Rect.java": "package android.graphics; public class Rect { public int width(){return 1920;} public int height(){return 1080;} }",
    "android/view/InputEvent.java": "package android.view; public class InputEvent {}",
    "android/view/KeyEvent.java": "package android.view; public class KeyEvent extends InputEvent {}",
    "android/view/MotionEvent.java": "package android.view; public class MotionEvent extends InputEvent {}",
    "android/view/Surface.java": """package android.view; public class Surface {
        public boolean valid=true; public boolean isValid(){return valid;}
    }""",
    "android/view/SurfaceHolder.java": """package android.view; import android.graphics.Rect;
        public interface SurfaceHolder {
            interface Callback { void surfaceCreated(SurfaceHolder h); void surfaceChanged(SurfaceHolder h,int f,int w,int z); void surfaceDestroyed(SurfaceHolder h); }
            Surface getSurface(); Rect getSurfaceFrame(); void addCallback(Callback c); void removeCallback(Callback c);
        }""",
    "android/view/SurfaceView.java": """package android.view; import android.graphics.Rect; import java.util.*;
        public class SurfaceView {
            public final Holder holder=new Holder(); public SurfaceHolder getHolder(){return holder;}
            public static class Holder implements SurfaceHolder {
                public Surface surface=new Surface(); public final List<Callback> callbacks=new ArrayList<>();
                public Surface getSurface(){return surface;} public Rect getSurfaceFrame(){return new Rect();}
                public void addCallback(Callback c){callbacks.add(c);} public void removeCallback(Callback c){callbacks.remove(c);}
                public void created(){for(Callback c:new ArrayList<>(callbacks))c.surfaceCreated(this);}
                public void changed(){for(Callback c:new ArrayList<>(callbacks))c.surfaceChanged(this,4,1920,1080);}
                public void destroyed(){for(Callback c:new ArrayList<>(callbacks))c.surfaceDestroyed(this);}
            }
        }""",
    "android/widget/FrameLayout.java": "package android.widget; public class FrameLayout { public void requestFocus(){} }",
    "android/util/Log.java": """package android.util; import java.util.*; public class Log {
        public static final List<String> lines=new ArrayList<>(); public static boolean fail;
        public static int i(String tag,String line){if(fail)throw new IllegalStateException("log unavailable");lines.add(tag+":"+line);return 0;}
    }""",
    "android/app/Activity.java": """package android.app; import android.content.*; import android.content.res.*; import android.os.*; import android.view.*;
        public class Activity {
            protected void onCreate(Bundle b){} protected void onDestroy(){} protected void onStart(){} protected void onStop(){}
            protected void onResume(){} protected void onPause(){} protected void onNewIntent(Intent i){}
            public void onConfigurationChanged(Configuration c){} public void onWindowFocusChanged(boolean f){}
            public void onRequestPermissionsResult(int r,String[] p,int[] g){} public void setContentView(Object v){}
            public void setIntent(Intent i){} public boolean moveTaskToBack(boolean b){return true;}
            public boolean onKeyDown(int k,KeyEvent e){return false;} public boolean onKeyUp(int k,KeyEvent e){return false;}
            public boolean onGenericMotionEvent(MotionEvent e){return false;} public boolean onTouchEvent(MotionEvent e){return false;}
        }""",
    "com/unity3d/player/IUnityPlayerLifecycleEvents.java": "package com.unity3d.player; public interface IUnityPlayerLifecycleEvents {void onUnityPlayerUnloaded();void onUnityPlayerQuitted();}",
    "com/unity3d/player/UnityPlayer.java": """package com.unity3d.player;
        public abstract class UnityPlayer { public abstract android.view.SurfaceView getSurfaceView(); }
    """,
    "com/unity3d/player/UnityPlayerForActivityOrService.java": """package com.unity3d.player;
        import android.app.*; import android.content.*; import android.content.res.*; import android.view.*; import android.widget.*; import java.util.*;
        public class UnityPlayerForActivityOrService extends UnityPlayer {
            public final SurfaceView surface=new SurfaceView(); public final List<String> calls=new ArrayList<>();
            public UnityPlayerForActivityOrService(Activity a,IUnityPlayerLifecycleEvents e){
                surface.holder.addCallback(new SurfaceHolder.Callback(){
                    public void surfaceCreated(SurfaceHolder h){calls.add("surfaceCreated");}
                    public void surfaceChanged(SurfaceHolder h,int f,int w,int z){calls.add("surfaceChanged");}
                    public void surfaceDestroyed(SurfaceHolder h){calls.add("surfaceDestroyed");}
                });
            }
            public FrameLayout getFrameLayout(){return new FrameLayout();} public SurfaceView getSurfaceView(){return surface;}
            public void onStart(){calls.add("start");} public void onStop(){calls.add("stop");}
            public void onResume(){calls.add("resume");} public void onPause(){calls.add("pause");}
            public void destroy(){calls.add("destroy");} public void newIntent(Intent i){} public void configurationChanged(Configuration c){}
            public void windowFocusChanged(boolean f){calls.add("focus="+f);} public void permissionResponse(Activity a,int r,String[] p,int[] g){}
            public boolean injectEvent(InputEvent e){return true;}
        }""",
    "dev/silksong/shell/SecondaryDisplay.java": """package dev.silksong.shell; import android.app.Activity;
        public class SecondaryDisplay { public SecondaryDisplay(Activity a){} void onStart(){} void onStop(){} void onDestroy(){}
            void setEnabled(boolean b){} double[] poll(){return new double[0];} }
    """,
    "dev/silksong/shell/PlayerSurfaceTraceTest.java": """package dev.silksong.shell;
        import android.os.Bundle; import android.util.Log; import android.view.*; import java.util.*;
        public final class PlayerSurfaceTraceTest extends PlayerActivity {
            static void check(boolean b,String m){if(!b)throw new AssertionError(m);}
            static String line(String event){return Log.lines.stream().filter(s->s.contains("event="+event+" ")).reduce((a,b)->b).orElse("");}
            public static void main(String[] args){
                PlayerSurfaceTraceTest a=new PlayerSurfaceTraceTest();a.onCreate(new Bundle());
                SurfaceView.Holder h=a.mUnityPlayer.surface.holder;
                if(args[0].equals("surface")){
                    Surface old=h.surface;
                    h.created();h.changed();h.surface.valid=false;h.destroyed();h.surface=new Surface();h.created();
                    String destroyed=line("surfaceDestroyed"),created=line("surfaceCreated");
                    check(destroyed.contains("surface="+System.identityHashCode(old)+" ")&&destroyed.contains("valid=false"),"Destroyed wrapper observation missing or fabricated");
                    check(created.contains("surface="+System.identityHashCode(h.surface)+" ")&&created.contains("epoch=2"),"Replacement wrapper/callback epoch not distinguished");
                    check(created.contains("nativeGeneration=unavailable-public-sdk"),"Observer must explicitly leave native generation unavailable");
                    check(created.contains("monoNs=9000000001")&&created.contains("width=1920 height=1080"),"Missing observation clock/bounds");
                    check(!created.contains("ack=true")&&!created.contains("recovered=true"),"Observer must not claim native acknowledgment");
                    check(a.mUnityPlayer.calls.equals(Arrays.asList("surfaceCreated","surfaceChanged","surfaceDestroyed","surfaceCreated")),"Engine callback delivery changed");
                } else if(args[0].equals("lifecycle")){
                    a.onStart();a.onResume();a.onWindowFocusChanged(true);a.onPause();a.onStop();a.onDestroy();
                    check(a.mUnityPlayer.calls.equals(Arrays.asList("start","resume","focus=true","pause","stop","destroy")),"Unity lifecycle forwarding changed");
                    check(!line("resume-before").isEmpty()&&!line("resume-after").isEmpty(),"Resume lacks bounded entry/return observations");
                    check(!line("pause-before").isEmpty()&&!line("pause-after").isEmpty(),"Pause lacks bounded entry/return observations");
                    int n=Log.lines.size();h.created();check(Log.lines.size()==n,"Owned observer survived Activity destruction");
                    check(h.callbacks.size()==1,"Diagnostic teardown removed another owner's callback");
                } else if(args[0].equals("failure")){
                    Log.fail=true;h.created();a.onResume();a.onPause();a.onStop();a.onDestroy();
                    check(a.mUnityPlayer.calls.equals(Arrays.asList("surfaceCreated","resume","pause","stop","destroy")),"Diagnostic failure blocked engine lifecycle");
                } else throw new AssertionError("Unknown scenario");
                System.out.println("PASS "+args[0]);
            }
        }""",
}


class PlayerSurfaceTraceTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.javac = shutil.which("javac")
        cls.java = shutil.which("java")
        if not cls.javac or not cls.java:
            raise RuntimeError("Java 17+ is required for the executable Activity boundary test")
        cls.work = tempfile.TemporaryDirectory(prefix="player-surface-trace-")
        cls.root = Path(cls.work.name)
        files = []
        for relative, text in STUBS.items():
            path = cls.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
            files.append(str(path))
        # The old Activity must still compile so RED is a missing observed
        # behavior, not an absent-class/compiler error.
        files.append(str(SHELL / "PlayerActivity.java"))
        observer = SHELL / "PlayerSurfaceTrace.java"
        if observer.exists():
            files.append(str(observer))
        result = subprocess.run([cls.javac, "--release", "17", "-d", str(cls.root / "out"), *files],
                                capture_output=True, text=True, timeout=60)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def scenario(self, name):
        result = subprocess.run([self.java, "-cp", str(self.root / "out"),
                                 "dev.silksong.shell.PlayerSurfaceTraceTest", name],
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_apk_shell_stages_and_compiles_its_observer(self):
        source = (ROOT / "tools/depot-to-apk/build.sh").read_text(encoding="utf-8")
        start = source.index('    mkdir -p "$sh/src"')
        end = source.index('    if (( have_launcher )); then', start)
        # Execute the unchanged staging/compile-input block, not a source-text
        # assertion or a second implementation of its source-selection policy.
        script = self.root / "probe-shell-inputs.sh"
        script.write_text('set -eu\nprobe() {\n' + source[start:end]
                          + '\n printf "%s\\n" "${srcs[@]}"\n}\nprobe\n', encoding="utf-8")
        env = dict(os.environ, SCRIPT_DIR=str(SHELL.parent).replace("\\", "/"),
                   sh=str(self.root / "delivery").replace("\\", "/"),
                   VARIATION="unused-player-reference", android_jar="unused-api-reference")
        result = subprocess.run([shutil.which("bash") or "bash", "--noprofile", "--norc", str(script)],
                                env=env, capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        staged = self.root / "delivery/src/PlayerSurfaceTrace.java"
        self.assertTrue(staged.is_file(), "APK source staging omitted the observer dependency")
        self.assertEqual((SHELL / "PlayerSurfaceTrace.java").read_bytes(), staged.read_bytes())
        self.assertIn(staged.as_posix(), result.stdout.splitlines(),
                      "Staged observer was omitted from the actual javac source array")

    def test_compiled_activity_observes_surface_across_unity_return_type_changes(self):
        # Real retained SDKs override UnityPlayer's stable SurfaceView getter
        # with these different covariant return types. The JVM descriptor includes
        # that return type. Compile the real Activity once, then replace ONLY the
        # external Unity boundary; recompiling our Activity would hide the bug.
        player_path = "com/unity3d/player/UnityPlayerForActivityOrService.java"
        profiles = (("ss50", "com.unity3d.player.f"),
                    ("hk61", "com.unity3d.player.a.f"))

        def player_sources(return_type):
            source = STUBS[player_path].replace(
                "surface=new SurfaceView()", f"surface=new {return_type}()")
            source = source.replace(
                "public SurfaceView getSurfaceView(){return surface;}",
                f"public {return_type} getSurfaceView(){{return ({return_type})surface;}}")
            package, name = return_type.rsplit(".", 1)
            return {player_path: source,
                    return_type.replace(".", "/") + ".java":
                    f"package {package}; public class {name} extends android.view.SurfaceView {{}}"}

        def compile_sources(directory, sources, classpath=None, real_shell=False):
            files = []
            for relative, text in sources.items():
                path = directory / "src" / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8")
                files.append(str(path))
            if real_shell:
                files.extend(str(SHELL / name) for name in
                             ("PlayerActivity.java", "PlayerSurfaceTrace.java"))
            command = [self.javac, "--release", "17", "-d", str(directory / "out")]
            if classpath is not None:
                command.extend(("-cp", str(classpath)))
            result = subprocess.run(command + files, capture_output=True, text=True, timeout=60)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            return directory / "out"

        for index, (compiled_profile, compiled_return) in enumerate(profiles):
            runtime_profile, runtime_return = profiles[1 - index]
            with self.subTest(compiled=compiled_profile, runtime=runtime_profile):
                directory = self.root / ("abi-" + compiled_profile)
                classes = compile_sources(directory, {**STUBS, **player_sources(compiled_return)},
                                          real_shell=True)
                baseline = subprocess.run(
                    [self.java, "-cp", str(classes),
                     "dev.silksong.shell.PlayerSurfaceTraceTest", "surface"],
                    capture_output=True, text=True, timeout=30)
                self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
                runtime = compile_sources(directory / "runtime", player_sources(runtime_return),
                                          classpath=classes)
                for scenario in ("surface", "lifecycle", "failure"):
                    result = subprocess.run(
                        [self.java, "-cp", os.pathsep.join((str(runtime), str(classes))),
                         "dev.silksong.shell.PlayerSurfaceTraceTest", scenario],
                        capture_output=True, text=True, timeout=30)
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_public_surface_observations_do_not_invent_native_generation(self):
        self.scenario("surface")

    def test_lifecycle_entry_return_and_owned_observer_teardown(self):
        self.scenario("lifecycle")

    def test_trace_failure_does_not_intercept_unity_callbacks(self):
        self.scenario("failure")


if __name__ == "__main__":
    unittest.main()
