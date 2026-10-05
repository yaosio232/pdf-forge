# Issue 1 conversion recovery implementation plan

> Execute inline with regression tests and a final independent review.

**Goal:** Recover from hung PDF → Word conversions and add offline batch Markdown → PDF without Word.

**Architecture:** Keep transactional output validation in the parent. Run Word COM in a disposable STA worker process, supervising cancellation and a five-minute timeout. Record the created Word process identity, excluding all preexisting Word instances. Render Markdown locally using Markdig and an isolated headless Edge/Chrome profile.

**Spec:** GitHub issue 1 and the user's clarification: PDF → Word hangs; Markdown must not depend on Word.

**Constraints:** Preserve source and prior output on failure/cancellation. No document upload. Do not change Split/Merge functional tests or waive UI gates. Remain on `codex/issue-1-conversion-recovery`; no automatic release or merge.

## 1. Recoverable conversion

- [ ] Add failing UI tests for accessible Cancel and Open log folder buttons while busy.
- [ ] Add cancellation transaction tests using existing rich 20-page fixture: cancel before conversion and after writing a valid temporary DOCX; prior output remains and no temporary survives.
- [ ] Implement `ConversionProcess.Run(ProcessStartInfo, CancellationToken, TimeSpan)` with process-tree cleanup on cancellation/timeout. Test with an owned slow process and an unrelated live process.
- [ ] Implement `WordConversionWorker.Run(source, temporary, token)`: invoke apphost worker mode through argument list, unique job files, own Word PID/start-time marker, error file, per-phase immediate logs. Capture preexisting Word IDs before COM creation and refuse to close/kill a preexisting process. Worker mode runs before the WinForms main window.
- [ ] Extend `PdfToWordConverter.Convert` with a cancellation token, validate token before committing output, and keep existing atomic replacement tests.
- [ ] Connect Cancel, close-after-cleanup, five-minute timeout, and log-folder buttons. Restore all controls after any outcome.

## 2. Offline Markdown batch

- [ ] Add tests for Markdown headings/tables/code/Traditional Chinese, blocked remote images/raw HTML, same-stem collision, cancellation, failed conversion preserving existing output and cleaning temporaries.
- [ ] Add Markdig parser dependency; reuse installed Edge/Chrome and stdlib process supervisor. Render generated HTML with A4 CSS, CSP prohibiting network and scripts, isolated profile, disabled browser background networking. Validate temporary PDF structure before atomic replacement.
- [ ] Rename fourth TAB `Convert Files`; retain PDF → Word default mode and Split startup TAB. Add keyboard-accessible mode selector and batch file list. Accept multiple `.md`/`.markdown` drops only in Markdown mode.
- [ ] Choose an output folder; confirm existing output replacement once before starting. Reject duplicate stems; report every success/failure, continue after per-file errors, stop queue on cancel. Keep successful outputs available through Open output folder.

## 3. Verify and document

- [ ] Inspect diff, then `dotnet build .\Pdf_Merger.sln --no-restore`, then `dotnet test .\Pdf_Merger.sln --no-build --logger "console;verbosity=minimal"`.
- [ ] Exercise actual local browser conversion on fresh substantial 20-page Markdown content; extract PDF text/page IDs and record unchanged source hashes. Service evidence is separate from UI evidence.
- [ ] Attempt actual WinForms UI validation through the computer-use skill; preserve every SPLIT-01..12 result and mark blocked items honestly.
- [ ] Save local report/results/hashes under unique `TestResults` directory. Update README with dependencies, cancellation/timeout, offline image limitations. Request independent code review and address findings; repeat checks only after relevant changes.
