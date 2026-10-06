# PDF Forge 2.x

Windows x64 Tauri 2 / React / TypeScript frontend with a full Rust backend. This branch is independent of the inherited 1.x WinForms solution. Version starts at 2.0.0-alpha.1; application identifier and local data directory differ from 1.x.

## Development

Install Node.js 22.12+ and a Windows Rust toolchain. The standard Tauri route uses MSVC Build Tools and the Windows SDK. Windows GNU with a compatible LLVM-MinGW toolchain is also supported by the source (set `CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER`, put its `bin` on the process PATH and set `RUSTFLAGS=-C link-self-contained=yes` when using Rust's bundled GNU runtime libraries). WebView2 Runtime is required for the application and Markdown printing; Microsoft Word desktop is required only for PDF/Word conversions.

```powershell
cd tauri
npm ci
npm test
cargo test --manifest-path src-tauri/Cargo.toml
npm run tauri -- dev
```

Rust core tests do not launch Word, WebView2 or the application. Desktop dependencies are behind the `desktop` feature. `npm run build` embeds PDF.js, Mermaid, KaTeX and all their rendering assets locally; no CDN is used.

## Build and package

```powershell
cd tauri
npm ci
npm run tauri -- build
```

The build produces a Windows binary and NSIS installer under `src-tauri/target/release`. The installer checks WebView2 and installs its bootstrapper when missing. The primary portable delivery is a single self-contained EXE. It embeds a runtime-only archive and automatically prepares verified resources under `%LOCALAPPDATA%/PDFForge2/portable`; no adjacent DLL/resource folders are needed. Word and WebView2 Runtime remain external prerequisites. A supplementary portable ZIP includes `resources/markdown`, licenses, the executable and any DLLs Tauri placed beside it (including WebView2Loader.dll for GNU builds); the destination computer needs WebView2 Runtime. Header/footer/page-number print options require Runtime 131+. Use `scripts/Publish-Tauri.ps1` from the repository root to prepare the single portable EXE, supplementary ZIP and a copy of the installer after a successful Tauri build.

For the complete delivery with Rust and npm third-party notices, use `scripts/Build-Tauri.ps1` from the repository root (requires Python 3 for copying the original notices from installed dependencies), then `scripts/Publish-Tauri.ps1`.

## Current acceptance status

On 2026-10-06 the user required direct function tests and prohibited UI operation. Version 2.0.0-alpha.2 fixes file-dialog deadlock, PDF.js compatibility, Word COM initialization/long paths and Markdown renderer script injection. Actual supervised workers are tested with independent output inspection for PDF split/merge/unlock, Word conversions and advanced Markdown printing. No UI, DPI, cross-machine validation or full release gate is claimed. SPLIT-01 through SPLIT-12 and conversion UI checks remain BLOCKED. Refer to the local test report and [UX contract](../docs/TAURI_V2_UX.md) for supported and unsupported content.

Output location pickers have been removed. Every operation writes beside the first selected source; split names remain editable and overwrite requires consent.

Files are processed locally. Existing sources and output files are protected by staging and atomic replacement. Individual batch success is retained when another item fails; merged Markdown output is not committed if any item fails. Cancel or close stops the supervised worker. Passwords remain in temporary job requests for worker IPC only and are never logged.

## License

Project source remains AGPL-3.0-only. Tauri, React, PDF.js, lopdf, pulldown-cmark, Mermaid, KaTeX, Windows bindings and other dependencies retain their own licenses. Build resources carry third-party notices; consult the root LICENSE.md and NOTICE.md. The user subsequently authorized a pending-validation GitHub prerelease under v2.0.0-alpha.1 on 2026-10-06. No main merge is included.
