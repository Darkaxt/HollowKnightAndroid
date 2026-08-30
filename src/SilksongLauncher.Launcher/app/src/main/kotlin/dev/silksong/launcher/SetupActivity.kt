// SetupActivity — the first screen, and the app's entry point.
//
// The APK ships neither the game nor the engine. Unity's redistributables are
// fetched from Unity, and the game itself is built on the device out of the
// user's own Steam depot. On a fresh install almost nothing the player needs
// is present yet.
//
// There is exactly one question worth asking: where is your copy of the game?
// Either it is already on this device, or it is in a Steam library that has to
// be signed into. Everything after that answer is machinery -- fetching a
// compiler, a .NET runtime, Unity's toolchain, converting, compiling, packing
// -- and none of it is a decision anybody can usefully make.
//
// An earlier version of this screen listed seven requirements with three
// buttons and left the user to work out which to press in what order. It was
// showing them the shape of the pipeline instead of the shape of the task.
//
// So there are three screens, in order:
//
//   1. Where is your copy of the game?
//   2. What is about to happen, and roughly how long it will take. Answering
//      the first question does not start half an hour of work on its own --
//      signing in is not consent to begin, and a screen that starts working
//      the moment a login returns gives nobody the chance to plug in first.
//   3. Progress, as three numbered steps rather than eleven internal stages.
//      The stages are real but they are not the user's model of the job; the
//      steps are "get the game", "get the tools", "build it".
//
// Nothing here is a layout resource: this screen is a few rows of text and two
// buttons, and building it in code keeps the first screen the app opens
// independent of resource merging.

package dev.silksong.launcher

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.net.Uri
import android.os.Bundle
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import dev.silksong.launcher.profiles.ContentLayout
import dev.silksong.launcher.profiles.LegacySilksongAdopter
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.ProfileManifestLoader
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.build.BuildStage
import dev.silksong.launcher.build.GenerationMetadata
import dev.silksong.launcher.build.GenerationProvenance
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.build.ProfileBuildCoordinator
import dev.silksong.launcher.build.ProfileBuildRequest
import dev.silksong.launcher.build.UnityToolchainRegistry
import dev.silksong.launcher.runtime.EvidenceKind
import dev.silksong.launcher.runtime.LauncherRuntimeProvider
import dev.silksong.launcher.runtime.ProvisionRequest
import dev.silksong.launcher.runtime.ProvisionSource
import dev.silksong.launcher.runtime.RuntimeProgress
import dev.silksong.launcher.runtime.RuntimeRequest
import dev.silksong.launcher.runtime.ProductionBuildSignature
import java.util.UUID

class SetupActivity : Activity() {

    private companion object {
        // Where setup hands off to. The launcher is the app's home screen --
        // Steam login, cloud saves, and the button that starts the game; this
        // screen exists only to get the device to the point where that screen
        // has something to launch.
        private const val LAUNCHER_ACTIVITY_CLASS = "dev.silksong.launcher.LauncherActivity"

        // Mirrors the layout Android installs, so the engine's own library
        // lookup can be pointed at it. GameActivity documents why.
        private const val ABI = "arm64"

        private const val REQ_LOGIN = 1
        private const val REQ_PICK_DEPOT = 2
        private const val REQ_STORAGE = 3
    }

    private lateinit var header: TextView
    private lateinit var stepLabel: TextView
    private lateinit var status: TextView
    private lateinit var detail: TextView
    private lateinit var progress: ProgressBar
    private lateinit var primary: Button
    private lateinit var secondary: Button
    private lateinit var resetBuild: Button
    private lateinit var changeFolder: Button
    private var busy = false
    // Set by an action to explain what just happened; cleared when the next
    // one starts. Null means the state summary is shown instead.
    private var message: String? = null

    // The user has answered the question -- signed in, or the files are here
    // -- but has not yet said to begin. This is the state the explanation
    // screen is shown in, and it exists so that answering does not silently
    // start half an hour of downloading and compiling.
    private var readyToPort = false
    private var pendingCreds: TokenStore.Credentials? = null

    // Which numbered step is running, out of how many. Two when the game is
    // already on the device, three when it has to be downloaded first.
    private var stepNumber = 0
    private var stepCount = 3
    private var stepTitle = ""

    private val profile by lazy { SelectedGameStore(this).get() }
    private val unityDescriptor by lazy { UnityToolchainRegistry.resolve(profile) }
    private val buildPaths by lazy {
        ProfileBuildPaths(
            filesDir,
            requireNotNull(getExternalFilesDir(null)) { "No external files directory" },
            profile,
        )
    }
    private val runtime by lazy { LauncherRuntimeProvider.from(this) }
    private val runtimeRequest by lazy { RuntimeRequest(this, profile, buildPaths) }
    private val generationPublisher by lazy { GenerationPublisher(buildPaths.profilePaths) }

    // The current immutable generation mirrors an installed package:
    // lib/<abi> beside data.apk. The legacy package path remains readable so
    // existing Silksong installs survive adoption until the first rebuild.
    private val pkgDir: File
        get() = generationPublisher.current()
            ?.let { File(it.root, "pkg") }
            ?: buildPaths.packageDir
    private val engineDir: File get() = File(pkgDir, "lib/$ABI")
    // The player image and catalog, as one zip laid out like an APK. Only the
    // zip counts: the engine opens "jar:file://<package path>!/assets" and
    // reads the tree out of that archive itself, so loose files at the same
    // paths are never read.
    private val dataApk: File get() = File(pkgDir, "data.apk")

