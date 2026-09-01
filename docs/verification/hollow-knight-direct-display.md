# Hollow Knight direct-display transport verification

Date: 2026-09-01  
Device: AYN Thor, serial `bfa98654`, Android 13  
Profile: Hollow Knight Linux `1.5.12620`, Unity `6000.0.61f1`  
Candidate: `9fb2db23c40e3ff4ed86fbe7efa92016dcb4ad12`

## Result

Stage H1's transport implementation is complete and its physical display-1,
fail-closed, signed-build, update-preservation, and pause/resume gates pass.
The device gate remains `DEVICE-PARTIAL` with one tracked deferral: the Thor's
two internal panels cannot produce an Android/Unity display-removal event at
runtime, so physical display loss and a true single-display start are not yet
device-proved. The pure absence, loss, reactivation, and teardown state paths
are executable and green, but that is not presented as physical proof.

This stage proves only the shared direct-display transport. The diagnostic
card is not the Hollow Knight Dual Souls HUD and does not advance H2.

## Implemented boundary

- `DirectDisplayHost` owns presence, readiness, pause, active/fallback state,
  transactional activation, content attachment, and ordered teardown.
- `DirectDisplayPresentation` owns Unity display 1, private content/overlay
  cameras and layers, measured panel geometry, camera-mask isolation, and
  presentation release.
- `DirectDisplayTouch` owns display-attributed touch collection and the
  primary-display input fence.
- `IDirectDisplayContent` is the game-independent content lifecycle.
- Silksong's existing transport delegates to this boundary through thin
  shims; the shared files reference neither game's types.
- Hollow Knight supplies only an opt-in diagnostic consumer. It runs only
  when `hollow_knight_direct_display_probe` contains exactly `enabled=1` or
  `enabled=true`; absent, malformed, or disabled files fail closed.

No `DsPort*`, `DsShell`, HUD, page, Mods, skin, game-data, EGL blitter, or Java
touch-bridge responsibility moved into the shared transport.

## Host proof

- RED/GREEN development covered presence, readiness, pause, display loss,
  reactivation, single-display fallback, transactional failure paths, content
  ownership, and ordered teardown.
- `dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release`:
  49/49 passed.
- `python -m unittest discover tools/ci/tests -v`: 78/78 passed.
- Exact Hollow Knight patch compile: 9 sources, 2 entry points, 0 warnings,
  0 errors; host DLL size 43,520 bytes.
- Exact Silksong patch compile: 50 sources, 10 entry points.
- Final independent specification and quality reviews found no remaining
  Critical, Important, or Minor source issue.

## Signed candidate and production generation

GitHub Actions dry-run `33494317664` built commit `9fb2db2`; the release step
was intentionally skipped. The downloaded APK is 72,360,191 bytes with
SHA-256 `2d9024b7e100c772abf73fc652adc1223221d3b6b13c4c6d7f5184b103c1dfbe`.
It is non-debuggable, package `io.github.darkaxt.dualsouls`, version
`1.0.3`/`10003`, and verifies with APK Signature Scheme v2/v3. Its one RSA
3072 signer has certificate SHA-256
`324b3a3e854b69d567d1527ae52e96a1051adf13550b485e320f8ce8cf678c38`.

The APK updated the existing production package in place. The UID and
first-install timestamp were preserved. The exact Hollow Knight source built
on-device into a 267,870,152-byte `libil2cpp.so`, a 3,575,701,375-byte ZIP32
base player image, and a 1,603,506,295-byte main OBB. The launcher reported the
profile ready and Hollow Knight reached its `1.5.12620` title screen.

## Thor matrix

| Scenario | Result | Evidence |
| --- | --- | --- |
| Probe absent | PASS | Hollow Knight rendered on the primary display; the lower panel remained the normal Thor launcher; no probe/transport log appeared |
| Probe explicitly enabled | PASS | Primary display remained Hollow Knight; display 1 rendered `HOLLOW KNIGHT DIRECT DISPLAY / TRANSPORT ONLY` at 1240x1080; logs recorded panel geometry, owned-layer removal, display-1 activation on layers 6/7, and `transport ready` |
| Background and resume | PASS | HOME paused task 167 while PID 27284 remained alive; returning through Recents resumed the same `GameActivity` and PID; logs show `OnApplicationPause` and `DirectDisplayHost.Activate` on resume with no crash |
| Paused surface | EXPECTED TRANSPORT BEHAVIOR | Disabling both Unity cameras/canvases/content stops updates; SurfaceFlinger retains the last submitted static card frame. This matches the pre-extraction Silksong direct-display implementation and is not claimed as a blank-panel transition |
| Physical display loss | TRACKED DEFERRAL | Forcing DRM connector `card0-DSI-2/status` from `connected` to `disconnected` succeeded and was restored with `detect`, but the Thor composer kept logical display 4 registered, so Unity emitted no display-count event |
| True single-display start | TRACKED DEFERRAL | The reference device always exposes both internal panels. Existing API-35 AVDs are x86-64 and cannot execute the ARM64-only Unity player, so emulator shell evidence cannot close this native gate |
| Cleanup and preservation | PASS | Opt-in file removed; game force-stopped; no package PID remained; `stay_on_while_plugged_in` restored to `0`; connector restored `connected`; firmware display mode remained `0`; `game-settings.txt` SHA-256 stayed `68c47a8a374de2a97741afbeb140d247518480ab68fd15fb6895d5f3ef6f7e21` |

