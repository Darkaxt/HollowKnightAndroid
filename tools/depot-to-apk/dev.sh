#!/usr/bin/env bash
#
# The edit-test loop for the launcher and the APK shell: rebuild the launcher,
# repackage, install. One command, and about three minutes.
#
# Steps 1-4 are skipped. The converted player, the compiled libil2cpp and the
# retargeted depot data are built ON THE DEVICE now, so nothing here produces
# them -- this only ships the app that does.
#
# Paths are discovered rather than configured; see find_sdk and find_jdk
# below. dev.env beside this script still wins if it exists, for an install
# somewhere unusual, but it is no longer required.
#
# LAUNCHER=0 skips the Gradle build when only the shell or build.sh changed.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
[[ -f "$SCRIPT_DIR/dev.env" ]] && source "$SCRIPT_DIR/dev.env"

# Nothing this build produces or fetches belongs in the checkout. The APK
# staging tree carries game data lifted out of the depot, and the player
# module is Unity's redistributable -- neither is ours to vendor, and the repo
# should stay copyable and publishable without either. Both default under one
# cache root outside the tree; override either to move it.
SILKSONG_CACHE="${SILKSONG_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/silksong}"
BUILD_ROOT="${BUILD_ROOT:-$SILKSONG_CACHE/build}"
PLAYER_ROOT="${PLAYER_ROOT:-$SILKSONG_CACHE/unity-player}"
export SILKSONG_CACHE BUILD_ROOT PLAYER_ROOT

# The Android SDK: build-tools for d8/aapt2/zipalign/apksigner, and a platform
# android.jar to compile the shell against.
find_sdk() {
    local candidates=(
        "${ANDROID_HOME:-}"
        "${ANDROID_SDK_ROOT:-}"
        "$HOME/AppData/Local/Android/Sdk"
        "$HOME/Android/Sdk"
        "$HOME/Library/Android/sdk"
        "/usr/lib/android-sdk"
    )
    local c
    for c in "${candidates[@]}"; do
        [[ -n "$c" && -d "$c/platforms" ]] && { echo "$c"; return; }
    done
    return 0    # finding nothing is a normal outcome; see the AP block below
}

ANDROID_SDK="${ANDROID_SDK:-$(find_sdk)}"

# The Android player module: Unity's player classes, UnityPlayerActivity's
# source, Gradle, and libunity/libmain. Fetched from Unity's CDN -- the same
# .pkg, at the same URL and digest, that the app downloads on the device -- and
# kept in PLAYER_ROOT, outside the checkout, because it is Unity's
# redistributable and not ours to vendor.
#
# An installed editor contains the same module, and used to be preferred. It is
# not consulted any more: whether a build is reproducible should not depend on
# what happens to be installed, and the fetched copy is pinned by digest while
# a Hub install drifts with whatever the user upgraded to.
FETCHED_PLAYER="$PLAYER_ROOT/android"
if [[ -n "${AP:-}" ]]; then
    :   # caller knows better
elif [[ -d "$FETCHED_PLAYER/Variations" ]]; then
    AP="$FETCHED_PLAYER"
else
    echo "[dev] no Android player module in $PLAYER_ROOT." >&2
    echo "      Run 'make player' to fetch it (~642 MB, once)." >&2
    exit 1
fi
[[ -n "$ANDROID_SDK" ]] || {
    echo "[dev] no Android SDK found. Set ANDROID_HOME or ANDROID_SDK." >&2
    exit 1
}

# A JDK, for javac, jar and Gradle. Unity ships one, but any 17+ will do and a
# machine without Unity will have its own -- so Unity's is preferred only
# because it is certainly the right version when it is there.
find_jdk() {
    local c
    for c in "$AP/OpenJDK" "${JAVA_HOME:-}" \
             "/c/Program Files/Android/Android Studio/jbr" \
             "/Applications/Android Studio.app/Contents/jbr/Contents/Home"; do
        [ -n "$c" ] && [ -x "$c/bin/javac" ] && { echo "$c"; return; }
    done
    command -v javac >/dev/null 2>&1 && { dirname "$(dirname "$(command -v javac)")"; return; }
    return 0    # see find_sdk
}
JDK="${JDK:-$(find_jdk)}"
[[ -n "$JDK" ]] || { echo "[dev] no JDK found. Set JAVA_HOME." >&2; exit 1; }