    // Where a download leaves things for us to install.
    private val stagingDir: File? get() = buildPaths.installStaging

    // The depot lands on external storage: it is ~8 GB of data, not code, and
    // nothing has to execute out of it.
    //
    // Every volume is considered, not just the primary one, and so is a folder
    // the user picked. DepotLocation owns that order and the reasons for it;
    // this screen asks rather than knowing.
    private val depotDirs: List<File> get() = DepotLocation.candidates(this, buildPaths)
    private val depotDir: File? get() = DepotLocation.resolve(this, buildPaths)
    private val depotStagingDir: File? get() = buildPaths.depotStaging

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    override fun onDestroy() {
        super.onDestroy()
        scope.cancel()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (profile.id == "silksong") {
            val adoption = LegacySilksongAdopter.adopt(
                filesDir,
                requireNotNull(getExternalFilesDir(null)) { "No external files directory" },
                buildPaths,
            )
            if (adoption.moved.isNotEmpty()) {
                LauncherLog.log("adopted legacy Silksong state: ${adoption.moved.joinToString()}")
            }
            if (adoption.conflicts.isNotEmpty()) {
                LauncherLog.log("legacy Silksong state kept beside existing profile state: ${adoption.conflicts.joinToString()}")
            }
        }
        setContentView(buildUi())
        // Forks a process, so it cannot be answered from refresh(). Warmed
        // once here; every later ask reads the cached result.
        if (runtime.evidenceKind == EvidenceKind.ARM64_DEVICE) {
            scope.launch {
                withContext(Dispatchers.IO) { Toolchain.canExecute(Toolchain.rootFor(this@SetupActivity)) }
                if (!busy) refresh()
            }
        }
    }

    override fun onResume() {
        super.onResume()
        if (busy) return
        // Anything already downloaded is moved into place without being asked
        // for: the user has no way of knowing that a file in one directory has
        // to be copied to another before it counts.
        if (stagingDir?.listFiles()?.isNotEmpty() == true) {
            installStaged()
            return
        }
        refresh()
        if (isBuilt()) startLauncher()
    }

    // ── what state are we in ───────────────────────────────────────────────

    /**
     * Written as the last act of a successful run, and required before the
     * build counts as done.
     *
     * The two big outputs are each written atomically, so neither can be
     * truncated under its final name -- but they are written at different
     * points, and the content retarget runs after both. Judging "built" by
     * their presence therefore accepts a run that died in between: a fresh
     * engine beside the previous run's data, or a data package whose content
     * was never retargeted. Both look finished and fail later, somewhere with
     * no connection to the cause.
     *
     * The file holds a signature of what produced it, so a new app -- new
     * patches, new player-image logic -- invalidates the previous build
     * instead of being told it has nothing to do. Same reasoning as
     * Il2cppConverter.isStale, one layer up.
     */
    private val builtMarker: File get() = File(pkgDir, ".built")

    /**
     * What the current app would produce, as a string.
     *
     * The on-device assets and nothing else: the patch sources, the build
     * script and the tools that run there are what end up in the built game.
     * Signing with the APK's own timestamp would be simpler and wrong -- it
     * changes on every install, so editing a settings screen would throw away
     * a good build and charge the user twenty minutes to get an identical one
     * back.
     */
    private val buildSignature: String by lazy { ProductionBuildSignature.compute(this) }

    /**
     * The game is built and ready to play.
     *
     * The depot counts, and not only because the build reads it: the content
     * is never copied inside the app, so a game whose files have been deleted
     * or moved since it was built is not ready to play, however finished the
     * build is. Answering yes would send the user to a Play button that starts
     * a world with nothing in it.
     */
    private fun isBuilt(): Boolean = runCatching {
        if (runtime.evidenceKind == EvidenceKind.EMULATOR_FAKE) {
            runtime.inspect(runtimeRequest).ready
        } else {
            dataApk.isFile && File(engineDir, "libil2cpp.so").length() > 0 &&
                UnityDex.isBuilt(this, unityDescriptor, UnityFetcher.rootFor(filesDir, profile)) &&
                haveGameFiles() &&
                runCatching { builtMarker.readText() }.getOrNull() == buildSignature
        }
    }.getOrElse {
        LauncherLog.log("Current generation is not usable", it)
        false
    }

    /** The game's own files are on this device, however they got here. */
    private fun haveGameFiles(): Boolean =
        runtime.evidenceKind == EvidenceKind.EMULATOR_FAKE ||
            depotDir?.let {
                PlayerImage.depotData(it) != null && PlayerImage.foreignBuild(it) == null
            } == true

