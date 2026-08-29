package dev.silksong.launcher.profiles

internal val HollowKnightProfile = GameProfile(
    id = "hollow-knight",
    displayName = "Hollow Knight",
    steamAppId = 367520,
    steamDepotId = 367522,
    unityVersion = "6000.0.61f1",
    currentGameVersion = "1.5.12620",
    backwardCompatibilityVersions = setOf("1.5.12612"),
    acceptedPlatforms = setOf("LinuxPlayer"),
    dataDirectoryName = "hollow_knight_Data",
    executableNames = setOf("hollow_knight.x86_64"),
    contentLayout = ContentLayout.CLASSIC_PLAYER,
    patchSet = "hollow-knight-patches",
)
