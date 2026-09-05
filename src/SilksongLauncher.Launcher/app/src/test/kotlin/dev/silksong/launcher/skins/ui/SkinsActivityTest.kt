package dev.silksong.launcher.skins.ui

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Looper
import android.widget.Button
import android.widget.LinearLayout
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.R
import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config
import org.robolectric.shadows.ShadowAlertDialog
import java.io.ByteArrayInputStream
import java.util.UUID
import java.util.concurrent.Executor

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28])
class SkinsActivityTest {
    @Test fun `default surface is unavailable and cannot launch picker`() {
        SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
        val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
        try {
            val activity = controller.get()
            assertFalse(activity.findViewById<Button>(R.id.skins_prepare_file).isEnabled)
            assertFalse(activity.findViewById<Button>(R.id.skins_advance_mode).isEnabled)
        } finally { controller.pause().stop().destroy() }
    }

    @Test fun `injected Activity picker prepares then explicit second source Replace confirms captured CAS`() {
        val fixture = Fixture()
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            val activity = controller.get()
            try {
                fixture.idle()
                activity.findViewById<Button>(R.id.skins_prepare_file).performClick()
                val launch = shadowOf(activity).nextStartedActivityForResult
                assertEquals(Intent.ACTION_OPEN_DOCUMENT, launch.intent.action)
                assertEquals("*/*", launch.intent.type)
                shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK, Intent().setData(Uri.parse("content://test/document/input")))
                assertEquals(0, fixture.opens)
                fixture.idle()
                assertEquals(1, fixture.opens)
                val buttons = activity.findViewById<LinearLayout>(R.id.skins_packs)
                val replace = (0 until buttons.childCount).map { buttons.getChildAt(it) }.filterIsInstance<Button>()
                    .single { it.text.toString().startsWith("Replace") }
                replace.performClick()
                val picker = ShadowAlertDialog.getLatestAlertDialog()
                assertTrue(fixture.replaced.isEmpty())
                picker.listView.performItemClick(picker.listView.getChildAt(1), 1, 1)
                val confirmation = ShadowAlertDialog.getLatestAlertDialog()
                assertTrue(fixture.replaced.isEmpty())
                confirmation.getButton(AlertDialog.BUTTON_POSITIVE).performClick()
                fixture.idle()
                val sent = fixture.replaced.single()
                assertEquals("b".repeat(64), sent.sourceCandidateKey)
                assertEquals(SkinReplaceTarget("target", "c".repeat(64), "d".repeat(64), "e".repeat(64)), sent.target)
                assertEquals(1, fixture.opens)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
        }
    }

    @Test fun `configuration recreation retains prepared handle and terminal exit cancels it`() {
        val fixture = Fixture()
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                activity.findViewById<Button>(R.id.skins_prepare_file).performClick()
                val launch = shadowOf(activity).nextStartedActivityForResult
                shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK, Intent().setData(Uri.parse("content://test/document/input")))
                fixture.idle()
                controller.recreate()
                fixture.idle()
                assertEquals(1, fixture.opens)
                assertEquals(0, fixture.cancels)
                assertTrue(controller.get().findViewById<Button>(R.id.skins_import_all).isEnabled)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
            assertEquals(1, fixture.cancels)
        }
    }

    @Test fun `destroyed Activity confirmation cannot mutate retained preparation`() {
        val fixture = Fixture()
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                activity.findViewById<Button>(R.id.skins_prepare_file).performClick()
                val launch = shadowOf(activity).nextStartedActivityForResult
                shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK, Intent().setData(Uri.parse("content://test/document/input")))
                fixture.idle()
                val packs = activity.findViewById<LinearLayout>(R.id.skins_packs)
                (0 until packs.childCount).map { packs.getChildAt(it) }.filterIsInstance<Button>()
                    .single { it.text.toString().startsWith("Replace") }.performClick()
                val picker = ShadowAlertDialog.getLatestAlertDialog()
                picker.listView.performItemClick(picker.listView.getChildAt(0), 0, 0)
                val staleConfirmation = ShadowAlertDialog.getLatestAlertDialog()
                controller.recreate(); fixture.idle()
                staleConfirmation.getButton(AlertDialog.BUTTON_POSITIVE).performClick(); fixture.idle()
                assertTrue(fixture.replaced.isEmpty())
                assertEquals(0, fixture.cancels)
                assertTrue(controller.get().findViewById<Button>(R.id.skins_import_all).isEnabled)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
        }
    }

    @Test fun `old preparation dialogs cannot cancel newer preparation in same Activity`() {
        val fixture = Fixture()
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                fun prepare() {
                    activity.findViewById<Button>(R.id.skins_prepare_file).performClick()
                    val launch = shadowOf(activity).nextStartedActivityForResult
                    shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK,
                        Intent().setData(Uri.parse("content://test/document/input")))
                    fixture.idle()
                }
                prepare()
                val packs = activity.findViewById<LinearLayout>(R.id.skins_packs)
                (0 until packs.childCount).map { packs.getChildAt(it) }.filterIsInstance<Button>()
                    .single { it.text.toString().startsWith("Replace") }.performClick()
                val oldPicker = ShadowAlertDialog.getLatestAlertDialog()
                oldPicker.listView.performItemClick(oldPicker.listView.getChildAt(0), 0, 0)
                val oldConfirmation = ShadowAlertDialog.getLatestAlertDialog()
                oldConfirmation.getButton(AlertDialog.BUTTON_POSITIVE).performClick(); fixture.idle()
                assertEquals(1, fixture.replaced.size)
                prepare()
                assertEquals(2, fixture.opens)
                // Retained callbacks from A must not cancel B, even though this Activity is still live.
                for (oldDialog in listOf(oldPicker, oldConfirmation)) {
                    oldDialog.getButton(AlertDialog.BUTTON_NEGATIVE).performClick(); fixture.idle()
                    assertEquals(0, fixture.cancels)
                    assertTrue(activity.findViewById<Button>(R.id.skins_import_all).isEnabled)
                    oldDialog.cancel(); fixture.idle()
                    assertEquals(0, fixture.cancels)
                    assertTrue(activity.findViewById<Button>(R.id.skins_import_all).isEnabled)
                }
                oldConfirmation.getButton(AlertDialog.BUTTON_POSITIVE).performClick(); fixture.idle()
                assertEquals(1, fixture.replaced.size)
                assertTrue(activity.findViewById<Button>(R.id.skins_import_all).isEnabled)
                // B's own cancellation is still reachable and releases exactly B.
                (0 until packs.childCount).map { packs.getChildAt(it) }.filterIsInstance<Button>()
                    .single { it.text.toString().startsWith("Replace") }.performClick()
                ShadowAlertDialog.getLatestAlertDialog().getButton(AlertDialog.BUTTON_NEGATIVE).performClick(); fixture.idle()
                assertEquals(1, fixture.cancels)
                assertFalse(activity.findViewById<Button>(R.id.skins_import_all).isEnabled)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
            assertEquals(1, fixture.cancels)
        }
    }

    @Test fun `status read error does not claim that no changes were made`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        assertFalse(context.getString(R.string.skins_read_error, "ERROR", "refresh failed").contains("No changes were made"))
    }

    private class Queue : Executor {
        val tasks = ArrayDeque<Runnable>()
        override fun execute(command: Runnable) { tasks += command }
        fun runAll() { while (tasks.isNotEmpty()) tasks.removeFirst().run() }
    }
    private class Fixture {
        val worker = Queue(); var opens = 0; var cancels = 0
        val replaced = mutableListOf<SkinReplaceRequest>()
        val imports = object : SkinImportService {
            override val available = true
            override fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle> {
                input.openOnce().close()
                return SkinResult.Ok(SkinPreparationHandle(UUID.randomUUID(), listOf(
                    CandidatePreparationSummary("61", "a".repeat(64), "First", SkinImportCode.OK, "Ready"),
                    CandidatePreparationSummary("62", "b".repeat(64), "Second", SkinImportCode.OK, "Ready"))))
            }
            override fun commitImport(handleId: UUID): SkinResult<List<SkinImportSummary>> = error("unused")
            override fun commitReplace(request: SkinReplaceRequest): SkinResult<SkinImportSummary> {
                replaced += request
                return SkinResult.Ok(SkinImportSummary("62", SkinImportCode.OK, "target", "Committed", emptyList()))
            }
            override fun cancel(handleId: UUID): SkinResult<Unit> { cancels++; return SkinResult.Ok(Unit) }
        }
        val provider = object : SkinDocumentProvider {
            override fun file(document: String) = SkinDocument(document, "input.bin", SkinDocumentKind.FILE)
            override fun children(tree: String): SkinDocumentCursor = error("unused")
            override fun open(document: String) = ByteArrayInputStream(byteArrayOf(1)).also { opens++ }
        }
        val services = SkinLibraryUiServices(HollowKnightProfile, {
            SkinResult.Ok(SkinLibraryViewState("c".repeat(64), "OFF", "target", null, emptyList(), "CLEAR", null, null, "CLEAR", listOf(
                SkinPackRow("target", "Target", "Author", "f".repeat(64), "d".repeat(64), "e".repeat(64), true, false, SkinReceiptSummary()))))
        }, imports, UnavailableSkinLibraryMutations, UnavailableSkinModeAdvancePort)
        val binding = SkinActivityHostBinding(services, provider, worker)
        fun idle() { shadowOf(Looper.getMainLooper()).idle(); worker.runAll(); shadowOf(Looper.getMainLooper()).idle() }
    }
}