    /**
     * The wrong platform's copy, and where it is, or null when there is none.
     *
     * Only asked once there is no usable depot, so a stray Windows folder
     * beside a working Linux one is nobody's problem: [DepotLocation.resolve]
     * has already passed over it, and a screen that complained about it would
     * be complaining about a build that is going to work.
     *
     * Across every candidate rather than the resolved one, because a macOS
     * copy is never resolved at all -- its data directory sits a level below
     * where [PlayerImage.depotData] looks -- and "where is your copy of
     * Silksong" is the wrong thing to say to someone whose copy is right
     * there. See [PlayerImage.foreignBuild].
     */
    private fun wrongBuild(): Pair<File, PlayerImage.ForeignBuild>? {
        if (haveGameFiles()) return null
        return depotDirs.firstNotNullOfOrNull { dir ->
            PlayerImage.foreignBuild(dir)?.let { dir to it }
        }
    }

    /**
     * Says so in the log, once per folder.
     *
     * The screen is where this gets read, and the log is where it gets
     * reported: the whole reason this check exists is a bug report that could
     * not say which copy of the game it was about. [refresh] runs on every
     * resume and every state change, so the folder is remembered to keep one
     * mistake from filling the file.
     */
    private var notedWrongBuild: String? = null

    private fun noteWrongBuild(where: File, build: PlayerImage.ForeignBuild) {
        val key = "${where.absolutePath}:${build.depot}"
        if (notedWrongBuild == key) return
        notedWrongBuild = key
        LauncherLog.log(
            "depot: ${build.label} build (depot ${build.depot}) in ${where.absolutePath}; " +
                "this port needs the Linux depot ${DepotFetcher.DEPOT_ID}",
        )
    }

    /** Signed in to Steam, so the depot can be downloaded. */
    private fun signedIn(): Boolean = TokenStore(this).read() != null

    // ── UI ─────────────────────────────────────────────────────────────────

    private fun buildUi(): View {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0D0A0B"))
            setPadding(dp(28), dp(28), dp(28), dp(28))
            gravity = Gravity.CENTER_VERTICAL
        }

        header = text("Silksong Android", 30f, Color.WHITE, bold = true)
        root.addView(header)
        // "Step 2 of 3" -- present only while something is running.
        stepLabel = text("", 12f, Color.parseColor("#7D3341"), bold = true).apply {
            setPadding(0, dp(16), 0, 0)
            visibility = View.GONE
        }
        root.addView(stepLabel)
        status = text("", 16f, Color.WHITE).apply {
            setPadding(0, dp(18), 0, dp(6))
        }
        root.addView(status)
        detail = text("", 13f, Color.parseColor("#B5A9AC")).apply {
            setPadding(0, 0, 0, dp(18))
            setLineSpacing(dp(4).toFloat(), 1f)
        }
        root.addView(detail)

