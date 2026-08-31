package dev.silksong.launcher.profiles

internal val SilksongProfile = GameProfile(
    id = "silksong",
    runtimeStorageKey = "ss",
    displayName = "Hollow Knight: Silksong",
    steamAppId = 1030300,
    steamDepotId = 1030303,
    unityVersion = "6000.0.50f1",
    currentGameVersion = "1.0.29980",
    acceptedPlatforms = setOf("LinuxPlayer"),
    dataDirectoryName = "Hollow Knight Silksong_Data",
    executableNames = setOf(
        "Hollow Knight Silksong",
        "Hollow Knight Silksong.x86_64",
    ),
    contentLayout = ContentLayout.ADDRESSABLES,
    patchSet = "silksong-patches",
    addressablesRoot = "StreamingAssets/aa/StandaloneLinux64",
)
