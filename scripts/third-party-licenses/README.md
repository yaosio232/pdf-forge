# Dependency notice fallbacks

Some published dependency archives omit their repository's license files. These version-specific copies preserve the upstream notices for the Windows x64 build. Each provenance.json records the exact upstream commit/package, source URL and SHA-256. The offline collector checks those hashes before copying.

Fastdom and strictdom retain their complete published README license sections. Selectors identifies MPL 2.0 in its upstream source; its copy contains that source notice and the Mozilla license text. WebView2 SDK LICENSE/NOTICE cover Microsoft's loader separately from the Rust wrapper's MIT license. PDF.js WASM and font component licenses are collected directly from the installed package.

The complete Cargo metadata also contains dependencies for other targets. Their presence in the manifest does not mean they are linked into this Windows application. The Windows normal/build dependency closure has physical notices; future targets need their own notice audit. This directory contains upstream licensing material, not local test evidence or user documents.
