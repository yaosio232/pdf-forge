# Poppler 26.07.0 source and license information

PDF Forge portable packages use `pdftoppm.exe` from the Windows x64 conda-forge package:

- Package specification: `poppler=26.07.0=h6618ce5_3`
- Package URL: https://api.anaconda.org/download/conda-forge/poppler/26.07.0/win-64/poppler-26.07.0-h6618ce5_3.conda
- Package SHA-256: `3FDBB4CAF50ECAE4FA447CB7A7D6D5B521A39AE8D4DE4F6565E0CE9CD6D7F903`
- Upstream project: https://poppler.freedesktop.org/
- Upstream source repository: https://gitlab.freedesktop.org/poppler/poppler/-/tree/poppler-26.07.0
- Upstream source archive: https://poppler.freedesktop.org/poppler-26.07.0.tar.xz
- Source archive SHA-256: `304832F48F8A47FDCA90C6B6D1F684E68F37C10C9A0726F345F4CA9DF4CA01E2`
- Archived upstream source: included in the portable ZIP at `licenses/poppler-26.07.0.tar.xz`
- Exact conda-forge feedstock revision: https://github.com/conda-forge/poppler-feedstock/tree/4f5f76cc7b8aa5ac5090988bd78ab7bde271c10a
- Archived build recipe and Windows patches: included in the portable ZIP at `licenses/Poppler-26.07.0-conda-recipe/`
- Package license: `GPL-2.0-or-later`

The portable ZIP includes the GNU GPL version 2 text at `licenses/Poppler-26.07.0-COPYING.txt`, copied from the exact conda package metadata; the packaging gate compares its normalized text to the package copy. Poppler and all bundled native libraries remain subject to their respective upstream copyright and license terms. PDF Forge uses Poppler as a separate command-line renderer; the application invokes `pdftoppm.exe` to create local preview images.
