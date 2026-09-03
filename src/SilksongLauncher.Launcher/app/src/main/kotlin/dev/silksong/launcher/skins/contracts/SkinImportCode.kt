package dev.silksong.launcher.skins.contracts

enum class SkinImportCode {
    OK,
    INVALID_INPUT,
    UNSUPPORTED_RAR,
    LIMIT_EXCEEDED,
    UNSUPPORTED_ZIP,
    ZIP_CORRUPT,
    PATH_REJECTED,
    PATH_COLLISION,
    AMBIGUOUS_LAYOUT,
    NO_CANDIDATE,
    TARGET_COLLISION,
    PNG_INVALID,
    DOCUMENT_INVALID,
    ID_COLLISION,
    DURABILITY_UNAVAILABLE,
    OBJECT_CORRUPT,
    IMPORT_RECEIPT_CORRUPT,
    REGISTRY_CORRUPT,
    REIMPORT_CHANGED,
    CANDIDATE_ALREADY_INSTALLED,
    REGISTRY_CONFLICT,
    REGISTRY_GENESIS_CORRUPT,
    REGISTRY_RECOVERY_AMBIGUOUS,
    REGISTRY_UNRECOVERABLE,
    SESSION_RECOVERY_AMBIGUOUS,
    PROFILE_QUOTA_EXCEEDED,
    NO_SELECTED_SKIN,
    ADAPTER_BLOCKED,
    LIFECYCLE_BLOCKED,
    ROLLBACK_FAILED,
    INDETERMINATE,
}

sealed interface SkinResult<out T> {
    data class Ok<T>(val value: T) : SkinResult<T>
    data class Error(val code: SkinImportCode, val detail: String) : SkinResult<Nothing>
}

inline fun <T, R> SkinResult<T>.map(transform: (T) -> R): SkinResult<R> = when (this) {
    is SkinResult.Ok -> SkinResult.Ok(transform(value))
    is SkinResult.Error -> this
}
