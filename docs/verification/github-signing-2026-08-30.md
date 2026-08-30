# GitHub Signing Evidence — 2026-08-30

## Scope

This record covers the early signing slice of Task 17: establishing the
HollowKnightAndroid fork's durable Android identity, moving all release signing
to GitHub repository secrets, pinning the public certificate, and proving a
dry-run artifact through both in-job and independent fresh-download checks. It
does not claim a published release, an installed update, a playable game, or
completion of the remaining Task 17 and Task 18 gates.

## Durable identity and secret boundary

The new fork identity is:

```text
policy: hollowknightandroid
package at this original proof: com.jakobkhansen.silksong
final product package: io.github.darkaxt.dualsouls
alias: hollowknightandroid-release
certificate SHA-256: 324b3a3e854b69d567d1527ae52e96a1051adf13550b485e320f8ce8cf678c38
```

The PKCS#12 keystore and DPAPI-protected password are retained outside the
repository under the local protected-signing credential root. The public
fingerprint alone is committed at
`signing/hollowknightandroid-release.sha256`. GitHub has the four established
repository secrets, with no values committed or printed:

```text
ANDROID_KEYSTORE_BASE64
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_PASSWORD
ANDROID_KEY_ALIAS
```

Every release-workflow invocation now requires that stable identity, including
a dry run. The decoded keystore remains in `$RUNNER_TEMP`, is mounted read-only
at `/run/secrets/release.p12`, and is never copied into the checkout or a cached
directory. The workflow has no ephemeral signing fallback.

## Test-first verifier

The first `python -m unittest discover tools/ci/tests -v` run failed twice on
the expected missing `verify_apk_signing.py` contract. After the helper was
implemented, both parser/normalization tests passed. A real local APK then
proved both branches: its known debug certificate was accepted, while requiring
the new fork certificate rejected it with exit code 1.

## GitHub dry run

The final proof used commit `773f001c5284cea234cceac7a7fd20034c3f381f` and
[GitHub Actions run 33286699823](https://github.com/Darkaxt/HollowKnightAndroid/actions/runs/33286699823).
The job completed successfully in 4 minutes 46 seconds. It proved:

- all four signing secrets are present and the configured alias opens;
- the release verification unit tests pass from a clean checkout;
- the APK package is `com.jakobkhansen.silksong`;
- `versionName=1.0.3` and derived `versionCode=10003` agree;
- the APK is non-debuggable and contains only `arm64-v8a` native code;
- the APK signer matches the committed certificate pin;
- the uploaded GitHub artifact downloads into a fresh runner directory;
- the downloaded hash and signer still match; and
- the publish step is skipped and creates no release or new tag. The inherited
  `v1.0.3` tag was later confirmed to point to upstream commit `d504275`.

The artifact was then downloaded independently from the completed run into a
unique `D:\Temp` verification root and checked with local Android SDK tools:

```text
file: SilksongAndroid-1.0.3.apk
bytes: 69081072
SHA-256: c3ac0d42704af8d365a3d6862765b67ddc62b2b52b224e287b1b05763f8cf8ff
package: com.jakobkhansen.silksong
versionName/versionCode: 1.0.3 / 10003
ABI: arm64-v8a
debuggable: false
signer SHA-256: 324b3a3e854b69d567d1527ae52e96a1051adf13550b485e320f8ce8cf678c38
```

A stage-boundary scan found zero forbidden game, Unity-player, APK/OBB/archive,
or private-keystore paths among 161 tracked files, and zero forbidden game,
Unity-player, or private-keystore entries among the APK's 252 ZIP entries.

## Blockers and tracked deferrals

- **BLOCKER — signing transition/device proof:** the installed upstream APK is
  signed by `16a868fe…dd9e`, not the fork certificate. A controlled one-time
  migration that preserves generated state, saves, source selection, and
  encrypted credentials is still required before Task 4 can close. Two
  sequential fork-signed builds must then prove update-in-place.
- **BLOCKER — final release contract:** no new tag or release was published,
  and the artifact was deliberately not installed or exercised. Task 18
  retains those gates.
- **BLOCKER — identical-input reproducibility:** two successful artifacts came
  from different commits, so their differing whole-file hashes do not test
  deterministic rebuilding of one exact revision. Repeat the same commit twice
  and compare before claiming byte-for-byte reproducibility.
- **DEFERRED to remaining Task 17 — action runtime maintenance:** GitHub reports
  that several current action majors target deprecated Node.js 20 and are being
  forced onto Node.js 24. Update the action majors and require a dry run with no
  Node.js runtime annotation.
- **DEFERRED to remaining Task 17 — Docker platform lint:** BuildKit warns about
  the constant `linux/amd64` base-image platform. Preserve the required amd64
  Android build-tools environment while removing the lint annotation, then
  repeat the signed dry run.

Task 8 remains forbidden while Task 4's device regression is blocked. This
signing slice changes the available migration identity; it does not substitute
for that regression.

## Product-identity follow-up

Task 16 later established and signed the final `io.github.darkaxt.dualsouls`
package, `Dual Souls` label, and `DualSouls-1.0.3.apk` artifact in dry run
`33305033556`. The original final-product-alignment blocker is therefore
resolved at the packaged APK level; see
`docs/verification/product-identity-2026-08-30.md`.

Publication remains blocked. The inherited `v1.0.3` tag points to upstream
commit `d504275`, not the product-identity commit, and no GitHub release exists.
The new non-colliding package also does not by itself adopt the installed
upstream package's data or prove device behavior.
