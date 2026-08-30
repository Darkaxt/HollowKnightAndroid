package dev.silksong.launcher.runtime

import android.content.Intent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class LauncherRuntimeProviderTest {
    @Test
    fun `production package without metadata uses production runtime`() {
        val runtime = LauncherRuntimeProvider.resolve(
            packageName = "io.github.darkaxt.dualsouls",
            className = null,
            classLoader = javaClass.classLoader!!,
        )

        assertTrue(runtime is ProductionLauncherRuntime)
        assertEquals(EvidenceKind.ARM64_DEVICE, runtime.evidenceKind)
    }

    @Test
    fun `exact lab package loads declared runtime`() {
        val runtime = LauncherRuntimeProvider.resolve(
            packageName = LauncherRuntimeProvider.LAB_PACKAGE,
            className = FakeRuntime::class.java.name,
            classLoader = javaClass.classLoader!!,
        )

        assertTrue(runtime is FakeRuntime)
        assertEquals(EvidenceKind.EMULATOR_FAKE, runtime.evidenceKind)
    }

    @Test
    fun `production package cannot request lab runtime`() {
        assertThrows(IllegalStateException::class.java) {
            LauncherRuntimeProvider.resolve(
                packageName = "io.github.darkaxt.dualsouls",
                className = FakeRuntime::class.java.name,
                classLoader = javaClass.classLoader!!,
            )
        }
    }

    @Test
    fun `lab package without metadata fails closed`() {
        assertThrows(IllegalStateException::class.java) {
            LauncherRuntimeProvider.resolve(
                packageName = LauncherRuntimeProvider.LAB_PACKAGE,
                className = null,
                classLoader = javaClass.classLoader!!,
            )
        }
    }

    @Test
    fun `missing runtime class fails closed`() {
        assertThrows(IllegalStateException::class.java) {
            LauncherRuntimeProvider.resolve(
                packageName = LauncherRuntimeProvider.LAB_PACKAGE,
                className = "missing.Runtime",
                classLoader = javaClass.classLoader!!,
            )
        }
    }

    @Test
    fun `unassignable runtime class fails closed`() {
        assertThrows(IllegalStateException::class.java) {
            LauncherRuntimeProvider.resolve(
                packageName = LauncherRuntimeProvider.LAB_PACKAGE,
                className = String::class.java.name,
                classLoader = javaClass.classLoader!!,
            )
        }
    }

    class FakeRuntime : LauncherRuntime {
        override val evidenceKind = EvidenceKind.EMULATOR_FAKE

        override fun inspect(request: RuntimeRequest): RuntimeState =
            RuntimeState(ready = false, generationId = null, detail = "fake")

        override suspend fun provision(
            request: ProvisionRequest,
            emit: suspend (RuntimeProgress) -> Unit,
        ): RuntimeState = RuntimeState(ready = true, generationId = "fake", detail = "ready")

        override fun reset(request: RuntimeRequest): Long = 0L

        override fun gameIntent(request: RuntimeRequest): Intent = Intent()
    }
}
