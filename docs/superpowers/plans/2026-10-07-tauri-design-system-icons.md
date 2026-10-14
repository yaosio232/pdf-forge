# Tauri Design System and Icon Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the approved `Design.md` icon, tooltip, and page-gap interaction rules to the Rust/Tauri 2.x UI without changing its Rust workflow contract or replacing the existing compact layout.

**Architecture:** Keep the existing React layout and semantic CSS as the source of page geometry. Add Tailwind v4 only as the shadcn/ui component styling compiler, add owned Button and Tooltip source under `src/components/ui`, and use those components for compact icon controls. `Preview` continues to receive the cut state and toggle callback from `App`; it only changes the visual representation of an unset versus selected cut.

**Tech Stack:** React 19, Vite 7, TypeScript, Tailwind CSS v4, shadcn/ui-owned source, Radix Tooltip, Lucide, Vitest, React Testing Library.

**Spec:** `Design.md` sections 2, 4, 5, 8, 9, 10, and 11; `docs/TAURI_V2_UX.md`.

## Global Constraints

- Preserve the current uncommitted compact-layout changes in `App.tsx`, `App.test.tsx`, and `style.css`; do not reset, overwrite, or expand their scope.
- Keep all four workspaces, direct source-folder output, output protection, continuous preview, cuts, zoom, panning, and existing Rust invoke contracts unchanged.
- Use the approved tokens and dimensions: 8–12 px workspace padding, 10–12 px panels, 8–10 px cards, 6–8 px local gaps, 32–36 px desktop controls, and visible keyboard focus.
- Tooltip delay is 300 ms; it opens on hover and focus, closes on Escape, uses a portal, and never substitutes for a control's accessible name or an input label.
- A page-gap control remains focusable and clickable when unset, but its scissors icon only appears after that page gap is selected. Right and middle click never toggle a cut; last page has no cut control.
- Do not claim UI/DPI acceptance. This plan only requires component tests, frontend build, and Rust test execution through the project-local cargo toolchain.

---

### Task 1: Add the minimal shadcn/ui foundation without replacing global layout CSS

**Files:**
- Create: `tauri/components.json`
- Create: `tauri/src/lib/utils.ts`
- Create: `tauri/src/components/ui/button.tsx`
- Create: `tauri/src/components/ui/tooltip.tsx`
- Modify: `tauri/package.json`
- Modify: `tauri/package-lock.json`
- Modify: `tauri/tsconfig.json`
- Modify: `tauri/vite.config.ts`
- Modify: `tauri/src/style.css`
- Modify: `tauri/src/main.tsx`
- Test: `tauri/src/App.test.tsx`

**Interfaces:**
- Consumes: existing React entry point and `style.css` semantic variables.
- Produces: `Button`, `Tooltip`, and a root `TooltipProvider` available to every workspace without changing Tauri invokes.

- [ ] **Step 1: Write a failing app-level tooltip test**

Add a test that focuses the log-folder icon action, advances the 300 ms tooltip delay, and expects a `role="tooltip"` containing the localized `Open logs` text. The regression it catches is an icon-only action with no keyboard-visible operation description.

- [ ] **Step 2: Run the focused test to verify it fails for the missing tooltip**

Run: `npm test -- src/App.test.tsx`

Expected: the new tooltip assertion fails because the current app only renders button text or native `title` attributes.

- [ ] **Step 3: Install and configure the smallest approved dependency set**

Install `tailwindcss`, `@tailwindcss/vite`, `@radix-ui/react-tooltip`, `class-variance-authority`, `clsx`, and `tailwind-merge`. Add the Vite Tailwind plugin plus `@/*` aliases in Vite and TypeScript. Add `components.json` with the `src/components`, `src/lib`, and `src/components/ui` aliases. Import Tailwind's theme and utilities without its preflight reset so existing global button/input, PDF canvas, and native select rules remain authoritative.

- [ ] **Step 4: Add owned Button and Tooltip sources**

Implement the standard shadcn ownership model: `cn()` merges class values; `Button` exposes `default`, `outline`, `ghost`, and 34 px `icon` variants; `Tooltip` wraps Radix Provider/Root/Trigger/Portal/Content and sets the required 300 ms delay. Its content uses the existing `--surface`, `--text`, `--border`, and `--blue` variables, wraps long paths, and does not use a native `title` attribute.

- [ ] **Step 5: Mount one root TooltipProvider and retain all existing CSS geometry**

Wrap `App` in `TooltipProvider` in `main.tsx`. Add only component support CSS and `data-slot`-safe selectors to `style.css`; do not remove the existing compact dimensions or add Tailwind preflight.

- [ ] **Step 6: Run the focused test to verify it passes**

Run: `npm test -- src/App.test.tsx`

Expected: the test renders one accessible tooltip after focus and delay; no existing App tests regress.

### Task 2: Make page-gap cut feedback match the approved state model

**Files:**
- Modify: `tauri/src/Preview.tsx`
- Modify: `tauri/src/Preview.test.tsx`
- Modify: `tauri/src/style.css`

**Interfaces:**
- Consumes: `cuts: number[]`, `toggle(page: number)`, `disabled`, and existing `t` translation callback.
- Produces: identical clickable page-gap controls in thumbnail and main preview, with scissors only for selected gaps.

- [ ] **Step 1: Write failing preview tests for both representations**

Extend the existing 40-page preview fixture to assert that all 39 gap controls exist while `cuts` is empty, neither surface contains a scissors SVG for page 7, and both surfaces show a scissors SVG after rerendering with `[7]`. Add an Enter/Space assertion against a focused page-gap control and confirm that a middle-button auxiliary click does not call `toggle`.

- [ ] **Step 2: Run the focused preview test to verify it fails**

Run: `npm test -- src/Preview.test.tsx`

