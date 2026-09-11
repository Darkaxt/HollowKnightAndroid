import hashlib
import json
import pathlib
import subprocess
import tempfile
import unittest


REPO_ROOT = pathlib.Path(__file__).parents[3]
LAUNCHER = REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "src" / "main" / "kotlin" / "dev" / "silksong" / "launcher"


class ProfileModPipelineContractTest(unittest.TestCase):
    def _package_manifest_fixture(self, root):
        root = pathlib.Path(root)
        packages = root / "packages"
        packages.mkdir()
        authorities = {}
        for name, content in (
            ("bcl.dll", b"bcl"),
            ("core.dll", b"core"),
            ("Assembly-CSharp.dll", b"game"),
        ):
            path = root / name
            path.write_bytes(content)
            authorities[name] = path
        assemblies = {}
        for name in (
            "Unity.InputSystem.dll",
            "HollowKnightPatches.dll",
            "0Harmony.dll",
            "BepInEx.dll",
        ):
            path = packages / name
            path.write_bytes(name.encode("utf-8"))
            assemblies[name] = path

        digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
        values = {
            "schema": "1",
            "profile": "hollow-knight",
            "unityVersion": "6000.0.61f1",
            "roslynVersion": "4.12.0",
            "steamDepotId": "367522",
            "gameVersion": "1.5.12620",
            "unityMscorlibSha256": digest(authorities["bcl.dll"]),
            "androidCoreModuleSha256": digest(authorities["core.dll"]),
            "depotAssemblySha256": digest(authorities["Assembly-CSharp.dll"]),
        }
        values.update({f"assembly.{name}": digest(path) for name, path in assemblies.items()})
        manifest = packages / "dualsouls-package-assemblies-v1.properties"
        manifest.write_text("\n".join(f"{key}={value}" for key, value in values.items()) + "\n", encoding="utf-8")
        return packages, authorities, assemblies, values, manifest

    def _validate_package_manifest(self, packages, authorities):
        script = REPO_ROOT / "tools" / "bepinex-shim" / "package-manifest.ps1"
        quote = lambda value: "'" + str(value).replace("'", "''") + "'"
        command = (
            f". {quote(script)}; "
            "try { Test-ProductionPackageManifest "
            f"-PackageDirectory {quote(packages)} -Profile 'hollow-knight' "
            "-UnityVersion '6000.0.61f1' -RoslynVersion '4.12.0' "
            "-DepotId '367522' -GameVersion '1.5.12620' "
            f"-BclPath {quote(authorities['bcl.dll'])} "
            f"-PlayerCorePath {quote(authorities['core.dll'])} "
            f"-DepotAssemblyPath {quote(authorities['Assembly-CSharp.dll'])} | Out-Null; exit 0 "
            "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"
        )
        return subprocess.run(
            ["pwsh", "-NoProfile", "-NonInteractive", "-Command", command],
            text=True,
            capture_output=True,
            check=False,
        )

    def test_package_manifest_validator_accepts_exact_profile_package(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, _, _, _ = self._package_manifest_fixture(temp)
            result = self._validate_package_manifest(packages, authorities)
            self.assertEqual(0, result.returncode, result.stderr)

    def test_package_manifest_validator_rejects_wrong_profile_and_stale_digest(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, assemblies, values, manifest = self._package_manifest_fixture(temp)
            manifest.write_text(
                manifest.read_text(encoding="utf-8").replace("profile=hollow-knight", "profile=silksong"),
                encoding="utf-8",
            )
            wrong = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, wrong.returncode)
            self.assertIn("profile does not match", wrong.stderr)

            values["profile"] = "hollow-knight"
            manifest.write_text("\n".join(f"{key}={value}" for key, value in values.items()) + "\n", encoding="utf-8")
            assemblies["BepInEx.dll"].write_bytes(b"changed after manifest publication")
            stale = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, stale.returncode)
            self.assertIn("digest is stale", stale.stderr)

    def test_package_manifest_validator_rejects_opposite_profile_assembly(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, _, _, _ = self._package_manifest_fixture(temp)
            (packages / "SilksongPatches.dll").write_bytes(b"wrong profile")
            result = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Opposite-profile package assembly is present", result.stderr)

    def test_package_manifest_validator_rejects_empty_and_missing_selected_patch(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, assemblies, values, manifest = self._package_manifest_fixture(temp)
            manifest.unlink()
            empty = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, empty.returncode)
            self.assertIn("manifest is missing", empty.stderr)

            manifest.write_text("\n".join(f"{key}={value}" for key, value in values.items()) + "\n", encoding="utf-8")
            assemblies["HollowKnightPatches.dll"].unlink()
            missing = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, missing.returncode)
            self.assertIn("Required production package assembly is missing: HollowKnightPatches.dll", missing.stderr)

    def test_clean_host_test_gate_builds_the_weaver_first(self):
        source = (REPO_ROOT / "Makefile").read_text(encoding="utf-8")

        self.assertIn("test: weaver ##", source)
        self.assertRegex(source, r"(?s)\.PHONY:.*\bweaver\b")

    def test_setup_builds_and_converts_mod_support_in_the_selected_profile(self):
        source = (LAUNCHER / "SetupActivity.kt").read_text(encoding="utf-8")

        self.assertIn("PackageCompiler.compileShims(", source)
        self.assertIn("PackageCompiler.publishAssemblyManifest(profile, unity, depot, out)", source)
        self.assertIn("Il2cppConverter.isStale(profile, out, mods, assets)", source)
        self.assertIn("mods = mods,", source)
        self.assertIn("assets = assets,", source)
        self.assertIn("Mods.ensure(mods, buildPaths.modStateRoot)", source)
        self.assertIn("Mods.stageForGeneration(out, workspace.root)", source)

    def test_conversion_records_only_candidate_mod_metadata(self):
        converter = (LAUNCHER / "Il2cppConverter.kt").read_text(encoding="utf-8")

        self.assertIn("Mods.snapshotForBuild(mods, root)", converter)
        self.assertIn("Mods.recordCandidate(modInput, root, assets)", converter)
        self.assertNotIn("Mods.markCurrent", converter)

    def test_setup_heading_is_not_hard_coded_to_silksong(self):
        source = (LAUNCHER / "SetupActivity.kt").read_text(encoding="utf-8")

        self.assertNotIn('"Silksong Android"', source)
        self.assertNotIn('"Porting Silksong"', source)
        self.assertIn('"Building ${profile.displayName}"', source)
        self.assertIn("text(profile.displayName", source)

    def test_launcher_and_mod_screen_use_published_profile_generation_authority(self):
        launcher = (LAUNCHER / "LauncherActivity.kt").read_text(encoding="utf-8")
        mods_activity = (LAUNCHER / "ModsActivity.kt").read_text(encoding="utf-8")
        display_model = (LAUNCHER / "ModsDisplayModel.kt").read_text(encoding="utf-8")

        self.assertIn("GenerationPublisher(buildPaths.profilePaths).current()", launcher)
        self.assertIn("Mods.publishedMetadataRoot(published)", launcher)
        self.assertIn("Mods.writeCurrentGates(", launcher)
        self.assertNotIn("Mods.isStale(Mods.dir(this), out", launcher)
        self.assertIn("GenerationPublisher(buildPaths.profilePaths)", mods_activity)
        self.assertIn("publisher.current()", display_model)
        self.assertIn("Mods.publishedMetadataRoot(generation)", display_model)
        self.assertNotIn("val root = buildPaths.buildRoot", mods_activity)
        self.assertNotIn("Il2cppConverter.rootFor", mods_activity)

    def test_profile_mod_runtime_state_comes_from_immutable_game_startup(self):
        paths = (REPO_ROOT / "tools" / "bepinex-shim" / "src" / "bepinex" / "Paths.cs").read_text(encoding="utf-8")
        chainloader = (REPO_ROOT / "tools" / "bepinex-shim" / "src" / "bepinex" / "Chainloader.cs").read_text(encoding="utf-8")

        self.assertIn('CallStatic<string>("requireModStatePath")', paths)
        self.assertIn("PluginPath { get { return Root; } }", paths)
        self.assertIn("ConfigPath { get { return Path.Combine(StateRoot, \"config\"); } }", paths)
        self.assertIn("Paths.ModStatePath", chainloader)
        self.assertNotIn('Path.Combine(Paths.PluginPath, "disabled-assemblies.txt")', chainloader)

    def test_mod_import_replacement_traversal_and_recovery_are_bounded(self):
        source = (LAUNCHER / "ModImport.kt").read_text(encoding="utf-8")
        mods = (LAUNCHER / "Mods.kt").read_text(encoding="utf-8")
        migration = (LAUNCHER / "ModStateMigration.kt").read_text(encoding="utf-8")

        self.assertIn("fun reconcileTransactions(", source)
        self.assertIn("fun copyDirectTree(", source)
        self.assertIn("fun copyDocumentTree(", source)
        self.assertIn("fun deleteOwnedTree(", source)
        self.assertIn("budget.accept(count)", source)
        self.assertIn("transactionRoot(mods)", source)
        self.assertIn("ModImport.reconcileTransactions(mods)", mods)
        self.assertIn("ModStateMigration.migrate(mods, state)", mods)
        self.assertIn('.legacy-mod-state-migrated-v1', migration)
        self.assertNotIn("target.deleteRecursively()", source)
        self.assertNotIn("input.copyTo(output)", source)
        self.assertNotIn("private fun copyTree(", source)
        self.assertNotIn("private fun copyDocuments(", source)

    def test_mod_import_ui_is_lifecycle_bound_async_and_confirms_unverified_replacement(self):
        source = (LAUNCHER / "ModsActivity.kt").read_text(encoding="utf-8")

        self.assertIn("CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)", source)
        self.assertIn("withContext(Dispatchers.IO)", source)
        self.assertIn("ModImport.prepare", source)
        self.assertIn("ModImport.ReplacementConfirmationRequired", source)
        self.assertIn('.setPositiveButton("Replace")', source)
        self.assertIn("importButton.isEnabled = !importRunning && !mutationRunning", source)
        self.assertIn("importProgress", source)
        self.assertIn("scope.cancel()", source)
        self.assertIn("coroutineContext.ensureActive()", source)
        self.assertIn("if (isFinishing || isDestroyed) return@launch", source)
        self.assertIn("DurableDisabledStateMutations.coordinator", source)
        self.assertIn("mutationSession.submit", source)
        self.assertIn("if (canNavigateNow()) super.onBackPressed()", source)
        self.assertIn("Could not update the mod switch", source)

    def test_mod_check_uses_profile_content_keyed_production_assembly_precedence(self):
        source = (REPO_ROOT / "tools" / "bepinex-shim" / "check.ps1").read_text(encoding="utf-8")

        self.assertIn("ValidateSet('hollow-knight', 'silksong')", source)
        self.assertIn("[string]$Profile", source)
        self.assertIn("6000.0.50f1", source)
        self.assertIn("6000.0.61f1", source)
        self.assertIn("Unity toolchain does not match profile", source)
        self.assertIn("unityaot", source)
        self.assertIn("Get-AssemblyUniverseDigest", source)
        self.assertIn("Copy-ProductionAssemblyUniverse", source)
        self.assertIn("[System.StringComparer]::Ordinal", source)
        self.assertIn("$PackageAssemblies", source)
        self.assertIn("Test-ProductionPackageManifest", source)
        self.assertIn("package-manifest.ps1", source)
        self.assertIn("$Plugin.Count -gt 0 -and $PackageAssemblies.Count -eq 0", source)
        self.assertIn("Exact mod checking requires production-built package assemblies", source)
        self.assertIn("profile=$Profile", source)
        self.assertIn("schema=", source)
        self.assertIn("toolchain=", source)
        self.assertNotIn(".Trim() -ne $Depot", source)
        makefile = (REPO_ROOT / "Makefile").read_text(encoding="utf-8")
        self.assertIn('-Profile "$(PROFILE)"', makefile)

    def test_runtime_registration_keeps_the_selected_profile_patch(self):
        source = (LAUNCHER / "PlayerImage.kt").read_text(encoding="utf-8")

        self.assertIn("PackageCompiler.patchAssembly(profile, root)", source)
        self.assertIn("patchAssemblyName", source)
        self.assertIn('register("BepInEx", "BepInEx.Bootstrap", "Chainloader", "Start", 2)', source)

    def test_built_in_mod_core_is_staged_for_both_game_profiles(self):
        source = (REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "build.gradle.kts").read_text(encoding="utf-8")

        self.assertGreaterEqual(source.count('from(rootProject.file("../../tools/shared-patches/src"))'), 2)
        self.assertIn('into("src/shared")', source)

    def test_silksong_mods_are_process_owned_and_reachable_from_ds_port(self):
        bootstrap = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DualScreenV2.cs").read_text(encoding="utf-8")
        port = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsPortRuntime.cs").read_text(encoding="utf-8")
        frame = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsPortFrame.cs").read_text(encoding="utf-8")
        presenter_path = REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsPortMods.cs"
        runtime_path = REPO_ROOT / "tools" / "silksong-patches" / "src" / "mods" / "SilksongModsRuntime.cs"
        shell = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsShell.cs").read_text(encoding="utf-8")

        self.assertTrue(runtime_path.is_file())
        self.assertTrue(presenter_path.is_file())
        runtime = runtime_path.read_text(encoding="utf-8")
        presenter = presenter_path.read_text(encoding="utf-8")
        bootstrap_body = bootstrap[bootstrap.index("static void Bootstrap()") : bootstrap.index("public static bool ShouldRun()")]
        self.assertIn("SilksongProcessStartup.Run(", bootstrap_body)
        self.assertLess(bootstrap_body.index("SilksongModsRuntime.EnsureStarted"), bootstrap_body.index("ShouldRun()"))
        self.assertIn("Current != null || _creating", runtime)
        self.assertIn("new TweakSession", runtime)
        self.assertIn("new SilksongTweakAdapter", runtime)
        self.assertIn("new PlayerPrefsTweakStore", runtime)
        self.assertIn("SilksongModsRestorePump.BlocksReplacement", runtime)
        self.assertIn("SilksongSkinRestorePump.BlocksReplacement", runtime)
        self.assertIn("new PendingSkinTeardown()", runtime)
        self.assertIn("_pending.TryRetain(session)", runtime)
        self.assertIn("SilksongSkinRestorePump.Create(skins)", runtime)
        self.assertIn("if (!session.TeardownComplete)", runtime)
        self.assertIn("new PendingTweakTeardown()", runtime)
        self.assertIn("_pending.TryRetain(session)", runtime)
        self.assertIn("_pending.Tick()", runtime)
        self.assertIn("session.Tick()", runtime)
        self.assertNotIn("Display.", runtime)
        self.assertIn("new DsPortMods", port)
        self.assertIn("SetModsGestureConsumer", port)
        self.assertIn("SetModsGestureConsumer(null)", port)
        self.assertIn("ModsAnchor", frame)
        self.assertIn("ModsAnchor", presenter)
        self.assertIn("CloneModsLabel", presenter)
        self.assertIn("TweakMenuModel", presenter)
        self.assertIn("TweakPresenterPaintInvalidation", presenter)
        self.assertIn("TweakPresenterModelPaintStamp.Compute", presenter)
        self.assertIn("TweakPresenterGeometryPaintStamp.WithLayoutRevision", presenter)
        self.assertIn("_frame.LayoutRevision", presenter)
        self.assertIn("_paint.ShouldPaint", presenter)
        self.assertIn("_paint.Acknowledge", presenter)
        self.assertNotIn("if (_modal != null) Paint();", presenter)
        self.assertIn("_session.SetPresenterAttached(false)", presenter)
        self.assertIn("_setConsumer(null)", presenter)
        self.assertNotIn("_session.Dispose()", presenter)
        self.assertIn("Reset()", presenter)
        self.assertNotIn("new DsShell", bootstrap + port)
        self.assertNotIn("DsModsScreen", bootstrap + port + presenter)
        self.assertIn("new DsModsScreen", shell)

    def test_silksong_skin_runtime_is_process_owned_independent_of_direct_display(self):
        runtime = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "mods" / "SilksongModsRuntime.cs").read_text(encoding="utf-8")
        bootstrap = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DualScreenV2.cs").read_text(encoding="utf-8")
        bootstrap_body = bootstrap[bootstrap.index("static void Bootstrap()") : bootstrap.index("public static bool ShouldRun()")]

        self.assertIn("SilksongProcessStartup.Run(", bootstrap_body)
        self.assertLess(bootstrap_body.index("SilksongModsRuntime.EnsureStarted"), bootstrap_body.index("ShouldRun()"))
        self.assertIn("public SilksongSkinRuntime Skins", runtime)
        self.assertIn("new SilksongSkinRuntime()", runtime)
        self.assertIn("new SilksongSkinLibrary(Skins)", runtime)
        self.assertIn("Skins.Tick()", runtime)
        self.assertIn("skinLibrary.Tick()", runtime)
        self.assertIn("skinLibrary.Dispose()", runtime)
        self.assertIn("skins.Dispose()", runtime)
        self.assertIn("SilksongSkinRestorePump.Create(skins)", runtime)

    def test_task100_receipt_is_bound_to_committed_compile_sources(self):
        evidence = REPO_ROOT / "docs" / "verification" / "evidence" / "task100-quality-c3f61f5"
        receipt = json.loads((evidence / "completion.json").read_text(encoding="utf-8"))

        self.assertEqual("HOST_COMPLETE_DEVICE_DEFERRED", receipt["status"])
        self.assertRegex(receipt["sourceCommit"], r"^[0-9a-f]{40}$")
        self.assertEqual(
            "docs/verification/evidence/task100-spec-fix-0c422bc/completion.json",
            receipt["supersedes"],
        )
        self.assertEqual(4, len(receipt["exactSilksongBindings"]))
        self.assertIn("device UI and interaction acceptance remains deferred", receipt["evidenceBoundary"])
        self.assertEqual(3, len(receipt["redGreen"]))
        for regression in receipt["redGreen"]:
            self.assertIn("executableTest", regression["green"])
            self.assertEqual(1, regression["green"]["passed"])
            self.assertEqual(0, regression["green"]["failed"])
        for test_run in receipt["testRuns"]:
            self.assertEqual(0, test_run["failed"])
            if test_run["name"] == "profile-mod-python":
                continue  # This suite cannot validate the digest of its own active log.
            log = (REPO_ROOT / test_run["log"]).read_bytes()
            self.assertEqual(test_run["logSha256"], hashlib.sha256(log).hexdigest())
        for compile_receipt in receipt["managedCompiles"].values():
            manifest_path = REPO_ROOT / compile_receipt["sourceManifest"]
            manifest = manifest_path.read_bytes()
            entries = manifest.decode("utf-8").splitlines()
            self.assertEqual(compile_receipt["compiledSourceCount"], len(entries))
            self.assertEqual(compile_receipt["sourceManifestSha256"], hashlib.sha256(manifest).hexdigest())
            self.assertEqual(0, compile_receipt["errors"])
            self.assertIn("--no-restore", compile_receipt["command"])
            self.assertIn("--no-incremental", compile_receipt["command"])
            compile_log = (REPO_ROOT / compile_receipt["log"]).read_bytes()
            self.assertEqual(compile_receipt["logSha256"], hashlib.sha256(compile_log).hexdigest())
            project = (REPO_ROOT / compile_receipt["compileProjectSnapshot"]).read_bytes()
            self.assertEqual(compile_receipt["compileProjectSha256"], hashlib.sha256(project).hexdigest())
            if "compileProjectSource" in compile_receipt:
                historical_project = subprocess.run(
                    ["git", "-C", str(REPO_ROOT), "show",
                     f"{receipt['sourceCommit']}:{compile_receipt['compileProjectSource']}"],
                    check=True,
                    capture_output=True,
                ).stdout
                self.assertEqual(project, historical_project)
            for entry in entries:
                digest, relative_path = entry.split("  ", 1)
                historical = subprocess.run(
                    ["git", "-C", str(REPO_ROOT), "show",
                     f"{receipt['sourceCommit']}:{relative_path}"],
                    check=True,
                    capture_output=True,
                ).stdout
                self.assertEqual(digest, hashlib.sha256(historical).hexdigest())

    def test_second_screen_uses_the_live_native_pane_font_source(self):
        resident = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsResidentUi.cs").read_text(encoding="utf-8")
        frame = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsPortFrame.cs").read_text(encoding="utf-8")
        runtime = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsPortRuntime.cs").read_text(encoding="utf-8")

        self.assertIn('GetField("currentPaneText"', resident)
        self.assertIn("ClonePaneName", resident)
        self.assertIn("ClonePaneName", frame)
        self.assertIn("InvalidateResidentSources", runtime)
        self.assertNotIn("DsTheme.FontRevision", runtime)

    def test_built_in_tweaks_do_not_use_native_memory_offsets(self):
        roots = [
            REPO_ROOT / "tools" / "shared-patches" / "src",
            REPO_ROOT / "tools" / "silksong-patches" / "src" / "mods",
        ]
        source = "\n".join(
            path.read_text(encoding="utf-8")
            for root in roots
            for path in root.rglob("*.cs")
        ).lower()

        for forbidden in ("intptr", "marshal.", "unsafe", "processmemory", "readprocessmemory", "writeprocessmemory"):
            self.assertNotIn(forbidden, source)

    def test_master_defaults_off_and_persistence_is_game_qualified(self):
        source = (REPO_ROOT / "tools" / "shared-patches" / "src" / "Mods" / "TweakController.cs").read_text(encoding="utf-8")

        self.assertIn('"dualsouls.mods." + adapter.GameId + "."', source)
        self.assertIn("MasterEnabled = false", source)
        self.assertNotIn("MasterEnabled = true;\n        public", source)


if __name__ == "__main__":
    unittest.main()
