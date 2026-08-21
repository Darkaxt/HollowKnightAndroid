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
    }

    private lateinit var header: TextView
    private lateinit var stepLabel: TextView
    private lateinit var status: TextView
    private lateinit var detail: TextView
    private lateinit var progress: ProgressBar
    private lateinit var primary: Button
    private lateinit var secondary: Button
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

    // <files>/pkg mirrors an installed package: lib/<abi> beside assets/.
    private val pkgDir: File get() = File(filesDir, "pkg")
    private val engineDir: File get() = File(pkgDir, "lib/$ABI")
    // The player image and catalog, as one zip laid out like an APK. Only the
    // zip counts: the engine opens "jar:file://<package path>!/assets" and
    // reads the tree out of that archive itself, so loose files at the same
    // paths are never read.
    private val dataApk: File get() = File(pkgDir, "data.apk")

    // Where a download leaves things for us to install.
    private val stagingDir: File? get() = getExternalFilesDir(null)?.let { File(it, "staging") }

    // The depot lands on external storage: it is ~8 GB of data, not code, and
    // nothing has to execute out of it.
    private val depotDir: File? get() = getExternalFilesDir(null)?.let { File(it, "depot") }
    private val depotStagingDir: File? get() = getExternalFilesDir(null)?.let { File(it, "depot-staging") }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    override fun onDestroy() {
        super.onDestroy()
        scope.cancel()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildUi())
        // Forks a process, so it cannot be answered from refresh(). Warmed
        // once here; every later ask reads the cached result.
        scope.launch {
            withContext(Dispatchers.IO) { Toolchain.canExecute(Toolchain.rootFor(this@SetupActivity)) }
            if (!busy) refresh()
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
    private val buildSignature: String by lazy {
        val md = java.security.MessageDigest.getInstance("SHA-256")
        fun walk(path: String) {
            val names = runCatching { assets.list(path) }.getOrNull().orEmpty().sorted()
            if (names.isEmpty()) {
                runCatching { assets.open(path).use { md.update(it.readBytes()) } }
                return
            }
            for (n in names) walk("$path/$n")
        }
        walk("ondevice")
        "1|" + md.digest().joinToString("") { "%02x".format(it) }.take(16)
    }

    /** The game is built and ready to play. */
    private fun isBuilt(): Boolean =
        dataApk.isFile && File(engineDir, "libil2cpp.so").length() > 0 &&
            UnityDex.isBuilt(this, UnityFetcher.rootFor(this)) &&
            runCatching { builtMarker.readText() }.getOrNull() == buildSignature

    /** The game's own files are on this device, however they got here. */
    private fun haveGameFiles(): Boolean =
        depotDir?.let { PlayerImage.depotData(it) != null } == true

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
        return root
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

        when {
            built -> {
                status.text = message ?: "Ready to play."
                detail.text = ""
                primary.text = "Play"
                primary.visibility = View.VISIBLE
                secondary.visibility = View.GONE
            }
            readyToPort || haveGame -> {
                status.text = message ?: "Ready to port Silksong"
                detail.text =
                    "Silksong will now be ported to Android, here on this device.\n\n" +
                    "Some supporting tools are downloaded as part of this, so an internet " +
                    "connection is needed while it runs.\n\n" +
                    "Expect 20-30 minutes on a Snapdragon 8 Gen 2, depending on your network " +
                    "speed. Keep the app open while it works."
                primary.text = "Start porting"
                primary.visibility = View.VISIBLE
                secondary.visibility = View.GONE
            }
            else -> {
                status.text = message ?: "Where is your copy of Silksong?"
                detail.text = "Sign in to Steam to download it, or copy the game's files to\n" +
                    "${depotDir?.absolutePath ?: "external storage"}"
                primary.text = if (signedIn()) "Download from Steam" else "Sign in to Steam"
                primary.visibility = View.VISIBLE
                secondary.text = "I have the files"
                secondary.visibility = View.VISIBLE
            }
        }
        primary.isEnabled = true
        secondary.isEnabled = true
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
     * There is no folder picker: the game is eight gigabytes and copying it a
     * second time to satisfy a permission model is not a reasonable thing to
     * ask. The app's own external directory needs no permission and is
     * reachable over USB or from any file manager, so the answer is to look
     * there and say plainly what was expected if it is empty.
     */
    private fun onSecondary() {
        refresh()
        if (haveGameFiles()) return
        val where = depotDir?.absolutePath ?: "external storage"
        say("No game files found.")
        detail.text = "Copy the game's files -- \"Hollow Knight Silksong_Data\" and " +
            "everything beside it -- into\n$where\nthen press this again."
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
     */
    private fun moveStaged() {
        val src = stagingDir ?: return
        for (f in src.listFiles().orEmpty()) {
            val name = f.name
            val dst = when {
                name.endsWith(".so") -> File(engineDir, name)
                name == "data.apk" -> File(pkgDir, "data.apk")
                else -> continue
            }
            if (dst.length() == f.length()) { f.delete(); continue }
            dst.parentFile?.mkdirs()
            val tmp = File(dst.parentFile, "${dst.name}.part")
            f.inputStream().use { i -> tmp.outputStream().use { o -> i.copyTo(o, 1 shl 20) } }
            if (!tmp.renameTo(dst)) throw java.io.IOException("rename to $dst")
            if (name.endsWith(".so")) dst.setExecutable(true, true)
            f.delete()
            LauncherLog.log("Installed ${dst.name} (${dst.length()} bytes)")
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
        val unity = UnityFetcher.rootFor(this)
        val tools = ToolchainFetcher.rootFor(this)
        val dotnet = DotnetFetcher.rootFor(this)
        val out = Il2cppConverter.rootFor(this)
        val depot = depotDir
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
        // Nothing survives the process being reclaimed, and a screen that
        // sleeps is the most likely way for that to happen during a build
        // nobody is watching. This costs no permission.
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        scope.launch {
            try {
                // ── Step 1: the game ──────────────────────────────────────
                if (download != null && !DepotFetcher.isPresent(depot)) {
                    setStep(1, "Downloading game files from Steam")
                    DepotFetcher.download(download, depot, staging).collect { event ->
                        when (event) {
                            is DepotFetcher.Event.Progress ->
                                setBusy(true, "", event.fraction, "${event.bytes / 1024 / 1024} MB")
                            is DepotFetcher.Event.Status ->
                                setBusy(true, "", -1f, event.message)
                            DepotFetcher.Event.Done -> Unit
                        }
                    }
                }

                // ── Step 2: the supporting tools ──────────────────────────
                setStep(toolsStep, "Downloading supporting tools")
                if (!UnityFetcher.isPresent(unity)) {
                    UnityFetcher.fetch(unity).collect { setBusy(true, it.step, it.fraction, it.detail) }
                }
                // The player's Java classes arrive with the module as ordinary
                // Java bytecode, which ART cannot load. Dexing them here is
                // what lets the APK ship none of them; it takes a couple of
                // seconds. Injection happens at process start, so this only
                // has to be on disk before the game is launched.
                if (!UnityDex.isBuilt(this@SetupActivity, unity)) {
                    setBusy(true, "preparing the engine", -1f, "")
                    withContext(Dispatchers.IO) { UnityDex.build(this@SetupActivity, unity) }
                }
                if (stagingDir?.listFiles()?.isNotEmpty() == true) {
                    setBusy(true, "installing the engine", -1f, "")
                    withContext(Dispatchers.IO) { moveStaged() }
                }
                if (!ToolchainFetcher.isPresent(tools)) {
                    ToolchainFetcher.fetch(tools).collect { setBusy(true, it.step, it.fraction, it.detail) }
                }
                if (!DotnetFetcher.isPresent(dotnet)) {
                    DotnetFetcher.fetch(dotnet).collect { setBusy(true, it.step, it.fraction, it.detail) }
                }

                if (PlayerImage.depotData(depot) == null) {
                    throw java.io.IOException("the game's files are not on this device")
                }

                // ── Step 3: building ──────────────────────────────────────
                setStep(buildStep, "Building Silksong")
                if (!PackageCompiler.isPresent(out)) {
                    PackageCompiler.compile(unity, depot, dotnet, out).collect { setBusy(true, it.step, it.fraction, it.detail) }
                }
                // Always rebuilt: these are ours and change with the app, and
                // they are a few seconds to compile.
                PackageCompiler.compilePatches(unity, depot, dotnet, out, assets)
                    .collect { setBusy(true, it.step, it.fraction, it.detail) }
                if (!Il2cppConverter.isPresent(out) || Il2cppConverter.isStale(out)) {
                    Il2cppConverter.convert(unity, depot, dotnet, out).collect { setBusy(true, it.step, it.fraction, it.detail) }
                }
                NativeBuild.build(unity, tools, out, assets, install = engineDir)
                    .collect { setBusy(true, it.step, it.fraction, it.detail) }
                // Both of these are skipped when the image already matches
                // what they would produce, which is every build where the
                // conversion had nothing to do. Together they are a full
                // re-copy of the depot's serialized data and a 55 MB zip.
                if (PlayerImage.isCurrent(out, pkgDir, depot)) {
                    LauncherLog.log("player image is current; not rebuilding or repacking")
                } else {
                    PlayerImage.build(unity, depot, dotnet, out, assets, PlayerImage.contentRootFor(this@SetupActivity))
                        .collect { setBusy(true, it.step, it.fraction, it.detail) }
                    setBusy(true, "packing the player image", -1f, "")
                    withContext(Dispatchers.IO) {
                        PlayerImage.install(out, pkgDir, filesDir, depot)
                        PlayerImage.markCurrent(out, depot)
                    }
                }
                // Always: the link is in internal storage, so it can go missing
                // without anything about the image having changed.
                withContext(Dispatchers.IO) { PlayerImage.linkContent(filesDir, depot) }
                PlayerImage.retargetContent(depot, dotnet, out, assets)
                    .collect { setBusy(true, it.step, it.fraction, it.detail) }

                // Last, and only on the way out of a run that got here: this
                // is what makes the build count as finished.
                withContext(Dispatchers.IO) { builtMarker.writeText(buildSignature) }

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
