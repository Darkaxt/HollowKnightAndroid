package dev.silksong.launcher.skins.session

/** Whether the skin library may be mutated while a game session could be reading it. */
enum class LeaseMutationGate { CLEAR, ACTIVE, UNKNOWN }
