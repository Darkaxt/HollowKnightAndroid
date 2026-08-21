// DepotFetcher — downloads the game from the user's own Steam account.
//
// Nothing game-derived ships in the APK, so the content has to come from the
// person running it, out of the library they already own. That is what this
// does: it signs in with the refresh token QR sign-in produced, and pulls the
// depot straight from Steam's CDN onto the device.
//
// The download itself is JavaSteam's, not a port of one. It is the same
// library the QR sign-in already uses, which means one Steam stack, one
// connection, and no handing tokens to a second process to do the work.
//
// (There is an older C# depot downloader in src/SilksongLauncher/Steam/ built
// on SteamKit2. It still works, but using it here would mean running .NET to
// download and re-implementing sign-in on that side to get a session it could
// use. This route needs neither.)

package dev.silksong.launcher

import `in`.dragonbra.javasteam.depotdownloader.DepotDownloader
import `in`.dragonbra.javasteam.depotdownloader.IDownloadListener
import `in`.dragonbra.javasteam.depotdownloader.data.AppItem
import `in`.dragonbra.javasteam.depotdownloader.data.DownloadItem
import `in`.dragonbra.javasteam.util.log.LogListener
import `in`.dragonbra.javasteam.util.log.LogManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.isActive
import java.io.File
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

object DepotFetcher {

    /** Hollow Knight: Silksong. */
    const val APP_ID = 1030300

    /**
     * The Linux depot.
     *
     * The port is built around the Linux build specifically: its shaders carry
     * a Vulkan slice that Android can load once retargeted, and its assemblies
     * are what the IL2CPP converter is fed. The Windows depot would need a
     * different path for both.
     */
    const val DEPOT_ID = 1030303

    /** The depot's own layout, and what the build pipeline looks for. */
    const val DATA_DIR = "Hollow Knight Silksong_Data"

    private const val BRANCH = "public"

    /**
     * Writes were what was starved, not connections: with one file written at
     * a time, chunks arriving for every other file in a 2068-file depot have
     * nowhere to go. Raising that took the download from 8.8 to 20 MB/s.
     *
     * The tail is where it still shows. Once the large files are done, what
     * remains is thousands of small ones, and the cost per file -- allocate,
     * write, checksum, move out of staging -- stops overlapping with anything.
     * Measured there: 5.5 MB/s while a plain download on the same link at the
     * same moment managed 17, with the cores unclamped. Nothing was throttled;
     * the pipeline had simply run out of files to work on at once.
     *
     * Chunk workers stay at eight. Sixteen ran the heap out of room, for a
     * part of the pipeline that was not the limit anyway.
     */
    private const val MAX_CONCURRENT = 8
    private const val MAX_FILE_WRITES = 16

    sealed class Event {
        /** 0f..1f across the depot, with what has been transferred so far. */
        data class Progress(val fraction: Float, val bytes: Long) : Event()
        data class Status(val message: String) : Event()
        data object Done : Event()
    }

    /**
     * True once a download has run all the way to the end.
     *
     * This used to ask only whether the game's data directory existed, which
     * is true from the moment the downloader creates its first file. A
     * cancelled download therefore looked exactly like a finished one, so the
     * next run skipped the download step entirely and the depot was never
     * resumed -- it just stayed half-fetched, permanently.
     *
     * The failure surfaced nowhere near here. The missing files were sparse
     * (see [dropUnwritten]), so the build ran for the best part of an hour and
     * then died in bundle-surgery with "signature not supported" on a bundle
     * that was the right length and entirely zeroes.
     *
     * So: a marker, written only after the downloader reports it is done. Same
     * rule the toolchain and dotnet fetchers already use, and for the same
     * reason -- "some of it is on disk" is not the question anyone is asking.
     */
    fun isPresent(installDir: File): Boolean =
        completeMarker(installDir).isFile && isPresentOnDisk(installDir)

    /** The tree looks right, regardless of whether a run ever finished. */
    private fun isPresentOnDisk(installDir: File): Boolean =
        installDir.walkTopDown().maxDepth(4).any { it.isDirectory && it.name == DATA_DIR }

    private fun completeMarker(installDir: File) = File(installDir, ".download-complete")

    /**
     * Deletes files the last attempt allocated but never filled.
     *
     * The downloader sets each file to its final length before it has the
     * bytes, so a cancelled run leaves its in-flight files at full size with
     * nothing behind them: sparse, all zeroes, and indistinguishable from real
     * content until something tries to parse one.
     *
     * They cannot heal on their own. Resume compares lengths against the
     * manifest, and a full-length hole matches, so the downloader skips it
     * every time it is asked. Deleting them is what turns "skip, it's the
     * right size" into "fetch, it isn't there" -- and it is why this runs
     * before the download rather than after.
     *
     * st_blocks is the filesystem's own answer to "is there anything actually
     * stored here", which is why this does not read the files. A file that was
     * partly written has blocks and survives; that is a narrower hole than the
     * one being closed here, and closing it properly means hashing the depot
     * against the manifest.
     */
    private fun dropUnwritten(installDir: File): Int {
        // Collected before anything is deleted: removing entries from under a
        // directory walk that is still reading them is its own bug.
        val holes = ArrayList<File>()
        for (f in installDir.walkTopDown()) {
            if (!f.isFile || f.length() == 0L) continue
            val blocks = try {
                android.system.Os.stat(f.absolutePath).st_blocks
            } catch (e: android.system.ErrnoException) {
                continue
            }
            if (blocks == 0L) holes.add(f)
        }
        return holes.count { it.delete() }
    }

