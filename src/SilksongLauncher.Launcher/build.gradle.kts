// Root project — declares plugin versions for sub-modules.
// We pin to AGP 8.7+ to match Unity 6's bundled Gradle 8.11.
plugins {
    id("com.android.library") version "8.7.3" apply false
    id("com.android.application") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "2.1.0" apply false
}
