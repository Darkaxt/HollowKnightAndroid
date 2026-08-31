package dev.silksong.launcher.runtime

enum class LaunchEligibilityCode {
    READY,
    NOT_READY,
    GAME_PROCESS_ACTIVE,
    GAME_PROCESS_STATE_UNKNOWN,
}

data class LaunchEligibilityResult(
    val code: LaunchEligibilityCode,
    val runtime: RuntimeState,
)

object LaunchEligibility {
    fun evaluate(
        runtime: RuntimeState,
        gameProcessActive: Boolean,
    ): LaunchEligibilityResult = evaluate(
        runtime,
        if (gameProcessActive) GameProcessState.ACTIVE else GameProcessState.INACTIVE,
    )

    fun evaluate(
        runtime: RuntimeState,
        gameProcessState: GameProcessState,
    ): LaunchEligibilityResult = LaunchEligibilityResult(
        code = when {
            gameProcessState == GameProcessState.ACTIVE -> LaunchEligibilityCode.GAME_PROCESS_ACTIVE
            gameProcessState == GameProcessState.UNKNOWN ->
                LaunchEligibilityCode.GAME_PROCESS_STATE_UNKNOWN
            runtime.ready -> LaunchEligibilityCode.READY
            else -> LaunchEligibilityCode.NOT_READY
        },
        runtime = runtime,
    )
}
