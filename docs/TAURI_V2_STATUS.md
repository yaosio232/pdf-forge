# PDF Forge 2.0.0-alpha.1 status — 2026-10-06

Implementation is on `codex/tauri-v2`, based on v1.0.5 `a78a22b`, in an isolated worktree. The existing 1.x C# sources and workspace changes are preserved. No main merge, push or GitHub release is included.

## Basic checks

| Check | Result |
|---|---|
| Rust basic unit/integration tests | PASS: 21 passed, 0 failed |
| Frontend basic unit tests (mocked desktop/renderer APIs) | PASS: 16 passed, 0 failed |
| Inherited .NET build and unit baseline | PASS: 53 passed, 1 existing skip, 0 failed; existing NU1701 warnings |
| React production compilation | PASS; nonfatal large-chunk warning |
| Tauri Windows x64 compilation and NSIS packaging | Built locally; installation/runtime acceptance not executed |

The Rust core test creates a fresh rich 40-page fixture and checks 7/7/7/7/6/6 outputs, page content, navigation and source hashes. These are basic core assertions, not UI acceptance evidence. Test fixtures are temporary and are not distributed.

## Pending acceptance

SPLIT-01 through SPLIT-12 are individually BLOCKED. Real Word installation/missing/hung behavior, WebView2 printing, advanced Markdown rendering, cancellation/close, sorted merging, partial failure, overwrite, translated UI, 100–200% DPI and a computer without development tools remain BLOCKED. The user explicitly requested basic unit tests only and no computer use this turn.

No complete acceptance or release-ready claim is made. A future actual Tauri UI run must produce the 40-page six-part outputs and preserve per-case screenshots, read-only content/structure checks and source/output hashes. Previous 1.x BLOCKED results stay unchanged.

See [UX and feature contract](TAURI_V2_UX.md) for supported content and current limitations, including unsupported user-supplied tagged PDFs/forms/layers. Local packages must include the Markdown resources, dependency notices and any beside-executable DLLs. WebView2 Runtime is required; Word desktop is required for Word conversion. Native execution is unverified.
