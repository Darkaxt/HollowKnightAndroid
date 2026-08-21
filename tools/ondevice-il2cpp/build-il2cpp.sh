#!/system/bin/sh
#
# Build libil2cpp.so ON AN ANDROID DEVICE from IL2CPP-generated C++.
#
# Proves the final link in the on-device build chain: a phone compiling and
# linking the game's native code itself, with no PC involved. Uses Termux's
# aarch64-hosted clang (Android has no system compiler) plus the NDK sysroot
# for the bionic headers and the static libc++ that Unity itself links against.
#
# Layout expected under $ROOT:
#   usr/       Termux toolchain (clang, lld, its own libs)
#   sysroot/   NDK sysroot (bionic headers + libc++_static.a)
#   cpp/       IL2CPP-generated .cpp/.c translation units
#   libil2cpp/ + external/   IL2CPP runtime sources and headers
#   baselib.a  Unity's prebuilt arm64 static library
#
# Any of those may be a symlink, and each can instead be pointed somewhere else
# entirely with the matching variable below. When the app drives this build the
# pieces are already on the device in three different places -- the toolchain on
# internal storage because that is the only kind Android will exec from, the
# generated C++ and the Unity sources on external storage because that is where
# there is room -- and naming them is cheaper than copying several gigabytes to
# satisfy a layout.
#
# It used to LINK them into one directory instead, which is nicer to poke at by
# hand and is not portable: $ROOT is on external storage, that is FUSE-backed,
# and FUSE returns EPERM for symlink() on most Android builds. It happens to be
# permitted on some vendor ROMs, which is why this survived until a Retroid
# Pocket Flip 2 met it. Paths cost nothing and work everywhere.
#
# Hence find -L throughout: with a symlinked layout, a find rooted at a linked
# directory returns the link and nothing under it, and the build silently
# compiles nothing.
ROOT="${ROOT:-/data/local/tmp/tx}"
cd "$ROOT" || exit 1

# Every piece, defaulting to the linked layout so a hand-made $ROOT still works.
USR="${USR:-$ROOT/usr}"
SYSROOT="${SYSROOT:-$ROOT/sysroot}"
LIBIL2CPP="${LIBIL2CPP:-$ROOT/libil2cpp}"
EXTERNAL="${EXTERNAL:-$ROOT/external}"
BASELIB="${BASELIB:-$ROOT/baselib.a}"
CPPDIR="${CPPDIR:-$ROOT/cpp}"

for _p in "$USR" "$SYSROOT" "$LIBIL2CPP" "$EXTERNAL" "$BASELIB" "$CPPDIR"; do
    [ -e "$_p" ] || { echo "missing: $_p" >&2; exit 1; }
done

export LD_LIBRARY_PATH="$USR/lib"
CLANG="$USR/bin/clang"
JOBS=$(nproc)

# --sysroot points at the NDK (not Termux's) so the produced .so depends only
# on bionic: liblog/libm/libdl/libc, exactly like Unity's own libil2cpp.so.
# NOTE: do NOT force -resource-dir at the NDK's clang here. The resource dir
# carries compiler-specific headers (arm_neon.h among them), and Termux's
# clang-21 against the NDK's clang-18 resource dir fails on NEON intrinsics
# ("incompatible constant for this __builtin_neon function") in brotli's
# dec/decode.c -- which is where BrotliDecoderCreateInstance lives, so the .so
# then dies at dlopen. Let the compiler use its own resource dir; only the
# sysroot needs to come from the NDK.
TGT="--target=aarch64-linux-android33 --sysroot=$SYSROOT -stdlib=libc++ --unwindlib=none"
# Flags below are Unity's own, recovered verbatim from the Bee build graph of a
# desktop Android build (Library/Bee/Player*.dag.json). Guessing them does not
# work: without -DBASELIB_INLINE_NAMESPACE=il2cpp_baselib the baselib classes
# land directly in namespace baselib and collide with il2cpp's own forward
# declarations, and every TU that pulls in il2cpp-object-internals.h fails with
# "reference to 'ReentrantLock' is ambiguous".
INC="-I$CPPDIR -I$LIBIL2CPP -I$LIBIL2CPP/pch \
     -I$EXTERNAL/baselib/Include \
     -I$EXTERNAL/baselib/Platforms/Android/Include \
     -I$EXTERNAL/bdwgc/include \
     -I$LIBIL2CPP/os/ClassLibraryPAL/brotli/include"
