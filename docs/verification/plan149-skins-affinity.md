# PLAN149 Skins host checkpoint

## Scope

Host contracts for already-loaded imported/default switching, true-default rotation, normal death/respawn successor persistence, and save/profile isolation. Kotlin remains the durable writer; ordinary loading remains rotation-neutral and healthy idle remains allocation/transport/discovery free.

The bounded schema-2 affinity map restores only confirmed imported/default choices for slots 0–4. Typed loaded-hero admission distinguishes valid slot 0 from startup unbound state. Save admission precedes visual refresh; retired save/configuration feedback cannot overwrite another slot. Corrupt remembered selection fails explicitly and can recover through ordinary default/valid selection without changing another slot. Legacy schema and valid ON-registry selection migrate without fabricating confirmed affinity.

JNI I/O is event-bound and off the Unity frame path. Fixed operation lanes retain no cancelled queued submissions; deadline/cancellation authority is checked at existing publication barriers. Cancellation before atomic rename prevents publication; an already-committed rename is not rolled back by later cancellation. No healthy polling, timer, journal, or new general persistence subsystem is added.

## Fresh coordinator verification — 2026-09-30

Frozen input manifest SHA-256: `407568eb64bd6f76334f1c5912febab01592d6163d96a05038000ef6ee133a5e` (551 inputs, no mismatch before/after commands). Source base: `19559a729542651f93ac127be75a9afa6a3dbca8`. This report is added after source verification and is not a runtime input.

- Complete shared C#: **1806 passed, 0 failed/skipped**.
- Enabled actual owner/session feedback export: **1 passed**, ten outcomes exported.
- Relevant Kotlin library/bridge/I/O/affinity/production-transport/startup/containment/tree-verifier tests: **122 passed, 0 failures/errors/skips**. XML confirms **ten actual outcomes durably accepted**, both profiles and two slots each; reopen restores the independent confirmed choices.
- Exact HK **1.5.12620** + Android Unity player compile: **0 warnings/errors**, 852480-byte DLL, three entrypoints.
- Exact SS **1.0.29980** + Android Unity player compile: **105 sources**, 15 entrypoints.
- Normal Sync staging retains one shared parser per profile; the existing parser implementation/namespace was moved, not duplicated.
- `git diff --check`: passed.

Independent spec recheck confirmed parser availability, production save-boundary visual gating, one-shot publication fencing, corruption recovery, and substantive two-slot feedback assertions. The final bounded quality pass found cancelled queue retention and valid ON migration loss; both reached behavioral RED→GREEN and coordinator verification above includes their regressions. No further feature scope was added.

## Commands

```sh
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj --no-restore --logger 'console;verbosity=minimal'

SKIN_RUNTIME_FEEDBACK_OUTPUT=D:/Temp/skins-affinity-main407568-feedback.json dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj --no-restore --filter 'FullyQualifiedName~Kotlin_native_mutations_shared_production_decoder_both_profile_owners_and_same_loaded_session_round_trip'

SKIN_RUNTIME_FEEDBACK_INPUT=D:/Temp/skins-affinity-main407568-feedback.json java -classpath D:/Temp/dualsouls-unity-player/android/Tools/gradle/lib/gradle-launcher-8.11.jar org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest --tests 'dev.silksong.launcher.skins.library.*' --tests 'dev.silksong.launcher.runtime.SkinLibraryRuntimeBridgeTest' --tests 'dev.silksong.launcher.runtime.SkinRuntimeIoTest' --tests 'dev.silksong.launcher.runtime.SkinProductionTransportTest' --tests 'dev.silksong.launcher.runtime.GameProcessStartupTest' --tests 'dev.silksong.launcher.skins.storage.SkinTreeVerifierTest' --tests 'dev.silksong.launcher.skins.storage.SkinFileSystemContainmentTest' --no-daemon

powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/hollow-knight-patches/check.ps1 -Depot D:/Temp/dualsouls-hk-12620-converted/Managed -Player D:/Temp/dualsouls-unity-player/android/Variations/il2cpp/Managed

powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/silksong-patches/check.ps1 -Depot 'D:/Temp/task99-silksong-linux-1.0.29980/Hollow Knight Silksong/Hollow Knight Silksong_Data/Managed' -Player D:/Temp/dualsouls-unity-player/android/Variations/il2cpp/Managed
```

## Evidence and limits

Protected host evidence: `D:\Temp\dualsouls-2.0.0-c1c653f-gate\evidence\host-checkpoints\2026-09-30-plan149-407568\manifest.json`, with retained coordinator logs, all ten Kotlin XML reports, source freeze, actual feedback, and earlier implementer logs. Earlier failed attempts and the pause archive remain historical; they are not relabelled as current passes.

These results close the host contract only. They do **not** establish Android live switching, real-device performance, actual JNI graphics behavior, signed-candidate identity, or live death/respawn acceptance. Existing live statuses/applicability remain unchanged until the planned candidate validation.
