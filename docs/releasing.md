# Publishing a release

Finish the compatibility and validation gates in [migration status](migration-status.md)
before publishing a stable version. Release notes must describe the verified formats,
remaining platform limits, runtime requirement and installation steps.

Add notes in `docs/releases/vX.Y.Z.md`, then set `release/version.txt` to `vX.Y.Z`.
Pushing that version file runs the release workflow. After the workflow is available
on the default branch, it can also be run manually with a version tag. Tags such as
`v1.0.0-rc.1` produce a GitHub prerelease.

The workflow reruns the Linux x64, Windows x64 and macOS arm64 tests and unpacked
package checks, plus the original Windows reference comparisons. Publishing only
runs after those jobs succeed. The release targets the tested source commit and
includes the three packages, compatibility ledger, source/asset manifest and
`SHA256SUMS`. Existing releases are not overwritten.

A newer release run cancels an older run on the same branch. Assets upload to a
draft first; the workflow publishes it only after all uploads finish.

Packages require the .NET 10 runtime. macOS app bundles are unsigned; signing and
notarization require the project's distribution credentials.