DEF="-DANDROID -DHAVE_INTTYPES_H -DNDEBUG -DTARGET_ARM64 -DGC_NOT_DLL \
     -DBASELIB_INLINE_NAMESPACE=il2cpp_baselib \
     -DIL2CPP_MONO_DEBUGGER_DISABLED -DRUNTIME_IL2CPP \
     -DIL2CPP_ENABLE_WRITE_BARRIERS=1 -DIL2CPP_INCREMENTAL_TIME_SLICE=3 \
     -DIL2CPP_DEFAULT_DATA_DIR_PATH=Data \
     -D__ANDROID_UNAVAILABLE_SYMBOLS_ARE_WEAK__"
# Unity builds il2cpp with exceptions on and RTTI off; mismatching either
# produces link-time surprises rather than compile errors.
#
# OPT is the one flag here that is a choice rather than a requirement. Unity's
# own graph says -Os, which is what its "Faster (smaller) builds" setting
# means; -O2 is the other side of that switch and is what a shipped game
# normally uses. On a phone the trade is worth measuring rather than assuming:
# -O2 costs compile time and library size, and buys frame time.
#
# Changing it invalidates every object, which is handled -- the signature
# below covers CXXFLAGS, so obj/ is wiped and the whole thing rebuilds.
OPT="${OPT:--O2}"
CXXFLAGS="-march=armv8-a $OPT -fPIC -fexceptions -fno-rtti -funwind-tables \
     -fvisibility=hidden -fomit-frame-pointer -fno-strict-overflow \
     -ffunction-sections -fdata-sections -fstack-protector -no-canonical-prefixes \
     -Wno-invalid-offsetof -Wno-missing-declarations -Wno-unused-value \
     -Wno-unknown-warning-option -Wno-pragma-once-outside-header \
     -Wno-tautological-compare -Wno-null-conversion -Wno-undef-prefix"

rm -f err.log; : > err.log
mkdir -p obj

# ── incremental build ────────────────────────────────────────────────────────
#
# A full build is ~1550 translation units and about 20 minutes. Almost every
# iteration changes far less than that: swapping one assembly reconverts ~500
# of the generated TUs, and patching the runtime touches one. Rebuilding
# everything each time dominates the edit-test loop, so objects are kept and
# only genuinely changed sources are recompiled.
#
# Freshness is decided by content hash rather than timestamp. The generated
# tree is rewritten wholesale by the converter, so every file looks new by
# mtime even when its contents are identical -- which is the common case, and
# exactly the case worth skipping.
#
# FULL=1 forces everything to be rebuilt.
MANIFEST=obj/.hashes
FLAGSIG=obj/.flagsig

# Any change to the flags invalidates every object, and comparing a signature
# is cheaper and more reliable than remembering to wipe by hand.
sig=$(printf '%s|%s|%s|%s' "$TGT" "$DEF" "$INC" "$CXXFLAGS" | sha256sum | cut -d' ' -f1)
if [ "${FULL:-0}" = 1 ] || [ ! -f "$FLAGSIG" ] || [ "$(cat "$FLAGSIG")" != "$sig" ]; then
    [ -f "$FLAGSIG" ] && echo "### flags changed — full rebuild"
    rm -rf obj; mkdir -p obj
    : > "$MANIFEST"
    printf '%s' "$sig" > "$FLAGSIG"
fi
[ -f "$MANIFEST" ] || : > "$MANIFEST"

