# Release conventions

- GitHub Releases must contain installable, signed packages (`.appx`, `.msix`, or bundles). Complete installer ZIPs may include dependencies, public `.cer` certificates, and installation scripts.
- Never upload `.appxupload` or `.msixupload` to GitHub Releases. Keep Store submission containers local for Partner Center.
- Lead release notes with new features and user-visible improvements. Put commit SHA, branch, toolchain, and validation details at the end in a developer information section.
- Keep private keys, `.pfx` files, and signing secrets out of release assets.
- Verify release asset hashes and ensure tags reference the source commit used to build the binaries.
