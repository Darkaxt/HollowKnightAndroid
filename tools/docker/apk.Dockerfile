# syntax=docker/dockerfile:1
#
# Silksong Android port — APK build environment, with no Unity in it.
#
# The APK contains no game content and nothing Unity-made: it fetches Unity's
# toolchain, downloads the user's own Steam depot and builds the game on the
# device. So the only Unity artefact this image's build needs is the Android
# *player module*, and that is not installed here — tools/ondevice-il2cpp/
# fetch-unity.sh pulls it at run time from Unity's CDN into the bind-mounted
# checkout, which is the same download, from the same URL and digest, that the
# app makes on the phone. Nothing Unity-licensed is baked into this image.
#
# That module also supplies Gradle 8.11, so Gradle is not installed here
# either. What is left is genuinely generic:
#
#   JDK 17     — javac/jar for the APK shell, and the JVM Gradle and d8 run on.
#                Pinned, not discovered: d8 rejects class files newer than it
#                understands, and a JDK 25 on PATH emits major version 69,
#                which fails the dex step with no obvious connection to the JDK.
#   Android SDK— platform android.jar and build-tools (d8/aapt2/zipalign/
#                apksigner). No NDK and no CMake: the launcher has no native
#                code of its own, and the game's own libraries are compiled on
#                the phone.
#   .NET 8 SDK — tools/bundle-surgery, which Gradle stages into the APK and
#                will not build without.
#   bsdtar     — the Android module arrives as a macOS .pkg, which is a xar
#                archive wrapping a gzipped cpio. libarchive reads both.
#
# amd64: Google ships no Linux arm64 build-tools, so aapt2 and zipalign are
# x86-64 ELF regardless of host. On Apple Silicon this runs under emulation.
ARG TARGETPLATFORM=linux/amd64
FROM --platform=linux/amd64 eclipse-temurin:17-jdk-jammy

SHELL ["/bin/bash", "-o", "pipefail", "-c"]

# libarchive-tools -> bsdtar, for the .pkg the Android module ships as.
# The rest are what the build scripts shell out to.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libarchive-tools \
        curl ca-certificates \
        unzip zip xz-utils \
        git make \
        python3 \
        file \
        coreutils findutils gawk sed \
    && ln -sf /usr/bin/python3 /usr/bin/python \
    && rm -rf /var/lib/apt/lists/*

# ── Android SDK ────────────────────────────────────────────────────────────
#
# Bootstrapped from a pinned command-line tools zip, which then installs the
# current "latest" and hands over to it. Pinning only the bootstrap keeps this
# building as new platforms appear: an old sdkmanager does not know about
# packages published after it, and android-36 is newer than any fixed zip we
# could name here.
ENV ANDROID_HOME=/opt/android-sdk \
    ANDROID_SDK_ROOT=/opt/android-sdk
ARG CMDLINE_TOOLS_BUILD=11076708

RUN set -eux; \
    mkdir -p "$ANDROID_HOME/cmdline-tools"; \
    curl -fsSL -o /tmp/cmdline-tools.zip \
        "https://dl.google.com/android/repository/commandlinetools-linux-${CMDLINE_TOOLS_BUILD}_latest.zip"; \
    unzip -q /tmp/cmdline-tools.zip -d /tmp/cmdline-tools; \
    mv /tmp/cmdline-tools/cmdline-tools "$ANDROID_HOME/cmdline-tools/bootstrap"; \
    rm -rf /tmp/cmdline-tools.zip /tmp/cmdline-tools; \
    printf 'y\n%.0s' $(seq 1 50) | "$ANDROID_HOME/cmdline-tools/bootstrap/bin/sdkmanager" --licenses >/dev/null; \
    "$ANDROID_HOME/cmdline-tools/bootstrap/bin/sdkmanager" --install "cmdline-tools;latest" >/dev/null; \
    rm -rf "$ANDROID_HOME/cmdline-tools/bootstrap"

ENV PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$PATH"

# Versions are the launcher's, not arbitrary:
#   android-36  app/build.gradle.kts compileSdk
# build-tools 35 as well as 36 because AGP 8.7 defaults to 35.0.0 and would
# otherwise try to download it mid-build; build.sh independently picks the
# highest installed, so 36 is what ends up dexing and signing.
ARG ANDROID_PLATFORM=36
RUN set -eux; \
    printf 'y\n%.0s' $(seq 1 50) | sdkmanager --licenses >/dev/null; \
    sdkmanager --install \
        "platform-tools" \
        "platforms;android-${ANDROID_PLATFORM}" \
        "build-tools;36.0.0" \
        "build-tools;35.0.0" >/dev/null; \
    chmod -R a+rX "$ANDROID_HOME"

# ── .NET 8 ─────────────────────────────────────────────────────────────────
#
# Via Microsoft's install script rather than apt: Ubuntu's archive and
# packages.microsoft.com both provide dotnet-* and having both configured is a
# well-known source of unresolvable conflicts. The script writes one
# self-contained tree and has no package-manager opinion at all.
ENV DOTNET_ROOT=/usr/share/dotnet
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$DOTNET_ROOT" --no-path \
    && ln -s "$DOTNET_ROOT/dotnet" /usr/local/bin/dotnet \
    && rm -f /tmp/dotnet-install.sh \
    && dotnet --info >/dev/null

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    # .NET 8 maps JIT pages W^X by default, which qemu-user's x86-64 emulation
    # mishandles: `dotnet build` dies with "uncaught target signal 11" on an
    # arm64 host running this amd64 image -- which is the normal case here,
    # since Google ships no arm64 Linux build-tools and the image must be
    # amd64. Reverting to the .NET 7 mapping costs nothing measurable natively.
    DOTNET_EnableWriteXorExecute=0

# Where the Android player module is unpacked. Deliberately outside
# /workspace: the checkout is bind-mounted, and reading the copy the *host*
# fetched into build/ would make this build depend on host state -- it would
# work here and behave differently on a clean machine. Compose keeps this in a
# named volume so the 642 MB download still happens only once.
ENV UNITY_PLAYER_ROOT=/opt/unity-player

# Gradle writes its caches here. Declared so compose can keep them in a named
# volume: without that, every run re-resolves AGP, Kotlin and the launcher's
# dependencies from the network.
ENV GRADLE_USER_HOME=/gradle

# dev.sh looks for a JDK before it looks at PATH, and JAVA_HOME is what it
# finds first once Unity's bundled one is not there.
ENV JAVA_HOME=/opt/java/openjdk

COPY apk-entrypoint.sh /usr/local/bin/silksong-apk
RUN chmod +x /usr/local/bin/silksong-apk

WORKDIR /workspace
ENTRYPOINT ["/usr/local/bin/silksong-apk"]
CMD ["apk"]