        // Hidden until something is actually running: a bar sitting at zero
        // reads as broken.
        progress = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
            max = 1000
            visibility = View.INVISIBLE
        }
        root.addView(progress, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(22)
        })

        val buttons = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        // Same palette as the launcher: the action that moves things forward
        // is the red one, and there is never more than one of those on screen.
        primary = Button(this).apply {
            setOnClickListener { onPrimary() }
            backgroundTintList = android.content.res.ColorStateList.valueOf(Color.parseColor("#7D3341"))
            setTextColor(Color.WHITE)
            setPadding(paddingLeft, dp(16), paddingRight, dp(16))
        }
        secondary = Button(this).apply {
            setOnClickListener { onSecondary() }
            backgroundTintList = android.content.res.ColorStateList.valueOf(Color.parseColor("#B4AEB2"))
            setTextColor(Color.parseColor("#0D0A0B"))
            setPadding(paddingLeft, dp(16), paddingRight, dp(16))
        }
        buttons.addView(primary, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
            marginEnd = dp(10)
        })
        buttons.addView(secondary, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        root.addView(buttons)

        // Below the actions, small and quiet, but on THIS screen rather than
        // only in the launcher: setup is where the failures that need
        // reporting happen, and a user who never gets past it would otherwise
        // never reach a log screen at all. The reset is here for the same
        // reason -- a build that went wrong is looked at from this screen, and
        // sending someone into the settings menu to get out of it is a detour
        // through a screen about something else.
        val quiet = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.END
        }
        resetBuild = quietButton("Reset build") { confirmClearBuild() }
        changeFolder = quietButton("Change folder") { chooseFolder() }
        quiet.addView(changeFolder)
        quiet.addView(resetBuild)
        quiet.addView(quietButton("View logs") {
            try {
                startActivity(Intent(this@SetupActivity, LogActivity::class.java))
            } catch (t: Throwable) {
                say("Could not open the logs: ${t.message}")
            }
        })
        root.addView(quiet, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        return root
    }

    /** The small text-only actions under the buttons that matter. */
    private fun quietButton(label: String, onClick: () -> Unit) = Button(this).apply {
        text = label
        setOnClickListener { onClick() }
        backgroundTintList = android.content.res.ColorStateList.valueOf(Color.TRANSPARENT)
        setTextColor(Color.parseColor("#7A6E71"))
        textSize = 13f
        setPadding(0, dp(10), 0, 0)
    }

    /**
     * The two quiet actions, which are recovery tools rather than a menu.
     *
     * Both are gone once the game is built. This screen is not where a built
     * game is administered -- the launcher is, and its settings screen already
     * carries the reset; onResume forwards there the moment a build is found,
     * so the built state here lasts only for the moment after a port finishes.
     * Offering "reset the build" beside "Play" at exactly that moment invites
     * undoing the half hour that just ran.
     *
     * Changing the folder is not needed there either, and that is by
     * construction rather than by luck: the depot is part of isBuilt, so files
     * that are moved or deleted put this screen straight back into the state
     * where the picker is the main button.
     */
    private fun updateReset(built: Boolean = isBuilt()) {
        val offer = !busy && !built
        resetBuild.visibility =
            if (offer && BuildReset.hasBuild(buildPaths)) View.VISIBLE else View.GONE
        // Not while the big "choose folder" button is up: the same action
        // twice on one screen is a question about which one is different.
        changeFolder.visibility =
            if (offer && secondary.visibility != View.VISIBLE) View.VISIBLE else View.GONE
    }

    /**
     * Puts the screen into the state the device is actually in.
     *
     * Four of them, and they are a sequence rather than a menu: no copy of the
     * game, a copy (or a sign-in) but nothing started, built, or busy doing
     * one of those.
     */
    private fun refresh() {
        if (busy) return
        progress.visibility = View.INVISIBLE
        stepLabel.visibility = View.GONE
        val built = isBuilt()
        val haveGame = haveGameFiles()
        val foreign = wrongBuild()

        when {
            built -> {
                status.text = message ?: "Ready to play."
                detail.text = ""
                primary.text = "Play"
                primary.visibility = View.VISIBLE
                secondary.visibility = View.GONE
            }
            // Ahead of "ready to port", and ahead of a Steam download that
            // would otherwise unpack the Linux depot on top of this one and
            // leave a folder that is half of each.
            foreign != null -> {
                val (where, build) = foreign
                noteWrongBuild(where, build)
                status.text = "That is the ${build.label} version of Silksong"
                detail.text = "Found in\n${where.absolutePath}\n\n" +
                    "This port is built from the Linux version, depot " +
                    "${DepotFetcher.DEPOT_ID}. Install Silksong for Linux in Steam, or " +
                    "download that depot with DepotDownloader, then point this at it."
                primary.visibility = View.GONE
                secondary.text = "Choose folder"
                secondary.visibility = View.VISIBLE
            }
            readyToPort || haveGame -> {
                status.text = message ?: "Ready to port Silksong"
                detail.text =
                    "Silksong will now be ported to Android, here on this device.\n\n" +
                    "Some supporting tools are downloaded as part of this, so an internet " +
                    "connection is needed while it runs.\n\n" +
                    "Expect 20-30 minutes on a Snapdragon 8 Gen 2, depending on your network " +
                    "speed. Keep the app open while it works. Don't delete the game's files " +
                    "afterwards: they are read from where they are every time you play."
                primary.text = "Start porting"
                primary.visibility = View.VISIBLE
                secondary.visibility = View.GONE
            }
            else -> {
                status.text = message ?: "Where is your copy of Silksong?"
                detail.text = "Sign in to Steam to download it, or choose the folder you " +
                    "copied the game's Linux files into.\n\n" +
                    "Copying them to\n${depotDir?.absolutePath ?: "external storage"}\nworks too."
                primary.text = if (signedIn()) "Download from Steam" else "Sign in to Steam"
                primary.visibility = View.VISIBLE
                secondary.text = "Choose folder"
                secondary.visibility = View.VISIBLE
            }
        }
        primary.isEnabled = true
        secondary.isEnabled = true
        updateReset(built)
    }

    /**
     * Names the step that is now running, and how many there are.
     *
     * Three when the game has to be downloaded, two when it is already here.
     * The stages underneath are finer than this and are shown in the detail
     * line, but the numbered step is what tells someone how far through the
     * job they are.
     */
    private fun setStep(number: Int, title: String) {
        stepNumber = number
        stepTitle = title
    }

    /**
     * One place that owns "something long is running".
     *
     * The buttons go away, the bar appears, and the screen says which step it
     * is on, what that step is doing right now, and how far along it is.
     */
    private fun setBusy(running: Boolean, sub: String = "", fraction: Float = -1f, note: String = "") {
        busy = running
        if (running) message = null
        primary.visibility = if (running) View.GONE else View.VISIBLE
        secondary.visibility = if (running) View.GONE else View.VISIBLE
        updateReset()
        progress.visibility = if (running) View.VISIBLE else View.INVISIBLE
        stepLabel.visibility = if (running && stepNumber > 0) View.VISIBLE else View.GONE
        // The heading says what the app is doing, not what it is called: this
        // screen is on for half an hour and "Silksong" alone reads as an idle
        // title screen rather than work in progress.
        header.text = if (running) "Porting Silksong" else "Silksong Android"
        if (!running) return
        stepLabel.text = "STEP $stepNumber OF $stepCount"
        status.text = stepTitle.ifEmpty { sub }
        // The sub-stage belongs with the percentage rather than in the
        // headline: it changes several times within one step, and a heading
        // that rewrites itself every few seconds reads as churn.
        val parts = mutableListOf<String>()
        if (fraction >= 0f) {
            progress.isIndeterminate = false
            progress.progress = (fraction * 1000).toInt()
            // A long step sits on "0%" for minutes, which reads as stuck. A
            // decimal place moves early enough to show it is not.
            val pct = fraction * 100
            parts += if (pct < 10f) String.format("%.1f%%", pct) else "${pct.toInt()}%"
        } else {
            progress.isIndeterminate = true
        }
        if (stepTitle.isNotEmpty() && sub.isNotEmpty() && sub != stepTitle) parts += sub
        if (note.isNotEmpty()) parts += note
        detail.text = parts.joinToString("   ")
    }

    /** Shows a message that survives the next refresh. */
    private fun say(text: String) {
        message = text
        status.text = text
    }

    private fun text(s: String, sp: Float, colour: Int, bold: Boolean = false) =
        TextView(this).apply {
            text = s
            setTextColor(colour)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
            if (bold) setTypeface(typeface, Typeface.BOLD)
        }

    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    // ── Actions ────────────────────────────────────────────────────────────

    private fun onPrimary() {
        when {
            isBuilt() -> startLauncher()
            readyToPort || haveGameFiles() -> startPort()
            else -> onSteamClicked()
        }
    }

    /**
     * "I have the files."
     *
     * The game is eight gigabytes, so the answer cannot be to copy it a second
     * time into somewhere this app can see. It has to be used where it already
     * is -- and that means a real path, not a Storage Access Framework URI:
     * the catalog is repointed at a symlink to the depot's own content tree,
     * and the retarget rewrites that tree in place through a .NET process.
     * DepotLocation carries the whole argument.
     *
     * So: the system folder picker, resolved to a path, checked before it is
     * kept. Storage permission is asked for first, because without it the
     * picked folder is a name this app cannot open -- the picker grants access
     * to a URI, and what happens here is file access to a path.
     *
     * The app's own external directory is still searched first and still
     * works. Nobody who already copied the game there has to do anything.
     */
    private fun onSecondary() {
        refresh()
        if (haveGameFiles()) return
        for (dir in depotDirs) LauncherLog.log("depot $dir: ${PlayerImage.depotProblem(dir)}")
        chooseFolder()
    }

    /** Ask for what the picker needs, then open it. */
    private fun chooseFolder() {
        if (!DepotLocation.hasPermission(this)) {
            requestPermissions(DepotLocation.PERMISSIONS, REQ_STORAGE)
            return
        }
        pickDepotFolder()
    }

    private fun pickDepotFolder() {
        try {
            @Suppress("DEPRECATION")
            startActivityForResult(DepotLocation.pickIntent(), REQ_PICK_DEPOT)
        } catch (t: Throwable) {
            LauncherLog.log("no folder picker on this device", t)
            sayWhereToCopy("This device has no folder picker.")
        }
    }

    /**
     * What was picked, if anything usable was.
     *
     * "Not usable" is never left at that. The person doing this is looking at
     * their own files and needs to know which of the several possible things
     * went wrong -- the wrong folder, an unfinished copy, or a card this app
     * is allowed to read and not to write. All of it goes to the log too,
     * because the report of this failing reaches us second-hand.
     */
    private fun onDepotPicked(uri: Uri?) {
        if (uri == null) {
            refresh()
            return
        }
        val before = depotDir
        // Taken even though the path is what gets used: it costs nothing and
        // it is the only durable record that this folder was granted at all.
        runCatching {
            contentResolver.takePersistableUriPermission(
                uri,
                Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION,
            )
        }
        val dir = DepotLocation.pathFor(uri)
        LauncherLog.log("picked $uri -> ${dir?.absolutePath ?: "no path behind it"}")
        if (dir == null) {
            sayWhereToCopy("That folder is not on this device's own storage.")
            return
        }
        val problem = DepotLocation.problemWith(profile, dir)
        if (problem != null) {
            LauncherLog.log("rejected ${dir.name}: ${PlayerImage.depotProblemSummary(dir)}")
            say("That folder cannot be used.")
            detail.text = "$dir\n\n$problem"
            return
        }
        DepotLocation.remember(buildPaths, dir)
        DepotLocation.writeMarker(dir)
        // A different folder is a different content tree, and the built game
        // reads that tree directly rather than a copy. It has almost certainly
        // never been retargeted -- and the stamp that would say so identifies
        // a tree by what is in it, so it cannot tell "already done" from
        // "never seen". Dropping it, and the marker that says the build
        // finished, sends the user back through a run that redoes the retarget
        // and skips everything else.
        if (before != null && before.absolutePath != dir.absolutePath) {
            LauncherLog.log("depot moved from $before; the content will be retargeted again")
            PlayerImage.invalidateContent(buildPaths.buildRoot)
            builtMarker.delete()
        }
        message = null
        refresh()
    }

    /**
     * The route that has always worked, offered when picking cannot.
     *
     * The app's own external directory needs no permission and is reachable
     * over USB or from any file manager, so it is the fallback whatever the
     * device does about anything else.
     */
    private fun sayWhereToCopy(why: String) {
        val where = DepotLocation.appDirs(this, profile).firstOrNull()
        if (where == null) {
            say("No external storage to look in.")
            return
        }
        say(why)
        detail.text = "Copy the game's Linux files to\n$where\nso that " +
            "\"Hollow Knight Silksong_Data\" and everything beside it are in there, " +
            "then press this again.\n\nLooked there just now and ${PlayerImage.depotProblem(where)}."
    }

    // Moves what has been downloaded into where it gets used. Android will not
    // map code out of external storage, so the engine has to come inside
    // before anything can load it.
    //
    // This is the same move GameActivity makes when the game starts, in the
    // same layout, so whichever happens first the other finds nothing to do.
    private fun installStaged() {
        setBusy(true, "Installing", -1f, "moving files into place")
        scope.launch {
            val ok = withContext(Dispatchers.IO) {
                try { moveStaged(); true } catch (t: Throwable) {
                    LauncherLog.log("Installing staged files failed", t); false
                }
            }
            setBusy(false)
            if (!ok) say("Could not install the downloaded files - see the log.")
            refresh()
        }
    }

    /**
     * The move itself, with no UI around it.
     *
     * Each file goes through a temporary name, so an interrupted copy is never
     * left looking like a complete library.
     *
     * Everything staged is installed, with no attempt to decide that a copy
     * can be skipped. This used to skip when the sizes matched, which is not a
     * safe question -- see NativeBuild.installTo, where exactly that shipped a
     * stale engine -- and there is nothing to gain by asking it: a staged file
     * is deleted once it is in, so the only way to find one here is for it to
     * be new.
     */
    private fun moveStaged(destinationPackageDir: File = buildPaths.packageDir) {
        val src = stagingDir ?: return
        val destinationEngineDir = File(destinationPackageDir, "lib/$ABI")
        for (f in src.listFiles().orEmpty()) {
            val name = f.name
            val dst = when {
                name.endsWith(".so") -> File(destinationEngineDir, name)
                name == "data.apk" -> File(destinationPackageDir, "data.apk")
                else -> continue
            }
            dst.parentFile?.mkdirs()
            val tmp = File(dst.parentFile, "${dst.name}.part")
            f.inputStream().use { i -> tmp.outputStream().use { o -> i.copyTo(o, 1 shl 20) } }
            if (!tmp.renameTo(dst)) throw java.io.IOException("rename to $dst")
            if (name.endsWith(".so")) dst.setExecutable(true, true)
            f.delete()
            LauncherLog.log("Installed ${dst.name} (${dst.length()} bytes)")
        }
    }

    /**
     * Throw the build away and build it again.
     *
     * The same thing the settings screen offers, and deliberately the same
     * words: this deletes what the build produced and keeps everything that
     * was expensive to fetch, and the sentence people need to read before
     * pressing it should not exist in two versions. It is the recovery from a
     * build that finished wrong, so it belongs on the screen where that is
     * being looked at.
     */
    private fun confirmClearBuild() {
        android.app.AlertDialog.Builder(this)
            .setTitle(R.string.settings_clear_build_title)
            .setMessage(R.string.settings_clear_build_message)
            .setNegativeButton(R.string.settings_cancel, null)
            .setPositiveButton(R.string.settings_clear_build_confirm) { _, _ -> clearBuild() }
            .show()
    }

    /**
     * Gigabytes across thousands of small files, so not on the main thread --
     * and not cancellable, because a half-deleted build is the state this
     * exists to get out of.
     */
    private fun clearBuild() {
        setBusy(true, "Resetting", -1f, "deleting what the build produced")
        scope.launch {
            val failure = withContext(Dispatchers.IO) {
                runCatching { runtime.reset(runtimeRequest) }.exceptionOrNull()
            }
            setBusy(false)
            if (failure != null) {
                LauncherLog.log("could not clear the build", failure)
                say("Could not reset: ${failure.message ?: failure.javaClass.simpleName}")
            } else if (haveGameFiles()) {
                say("Reset. The game will be built again.")
            } else {
                say("Reset. The selected game source is no longer available.")
            }
            refresh()
        }
    }

    // ── Steam ──────────────────────────────────────────────────────────────

    // Sign in first if we have to. The token from QR sign-in is what the
    // downloader logs on with, so there is nothing else to ask the user for.
    //
    // Signing in does not start the port: it answers the question, and the
    // next screen explains what answering it leads to.
    private fun onSteamClicked() {
        val creds = TokenStore(this).read()
        if (creds == null) {
            @Suppress("DEPRECATION")
            startActivityForResult(Intent(this, LoginActivity::class.java), REQ_LOGIN)
            return
        }
        offerToPort(creds)
    }

    @Deprecated("The Activity Result APIs would pull in androidx for one call")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        @Suppress("DEPRECATION")
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQ_PICK_DEPOT) {
            onDepotPicked(if (resultCode == RESULT_OK) data?.data else null)
            return
        }
        if (requestCode != REQ_LOGIN) return
        if (resultCode != RESULT_OK || data == null) {
            say("Sign-in cancelled.")
            return
        }
        val account = data.getStringExtra(LoginActivity.EXTRA_ACCOUNT)
        val token = data.getStringExtra(LoginActivity.EXTRA_TOKEN)
        if (account.isNullOrEmpty() || token.isNullOrEmpty()) {
            say("Sign-in returned nothing usable.")
            return
        }
        val creds = TokenStore.Credentials(account, token)
        TokenStore(this).write(creds)
        offerToPort(creds)
    }

    /**
     * The answer to the storage prompt the folder picker needs.
     *
     * Refusing it is not a dead end: the app's own external directory is
     * readable without any permission at all, so the fallback is named rather
     * than the request being repeated.
     */
    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode != REQ_STORAGE) return
        if (DepotLocation.hasPermission(this)) {
            pickDepotFolder()
        } else {
            sayWhereToCopy("Without storage access the app cannot read a folder you choose.")
        }
    }

    // ── The whole thing ────────────────────────────────────────────────────

    /** Move to the explanation screen, with the game still to be downloaded. */
    private fun offerToPort(creds: TokenStore.Credentials) {
        pendingCreds = creds
        readyToPort = true
        message = null
        refresh()
    }

    /**
     * Begin. The game is downloaded first when it is not already here, so the
     * long network step happens while the user is still watching, rather than
     * after twenty minutes of quiet work.
     */
    private fun startPort() {
        val creds = if (haveGameFiles()) null else (pendingCreds ?: TokenStore(this).read())
        run(download = creds)
    }

    /**
     * Every step, in order, behind one progress bar.
     *
     * Each stage is skipped when its output is already present, so this is
     * also the resume path: a run that was interrupted picks up where it
     * stopped rather than starting again. That is what makes it safe to put
     * behind a single button.
     *
     * Grouped into the three the user was told about: get the game, get the
     * tools, build it. The internal stages are finer than that and several of
     * them are interleaved by necessity -- the engine has to be unpacked
     * before its classes can be dexed, for instance -- but the grouping is
     * honest about which part of the job is running.
     */
    private fun run(download: TokenStore.Credentials?) {
        if (runtime.evidenceKind == EvidenceKind.EMULATOR_FAKE) {
            runSynthetic()
            return
        }
        require(profile.contentLayout == ContentLayout.ADDRESSABLES) {
            "The production setup pipeline does not yet support ${profile.contentLayout}: ${profile.id}"
        }
        val unity = UnityFetcher.rootFor(filesDir, profile)
        val tools = ToolchainFetcher.rootFor(this)
        val out = buildPaths.buildRoot
        // A download goes to the app's own directory and never to a folder the
        // user picked: the resume path deletes files it did not write (see
        // DepotFetcher.dropUnwritten), which is only ever safe somewhere
        // nothing else lives. When the game is already on the device, that
        // copy is used wherever it is.
        val depot = if (download != null) DepotLocation.downloadTarget(buildPaths) else depotDir
        val staging = depotStagingDir
        if (depot == null || staging == null) {
            say("No external storage to work in.")
            return
        }

        // Two steps when the game is already here, three when it is not.
        stepCount = if (download != null) 3 else 2
        val toolsStep = if (download != null) 2 else 1
        val buildStep = toolsStep + 1

        setStep(if (download != null) 1 else toolsStep, "Starting")
        setBusy(true, "", -1f, "")
        LauncherLog.log("depot: $depot, data: ${PlayerImage.depotData(depot)}")
        // Nothing survives the process being reclaimed, and a screen that
        // sleeps is the most likely way for that to happen during a build
        // nobody is watching. This costs no permission.
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        scope.launch {
            try {
                // ── Step 1: the game ──────────────────────────────────────
                if (download != null && !DepotFetcher.isPresent(profile, depot)) {
                    setStep(1, "Downloading game files from Steam")
                    DepotFetcher.download(profile, download, depot, staging).collect { event ->
                        when (event) {
                            is DepotFetcher.Event.Progress ->
                                setBusy(true, "", event.fraction, "${event.bytes / 1024 / 1024} MB")
                            is DepotFetcher.Event.Status ->
                                setBusy(true, "", -1f, event.message)
                            DepotFetcher.Event.Done -> Unit
                        }
                    }
                }

                val exactSourceManifest = if (profile.contentLayout == ContentLayout.CLASSIC_PLAYER) {
                    ProfileManifestLoader.load(
                        assets,
                        "profiles/${profile.id}-${profile.currentGameVersion}.json",
                    ).manifestSha256
                } else {
                    null
                }
                val request = ProfileBuildRequest(
                    jobId = "job-${UUID.randomUUID()}",
                    generationId = "gen-${UUID.randomUUID()}",
                    metadata = GenerationMetadata(
                        sourceManifestSha256 = GenerationProvenance.sourceManifestSha256(
                            profile,
                            exactSourceManifest,
                        ),
                        toolchainId = unityDescriptor.contentHash,
                        patchManifestSha256 = ProductionBuildSignature.computeSha256(
                            this@SetupActivity,
                        ),
                    ),
                )
                val coordinator = ProfileBuildCoordinator(generationPublisher)
                coordinator.run(
                    request,
                    emit = { progress ->
                        when (progress.stage) {
                            BuildStage.ValidateSource,
                            BuildStage.ResolveToolchain ->
                                setStep(toolsStep, "Preparing supporting tools")
                            else -> setStep(buildStep, "Building ${profile.displayName}")
                        }
                        setBusy(
                            true,
                            progress.stage.javaClass.simpleName,
                            progress.index.toFloat() / progress.count,
                            "",
                        )
                    },
                ) { stage, workspace ->
                    when (stage) {
                        BuildStage.ValidateSource -> {
                            if (PlayerImage.depotData(depot) == null) {
                                throw java.io.IOException("the game's files are not on this device")
                            }
                            PlayerImage.wrongBuildProblem(depot)?.let {
                                throw java.io.IOException(it)
                            }
                        }

                        BuildStage.ResolveToolchain -> {
                            if (!UnityFetcher.isPresent(unityDescriptor, unity)) {
                                UnityFetcher.fetch(
                                    unityDescriptor,
                                    unity,
                                    buildPaths.installStaging,
                                ).collect { setBusy(true, it.step, it.fraction, it.detail) }
                            }
                            if (!UnityDex.isBuilt(this@SetupActivity, unityDescriptor, unity)) {
                                setBusy(true, "preparing the engine", -1f, "")
                                withContext(Dispatchers.IO) {
                                    UnityDex.build(this@SetupActivity, unityDescriptor, unity)
                                }
                            }
                            withContext(Dispatchers.IO) {
                                UnityFetcher.ensureEngineStaged(
                                    unityDescriptor,
                                    unity,
                                    workspace.engineDir,
                                )
                            }
                            if (!ToolchainFetcher.isPresent(tools)) {
                                ToolchainFetcher.fetch(tools).collect {
                                    setBusy(true, it.step, it.fraction, it.detail)
                                }
                            }
                            MonoRuntime.stage(this@SetupActivity).collect {
                                setBusy(true, it.step, it.fraction, it.detail)
                            }
                        }

                        BuildStage.CompilePatches -> {
                            if (!PackageCompiler.isPresent(out)) {
                                PackageCompiler.compile(unity, depot, this@SetupActivity, out)
                                    .collect { setBusy(true, it.step, it.fraction, it.detail) }
                            }
                            PackageCompiler.compileIo(
                                unity,
                                depot,
                                this@SetupActivity,
                                out,
                                assets,
                            ).collect { setBusy(true, it.step, it.fraction, it.detail) }
                            PackageCompiler.compilePatches(
                                unity,
                                depot,
                                this@SetupActivity,
                                out,
                                assets,
                            ).collect { setBusy(true, it.step, it.fraction, it.detail) }
                        }

                        BuildStage.ConvertIl2Cpp -> {
                            if (!Il2cppConverter.isPresent(out) || Il2cppConverter.isStale(out)) {
                                Il2cppConverter.convert(
                                    unity,
                                    depot,
                                    this@SetupActivity,
                                    out,
                                ).collect { setBusy(true, it.step, it.fraction, it.detail) }
                            }
                        }

                        BuildStage.CompileNative -> {
                            NativeBuild.build(
                                unity,
                                tools,
                                out,
                                assets,
                                install = workspace.engineDir,
                            ).collect { setBusy(true, it.step, it.fraction, it.detail) }
                        }

                        BuildStage.ConvertContent -> {
                            PlayerImage.build(
                                profile,
                                unity,
                                depot,
                                this@SetupActivity,
                                out,
                                assets,
                                PlayerImage.contentRootFor(packageName, buildPaths),
                            ).collect { setBusy(true, it.step, it.fraction, it.detail) }
                            setBusy(true, "packing the player image", -1f, "")
                            withContext(Dispatchers.IO) {
                                PlayerImage.install(out, workspace.packageDir, buildPaths, depot)
                                PlayerImage.markCurrent(profile, out, depot)
                                DepotLocation.relink(buildPaths, depot)
                            }
                            PlayerImage.retargetContent(
                                profile,
                                depot,
                                this@SetupActivity,
                                out,
                                assets,
                            ).collect { setBusy(true, it.step, it.fraction, it.detail) }
                        }

                        BuildStage.Verify -> withContext(Dispatchers.IO) {
                            val targetMarker = File(workspace.packageDir, ".built")
                            targetMarker.writeText(buildSignature)
                            check(File(workspace.packageDir, "data.apk").length() > 0L) {
                                "player image was not produced"
                            }
                            for (library in listOf("libunity.so", "libmain.so", "libil2cpp.so")) {
                                check(File(workspace.engineDir, library).length() > 0L) {
                                    "$library was not produced"
                                }
                            }
                        }

                        BuildStage.Publish -> Unit
                    }
                }

                readyToPort = false
                pendingCreds = null
                say("The game is ready.")
            } catch (t: Throwable) {
                LauncherLog.log("Setup failed", t)
                say("Failed: ${t.message}")
            } finally {
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                setBusy(false)
                refresh()
            }
        }
    }

    private fun runSynthetic() {
        stepCount = 1
        setStep(1, "Preparing ${profile.displayName}")
        setBusy(true, "", -1f, "")
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        scope.launch {
            try {
                val state = runtime.provision(
                    ProvisionRequest(runtimeRequest, ProvisionSource.Synthetic),
                ) { progress: RuntimeProgress ->
                    setBusy(true, progress.stage, progress.fraction, progress.detail)
                }
                readyToPort = false
                pendingCreds = null
                say(state.detail)
            } catch (t: Throwable) {
                LauncherLog.log("Synthetic setup failed", t)
                say("Failed: ${t.message}")
            } finally {
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                setBusy(false)
                refresh()
            }
        }
    }

    private fun startLauncher() {
        LauncherLog.log("Setup complete, opening $LAUNCHER_ACTIVITY_CLASS")
        try {
            startActivity(Intent().setClassName(packageName, LAUNCHER_ACTIVITY_CLASS))
            // Setup is done and there is nothing to come back to: leaving it
            // on the stack would put it behind the back button forever.
            finish()
        } catch (t: Throwable) {
            say("Could not open the launcher: ${t.message}")
            LauncherLog.log("Failed to open the launcher: $t")
        }
    }
}
