# Poppler 26.09.0 source and license information

The pinned Windows x64 preview renderer is the conda-forge package `poppler=26.09.0=h924501e_0`:

- Binary package: https://api.anaconda.org/download/conda-forge/poppler/26.09.0/win-64/poppler-26.09.0-h924501e_0.conda
- Binary SHA-256: `bb319f6881d91e175f90a9d33a25313e4cfddc15dd234521519985baf92fefcd`
- Upstream source: https://poppler.freedesktop.org/poppler-26.09.0.tar.xz
- Source SHA-256: `8059eadb6805340768f138c465b57f8164c92b4a0773c37ef031ea6c0d987b2e`
- Feedstock revision: https://github.com/conda-forge/poppler-feedstock/tree/13d784d77510e73d6066a75a79cd8e6040a6a261
- Extracted package recipe tree SHA-256: `2ed4e6b158eb3c9f0fa3f71c5d7162ab94c2d3485a34888f020277c7abb4392d`
- Recipe patches: `disable-libtiff-win32-io.patch`, `exportsymbols.patch`, `includesystembeforejpeg.patch`, `windows-data.patch`.
- License: `GPL-2.0-or-later`; the tracked [COPYING](Poppler-26.09.0-COPYING.txt) is copied from this exact binary package.

The verified upstream archive includes the `checkedAdd` change for CVE-2026-102620. Poppler 26.09.0 still has the Splash denial-of-service issue CVE-2026-93653. PDF Forge therefore applies a 120-second wall limit, 90-second process CPU limit, and 1 GiB Windows job memory limit before allowing `pdftoppm.exe` to parse a file. This limits impact; it is not a claim that the upstream issue is fixed.

Poppler is invoked as a separate local command-line renderer. A complete delivered single-file EXE must contain its renderer, native dependencies, fonts, and third-party licenses internally, with extraction confined to a private writable cache. `Publish-Portable.ps1 -StageOnly` stages the payload and file hashes; its default ZIP mode produces a supplemental package. Source archives and recipe patches are verified during building and are provided separately through the links above rather than mixed into the executable payload. Packaging does not imply completion of the WinForms UI acceptance gate.