Primary disabled/enabled capture hashes are
`e8022a862daa82a5b23f7dc79037e6cd5a658ea69e3b857f76fe1f428d87d0ab`
and `6288b113e7963e44a253b36bee2fa8e774e6d5e566a9a0f07b6c6f59f8f8bcff`.
The enabled, paused, resumed, and restored lower-panel captures share SHA-256
`ecaf5ab0f9de0601c773771fd47563a4a1af5d7af1b85e215958b33f86709694`,
as expected for a static card and retained last surface buffer.

## Specification reconciliation

- **DSUI-00:** satisfied for H1. Hollow Knight `1.5.12620` is the first
  production consumer, and no Silksong composition work advanced.
- **DSUI-02:** satisfied only for the transport seam. Camera/layer/touch and
  lifecycle responsibilities are isolated; the actual Dual Souls composition
  remains entirely H2.
- **DSUI-10:** source coverage, RED/GREEN tests, both exact compiles, signed
  build, update-preserving Thor captures, and this ledger are present. The
  physical detach/single-display device row is explicitly deferred rather than
  converted into a pass from render success.

H1 reconciliation: `blockers = 0`, `tracked_deferrals = 1`. The deferral must
reach zero by H5. H2 may begin because the transport implementation and its
real dual-panel lifecycle are proved, but H5 cannot close until an ARM64
single-display target or a composer-level Thor removal mechanism supplies the
missing device event.

## H2 pinned-source provenance

The Hollow Knight companion port is based on the MIT-licensed source from
`igawa6/dualsouls` at exact commit
`5c22451435b772acde0c7e6456f9019bc1baef73`. No game binary, managed
assembly, texture, sprite, sound, or other proprietary asset is imported into
the repository. The values below are the SHA-256 hashes of the pristine
upstream source files before the Android direct-display transport adaptation:

| Reference module | Pristine SHA-256 |
| --- | --- |
| `HKDualScreen.cs` | `7c9f11b59d768d7dde9506f0b546dee03f9704dbc4329ad20cfb8bd3f15b8d3d` |
| `HKDualScreen.Util.cs` | `20a021e2970ac0ff8852188506accba3277ce475e8f0a314a3a85e69fbc8f65a` |
| `HKDualScreen.Bottom.Layering.cs` | `a7c4a720c9756026de363a1c3744ca29e2a96455286cc779bcbe20ae5c1692ea` |
| `HKDualScreen.Bottom.Frame.cs` | `26f4f4650a76787d8e8286d6f375abebdd469c4ac1366cd19de88276d7cc5a7a` |
| `HKDualScreen.Bottom.Hud.cs` | `f8aeaadf76ee6e53380b9aeefa24af14ed4e6def9754e795ac5842d5145b33d7` |
| `HKDualScreen.Bottom.Inventory.cs` | `32f0b52f21fcbd8c89c10acd554853fe1dde41b01586f36ddc93a7b42d6ce7ef` |
| `HKDualScreen.Bottom.Charms.cs` | `66bb323f25130af869021d11ef0eb9b229fbbc8a09bc9fb9e400a371a9f2c67c` |
| `HKDualScreen.Bottom.Map.cs` | `5099609d8af787e6f421a685334d06285cc8210638498d3e6fb055befd3fe2bb` |
| `HKDualScreen.Bottom.Select.cs` | `9394e124f96072b7d73ea2f8b5eaf2cbd3ecb51452d2f364ce918d21cb17f968` |

The repository copies preserve those module boundaries and responsibilities.
Only the old Java/EGL auxiliary-display bridge and its native blit/touch path
are replaced; the existing HUD, inventory, charms, map, selection, routing,
and restore behavior remain the presentation oracle. H3 tweaks/mods and H4
skins are represented by an explicit inert stage boundary in H2 rather than
silently approximated or imported ahead of their gates.

## H2 active device findings

The first signed H2 candidate moved Hollow Knight's live masks/Soul HUD to the
lower panel and left the primary gameplay view clean on exact version
`1.5.12620`. It did not pass H2: the three resident tab labels were absent,
cloning the Inventory pane ran a source `iTween.Awake` with invalid cloned
arguments, and the direct-display backdrop retained too much of the original
scene exposure instead of the reference's subdued blurred wash.

The host correction is protected by RED/GREEN contracts that require the
retained TMP behavior to be enabled before mesh generation, pane clones to be
instantiated under an inactive staging parent and stripped of copied `iTween`
drivers before activation, and the replacement dimmer to use a material with
an actual `_Color` property. The full host suite is 96/96 and the exact
`1.5.12620` patch compiles at 221,696 bytes with two entry points. These are
component gates only; all three findings remain H2 device blockers until a
new signed candidate is rebuilt and visually/logically verified on the lower
panel.