export ANDROID_SDK AP JDK
# Gradle reads local.properties' sdk.dir first and ANDROID_HOME after. On a
# machine with no Unity there is no local.properties -- it is gitignored and
# usually written by Unity's own build -- so without this AGP fails with "SDK
# location not found" even though the SDK was found here a moment ago.
export ANDROID_HOME="${ANDROID_HOME:-$ANDROID_SDK}"

export STEPS="${STEPS:-5,6}"
PKG="${PKG:-io.github.darkaxt.dualsouls}"
# The Makefile exports this, but the script has to stand on its own: under
# `set -u` an unset APK_DIR aborts AFTER the APK is built, throwing away a
# two-minute build over a variable. Same default as the Makefile.
APK_DIR="${APK_DIR:-$REPO_ROOT/build}"

# The APK's filename, derived from the same VERSION file build.sh reads, so the
# two cannot disagree about what the build just produced. Exported for the same
# reason: build.sh takes the override if one is set here.
VERSION_NAME="${VERSION_NAME:-$(tr -d ' \t\r\n' < "$REPO_ROOT/VERSION" 2>/dev/null || echo 0.0.0)}"
APK_NAME="${APK_NAME:-DualSouls-$VERSION_NAME.apk}"

OUT="${OUT:-$BUILD_ROOT/mk/apk2}"
export PKG OUT VERSION_NAME APK_NAME

log() { echo "[dev] $*"; }
t0=$SECONDS

# Start adb's fork-server now, with its handles pointed at /dev/null.
#
# The first adb command in a session spawns a persistent daemon, and that
# daemon inherits whatever stdout/stderr the command had. When that is this
# script's output -- a pipe, or a redirected log -- the handle is never closed,
# because the daemon outlives the build by design. The build finishes, and the
# shell waiting on it hangs anyway, sometimes for minutes, looking exactly like
# a hung build. Spawning it here hands it /dev/null instead, and every later
# adb call reuses it rather than forking a new one.
adb start-server </dev/null >/dev/null 2>&1 || true

# ─── the launcher ───────────────────────────────────────────────────────────
#
# On the Gradle daemon deliberately: a warm rebuild is ~6s where --no-daemon,
# which the old Unity-side build used, pays ~60s of JVM and configuration
# startup every single time.
#
# GRADLE_DAEMON=0 turns that off, which is what the container wants: it exits
# after one build, so the daemon it started is killed rather than reused, and
# the locks it held in GRADLE_USER_HOME are left behind for the next run to
# time out on. There is no warm rebuild to protect, so there is nothing to pay.
if [[ "${LAUNCHER:-1}" == 1 ]]; then
    launcher_dir="$REPO_ROOT/src/SilksongLauncher.Launcher"
    if [[ -f "$launcher_dir/settings.gradle.kts" ]]; then
        gradle_jar=$(ls "$AP/Tools/gradle/lib/gradle-launcher-"*.jar 2>/dev/null | head -1)
        [[ -n "$gradle_jar" ]] || { echo "[dev] no gradle under $AP/Tools" >&2; exit 1; }
        export JAVA_HOME PATH
        JAVA_HOME=$(cygpath -m "$JDK" 2>/dev/null || echo "$JDK")
        PATH="$JDK/bin:$PATH"
        gradle_args=(:app:assembleRelease :app:collectRuntimeDeps --console=plain -q)
        [[ "${GRADLE_DAEMON:-1}" == 1 ]] || gradle_args+=(--no-daemon)
        log "building the launcher"
        ( cd "$launcher_dir" && java -classpath "$gradle_jar" \
            org.gradle.launcher.GradleMain "${gradle_args[@]}" )
    fi
fi

# ─── the APK ────────────────────────────────────────────────────────────────
log "packaging"
bash "$SCRIPT_DIR/build.sh"

# ─── install ────────────────────────────────────────────────────────────────
apk="$APK_DIR/$APK_NAME"
[[ -f "$apk" ]] || { echo "[dev] no APK at $apk" >&2; exit 1; }

# INSTALL=0 stops here with the APK built. That is how the container runs:
# it has the toolchain but no USB, so the host does the installing.
if [[ "${INSTALL:-1}" != 1 ]]; then
    log "built $apk"
    log "done in $((SECONDS - t0))s"
    exit 0
fi

adb shell am force-stop "$PKG" >/dev/null 2>&1 || true

log "installing $apk"
adb install -r "$apk"

log "done in $((SECONDS - t0))s"
