# CI/CD

The workflows separate hosted regression tests, legacy Windows builds, and hosted
signing/publication. The project's VS 2015 and VS 2026 UWP toolchains require a
dedicated Windows build runner; a stock GitHub-hosted image cannot build this
repository's four supported architectures.

## What runs

| Workflow | Trigger | Result |
| --- | --- | --- |
| CI | PR, push to `main`, manual, release call | Localization, 17 C# regression suites, and release source/asset gate tests on `windows-2022`; JSON evidence retained 14 days |
| Windows packages | Manual on `main`; push to `main` when `UWPDF_ENABLE_PACKAGE_BUILDS=true`; release call | Four unsigned, validated Release packages and architecture-specific dependencies; retained 7 days |
| Release | Push a version tag when `UWPDF_ENABLE_RELEASES=true` | Verify source, run CI, build, sign packages, assemble installer ZIPs, upload draft, download and check hashes, then publish |

CI tests use platform doubles and extracted production methods. They do not
establish installation, real PDF rendering, device input, latency, or ARM runtime
compatibility. The build workflow checks actual package baselines, native payloads,
source/DLL hashes, and WinRT registrations. No Store submission is performed.

## One-time GitHub setup

1. Add a dedicated **Windows x64 self-hosted runner** to this repository with the
   custom label **`uwpdf-build`**. Use a separate clean checkout and Windows account,
   not your interactive development directory. Keep the runner current (Node 24
   actions require runner 2.327.1 or later). Only trusted `main`/tag builds use it;
   PR tests always run on hosted Windows.
2. Install the prerequisites documented in the README: VS 2015 UWP tools/MSBuild
   14, SDK 14393 with .NET Native 1.x, modern MSVC/SDK 26100, and VS 2026 UWP XAML
   tools. The Visual Studio package catalog must also contain the pinned VS 2022
   **14.44.17.14 ARM32** packages used by `Prepare-CppWinRTArm32.ps1`. Install Git
   and Python 3 and expose them to the runner service account. The workflow restores
   NuGet, pinned MuPDF, and architecture overlays itself. There is no binary cache
   shared with PRs.
3. Use the GitHub Actions environment named **`release`** (already created for this
   repository with a `v*` tag deployment rule). Add these environment
   secrets using GitHub's secret UI or standard input to `gh secret set`:

   | Secret | Value |
   | --- | --- |
   | `UWPDF_SIGNING_PFX_BASE64` | Base64 of a password-protected code-signing PFX whose subject is **`CN=Ben`**, matching the sideload publisher |
   | `UWPDF_SIGNING_PFX_PASSWORD` | That PFX's password (nonempty) |

   Reuse the certificate trusted by existing sideload users when available. Keep the
   private key outside the repository. Do not use the repository's historical
   temporary development PFX as a new production signing secret. Signing happens
   only on an ephemeral hosted runner, with secret file/certificate cleanup in
   `finally`. The self-hosted build job receives no signing secrets.
4. Permit `v*` tags in the `release` environment's deployment rules. Configure
   required reviewers if your release process needs them. Protect `main` and release
   tags so only maintainers can change them; require the CI regression check for PRs.
5. Optionally set repository variable **`UWPDF_ENABLE_PACKAGE_BUILDS=true`** after
   the runner is online to enable full packaging for every push to `main`. Until
   then, hosted CI runs automatically and package builds can be launched manually.
6. Set **`UWPDF_ENABLE_RELEASES=true`** only after the runner and both signing
   secrets are ready. Until then, tag-triggered cloud releases are skipped and
   maintainers can use the same validation/signing scripts locally. Local signing
   accepts `New-CiRelease.ps1 -Tag v1.0.14.0 -CertificateThumbprint <thumbprint>`;
   that certificate must already be present in the current user's Personal store
   and is preserved by the script.

The workflow uses the built-in `GITHUB_TOKEN` with `contents: write` only in the
publish job. No personal access token is needed. Actions are pinned by commit SHA;
Dependabot checks monthly for updates.

## Validate and release

Run regression tests locally with Windows PowerShell and Python:

```powershell
.\scripts\Invoke-CiTests.ps1
```

Tests prefer the existing MSBuild 14 C# compiler, fall back to Visual Studio's
desktop Roslyn compiler, or accept an explicit `UWPDF_CSC_PATH`. They do not require
the full UWP packaging toolchain. Newer C# syntax support in a hosted test compiler
does not replace the legacy compiler check in full application builds.

After committing the workflows and pushing them to GitHub, run **Windows packages**
manually on `main` to validate the runner before the first release. A full package
build requires a clean checkout and never signs with the checked-in development key.

For a release:

1. Set the same four-part version in `Package.appxmanifest` and
   `Package.ARM64.appxmanifest`.
2. Write **`docs/releases/v<version>.md`**, leading with new features and user-visible
   improvements. Include user-facing limitations where relevant. Developer details
   are appended automatically after those notes.
3. Commit the release source to `main`, push it, then tag that exact commit:

   ```powershell
   git tag v1.0.13.0 HEAD
   git push origin v1.0.13.0
   ```

Only tag committed source. Both manifests, the tag, checkout, build provenance and
remote tag must agree. The tagged commit must belong to `origin/main`. Signed
annotated tags are supported as well as lightweight tags.

Each release contains four signed `.appx`/`.msix` files, four complete `_Install.zip`
files, public `uwPDF.cer`, `build-info.json`, and `SHA256SUMS.txt`. Installers contain
the exact signed application, architecture-specific dependencies, public
certificate and `Add-AppDevPackage.ps1`. They instruct users to trust the certificate
in Local Computer > Trusted People before installation; normal-user installation
then uses `Add-AppxPackage` with the included dependencies. `.pfx`, private keys,
`.appxupload` and `.msixupload` are rejected by the asset allowlist. Store containers
stay local for Partner Center.

Release creation starts as a draft. The workflow downloads every uploaded asset,
checks every hash against the original bytes and checks package/certificate bytes
inside installer ZIPs before making the release public. Existing releases are never
overwritten. If upload/download verification fails, inspect and remove only the
failed draft after reviewing the logs, then rerun the workflow. Never move a published
tag to different source.

GitHub references: [self-hosted runners](https://docs.github.com/en/actions/reference/runners/self-hosted-runners),
[secure use](https://docs.github.com/en/actions/reference/security/secure-use),
[checkout](https://github.com/actions/checkout),
[artifact uploads](https://github.com/actions/upload-artifact).
