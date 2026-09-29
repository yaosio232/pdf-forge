# Poppler portable runtime dependencies

The Windows portable ZIP ships the complete `poppler` runtime directory used to run `pdftoppm.exe`. The table below records every third-party component represented by the executables, DLLs, data files, and Microsoft runtime files in that directory. The exact packaged file set and SHA-256 values are generated into `licenses/Poppler-26.07.0-FILES.sha256` inside each release ZIP.

Version values marked **binary** come from the version resource in the packaged DLL. Values marked **package** come from the exact Poppler conda package dependency metadata or the bundled runtime manifest. A range means the Poppler package declares that compatibility range and the DLL does not expose a reliable version resource.

| Component / packaged files | Version in bundle | License identifier | License and source information |
|---|---:|---|---|
| Poppler (`pdftoppm.exe`, `pdfinfo.exe`, `poppler*.dll`) | 26.07.0 (package) | GPL-2.0-or-later | [COPYING](Poppler-26.07.0-COPYING.txt), [source and recipe](Poppler-26.07.0-SOURCE.md) |
| Cairo (`cairo*.dll`) | >=1.18.4,<2.0 (package) | LGPL-2.1-only OR MPL-1.1 | [conda-forge package](https://anaconda.org/conda-forge/cairo), [upstream](https://www.cairographics.org/) |
| Fontconfig (`fontconfig-1.dll`, configuration files) | runtime file set | MIT | [conda-forge package](https://anaconda.org/conda-forge/fontconfig), [upstream](https://www.freedesktop.org/wiki/Software/fontconfig/) |
| FreeType (`freetype.dll`) | 2.14.3 (binary) | GPL-2.0-only OR FTL | [conda-forge package](https://anaconda.org/conda-forge/freetype), [upstream](https://freetype.org/) |
| GLib (`glib`, `gio`, `gobject`, `gmodule`, `gthread`, `girepository`) | 2.88.3 (binary) | LGPL-2.1-or-later | [conda-forge package](https://anaconda.org/conda-forge/libglib), [upstream](https://gitlab.gnome.org/GNOME/glib) |
| libffi (`ffi-8.dll`) | 3.5.2 (package) | MIT | [conda-forge package](https://anaconda.org/conda-forge/libffi), [upstream](https://github.com/libffi/libffi) |
| GNU libiconv (`iconv.dll`, `charset.dll`) | 1.18 (package) | LGPL-2.1-only | [conda-forge package](https://anaconda.org/conda-forge/libiconv), [upstream](https://www.gnu.org/software/libiconv/) |
| GNU gettext runtime (`intl-8.dll`) | 0.22.5 (binary) | LGPL-2.1-or-later | [conda-forge package](https://anaconda.org/conda-forge/libintl), [upstream](https://www.gnu.org/software/gettext/) |
| ICU (`icu*.dll`) | 78.3 (binary) | ICU / MIT | [conda-forge package](https://anaconda.org/conda-forge/icu), [upstream](https://icu.unicode.org/) |
| libjpeg-turbo (`jpeg8.dll`, `turbojpeg.dll`) | 3.2.0 (binary) | IJG AND BSD-3-Clause AND Zlib | [conda-forge package](https://anaconda.org/conda-forge/libjpeg-turbo), [upstream](https://libjpeg-turbo.org/) |
| MIT Kerberos (`krb5`, `gssapi`, `comerr`, `k5sprt`, `krbcc`, `leash`, `kfwlogon`, `xpprof`) | 4.1 prerelease DLL revision (binary) | MIT | [conda-forge package](https://anaconda.org/conda-forge/krb5), [upstream](https://web.mit.edu/kerberos/) |
| Little CMS (`lcms2.dll`) | 2.19 (binary) | MIT | [conda-forge package](https://anaconda.org/conda-forge/lcms2), [upstream](https://www.littlecms.com/) |
| Lerc (`Lerc.dll`) | 4.1.0 (package) | Apache-2.0 | [conda-forge package](https://anaconda.org/conda-forge/lerc), [upstream](https://github.com/Esri/lerc) |
| bzip2 (`libbz2.dll`) | 1.0.8 (package) | bzip2-1.0.6 | [conda-forge package](https://anaconda.org/conda-forge/bzip2), [upstream](https://gitlab.com/bzip2/bzip2) |
| OpenSSL (`libcrypto-3-x64.dll`, `libssl-3-x64.dll`) | 3.6.4 (binary) | Apache-2.0 | [conda-forge package](https://anaconda.org/conda-forge/openssl), [upstream](https://www.openssl.org/) |
| curl (`libcurl.dll`) | 8.21.0 (binary) | curl | [conda-forge package](https://anaconda.org/conda-forge/libcurl), [upstream](https://curl.se/) |
| Expat (`libexpat.dll`) | 2.8.1 (binary) | MIT | [conda-forge package](https://anaconda.org/conda-forge/libexpat), [upstream](https://libexpat.github.io/) |
| XZ Utils (`liblzma.dll`) | 5.8.3 (binary) | 0BSD AND LGPL-2.1-or-later AND GPL-2.0-or-later | [conda-forge package](https://anaconda.org/conda-forge/xz), [upstream](https://tukaani.org/xz/) |
| libpng (`libpng16.dll`) | 1.6.58 (package) | zlib-acknowledgement | [conda-forge package](https://anaconda.org/conda-forge/libpng), [upstream](http://www.libpng.org/pub/png/libpng.html) |
| libpsl (`psl-5.dll`) | 0.22.0 (package) | MIT | [conda-forge package](https://anaconda.org/conda-forge/libpsl), [upstream](https://github.com/rockdaboot/libpsl) |
| libssh2 (`libssh2.dll`) | 1.11.1 (binary) | BSD-3-Clause | [conda-forge package](https://anaconda.org/conda-forge/libssh2), [upstream](https://www.libssh2.org/) |
| libtiff (`libtiff.dll`, `tiff.dll`) | 4.7.2 (binary) | HPND | [conda-forge package](https://anaconda.org/conda-forge/libtiff), [upstream](https://libtiff.gitlab.io/libtiff/) |
| zlib (`zlib.dll`) | 1.3.2 (binary) | Zlib | [conda-forge package](https://anaconda.org/conda-forge/libzlib), [upstream](https://zlib.net/) |
| Zstandard (`libzstd.dll`, `zstd.dll`) | 1.5.7 (binary) | BSD-3-Clause | [conda-forge package](https://anaconda.org/conda-forge/zstd), [upstream](https://github.com/facebook/zstd) |
| OpenJPEG (`openjp2.dll`) | 2.5.4 (package) | BSD-2-Clause | [conda-forge package](https://anaconda.org/conda-forge/openjpeg), [upstream](https://www.openjpeg.org/) |
| PCRE2 (`pcre2*.dll`) | runtime file set | BSD-3-Clause | [conda-forge package](https://anaconda.org/conda-forge/pcre2), [upstream](https://github.com/PCRE2Project/pcre2) |
| Pixman (`pixman-1-0.dll`) | runtime file set | MIT | [conda-forge package](https://anaconda.org/conda-forge/pixman), [upstream](https://www.pixman.org/) |
| Poppler data (`share/poppler`) | 0.4.12 (package) | Adobe terms and GPL-2.0 | [conda-forge package](https://anaconda.org/conda-forge/poppler-data), [upstream](https://poppler.freedesktop.org/) |
| Microsoft Universal CRT (`ucrtbase.dll`, `api-ms-win-*.dll`) | 10.0.26100.4654 (binary) | LicenseRef-MicrosoftWindowsSDK10 | [conda-forge package metadata](https://anaconda.org/conda-forge/ucrt), [Microsoft Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) |
| Microsoft Visual C++ runtime (`msvcp140*.dll`, `vcruntime140*.dll`, `concrt140.dll`, `vcamp140.dll`, `vccorlib140.dll`, `vcomp140.dll`) | 14.51.36247.0 (binary) | LicenseRef-MicrosoftVisualCpp2015-2022Runtime | [conda-forge package metadata](https://anaconda.org/conda-forge/vc14_runtime), [Microsoft redistributables](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) |

The conda package pages above are the license-metadata source used for this inventory. They do not replace the upstream license terms. Each component remains copyrighted and licensed by its respective authors. The portable ZIP does not combine these native libraries into PDF Forge's managed assembly; they remain separate files used by the external Poppler renderer.