# Object names derive from the FULL path, not the basename: libil2cpp has 360
# sources but only 247 distinct basenames (os/Posix/Thread.cpp vs
# os/Win32/Thread.cpp), so basename-derived names silently overwrite each other
# and quietly drop a third of the runtime.
# The object's name, derived from the source path RELATIVE to its tree.
#
# Relative, not absolute, for two reasons. Names stay short -- an absolute path
# on external storage is ~70 characters before the source path even starts, and
# a filename has 255 to work with. And they stay the same whatever the storage
# layout is, so moving the sources does not invalidate every object.
#
# The relative path, not the basename: there are ~950 generated sources but only
# 247 distinct basenames (os/Posix/Thread.cpp vs os/Win32/Thread.cpp), so
# basename-derived names silently overwrite each other and the link then fails
# hundreds of objects later.
obj_name() { printf 'obj/%s%s.o' "$1" "$(echo "${2#$3/}" | tr -c 'A-Za-z0-9._-' '_')"; }

: > obj/.seen
: > obj/.objs

# ── the worker ───────────────────────────────────────────────────────────────
#
# One translation unit, invoked by xargs with the source path as its argument.
# It is a file rather than an inline `sh -c` because toybox xargs has no -I,
# so the only way to pass the path is as a trailing argument -- which is
# exactly what this takes.
cat > obj/.cc <<'WORKER'
#!/system/bin/sh
f="$1"
rel="${f#$CC_TREE/}"
out=$(printf 'obj/%s%s.o' "$CC_PREFIX" "$(echo "$rel" | tr -c 'A-Za-z0-9._-' '_')")
exec $CLANG -x "$CC_LANG" -std="$CC_STD" $CC_PCH $CXXFLAGS $DEF $INC $TGT \
     -c "$f" -o "$out" 2>>err.log
WORKER
chmod +x obj/.cc
export CLANG CXXFLAGS DEF INC TGT

# ── precompiled headers ──────────────────────────────────────────────────────
#
# Every one of the 946 generated C++ units opens with #include "pch-cpp.hpp",
# and every one of the 196 generated C units with "pch-c.h". Those headers pull
# in the il2cpp internals plus <limits>, <cmath> and <cstring> -- so without a
# precompiled header that work is done from source 1142 times over. Unity ships
# the headers precisely so it need not be.
#
# The PCH must be built with the SAME flags as the units that use it; clang
# refuses one compiled with different ones, which is why this shares $CXXFLAGS
# $DEF $INC $TGT rather than naming them again. libil2cpp's own runtime sources
# do not include either header, so phase C is compiled without one.
#
# A failure here is not fatal. The PCH is an optimisation, and a build that is
# slower is better than a build that does not happen.
build_pch() {
    hdr="$1"; lang="$2"; std="$3"; out="$4"
    [ -f "$hdr" ] || return 1
    # Rebuilt when the header is newer, so a re-fetched or updated Unity does
    # not leave a stale one behind. A flag change is already covered: that
    # wipes obj/ wholesale via the signature check above, and the PCH with it.
    [ -f "$out" ] && [ ! "$hdr" -nt "$out" ] && return 0
    $CLANG -x "$lang-header" -std="$std" $CXXFLAGS $DEF $INC $TGT \
        -o "$out" "$hdr" 2>>err.log
}

pch_for() {
    hdr="$1"; lang="$2"; std="$3"; out="$4"
    if build_pch "$hdr" "$lang" "$std" "$out"; then
        # Not `printf -- '-include-pch %s'`. That guard against printf reading
        # the flag as an option is the usual advice and it is wrong here:
        # this printf treats `--` as the FORMAT and prints it literally, so
        # the flags became a bare `--`, which tells clang that everything
        # after it is an input file. Every subsequent flag was then reported
        # as "no such file or directory: '-march=armv8-a'" -- and the phase
        # that passed no PCH at all compiled perfectly, which is what made it
        # look like a PCH problem rather than a quoting one.
        printf '%s %s' -include-pch "$out"
    else
        echo "### PCH unavailable for $lang, compiling without one" >&2
        printf ''
    fi
}

compile_all() {
    tree="$1"; prefix="$2"; ext="$3"; lang="$4"; std="$5"; pch="$6"
    srcs=$(find -L "$tree" -name "*.$ext" | sort)
    [ -z "$srcs" ] && return 0

    # One sha256sum call for the whole phase: per-file invocations cost more
    # than the hashing itself.
    echo "$srcs" | xargs sha256sum > obj/.now
    cat obj/.now >> obj/.seen

    # Decide what needs doing before doing any of it, so the work can be handed
    # to a pool rather than dribbled out one batch at a time.
    : > obj/.todo
    built=0
    while read -r hash f; do
        out=$(obj_name "$prefix" "$f" "$tree")
        echo "$out" >> obj/.objs
        old=$(grep -F "  $f" "$MANIFEST" 2>/dev/null | head -1 | cut -d' ' -f1)
        if [ -f "$out" ] && [ "$old" = "$hash" ]; then
            continue
        fi
        printf '%s\n' "$f" >> obj/.todo
        built=$((built+1))
    done < obj/.now

    if [ "$built" -gt 0 ]; then
        # Largest first. With a pool the run ends when the last unit ends, so
        # starting the long ones early keeps the tail short -- a 2 MB generated
        # file scheduled last leaves seven cores idle while it finishes.
        #
        # Only adopted if the sort produced a line for every source: a stat
        # that handled fewer arguments than it was given would silently drop
        # work, and a dropped translation unit is a link error hundreds of
        # objects later.
        if xargs stat -c '%s %n' < obj/.todo 2>/dev/null | sort -rn | cut -d' ' -f2- > obj/.todo.sorted &&
           [ "$(wc -l < obj/.todo.sorted)" = "$(wc -l < obj/.todo)" ]; then
            mv obj/.todo.sorted obj/.todo
        else
            rm -f obj/.todo.sorted
        fi

        # A refilling pool, not batches. The previous form launched $JOBS
        # compiles and then called bare `wait`, which blocks until the WHOLE
        # batch is done -- so each batch cost as much as its slowest member and
        # the cores drained idle waiting for it. Measured on an 8-core device
        # that left about a third of the CPU unused: concurrency oscillated
        # between 1 and 8 and idle time swung from 14% to 596% of 800%. It is
        # worse than it sounds because the cores are not equal (3 at 2.0 GHz,
        # 4 at 2.8, 1 at 3.19), so a batch waits on whichever unit landed on a
        # little core.
        #
        # xargs -P keeps $JOBS running at all times, starting a new unit the
        # moment one finishes. It is absent from toybox's --help but present
        # and working.
        CC_LANG="$lang" CC_STD="$std" CC_PREFIX="$prefix" CC_PCH="$pch" CC_TREE="$tree" \
            xargs -P "$JOBS" -n 1 sh obj/.cc < obj/.todo

        # Every unit must have produced an object, and this is checked rather
        # than assumed. The link allows undefined symbols, so a phase that
        # compiled nothing at all still links -- into a library a fraction of
        # the right size that fails at dlopen, long after the thing that broke.
        # That is exactly what a stray `--` in the flags once did here: 1142 of
        # 1546 units failed, the build reported success, and the first sign of
        # trouble was the game crashing on launch.
        failed=0
        while read -r f; do
            [ -f "$(obj_name "$prefix" "$f" "$tree")" ] || failed=$((failed + 1))
        done < obj/.todo
        if [ "$failed" -gt 0 ]; then
            echo "### FAILED: $failed of $built translation units produced no object" >&2
            grep -m 5 'error:' err.log >&2
            exit 1
        fi
    fi
    echo "  $built rebuilt, $(( $(wc -l < obj/.now) - built )) unchanged"
}


echo "### device: $(getprop ro.product.model), $JOBS cores"
echo "### clang: $($CLANG --version 2>/dev/null | head -1)"

T0=$(date +%s)
PCH_CPP=$(pch_for "$LIBIL2CPP/pch/pch-cpp.hpp" c++ c++11 obj/pch-cpp.pch)
echo "### PHASE A: compiling generated C++ ($(find -L "$CPPDIR" -name '*.cpp' | wc -l) TUs)"
compile_all "$CPPDIR" g cpp c++ c++11 "$PCH_CPP"
echo "  objects: $(ls obj/g*.o 2>/dev/null | wc -l)  $(( $(date +%s)-T0 ))s"

T1=$(date +%s)
PCH_C=$(pch_for "$LIBIL2CPP/pch/pch-c.h" c c11 obj/pch-c.pch)
echo "### PHASE B: compiling generated C ($(find -L "$CPPDIR" -name '*.c' | wc -l) TUs)"
compile_all "$CPPDIR" c c c c11 "$PCH_C"
echo "  objects: $(ls obj/c*.o 2>/dev/null | wc -l)  $(( $(date +%s)-T1 ))s"

T2=$(date +%s)
echo "### PHASE C: compiling libil2cpp runtime ($(find -L "$LIBIL2CPP" -name '*.cpp' | wc -l) TUs)"
# No PCH: libil2cpp's own sources do not include either pch header, and one
# built for them would have to be a different header from the one the
# generated code uses.
compile_all "$LIBIL2CPP" r cpp c++ c++11 ""
echo "  objects: $(ls obj/r*.o 2>/dev/null | wc -l)  $(( $(date +%s)-T2 ))s"

# Boehm GC. Unity builds bdwgc as ONE amalgamated translation unit
# (extra/gc.c, which #includes the individual sources). Compiling the
# top-level .c files separately links but then fails at dlopen with
# "cannot locate symbol maybe_finalize": several cross-file helpers are
# static, so they only resolve inside the single-TU build.
GCDEF="-DANDROID -DHAVE_INTTYPES_H -DNDEBUG -DGC_NOT_DLL -DHAVE_BOEHM_GC \
     -DALL_INTERIOR_POINTERS=1 -DGC_GCJ_SUPPORT=1 -DJAVA_FINALIZATION=1 \
     -DNO_EXECUTE_PERMISSION=1 -DGC_NO_THREADS_DISCOVERY=1 \
     -DIGNORE_DYNAMIC_LOADING=1 -DGC_DONT_REGISTER_MAIN_STATIC_DATA=1 \
     -DGC_VERSION_MAJOR=7 -DGC_VERSION_MINOR=7 -DGC_VERSION_MICRO=0 \
     -DGC_THREADS=1 -DUSE_MMAP=1 -DUSE_MUNMAP=1 \
     -DIL2CPP_ENABLE_WRITE_BARRIERS=1 -DIL2CPP_INCREMENTAL_TIME_SLICE=3 \
     -D__ANDROID_UNAVAILABLE_SYMBOLS_ARE_WEAK__"
T2b=$(date +%s)
echo "### PHASE C2: compiling bdwgc (amalgamated)"
# C2-C4 are vendored sources that never change between iterations, so an
# existing object is always current -- a flag change wipes obj/ wholesale via
# the signature check above.
if [ ! -f obj/zgc_amalgam.o ]; then
$CLANG -x c -std=gnu99 -march=armv8-a $OPT -fPIC -fvisibility=hidden \
       -Wno-implicit-function-declaration $GCDEF \
       -I"$EXTERNAL/bdwgc/include" -I"$EXTERNAL/bdwgc/libatomic_ops/src" \
       $TGT -c "$EXTERNAL/bdwgc/extra/gc.c" -o obj/zgc_amalgam.o 2>>err.log
fi
echo "  $([ -f obj/zgc_amalgam.o ] && echo OK || echo FAILED)  $(( $(date +%s)-T2b ))s"

# zlib. Unity renames every symbol to il2cpp_z_* via Z_PREFIX in zconf.h, so
# the runtime's calls only resolve against a Z_PREFIX build of these sources.
T2c=$(date +%s)
echo "### PHASE C3: compiling zlib ($(ls "$EXTERNAL"/zlib/*.c | wc -l) TUs)"
for f in "$EXTERNAL"/zlib/*.c; do
    out="obj/zl_$(basename "$f").o"
    [ -f "$out" ] && continue
    $CLANG -x c -std=gnu99 -march=armv8-a $OPT -fPIC -fvisibility=hidden \
           -DZ_PREFIX -DHAVE_HIDDEN -DNDEBUG -DANDROID \
           -I"$EXTERNAL/zlib" $TGT -c "$f" -o "$out" 2>>err.log &
done
wait
echo "  objects: $(ls obj/zl_*.o 2>/dev/null | wc -l)  $(( $(date +%s)-T2c ))s"

# brotli. Its headers include il2cpp-config.h, so libil2cpp has to be on the
# include path as well as brotli's own. Without this the link still succeeds
# but the .so dies at dlopen on BrotliDecoderCreateInstance.
BR="$LIBIL2CPP/os/ClassLibraryPAL/brotli"
T2d=$(date +%s)
echo "### PHASE C4: compiling brotli ($(find -L "$BR" -name '*.c' | wc -l) TUs)"
n=0
for f in $(find -L "$BR" -name '*.c'); do
    out="obj/br_$(echo "$f" | tr -c 'A-Za-z0-9._-' '_').o"
    [ -f "$out" ] && continue
    $CLANG -x c -std=gnu99 -march=armv8-a $OPT -fPIC -fvisibility=hidden \
           -DNDEBUG -DANDROID -I"$BR/include" -I"$LIBIL2CPP" -I"$BR" \
           $TGT -c "$f" -o "$out" 2>>err.log &
    n=$((n+1)); [ $((n % JOBS)) -eq 0 ] && wait
done
wait
echo "  objects: $(ls obj/br_*.o 2>/dev/null | wc -l)  $(( $(date +%s)-T2d ))s"

# Objects whose source has disappeared must go, or the link silently keeps
# code for types that no longer exist -- which is exactly the kind of stale
# state that produces an internally inconsistent player.
stale=0
for o in obj/g*.o obj/c*.o obj/r*.o; do
    [ -f "$o" ] || continue
    case "$o" in obj/zgc_*|obj/zl_*) continue ;; esac
    grep -q "^$o\$" obj/.objs 2>/dev/null || { rm -f "$o"; stale=$((stale+1)); }
done
[ "$stale" -gt 0 ] && echo "### pruned $stale stale object(s)"

# Record what was built so the next run can skip it. Written only after every
# phase has succeeded, so an interrupted build re-does its work rather than
# trusting a half-written manifest.
mv obj/.seen "$MANIFEST"
rm -f obj/.now obj/.objs

T3=$(date +%s)
echo "### PHASE D: linking libil2cpp.so"
# baselib.a is Unity's PREBUILT arm64-android static library (shipped in the
# Editor's AndroidPlayer StaticLibs). It is not compilable from the sources in
# external/baselib, and without it the .so fails at dlopen on
# Baselib_Timer_TickToNanosecondsConversionFactor.
#
# -nostdlib++ with the NDK's static libc++: Unity's libil2cpp.so has no
# libc++_shared dependency, and the app process will not have Termux's libs.
#
# -soname matters once the library is loaded from app storage rather than out
# of the APK. The engine asks for it by name -- dlopen("libil2cpp.so") -- and
# bionic satisfies that from an already-loaded library by matching its soname.
# Preloading an absolute path only works if the library actually carries the
# name the engine will ask for; without DT_SONAME the match depends on how the
# loader derives a name from the path, which is not worth relying on.
# The link's own output goes to err.log and its STATUS is checked.
#
# This used to end in `2>&1 | head -10`, which is two problems in one. A pipe
# makes $? the status of the LAST command, so the shell saw head's success and
# never the linker's failure; and head closing the pipe after ten lines sends
# the linker SIGPIPE, so a link that had more than ten warnings to give was
# killed by the thing reading its warnings.
#
# Neither was visible, because the check below asked whether the output FILE
# existed -- and lld creates and sizes that file early, so it exists no matter
# how the link ends. The result was a build that reported success and left
# behind a libil2cpp.so that was 151 KB short of the real one and crashed in
# the dynamic linker, running an .init_array entry, before a line of game code:
#
#   #00 pc 00000000030f3740  <unknown>
#   #01 linker64 (__dl__ZN6soinfo17call_constructorsEv+752)
#
# So: no pipe, the status is kept, and a failed link takes its output with it
# rather than leaving a corpse for the installer to pick up and the engine to
# dlopen twenty minutes later.
"$USR/bin/clang++" $TGT -shared -fPIC -fuse-ld=lld -nostdlib++ \
    -Wl,-soname,libil2cpp.so \
    -o "$ROOT/libil2cpp.so" obj/*.o "$BASELIB" \
    -lc++_static -lc++abi -llog -lm -ldl -lc \
    -Wl,--allow-shlib-undefined >>err.log 2>&1
link_status=$?
echo "  $(( $(date +%s)-T3 ))s"

echo "### RESULT"
if [ "$link_status" -ne 0 ]; then
    echo "### LINK FAILED (exit $link_status)" >&2
    tail -20 err.log >&2
    rm -f "$ROOT/libil2cpp.so"
    exit 1
fi
if [ ! -f "$ROOT/libil2cpp.so" ]; then
    echo "### LINK PRODUCED NOTHING" >&2
    tail -20 err.log >&2
    exit 1
fi
ls -la "$ROOT/libil2cpp.so"
echo "total build time: $(( $(date +%s)-T0 ))s"
