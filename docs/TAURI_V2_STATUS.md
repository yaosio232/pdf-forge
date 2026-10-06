# PDF Forge 2.0.0-alpha.2 status — 2026-10-06

On `codex/tauri-v2`, based on v1.0.5 `a78a22b`; 1.x sources and its separate working directory are preserved. The user authorized direct real function/worker tests, with no application UI operation. The corrected package follows the earlier tagged-delivery authorization; alpha.1 is retained as history. No main merge.

## Confirmed defects and changes

- File dialogs ran blocking calls on Tauri’s event-loop thread. Input selection now runs on a blocking worker thread.
- All output pickers are removed; backend output locations are derived from the first source folder. Existing-output consent and source protection remain.
- PDF.js preview uses its locally bundled legacy main/worker pair, including the Promise compatibility required on older WebView2 runtimes. Synchronous loading errors are caught.
- Word COM initialization now identifies a newly created Word process instead of reading the nonexistent Application.Hwnd member. Long Office paths use a private short-path workspace, then a new staging-file transfer.
- Markdown initialization inserts the body readiness script before the bundled renderer; literal HTML strings inside Mermaid/DOMPurify are preserved.
- Retry location text follows the actual submitted first source while retaining the original batch inputs/settings.

## Test scope

Rust: 23 unit/integration tests plus 2 launcher payload/cache integrity tests. Frontend: 18 unit tests, including retry locations. Inherited .NET: 53 passed, 1 existing skipped test; existing NU1701 warnings. Production frontend/Tauri Windows x64 builds and portable/NSIS packaging are checked.

Real PDF workers use a fresh rich 40-page fixture, cuts 7/14/21/28/34 and six outputs of 7/7/7/7/6/6 pages. Independent pypdf checks verify every page ID/content, internal/external links, bookmark targets, metadata clearing, page ordering and source/output hashes. Actual encrypted PDFs, known/wrong passwords, existing-output/source protection, active cancellation and automatic output locations are exercised.

Real desktop Word and WebView2 workers exercise both Word conversion directions, >260-character paths, existing Word-instance protection, advanced Markdown/Mermaid/KaTeX/images/TOC/headers/footers/page numbers, explicit formula/diagram failures, individual partial success, sorted Markdown merging, mixed Word/PDF sorting and failed-merge output preservation. Real hung owned processes test cancellation/timeout without operating UI. Local machine-readable results and independent inspections are saved under ignored TestResults; fixtures and evidence are excluded from distribution.

## Remaining acceptance

SPLIT-01 through SPLIT-12 UI gates remain individually BLOCKED. Application UI, file-picker interaction, close-window interaction, translated UI, DPI and another computer without developer tools are not exercised. Word-not-installed behavior is not claimed as tested; the local host has Word. A dedicated five-minute Word timeout was not run; actual forced Word hang/cancellation and the two-minute Markdown timeout were tested. Existing 1.x BLOCKED evidence is unchanged. This is a function-tested prerelease, not a claim that the full UI release gate passed.

See [UX contract](TAURI_V2_UX.md) and [build instructions](../tauri/README.md). The primary portable EXE embeds all DLLs, offline Markdown resources and third-party notices and prepares a checksum-verified private cache at runtime; no source or test evidence is embedded. A supplementary ZIP and installer may also be supplied. WebView2 Runtime is required; desktop Word is required for Word conversion. No user documents or local test evidence are uploaded.
