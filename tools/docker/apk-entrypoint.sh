#!/usr/bin/env bash
#
# Entrypoint for the APK build container.
#
# Runs the same targets a developer runs on the host, after making the two
# adjustments a bind-mounted checkout needs: the Android module has to be
# present, and Gradle must not be pointed at the host's SDK.
set -euo pipefail

say() { echo "[docker] $*"; }

cd /workspace

# Two files in the checkout are gitignored, machine-specific, and describe the
# *host*: local.properties names Unity's bundled SDK with a C:/ path that means
# nothing here (and AGP reads it in preference to ANDROID_HOME), and dev.env
# overrides the path discovery in dev.sh. Both belong to the host and the
# checkout is bind-mounted, so they are moved aside and put back rather than
# overwritten. The trap covers a failed build; docker stop sends SIGTERM,
# which is why that is trapped too.
STASHED=()
restore() {
    local s
    for s in "${STASHED[@]:-}"; do
        [[ -n "$s" && -f "$s.docker-stashed" ]] || continue
        mv -f "$s.docker-stashed" "$s"
    done
}
trap restore EXIT INT TERM
for f in src/SilksongLauncher.Launcher/local.properties tools/depot-to-apk/dev.env; do
    # A container killed outright -- docker kill, or a stop that outran the
    # grace period -- never runs the trap, and leaves the host's file stashed.
    # Recovering here rather than only on the way out means the damage lasts
    # until the next run instead of until someone notices Gradle cannot find
    # the SDK.
    if [[ -f "$f.docker-stashed" && ! -f "$f" ]]; then
        mv -f "$f.docker-stashed" "$f"
        say "recovered $f left stashed by an earlier run"
    fi
    [[ -f "$f" ]] || continue
    mv -f "$f" "$f.docker-stashed"
    STASHED+=("$f")
    say "host $f stashed for the run"
done

# The Android player module: Gradle 8.11, the player classes, and
# UnityPlayerActivity's source. Fetched into a container-owned volume, not
# into the bind-mounted build/ -- this build reads nothing the host produced.
# fetch-unity.sh skips the download once the module is unpacked, so this is a
# no-op on every run after the first.
if [[ ! -d "$UNITY_PLAYER_ROOT/android/Variations" ]]; then
    say "fetching Unity's Android player module (~642 MB, once per volume)"
fi
WHAT=android ROOT="$UNITY_PLAYER_ROOT" bash tools/ondevice-il2cpp/fetch-unity.sh

# dev.sh discovers this when it is not told; telling it keeps it off both the
# host's Unity install and the host's build/ directory.
export AP="$UNITY_PLAYER_ROOT/android"

# Gradle only copies bundle-surgery into the APK; it does not build it, and
# fails with a bare "bundle-surgery is not built" if it is missing. Always
# built here rather than reused from the host: a Windows-built obj/ names
# paths that do not exist in this container.
say "building bundle-surgery"
dotnet build -c Release tools/bundle-surgery/BundleSurgery.csproj --nologo -v quiet

# A signing key, because a container has none.
#
# apksigner needs one and the build fails at the last step without it. On a
# workstation this file is the one Android Studio generates; here the container
# is fresh every run, so an equivalent is made with the same conventional alias
# and passwords. CI overrides KEYSTORE with a real release key, and this is
# skipped.
#
# Note that this key differs between containers, so an APK built here cannot be
# installed over one built elsewhere without uninstalling first. That is true of
# any two debug keys and is why releases use a stable key from a secret.
if [[ -z "${KEYSTORE:-}" && ! -f "$HOME/.android/debug.keystore" ]]; then
    say "generating a debug keystore (none in this container)"
    mkdir -p "$HOME/.android"
    keytool -genkeypair -v \
        -keystore "$HOME/.android/debug.keystore" \
        -storepass android -keypass android \
        -alias androiddebugkey \
        -keyalg RSA -keysize 2048 -validity 10000 \
        -dname "CN=Android Debug, O=Android, C=US" >/dev/null 2>&1
fi

case "${1:-apk}" in
    apk)
        # No adb here: the container has the toolchain but not the USB bus.
        # The APK lands in the bind mount for the host to install.
        #
        # DEBUGGABLE defaults to 1 because the usual reason to run this is a
        # dev build on a machine with no Android SDK, and android:debuggable is
        # what makes `run-as` work. A release must set it to 0 -- CI does.
        say "building the APK"
        INSTALL=0 DEBUGGABLE="${DEBUGGABLE:-1}" bash tools/depot-to-apk/dev.sh
        ;;
    shell)
        exec bash
        ;;
    *)
        exec "$@"
        ;;
esac
