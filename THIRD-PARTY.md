# Native PDF document layer

uwPDF uses Windows.Data.Pdf for page rendering. MuPDF supplies text geometry and
standard PDF annotation export; its page renderer is not used by the viewer.

- MuPDF 1.28.5, Artifex Software, commit `8ad45e92f0935d3d87f1db3f873086472a5e1b24`.
  Source: https://github.com/ArtifexSoftware/mupdf/tree/1.28.5.
  License: GNU Affero General Public License v3 or later, or a separately obtained
  commercial license from Artifex. The upstream COPYING file is bundled.
- FreeType: the FreeType Project License; copyright notices and FTL.TXT bundled.
- jbig2dec: GNU AGPL v3 or later; upstream COPYING and LICENSE bundled.
- Independent JPEG Group library: upstream README with its license bundled.
- zlib: zlib license, upstream LICENSE bundled.
- URW Base 14 font subsets: SIL Open Font License, OFL.txt bundled.
- Droid Sans Fallback: Android Open Source Project, Apache License 2.0;
  upstream NOTICE bundled.

`scripts/Prepare-NativeDependencies.ps1` restores the pinned release and its
submodule revisions, generates architecture-neutral C font data, and copies
licenses into the application package. Native builds disable non-PDF document
handlers, PDF JavaScript, OCR writers, HTML layout, ICC conversion, JPEG2000
decoding and Brotli. Windows.Data.Pdf continues to display these PDF images.

Compatibility edits made by the restore script replace desktop-only environment
and crypto calls with UWP-compatible behavior, initialize an outline pointer for
v140, and preserve unsigned arithmetic while avoiding v140 negation errors.
The modified source remains available in the pinned local checkout.
The text-only extraction path skips image decoding. A pinned processor extension
forwards temporary image placement metadata to the text device, preserving
image-wrapped ActualText without loading an image codec or retaining pixels.

The former PDF.js text bridge is no longer part of the application package.
Its legacy sources retain their original notices under LitePdfViewer/TextLayer.

The ARM32 C++/WinRT renderer uses Microsoft.Windows.CppWinRT 2.0.230706.1
from the Microsoft-owned NuGet package, with its original base library and
generated Windows projections. Its Microsoft author and NuGet repository
signatures were verified, and the preparation script pins the archive SHA-256.
The MIT notice is bundled as ThirdPartyLicenses/cppwinrt-LICENSE. The generator
is a build tool; no additional C++/WinRT runtime package is introduced.

Distribution of builds containing MuPDF requires compliance with the chosen
MuPDF license. This notice does not change uwPDF's license or imply that a
commercial license has been purchased.
