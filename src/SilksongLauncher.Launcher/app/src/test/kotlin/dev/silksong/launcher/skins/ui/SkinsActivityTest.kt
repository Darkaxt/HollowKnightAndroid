package dev.silksong.launcher.skins.ui

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Looper
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.R
import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SilksongProfile
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
    @Test fun `Import skin dialog offers exact file and folder intents and cancellation is silent`() {
        val fixture = Fixture()
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                activity.findViewById<Button>(R.id.skins_import).performClick()
                var dialog = ShadowAlertDialog.getLatestAlertDialog()
                assertTrue(allText(dialog.window!!.decorView as ViewGroup)
                    .contains(activity.getString(R.string.skins_import_source_title)))
                assertEquals(listOf(
                    activity.getString(R.string.skins_choose_archive),
                    activity.getString(R.string.skins_choose_folder),
                ), (0 until dialog.listView.adapter.count).map { dialog.listView.adapter.getItem(it).toString() })
                dialog.listView.performItemClick(dialog.listView.getChildAt(0), 0, 0)
                var launch = shadowOf(activity).nextStartedActivityForResult
                assertEquals(Intent.ACTION_OPEN_DOCUMENT, launch.intent.action)
                assertTrue(launch.intent.hasCategory(Intent.CATEGORY_OPENABLE))
                assertEquals("*/*", launch.intent.type)
                val messageBeforePickerCancel = activity.findViewById<TextView>(R.id.skins_notice).text
                shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_CANCELED, null)
                fixture.idle()
                assertEquals(messageBeforePickerCancel, activity.findViewById<TextView>(R.id.skins_notice).text)
                assertEquals(0, fixture.opens)

                activity.findViewById<Button>(R.id.skins_import).performClick()
                dialog = ShadowAlertDialog.getLatestAlertDialog()
                dialog.listView.performItemClick(dialog.listView.getChildAt(1), 1, 1)
                launch = shadowOf(activity).nextStartedActivityForResult
                assertEquals(Intent.ACTION_OPEN_DOCUMENT_TREE, launch.intent.action)

                activity.findViewById<Button>(R.id.skins_import).performClick()
                dialog = ShadowAlertDialog.getLatestAlertDialog()
                val messageBefore = activity.findViewById<TextView>(R.id.skins_notice).text
                dialog.cancel()
                fixture.idle()
                assertEquals(messageBefore, activity.findViewById<TextView>(R.id.skins_notice).text)
                assertEquals(0, fixture.opens)
                assertEquals(0, fixture.cancels)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
        }
    }

    @Test fun `pack card keeps technical data in Details and direct actions use session callbacks`() {
        val fixture = Fixture(mutationsAvailable = true)
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                val packList = activity.findViewById<LinearLayout>(R.id.skins_packs)
                val visible = allText(packList)
                assertTrue(visible.contains("Target"))
                assertTrue(visible.contains("Author"))
                assertFalse(visible.contains("target"))
                assertFalse(visible.contains("d".repeat(64)))
                assertFalse(visible.contains("e".repeat(64)))

                descendantButtons(packList).single { it.text == activity.getString(R.string.skins_enable) }.performClick()
                fixture.idle()
                assertEquals(listOf("enable:target"), fixture.directActions)

                descendantButtons(packList).single { it.text == activity.getString(R.string.skins_details) }.performClick()
                val details = ShadowAlertDialog.getLatestAlertDialog()
                val detailText = details.findViewById<TextView>(android.R.id.message).text.toString()
                assertTrue(detailText.contains("target"))
                assertTrue(detailText.contains("d".repeat(64)))
                assertTrue(detailText.contains("e".repeat(64)))
                details.dismiss()

                descendantButtons(packList).single { it.text == activity.getString(R.string.skins_delete) }.performClick()
                ShadowAlertDialog.getLatestAlertDialog().getButton(AlertDialog.BUTTON_POSITIVE).performClick()
                fixture.idle()
                assertEquals(listOf("target"), fixture.removed)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
        }
    }

    @Test fun `selected live card exposes Disable and blocks Delete with visible reason`() {
        val fixture = Fixture(mutationsAvailable = true, mode = "ON")
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                val packList = activity.findViewById<LinearLayout>(R.id.skins_packs)
                val buttons = descendantButtons(packList)
                val delete = buttons.single { it.text == activity.getString(R.string.skins_delete) }
                assertFalse(delete.isEnabled)
                assertTrue(allText(packList).contains(activity.getString(R.string.skins_delete_blocked)))
                buttons.single { it.text == activity.getString(R.string.skins_disable) }.performClick()
                fixture.idle()
                assertEquals(listOf("disable:target"), fixture.directActions)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
        }
    }

    @Test fun `library Details retains full authority report with Refresh and recovery`() {
        val fixture = Fixture(
            runtimeObservation = "Last game report: Applied · target · complete · 10 ms UTC; refresh to retry status",
            recoverAvailable = true,
        )
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                activity.findViewById<Button>(R.id.skins_library_details).performClick()
                val details = ShadowAlertDialog.getLatestAlertDialog()
                val text = details.findViewById<TextView>(android.R.id.message).text.toString()
                assertTrue(text.contains("target"))
                assertTrue(text.contains("Last game report"))
                assertTrue(text.contains("Lease"))
                assertEquals(activity.getString(R.string.skins_refresh),
                    details.getButton(AlertDialog.BUTTON_NEUTRAL).text.toString())
                details.getButton(AlertDialog.BUTTON_POSITIVE).performClick()
                fixture.idle()
                assertEquals(1, fixture.recoveries)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
        }
    }

    @Test fun `requested configuration and latest runtime report occupy separate compact views`() {
        val fixture = Fixture(runtimeObservation = "Last game report: Applied · target · complete · 10 ms UTC; refresh to retry status")
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                val requested = activity.findViewById<TextView>(R.id.skins_requested).text.toString()
                val runtime = activity.findViewById<TextView>(R.id.skins_runtime).text.toString()
                assertTrue(requested.contains("OFF"))
                assertTrue(requested.contains("Target"))
                assertFalse(requested.contains("Applied"))
                assertTrue(runtime.contains("Applied"))
                assertFalse(runtime.contains("target"))
                assertFalse(runtime.contains("10 ms"))
            } finally { controller.pause().stop().destroy(); fixture.idle() }
        }
    }

    @Test fun `default production surface enables ordinary ZIP picker and mode control`() {
        SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
        val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
        try {
            val activity = controller.get()
            val deadline = System.nanoTime() + java.util.concurrent.TimeUnit.SECONDS.toNanos(5)
            while (!activity.findViewById<Button>(R.id.skins_import).isEnabled && System.nanoTime() < deadline) {
                Thread.sleep(20); shadowOf(Looper.getMainLooper()).idle()
            }
            assertTrue(activity.findViewById<Button>(R.id.skins_import).isEnabled)
            assertTrue(activity.findViewById<Button>(R.id.skins_advance_mode).isEnabled)
            openArchivePicker(activity)
            assertEquals(Intent.ACTION_OPEN_DOCUMENT, shadowOf(activity).nextStartedActivityForResult.intent.action)
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
                openArchivePicker(activity)
                val launch = shadowOf(activity).nextStartedActivityForResult
                assertEquals(Intent.ACTION_OPEN_DOCUMENT, launch.intent.action)
                assertEquals("*/*", launch.intent.type)
                shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK, Intent().setData(Uri.parse("content://test/document/input")))
                assertEquals(0, fixture.opens)
                fixture.idle()
                assertEquals(1, fixture.opens)
                val picker = openReplacementSource(activity)
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

    @Test fun `prepared rows stay concise while Details retains candidate diagnostics`() {
        val fixture = Fixture()
        SkinsActivity.withHostBinding(fixture.binding) {
            SelectedGameStore(ApplicationProvider.getApplicationContext()).set(HollowKnightProfile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            try {
                fixture.idle()
                val activity = controller.get()
                openArchivePicker(activity)
                val launch = shadowOf(activity).nextStartedActivityForResult
                shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK,
                    Intent().setData(Uri.parse("content://test/document/input")))
                fixture.idle()
                val prepared = activity.findViewById<LinearLayout>(R.id.skins_prepared)
                assertFalse(allText(prepared).contains("a".repeat(64)))
                descendantButtons(prepared).first().performClick()
                val details = ShadowAlertDialog.getLatestAlertDialog()
                assertTrue(details.findViewById<TextView>(android.R.id.message).text.toString().contains("a".repeat(64)))
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
                openArchivePicker(activity)
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
                openArchivePicker(activity)
                val launch = shadowOf(activity).nextStartedActivityForResult
                shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK, Intent().setData(Uri.parse("content://test/document/input")))
                fixture.idle()
                val picker = openReplacementSource(activity)
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
                    openArchivePicker(activity)
                    val launch = shadowOf(activity).nextStartedActivityForResult
                    shadowOf(activity).receiveResult(launch.intent, Activity.RESULT_OK,
                        Intent().setData(Uri.parse("content://test/document/input")))
                    fixture.idle()
                }
                prepare()
                val oldPicker = openReplacementSource(activity)
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
                openReplacementSource(activity)
                ShadowAlertDialog.getLatestAlertDialog().getButton(AlertDialog.BUTTON_NEGATIVE).performClick(); fixture.idle()
                assertEquals(1, fixture.cancels)
                assertFalse(activity.findViewById<Button>(R.id.skins_import_all).isEnabled)
            } finally { controller.pause().stop().destroy(); fixture.idle() }
            assertEquals(1, fixture.cancels)
        }
    }

    @Test fun `production Context imports ordinary ZIP through Android decoder and exposes runtime mappings`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val isolated = object : android.content.ContextWrapper(context) {
            override fun getFilesDir() = java.io.File(context.cacheDir,"production-import").apply { mkdirs() }
        }
        val services = SkinLibraryUiServices.production(isolated,HollowKnightProfile)
        val archive = dev.silksong.launcher.skins.fixtures.RawZipFixture.build(listOf(
            dev.silksong.launcher.skins.fixtures.RawZipFixture.Entry("Blue/Knight.png".toByteArray(), dev.silksong.launcher.skins.fixtures.TinyPngFixture.rgba())))
        val prepared = services.imports.prepare(SkinImportInput.SelectedFile("Blue.zip") { archive.bytes.inputStream() })
        assertTrue(prepared.toString(),prepared is SkinResult.Ok)
        val installed = services.imports.commitImport((prepared as SkinResult.Ok).value.handleId)
        assertTrue(installed.toString(),installed is SkinResult.Ok)
        val view = (services.read() as SkinResult.Ok).value; val pack = view.packs.single()
        assertEquals("OFF",view.mode); assertFalse(pack.selected); assertFalse(pack.rotationEligible)
        val target = SkinReplaceTarget(pack.id,view.generationSha256,pack.treeSha256,pack.importReceiptSha256)
        assertTrue(services.mutations.enable(target) is SkinResult.Ok)
        val store = dev.silksong.launcher.skins.library.SkinLibraryStore.production(isolated,HollowKnightProfile)
        val wire = com.google.gson.JsonParser.parseString(dev.silksong.launcher.runtime.SkinLibraryRuntimeAccess(store).readConfiguration()).asJsonObject
        assertTrue(wire["ok"].asBoolean); assertEquals("ON",wire["mode"].asString)
        val mapping = wire["textures"].asJsonArray.single().asJsonObject
        assertEquals("Knight.png",mapping["target"].asString)
        assertTrue(mapping["path"].asString.matches(Regex("assets/[a-z2-7]{52}")))
        assertTrue(java.io.File(wire["root"].asString,mapping["path"].asString).isFile)
    }
    @Test fun `skin surface title and guidance are bound to selected game profile`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        fun text(profile: dev.silksong.launcher.profiles.GameProfile): Pair<String, String> {
            SelectedGameStore(context).set(profile)
            val controller = Robolectric.buildActivity(SkinsActivity::class.java).setup()
            return try {
                controller.get().findViewById<TextView>(R.id.skins_title).text.toString() to
                    controller.get().findViewById<TextView>(R.id.skins_availability).text.toString()
            } finally { controller.pause().stop().destroy() }
        }

        val hollowKnight = text(HollowKnightProfile)
        assertTrue(hollowKnight.first.contains("Hollow Knight"))
        assertTrue(hollowKnight.second.contains("CustomKnight"))
        assertTrue(hollowKnight.second.contains("original game files"))

        val silksong = text(SilksongProfile)
        assertTrue(silksong.first.contains("Silksong"))
        assertFalse(silksong.first.contains("Hollow Knight"))
        assertFalse(silksong.second.contains("CustomKnight"))
        assertFalse(silksong.second.contains("not enabled"))
        assertFalse(silksong.second.contains("launch Hollow Knight"))
        assertFalse(silksong.second.contains("11"))
        assertTrue(silksong.second.contains("Silksong"))
    }

    @Test fun `status read error does not claim that no changes were made`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        assertFalse(context.getString(R.string.skins_read_error, "ERROR", "refresh failed").contains("No changes were made"))
    }

    private fun openArchivePicker(activity: SkinsActivity) {
        activity.findViewById<Button>(R.id.skins_import).performClick()
        val dialog = ShadowAlertDialog.getLatestAlertDialog()
        dialog.listView.performItemClick(dialog.listView.getChildAt(0), 0, 0)
    }

    private fun openReplacementSource(activity: SkinsActivity): AlertDialog {
        val packs = activity.findViewById<LinearLayout>(R.id.skins_packs)
        descendantButtons(packs).single { it.text == activity.getString(R.string.skins_details) }.performClick()
        val details = ShadowAlertDialog.getLatestAlertDialog()
        val replace = details.getButton(AlertDialog.BUTTON_POSITIVE)
        assertTrue("Prepared replacement entry must be enabled", replace.isEnabled)
        assertTrue(replace.performClick())
        shadowOf(Looper.getMainLooper()).idle()
        return ShadowAlertDialog.getLatestAlertDialog().also { assertNotSame(details, it) }
    }

    private fun descendantButtons(root: ViewGroup): List<Button> = buildList {
        for (index in 0 until root.childCount) when (val child = root.getChildAt(index)) {
            is Button -> add(child)
            is ViewGroup -> addAll(descendantButtons(child))
        }
    }
    private fun allText(root: ViewGroup): String = buildList {
        for (index in 0 until root.childCount) when (val child = root.getChildAt(index)) {
            is TextView -> add(child.text.toString())
            is ViewGroup -> add(allText(child))
        }
    }.joinToString("\n")

    private class Queue : Executor {
        val tasks = ArrayDeque<Runnable>()
        override fun execute(command: Runnable) { tasks += command }
        fun runAll() { while (tasks.isNotEmpty()) tasks.removeFirst().run() }
    }
    private class Fixture(
        mutationsAvailable: Boolean = false,
        private val runtimeObservation: String? = null,
        private val mode: String = "OFF",
        recoverAvailable: Boolean = false,
    ) {
        val worker = Queue(); var opens = 0; var cancels = 0
        val replaced = mutableListOf<SkinReplaceRequest>()
        val directActions = mutableListOf<String>()
        val removed = mutableListOf<String>()
        var recoveries = 0
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
        private val mutations = if (mutationsAvailable) object : SkinLibraryMutations {
            override val available = true
            override fun select(target: SkinReplaceTarget) = SkinResult.Ok(Unit)
            override fun enable(target: SkinReplaceTarget): SkinResult<Unit> {
                directActions += "enable:${target.id}"
                return SkinResult.Ok(Unit)
            }
            override fun disable(target: SkinReplaceTarget): SkinResult<Unit> {
                directActions += "disable:${target.id}"
                return SkinResult.Ok(Unit)
            }
            override fun eligibility(target: SkinReplaceTarget, eligible: Boolean) = SkinResult.Ok(Unit)
            override fun remove(target: SkinReplaceTarget): SkinResult<Unit> {
                removed += target.id
                return SkinResult.Ok(Unit)
            }
        } else UnavailableSkinLibraryMutations
        val services = SkinLibraryUiServices(HollowKnightProfile, {
            SkinResult.Ok(SkinLibraryViewState("c".repeat(64), mode, "target", null, emptyList(), "CLEAR", null, null, "CLEAR", listOf(
                SkinPackRow("target", "Target", "Author", "f".repeat(64), "d".repeat(64), "e".repeat(64), true, false, SkinReceiptSummary())),
                simplifiedAuthority = true, runtimeObservation = runtimeObservation))
        }, imports, mutations, UnavailableSkinModeAdvancePort,
            simplifiedAuthority = true,
            recover = if (recoverAvailable) ({ recoveries++; SkinResult.Ok(Unit) }) else null,
        )
        val binding = SkinActivityHostBinding(services, provider, worker)
        fun idle() { shadowOf(Looper.getMainLooper()).idle(); worker.runAll(); shadowOf(Looper.getMainLooper()).idle() }
    }
}
