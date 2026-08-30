# Dual Souls product identity verification — 2026-08-30

## Contract

Task 16 establishes one non-colliding Android product identity without renaming
the existing Java/Kotlin namespaces:

```text
package: io.github.darkaxt.dualsouls
label: Dual Souls
artifact: DualSouls-${VERSION_NAME}.apk
```

The approved vertically split icon remains the user-supplied composition. This
stage changes no icon artwork and uses no generative image tooling.

## Test-first implementation

The initial `python -m unittest tools.ci.tests.test_product_identity -v` run
failed all three new contract tests against the former package, label, and
artifact defaults. After the implementation, those three tests passed.

The complete host gates then passed from the modified tree:

- Android/Robolectric: 38 tests, 37 passed and one intentionally skipped;
- BundleSurgery converter: 36 of 36 passed;
- CI helper contracts: five of five passed; and
- `git diff --check`: clean.

One existing Robolectric resource assertion initially retained the former
`Silksong Launcher` label. Its isolated reproduction proved that the resource
correctly returned `Dual Souls`; only the stale expected value was updated.

Implementation commit: `e559e62da5b5afa5701bbd5b02b28c23ec02f88c`.

## Signed GitHub proof

[GitHub Actions dry run 33305033556](https://github.com/Darkaxt/HollowKnightAndroid/actions/runs/33305033556)
completed successfully in 5 minutes 20 seconds. The job built from the
implementation commit, required the stable repository signing secrets,
uploaded the named APK artifact, downloaded it into a fresh runner
directory, and repeated its hash and signer verification. The release step was
skipped because `dry_run=true`.

The independently downloaded artifact is:

```text
file: DualSouls-1.0.3.apk
bytes: 69589412
SHA-256: 687765affac054eb1c47843cb23be2ebe3edb40f6430183f7074b865a6a1cc7a
package: io.github.darkaxt.dualsouls
label: Dual Souls
versionName/versionCode: 1.0.3 / 10003
ABI: arm64-v8a
debuggable: false
signer SHA-256: 324b3a3e854b69d567d1527ae52e96a1051adf13550b485e320f8ce8cf678c38
signature schemes: v2 and v3 verified
```

The plan names `make dev` as the packaging check. This stage instead used the
same build path from a clean GitHub checkout with `DEBUGGABLE=0` and the stable
release key, then ran local AAPT2 and apksigner checks on the independently
downloaded result. That is a verification substitution, not a design change.

The GitHub artifact is named `DualSouls-1.0.3.apk`, artifact ID `9730233038`.
Its transport ZIP is 28,738,359 bytes; GitHub's fresh download contained the
same 69,589,412-byte APK and the same APK SHA-256.

## Packaged icon proof

The manifest retains `@mipmap/ic_launcher` and
`@mipmap/ic_launcher_round`. The APK contains its adaptive XML plus legacy,
adaptive-background, and round PNGs at mdpi, hdpi, xhdpi, xxhdpi, and xxxhdpi:
15 PNG resources in total.

AAPT2 may normalize RGB channel values beneath fully transparent pixels when
it encodes PNG resources. Eight packaged files therefore differ bytewise or in
invisible zero-alpha RGB values. Across all 15 images, however, dimensions and
alpha are identical and there are zero premultiplied/rendered pixel
differences. The visible user-supplied composition is unchanged.

## Repository and release boundary

The stage-boundary scan found zero proprietary/private-key path matches among
178 tracked files and zero game-content or private-keystore matches among the
APK's 257 entries.

No GitHub release was created. A `v1.0.3` tag already exists in both upstream
and the fork at `d504275847d5477155dfe5bdc2edf7db84339eb7`; it predates this
run and was not created by it. Because that inherited tag does not identify
the product-identity commit, Task 17's final tag/version alignment remains a
release blocker. A later release must choose and verify an aligned version and
tag before publication.

## Design cross-check

Task 16 is complete: the non-colliding package, product label, artifact name,
approved icon resources, signature, and packaged manifest are proven. Task 17
Step 3 is also complete for the final identity and signing documentation.

The broader design Goal 1 remains blocked because the launcher still cannot
select and launch both profiles. Device preservation/adoption, encrypted
credential continuity, two sequential fork-signed updates, gameplay, and the
final published-release gates are unproven. The new package can coexist with
the installed upstream package, but that does not migrate its data. Task 8
remains forbidden, and this stage performed no install, launch, migration, or
publication.
