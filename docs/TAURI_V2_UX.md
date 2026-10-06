# PDF Forge 2.x UX and feature contract

Version: 2.0.0-alpha.2. Branch: codex/tauri-v2. Base: a78a22b (v1.0.5).

## Navigation and shared behavior

Split is first and selected at launch; the window starts maximized. Merge, Unlock and Convert remain visible. The interface supports system-default Traditional Chinese or English, a persisted manual language choice, visible keyboard focus, named icon controls and DPI scaling. Operation screens retain their state during navigation. Processing locks the active job's inputs while preserving Cancel, logs and window close.

Inputs are selected by native dialog or native file drop. Rust owns file access, allowed operations, passwords, process creation and output commits; the document renderer has no Tauri invoke permissions. Documents never leave the computer. Passwords are not logged. Results distinguish complete, partial, failed, cancelled and timed out jobs and disclose output locations.

Logs retain job ID, timestamp, stage and stable error code. Free-form parser/COM diagnostics are excluded from persistent logs because they can quote document contents. Formula, diagram and image rendering failures have distinct translated error codes.

## Behavior mapping

| Workspace | Preserved 1.x behavior | 2.x additions |
|---|---|---|
| Split | One PDF, continuous wheel browsing, thumbnails, page jump, zoom/Fit, page-gap scissors, right-side ranges and editable part names, individual/all save | PDF.js local preview; stable per-workspace state; unified results |
| Merge | PDF/DOC/DOCX ordered queue, move/delete, one PDF, per-file Word failures | Drag and keyboard ordering; isolated Rust workers; cancel/timeout/logs |
| Unlock | Multiple PDFs with shared known password, unencrypted copies, sources unchanged | Unified per-file progress/results and collision-safe output names |
| Convert | PDF to editable DOCX, desktop Word required, no OCR, layout caveat | Same-page Markdown mode with sorted batch, individual or merged PDFs, advanced content |

## Markdown contract

UTF-8 .md/.markdown documents; headings, tables, lists, tasks, footnotes, code and quotations; Mermaid, KaTeX math and optional heading TOC. Local PNG/JPEG/GIF/WebP images must be relative and canonicalize inside the document directory. Raw HTML is displayed as text and remote images are rejected. Renderer assets and KaTeX fonts are packaged locally.

A4, 18 mm margins. Optional header, footer and page numbers; Chromium margin-box support requires WebView2 131 or newer when these options are enabled. Fenced code is styled without syntax highlighting. Each file has a two-minute timeout. Invalid diagrams, formulae and images fail explicitly.

Output defaults to individual PDFs. Merged mode follows queue order and inserts no extra separator pages. Duplicate individual output stems are rejected before processing. Individual failures allow the remainder to run; merged output is committed only if every document converts successfully. Cancel preserves completed individual PDFs and existing final outputs.

## PDF and Word contracts

lopdf handles PDF structure and native known-password authentication; PDF.js handles preview only. Preserve contents, page boxes, rotations, URI and valid internal/named navigation, and outline hierarchy. Split prunes destinations outside retained pages. Remove standard Info and XMP structurally; never rewrite matching content bytes.

Forms, portfolios, tagged structures, layers and conflicting auxiliary name trees are explicitly unsupported until a preservation contract is tested. Do not present generated files as evidence of preservation. Original signature validity and PDF/A/PDF/UA compliance are not promised after rewriting.

Converter-created staging PDFs are normalized to an untagged PDF before merge: the converter's generated structure tags and page structure-parent indexes are removed, while page content, annotations and bookmarks remain. This applies only to newly generated Word/Markdown output, never user-supplied PDFs. Generated PDF accessibility tagging is not promised.

Word uses a disposable Rust STA COM worker with macros disabled, read-only source and no recent-document entries. Default timeout is five minutes; cancelled/closed jobs receive a five-second grace before termination. Only the created worker tree and a Word PID with matching recorded creation time are cleaned up; existing user Word instances are excluded. DOCX package structure is validated before output commit.

## Delivery and acceptance status

The latest user authorization requires real direct function/worker tests, without computer use or UI automation. Cross-machine tests and the full UI release gate are not performed. SPLIT-01 through SPLIT-12 and conversion UI tests remain BLOCKED. Formal future UI validation uses the unchanged rich 40-page fixture, cuts 7/14/21/28/34 and exact 7/7/7/7/6/6 outputs, with per-item evidence and hashes. Native COM/printing and packaging are distinct from basic unit results.

Tauri sources are under tauri/ alongside the unchanged 1.x C# solution. 2.x uses a different application identifier and local data folder. Build instructions and artifact limitations are documented in tauri/README.md. No main merge is included. The user separately authorized tagged pending-validation GitHub packages.


## 2026-10-06 direct output update

All output-file and output-folder pickers are removed. Outputs are placed in the first selected source folder. Merge uses `<first source stem>_merged.pdf`; PDF to Word uses `<source stem>.docx`; unlocked files use the existing `_unlocked` collision-safe names. Split filenames remain editable. Markdown individual PDFs keep source stems in the first source folder; merged output uses `<first source stem>_merged.pdf`. Duplicate individual names are rejected before execution, and existing files require overwrite consent. Results show actual paths and retain Open Folder.

Functional regression uses actual native workers and read-only independent tools per user authorization. These function tests do not turn UI acceptance into PASS.


Primary delivery is a single portable EXE containing the runtime-only ZIP. Resources are prepared in a verified private LocalAppData cache. No external resource folder is required, no project source or test evidence is included, and Word/WebView2 remain system prerequisites. All future packaging must follow AGENTS.md.
