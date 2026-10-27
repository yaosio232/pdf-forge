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

### Issue #2 security maintenance (2026-10-27)

Windows GNU LLVM builds must pass the matching compiler runtime directory and its original license to `Publish-Tauri.ps1 -NativeRuntimeDirectory <LLVM bin> -NativeRuntimeLicense <LLVM LICENSE.TXT>`. The inner application carries `libunwind.dll`; the outer single-file launcher is linked statically so it can start before extraction. Windows x64 and WebView2 Runtime remain prerequisites. The parallel 1.x maintenance release is v1.0.5.2 on `codex/issue-2-security-v1`; this v2 release is v2.0.0-alpha.5 on `codex/issue-2-security-v2`. The older main branch and previous tags do not contain these repairs.

The security branch updates PDF.js to 6.4.299 and the affected renderer/build dependencies. Preview uses only the local PDF.js canvas API and packaged worker, CMaps, standard fonts and WASM assets. It does not load the PDF.js viewer scripting manager; `enableScripting` is a viewer setting, not a `getDocument()` initialization option. The application CSP continues to prohibit inline/evaluated scripts and external connections.

Metadata, page counts, encryption detection and generated PDF validation run in supervised native workers. Metadata preview inputs are limited to 256 MiB and 1,000 pages. Each metadata worker is gated until a Windows Job Object is attached with 1 GiB memory and 10 seconds CPU limits; the supervisor enforces a 15 second wall deadline. Limits failing to initialize stop the operation. This is process and resource isolation, not a claim of a restricted-token security sandbox. Other workflows retain their existing deadlines and output transactions.

`pdf_info` accepts `{path, requestId}` and returns `{pages, encrypted}`. `cancel_pdf_info` accepts `{requestId}` and is idempotent. Replacing/clearing the source or closing the app cancels pending metadata; stale replies cannot update the current preview. Worker responses are bounded and a nonzero exit is always an error. Documents, request contents and local evidence remain on the computer.

Reproduce the real browser/canvas check after building frontend assets with `scripts/security_fixtures.py <local-results-directory>` (Python with reportlab/pypdf), then `node scripts/Verify-PdfJs-Security.mjs <same-directory> [Chrome-or-Edge-path]`. It covers 40 pages, full/thumbnail canvases, CJK text and an inert PDF JavaScript marker with the production CSP. It is direct function evidence, not desktop UI acceptance. Native worker and full application acceptance are recorded separately in local TestResults. This maintenance task does not publish binaries, merge main, close issues or retroactively pass earlier UI gates.

On 2026-10-06 the user required direct function tests and prohibited UI operation. Version 2.0.0-alpha.2 fixes file-dialog deadlock, PDF.js compatibility, Word COM initialization/long paths and Markdown renderer script injection. Actual supervised workers are tested with independent output inspection for PDF split/merge/unlock, Word conversions and advanced Markdown printing. No UI, DPI, cross-machine validation or full release gate is claimed. SPLIT-01 through SPLIT-12 and conversion UI checks remain BLOCKED. Refer to the local test report and [UX contract](../docs/TAURI_V2_UX.md) for supported and unsupported content.

Output location pickers have been removed. Every operation writes beside the first selected source; split names remain editable and overwrite requires consent.

Files are processed locally. Existing sources and output files are protected by staging and atomic replacement. Individual batch success is retained when another item fails; merged Markdown output is not committed if any item fails. Cancel or close stops the supervised worker. Passwords remain in temporary job requests for worker IPC only and are never logged.

## License

Project source remains AGPL-3.0-only. Tauri, React, PDF.js, lopdf, pulldown-cmark, Mermaid, KaTeX, Windows bindings and other dependencies retain their own licenses. Build resources carry third-party notices; consult the root LICENSE.md and NOTICE.md. The user subsequently authorized a pending-validation GitHub prerelease under v2.0.0-alpha.1 on 2026-10-06. No main merge is included.