    /**
     * Signs in and downloads the depot into [installDir].
     *
     * Resumable: re-running skips whatever already matches the manifest, which
     * matters for an 8 GB download on a connection that may well drop.
     */
    fun download(
        credentials: TokenStore.Credentials,
        installDir: File,
        stagingDir: File,
    ): Flow<Event> = channelFlow {
        // Held because the nested use-blocks below shadow this scope, and the
        // wait loop still needs to know whether the collector is still there.
        val producer = this
        installDir.mkdirs()
        stagingDir.mkdirs()

        // Before anything is asked of Steam: a previous attempt that was
        // cancelled has left holes the resume cannot see. See dropUnwritten.
        val dropped = dropUnwritten(installDir)
        if (dropped > 0) LauncherLog.log("dropped $dropped unwritten file(s) from a previous attempt")

        // JavaSteam logs through its own LogManager, which has no listener by
        // default, so everything it has to say about a stalled download is
        // discarded. It is the only view into what the downloader is doing.
        LogManager.addListener(logForwarder)

        LauncherLog.log("Downloading depot $DEPOT_ID to ${installDir.absolutePath}")
        try {
            SteamSession().use { session ->
                session.logOn(credentials)

                // The downloader needs to know what the account may download,
                // and Steam sends that on its own callback shortly after
                // logon rather than in the reply.
                val licenses = session.licenses()
                LauncherLog.log("Account has ${licenses.size} licence(s)")

                val finished = CountDownLatch(1)
                val failure = AtomicReference<Throwable?>(null)

                DepotDownloader(
                    steamClient = session.steamClient,
                    licenses = licenses,
                    maxDownloads = MAX_CONCURRENT,
                    maxFileWrites = MAX_FILE_WRITES,
                ).use { downloader ->
                    downloader.addListener(object : IDownloadListener {
                        // Chunk-level, so the bar moves steadily rather than
                        // in file-sized jumps -- some of these files are
                        // hundreds of megabytes.
                        override fun onChunkCompleted(
                            depotId: Int,
                            depotPercentComplete: Float,
                            compressedBytes: Long,
                            uncompressedBytes: Long,
                        ) {
                            trySend(Event.Progress(depotPercentComplete, uncompressedBytes))
                        }

                        override fun onStatusUpdate(message: String) {
                            trySend(Event.Status(message))
                        }

                        override fun onDepotCompleted(
                            depotId: Int,
                            compressedBytes: Long,
                            uncompressedBytes: Long,
                        ) {
                            LauncherLog.log("Depot $depotId complete: $uncompressedBytes bytes")
                        }

                        override fun onDownloadCompleted(item: DownloadItem) {
                            finished.countDown()
                        }

                        override fun onDownloadFailed(item: DownloadItem, error: Throwable) {
                            failure.set(error)
                            finished.countDown()
                        }
                    })

                    downloader.add(
                        AppItem(
                            appId = APP_ID,
                            installDirectory = installDir.absolutePath,
                            branch = BRANCH,
                            // Without this the downloader picks the depots for
                            // the platform it is running on, which here is
                            // Android and is not a platform this game ships.
                            os = "linux",
                            depot = listOf(DEPOT_ID),
                        )
                    )
                    downloader.finishAdding()

                    // The queue is processed on the downloader's own
                    // coroutines; this waits for the listener to say it is
                    // done rather than polling.
                    while (!finished.await(1, TimeUnit.SECONDS)) {
                        if (!producer.isActive) throw java.util.concurrent.CancellationException()
                    }
                }

                failure.get()?.let { throw RuntimeException("depot download failed: $it", it) }
                if (!isPresentOnDisk(installDir))
                    throw RuntimeException("depot downloaded but $DATA_DIR is missing")

                // Only now, and only here: everything above has to have run
                // without throwing for the depot to be worth trusting later.
                completeMarker(installDir).writeText(DEPOT_ID.toString())
                LauncherLog.log("Depot download complete")
                send(Event.Done)
            }
        } finally {
            LogManager.removeListener(logForwarder)
        }
    }.flowOn(Dispatchers.IO)

    /** Puts JavaSteam's own logging where the launcher's log can show it. */
    private val logForwarder = object : LogListener {
        override fun onLog(clazz: Class<*>, message: String?, throwable: Throwable?) =
            forward("", clazz, message, throwable)

        override fun onError(clazz: Class<*>, message: String?, throwable: Throwable?) =
            forward("ERROR ", clazz, message, throwable)

        private fun forward(level: String, clazz: Class<*>, message: String?, throwable: Throwable?) {
            LauncherLog.log("[${clazz.simpleName}] $level${message.orEmpty()}" +
                (throwable?.let { " — $it" } ?: ""))
        }
    }
}
