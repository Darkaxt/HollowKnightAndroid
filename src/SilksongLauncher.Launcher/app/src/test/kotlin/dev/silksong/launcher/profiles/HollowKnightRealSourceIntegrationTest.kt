package dev.silksong.launcher.profiles

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class HollowKnightRealSourceIntegrationTest {
    @Test
    fun current_source_report_matches_the_committed_exact_manifest() {
        val sourceRoot = System.getenv("HOLLOW_KNIGHT_SOURCE_ROOT")
        val reportPath = System.getenv("HOLLOW_KNIGHT_INVENTORY_REPORT")
        assumeTrue(!sourceRoot.isNullOrBlank() && !reportPath.isNullOrBlank())
        val context = ApplicationProvider.getApplicationContext<Context>()
        val manifest = ProfileManifestLoader.load(
            context.assets,
            "profiles/hollow-knight-1.5.12620.json",
        )
        val report = HollowKnightSourceReportLoader.load(File(reportPath!!))
        val validator = HollowKnightSourceValidator(
            GameProfiles.require("hollow-knight"),
            HollowKnightSourceReporter { report },
        )

        val result = validator.validate(File(sourceRoot!!), manifest)

        assertEquals(SourceValidationStatus.SUPPORTED, result.status)
    }
}