Expected: the unset-cut assertion fails because the current thumbnail and main controls always render `Scissors`.

- [ ] **Step 3: Implement the smallest state-only rendering change**

Keep each existing native button, `aria-pressed`, `disabled`, and `toggle(i + 1)` call. Render `Scissors` only when `cuts.includes(i + 1)`; keep its fixed gap control, translated `aria-label`, focusability, and active visual marker. Add tooltip wrapping to the gap action with `cut`/`uncut` text, and handle Enter/Space through the native button behavior without adding a second interaction path.

- [ ] **Step 4: Keep page-gap geometry stable**

Adjust only `.thumbnail-cut` and `.scissors` child alignment so the empty state retains its 34 px / existing main-gap footprint. Preserve middle-button panning, pointer capture, Ctrl zoom, continuous scroll, and the absence of a last-page cut.

- [ ] **Step 5: Run the focused preview test to verify it passes**

Run: `npm test -- src/Preview.test.tsx`

Expected: selected/unselected icon state, keyboard toggle, disabled behavior, shared state, and no last-page control all pass.

### Task 3: Replace repeated copy with labeled, accessible compact icon controls

**Files:**
- Modify: `tauri/src/App.tsx`
- Modify: `tauri/src/App.test.tsx`
- Modify: `tauri/src/i18n.ts`
- Modify: `tauri/src/style.css`

**Interfaces:**
- Consumes: current translations, queue operations, file chooser, folder opener, range saver, and result opener.
- Produces: icon controls whose accessible name and Tooltip content describe the same action, while primary execution controls and required labels remain textual.

- [ ] **Step 1: Write failing behavioral tests before replacing copy**

Add assertions that: (1) the selected workspace has a semantic heading but no duplicate visible title/description block, (2) the file action has the localized `Add files` or `Replace PDF` accessible name, (3) the queue remove control has `Remove selected`, (4) range reset exposes `Reset all filenames from the current prefix`, and (5) existing range-save, reorder, and output-folder actions retain their exact requests after becoming icon controls.

- [ ] **Step 2: Run the focused App test to verify it fails**

Run: `npm test -- src/App.test.tsx`

Expected: the new icon/tooltip behavior assertions fail against the current text-plus-native-title implementation.

- [ ] **Step 3: Apply the approved icon mapping only**

Use `Info` for workspace help, `FilePlus2` for add/replace, `FolderOpen` for logs/results, `Trash2` for removal, `GripVertical`/`ArrowUp`/`ArrowDown` for ordering, `RotateCcw` for range-name reset, `Download` for individual save, `Folder` for output location, and `MoveHorizontal` for Fit. Keep tab names, main execution labels, input labels, filenames, page/range counts, errors, status, and overwrite/Word restrictions visible.

- [ ] **Step 4: Make text reductions accessible and localized**

Keep `h1` in the DOM using `sr-only`; replace its visible explanatory line with the `Info` tooltip. Replace native `title` attributes for paths and icon actions with Tooltip triggers that are focusable and carry the full path/action in both languages. Add only the exact translation keys required by the new icon labels; never hide password, overwrite, or conversion limitations in tooltips.

- [ ] **Step 5: Apply compact icon-control styling without altering layout contracts**

Use the new 34 px icon Button variant and maintain 6–8 px gaps. Preserve 32 px existing result/reorder targets, increase only controls touched by this task to at least 34 px desktop targets, and retain visible focus, disabled semantics, and output-path wrapping. Do not alter preview/sidebar columns, add a resize handle, or restore output pickers.

- [ ] **Step 6: Run the focused App test to verify it passes**

Run: `npm test -- src/App.test.tsx`

Expected: all existing workflow requests remain unchanged and the new controls have localized names/tooltips.

### Task 4: Verify source, frontend, and Rust regressions without claiming UI acceptance

**Files:**
- Modify: `docs/superpowers/plans/2026-10-07-tauri-design-system-icons.md` (check completed steps only)

**Interfaces:**
- Consumes: completed source and test suites.
- Produces: fresh, scoped evidence for component/build validation; no SPLIT UI gate status change.

- [x] **Step 1: Inspect the final diff**

Run: `git diff --check` and `git diff -- tauri/package.json tauri/src/main.tsx tauri/src/App.tsx tauri/src/Preview.tsx tauri/src/style.css tauri/src/components`

Expected: only the planned UI foundation and interaction changes appear; no reset of pre-existing compact-layout work and no Rust workflow change.

- [x] **Step 2: Run the full frontend suite and production build**

Run: `npm test && npm run build`

Expected: every Vitest file passes and TypeScript/Vite emits the production frontend bundle.

- [ ] **Step 3: Run the Rust suite with the repository toolchain**

Run: `..\\.tools\\cargo\\bin\\cargo.exe test`

Expected: the Rust unit/integration suite passes without changing the UI acceptance status.

- [x] **Step 4: Report verification boundaries precisely**

Record the fresh command results, dependency additions, modified files, and the unchanged BLOCKED status for SPLIT-01 through SPLIT-12, DPI, and actual desktop UI acceptance. Do not package, tag, publish, merge, or claim release readiness.

## Verification result (2026-10-07)

- `npm test`: 32 tests in 4 files passed.
- `npm run build`: TypeScript and Vite production build passed. Vite retained its existing >500 kB chunk-size warning.
- `cargo test` was attempted with the repository-local rustup, cargo, and LLVM-Mingw paths. It is blocked before project tests by the local GNU toolchain's missing `libgcc` and `libgcc_eh` libraries. No Rust source, toolchain, or global configuration was changed.
- `git diff --check` passed. Actual Tauri desktop UI, DPI, and SPLIT-01 through SPLIT-12 acceptance remain BLOCKED and were not claimed.
