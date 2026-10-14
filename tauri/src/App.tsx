import { useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWebview } from "@tauri-apps/api/webview";
import {
  Scissors,
  Files,
  LockOpen,
  FileOutput,
  Folder,
  FolderOpen,
  FilePlus2,
  Info,
  ArrowUp,
  ArrowDown,
  Trash2,
  GripVertical,
  Globe,
  Download,
  RotateCcw,
} from "lucide-react";
import Preview from "./Preview";
import { Button } from "./components/ui/button";
import { Tooltip } from "./components/ui/tooltip";
import {
  acceptFiles,
  busy,
  filename,
  ranges,
  reorder,
  stem,
  systemLanguage,
  type JobEvent,
  type Language,
  type Options,
  type Range,
  type Request,
  type Workspace,
} from "./model";
import {
  translator,
  localizedError as errorText,
  phaseText,
  type Key,
} from "./i18n";
type State = {
  files: string[];
  selected: string[];
  submittedSource?: string;
  job?: JobEvent;
  error?: string;
};
const initial = (): State => ({ files: [], selected: [] });
export default function App() {
  const [language, setLanguage] = useState<Language>(() => {
    const saved = localStorage.getItem("forge-language");
    return saved === "en" || saved === "zh-TW"
      ? saved
      : systemLanguage(navigator.language);
  });
  const t = translator(language);
  const [workspace, setWorkspace] = useState<Workspace>("split");
  const [states, setStates] = useState<Record<Workspace, State>>({
    split: initial(),
    merge: initial(),
    unlock: initial(),
    convert: initial(),
  });
  const state = states[workspace];
  const locked = busy(state.job);
  const outputSource = state.submittedSource ?? state.files[0];
  const [cap, setCap] = useState({
    word: false,
    webview2: false,
    version: "2.0.0-alpha.3",
  });
  const [cuts, setCuts] = useState<number[]>([]);
  const [pages, setPages] = useState(0);
  const [pdf, setPdf] = useState<number[]>();
  const [previewView, setPreviewView] = useState({ page: 1, scale: 1 });
  const [names, setNames] = useState<Record<string, string>>({});
  const [prefix, setPrefix] = useState("");
  const [password, setPassword] = useState("");
  const [mode, setMode] = useState<"pdfToWord" | "markdown">("pdfToWord");
  const [modeStates, setModeStates] = useState({
    pdfToWord: initial(),
    markdown: initial(),
  });
  const [merged, setMerged] = useState(false);
  const [options, setOptions] = useState<Options>({
    toc: false,
    header: "",
    footer: "",
    pageNumbers: true,
  });
  const [overwrite, setOverwrite] = useState(false);
  const active = useRef({ workspace, mode, locked });
  active.current = { workspace, mode, locked };
  const owners = useRef(new Map<string, Workspace>());
  const dragIndex = useRef(-1);
  const splitLoad = useRef(0);
  const update = (w: Workspace, change: Partial<State>) =>
    setStates((all) => ({
      ...all,
      [w]: {
        ...all[w],
        ...change,
        ...(change.files !== undefined ? { submittedSource: undefined } : {}),
      },
    }));
  const report = (w: Workspace, error: unknown) => {
    const e = error as { code?: string };
    update(w, { error: e?.code || "OPERATION_FAILED" });
  };
  useEffect(() => {
    document.documentElement.lang = language;
    localStorage.setItem("forge-language", language);
  }, [language]);
  useEffect(() => {
    void invoke<typeof cap>("capabilities")
      .then(setCap)
      .catch((e) => report("split", e));
    let disposed = false;
    let off: (() => void) | undefined;
    void listen<JobEvent>("job-event", (event) => {
      const w = owners.current.get(event.payload.jobId);
      if (w) update(w, { job: event.payload });
    }).then((fn) => {
      if (disposed) fn();
      else off = fn;
    });
    return () => {
      disposed = true;
      off?.();
    };
  }, []);
  const ingest = async (w: Workspace, paths: string[], kind: string) => {
    if (active.current.locked) return;
    const accepted = acceptFiles(kind, paths);
    if (accepted.length !== paths.length)
      update(w, { error: "UNSUPPORTED_FORMAT" });
    if (!accepted.length) return;
    if (w === "split") {
      const path = accepted[0];
      const load = ++splitLoad.current;
      update(w, { files: [path], error: undefined });
      setPdf(undefined);
      setPreviewView({ page: 1, scale: 1 });
      setPages(0);
      setCuts([]);
      setNames({});
      setPrefix(stem(path));
      try {
        const info = await invoke<{ pages: number; encrypted: boolean }>(
          "pdf_info",
          { path },
        );
        if (info.encrypted) throw { code: "PDF_ENCRYPTED" };
        const bytes = await invoke<number[]>("read_pdf", { path });
        if (load !== splitLoad.current) return;
        setPages(info.pages);
        setPdf(bytes);
      } catch (e) {
        if (load === splitLoad.current) report(w, e);
      }
    } else
      setStates((all) => ({
        ...all,
        [w]: {
          ...all[w],
          submittedSource: undefined,
          files:
            kind === "pdfToWord"
              ? [accepted[0]]
              : [...new Set([...all[w].files, ...accepted])],
          error:
            accepted.length !== paths.length ? "UNSUPPORTED_FORMAT" : undefined,
        },
      }));
  };
  const ingestRef = useRef(ingest);
  ingestRef.current = ingest;
  useEffect(() => {
    let disposed = false;
    let off: (() => void) | undefined;
    void getCurrentWebview()
      .onDragDropEvent((event) => {
        if (event.payload.type === "drop") {
          const a = active.current;
          void ingestRef.current(
            a.workspace,
            event.payload.paths,
            a.workspace === "convert" ? a.mode : a.workspace,
          );
        }
      })
      .then((fn) => {
        if (disposed) fn();
        else off = fn;
      })
      .catch(() => {});
    return () => {
      disposed = true;
      off?.();
    };
  }, []);
  useEffect(() => {
    const jobs = Object.entries(states).filter(
      ([, s]) => busy(s.job) && s.job?.jobId !== "pending",
    );
    if (!jobs.length) return;
    const timer = setInterval(() => {
      for (const [w, s] of jobs)
        void invoke<JobEvent>("get_job", { jobId: s.job!.jobId })
          .then((job) => update(w as Workspace, { job }))
          .catch(() => {});
    }, 1000);
    return () => clearInterval(timer);
  }, [states]);
  const choose = async () => {
    try {
      const paths = await invoke<string[]>("select_files", {
        kind: workspace === "convert" ? mode : workspace,
      });
      await ingest(
        workspace,
        paths,
        workspace === "convert" ? mode : workspace,
      );
    } catch (e) {
      report(workspace, e);
    }
  };
  const parts = ranges(
    pages,
    cuts,
    prefix || stem(state.files[0] || "document"),
  ).map((r) => ({ ...r, name: names[`${r.first}-${r.last}`] || r.name }));
  const start = async (request: Request, w = workspace) => {
    if (busy(states[w].job)) return;
    const pending = {
      jobId: "pending",
      status: "preparing",
      phase: "preparing",
      completed: 0,
      total: 1,
      items: [],
      error: null,
    };
    try {
      update(w, {
        error: undefined,
        job: pending,
        submittedSource:
          "source" in request ? request.source : request.sources[0],
      });
      const jobId = await invoke<string>("start_job", { request });
      owners.current.set(jobId, w);
      update(w, {
        job: {
          jobId,
          status: "preparing",
          phase: "preparing",
          completed: 0,
          total:
            request.kind === "split"
              ? request.ranges.length
              : "sources" in request
                ? request.sources.length
                : 1,
          items: [],
          error: null,
        },
      });
      void invoke<JobEvent>("get_job", { jobId })
        .then((job) => update(w, { job }))
        .catch((e) => report(w, e));
    } catch (e) {
      update(w, { job: { ...pending, status: "failed" } });
      report(w, e);
    }
  };
  const run = (single?: Range, retry = false) => {
    const files =
      retry && !(workspace === "merge" || (workspace === "convert" && merged))
        ? state.job?.items.filter((i) => i.error).map((i) => i.source) || []
        : state.files;
    if (!files.length || (workspace === "split" && !pages)) {
      update(workspace, { error: "INVALID_INPUT" });
      return;
    }
    let request: Request;
    if (workspace === "split")
      request = {
        kind: "split",
        source: files[0],
        directory: "",
        ranges: single ? [single] : parts,
        overwrite,
      };
    else if (workspace === "merge")
      request = {
        kind: "merge",
        sources: files,
        output: "",
        overwrite,
      };
    else if (workspace === "unlock")
      request = {
        kind: "unlock",
        sources: files,
        password,
        directory: "",
      };
    else if (mode === "pdfToWord")
      request = {
        kind: "pdfToWord",
        source: files[0],
        output: "",
        overwrite,
      };
    else
      request = {
        kind: "markdown",
        sources: files,
        output: "",
        merged,
        overwrite,
        options,
      };
    void start(request);
  };
  const move = (from: number, to: number) =>
    update(workspace, { files: reorder(state.files, from, to) });
  const switchMode = (next: typeof mode) => {
    setModeStates((all) => ({ ...all, [mode]: state }));
    update("convert", modeStates[next]);
    setMode(next);
  };
  const open = async (path: string) => {
    try {
      await invoke("open_folder", { path });
    } catch (e) {
      report(workspace, e);
    }
  };
  const icon = {
    split: Scissors,
    merge: Files,
    unlock: LockOpen,
    convert: FileOutput,
  };
  const hint: Record<Workspace, Key> = {
    split: "splitHint",
    merge: "queueHint",
    unlock: "unlockHint",
    convert: "convertHint",
  };
  const available =
    workspace === "merge"
      ? cap.word || !state.files.some((p) => /\.docx?$/i.test(p))
      : workspace !== "convert" ||
        (mode === "pdfToWord" ? cap.word : cap.webview2);
  const job = state.job;
  return (
    <div className="app">
      <a className="skip" href="#main">
        {t(workspace)}
      </a>
      <header className="app-header">
        <div className="brand">
          <span className="brand-icon">
            <Files size={24} />
          </span>
          <div>
            <strong>PDF Forge</strong>
            <small>{t("local")}</small>
          </div>
        </div>
        <div className="header-actions">
          <Tooltip content={t("logs")}>
            <Button
              size="icon"
              aria-label={t("logs")}
              onClick={() =>
                void invoke<string>("log_folder")
                  .then(open)
                  .catch((e) => report(workspace, e))
              }
            >
              <FolderOpen size={17} aria-hidden="true" />
            </Button>
          </Tooltip>
          <Tooltip content={t("language")}>
            <label className="language">
              <Globe size={20} aria-hidden="true" />
              <select
                aria-label={t("language")}
                value={language}
                onChange={(e) => setLanguage(e.target.value as Language)}
              >
                <option value="zh-TW">繁體中文</option>
                <option value="en">English</option>
              </select>
            </label>
          </Tooltip>
          <span className="version">v{cap.version}</span>
        </div>
      </header>
      <nav className="tabs" aria-label="PDF Forge">
        {(["split", "merge", "unlock", "convert"] as Workspace[]).map((w) => {
          const Icon = icon[w];
          return (
            <button
              key={w}
              aria-current={workspace === w ? "page" : undefined}
              className={workspace === w ? "selected" : ""}
              onClick={() => setWorkspace(w)}
            >
              <Icon size={20} />
              {t(w)}
              {busy(states[w].job) && <span className="working-dot" />}
            </button>
          );
        })}
      </nav>
      <main id="main">
        <div className="workspace-heading">
          <h1 className="sr-only">{t(workspace)}</h1>
          <div className="workspace-actions">
            <Tooltip content={t(hint[workspace])}>
              <Button size="icon" aria-label={t(hint[workspace])}>
                <Info size={18} aria-hidden="true" />
              </Button>
            </Tooltip>
            <Tooltip content={t("choose")}>
              <Button
                variant="primary"
                size="icon"
                aria-label={t("choose")}
                disabled={locked}
                onClick={() => void choose()}
              >
                <FilePlus2 size={18} aria-hidden="true" />
              </Button>
            </Tooltip>
          </div>
        </div>
        {workspace === "convert" && (
          <div className="mode-switch">
            <button
              disabled={locked}
              aria-pressed={mode === "pdfToWord"}
              onClick={() => switchMode("pdfToWord")}
            >
              {t("word")}
            </button>
            <button
              disabled={locked}
              aria-pressed={mode === "markdown"}
              onClick={() => switchMode("markdown")}
            >
              {t("markdown")}
            </button>
          </div>
        )}
        {!available && (
          <div className="notice" role="status">
            {t(
              workspace === "merge" || mode === "pdfToWord"
                ? "wordMissing"
                : "webviewMissing",
            )}
          </div>
        )}
        {state.error && (
          <div className="error" role="alert">
            {errorText(language, state.error)}
            <button
              aria-label={t("close")}
              onClick={() => update(workspace, { error: undefined })}
            >
              ×
            </button>
          </div>
        )}
        <div
          className={`workspace-grid ${workspace === "split" ? "split-grid" : ""}`}
        >
          <div
            className={`work-surface ${state.files.length ? "has-files" : "empty-state"}`}
          >
            {workspace === "split" ? (
              pdf ? (
                <Preview
                  data={pdf}
                  view={previewView}
                  onView={setPreviewView}
                  cuts={cuts}
                  toggle={(page) =>
                    setCuts((old) =>
                      old.includes(page)
                        ? old.filter((p) => p !== page)
                        : [...old, page],
                    )
                  }
                  disabled={locked}
                  t={t}
                  onError={() =>
                    update("split", { error: "PDF_PREVIEW_FAILED" })
                  }
                />
              ) : (
                <div className="empty">
                  <Scissors size={40} />
                  <p>{state.files.length ? t("loading") : t("empty")}</p>
                  {state.files[0] && <span>{filename(state.files[0])}</span>}
                </div>
              )
            ) : (
              <>
                <div className="queue-tools">
                  <span>
                    {state.files.length} {t("choose")}
                  </span>
                  <Tooltip content={t("remove")}>
                    <Button
                      size="icon"
                      aria-label={t("remove")}
                      disabled={locked || !state.selected.length}
                      onClick={() =>
                        update(workspace, {
                          files: state.files.filter(
                            (p) => !state.selected.includes(p),
                          ),
                          selected: [],
                        })
                      }
                    >
                      <Trash2 size={16} aria-hidden="true" />
                    </Button>
                  </Tooltip>
                </div>
                {!state.files.length ? (
                  <div className="empty">
                    <Files size={40} />
                    <p>{t("empty")}</p>
                  </div>
                ) : (
                  <ol className="queue">
                    {state.files.map((path, i) => (
                      <li key={path} data-queue-index={i}>
                        <label className="queue-select">
                          <input
                            type="checkbox"
                            disabled={locked}
                            checked={state.selected.includes(path)}
                            onChange={(e) =>
                              update(workspace, {
                                selected: e.target.checked
                                  ? [...state.selected, path]
                                  : state.selected.filter((p) => p !== path),
                              })
                            }
                          />
                          <span className="order">
                            {String(i + 1).padStart(2, "0")}
                          </span>
                          <Tooltip content={path}>
                          <span className="queue-file" tabIndex={0}>
                            {filename(path)}
                          </span>
                          </Tooltip>
                        </label>
                        {(workspace === "merge" ||
                          (workspace === "convert" && mode === "markdown")) && (
                          <div className="reorder">
                            <Tooltip content={`${t("queueHint")} ${filename(path)}`}>
                              <button
                                className="drag-handle"
                                disabled={locked}
                                aria-label={`${t("queueHint")} ${filename(path)}`}
                                onPointerDown={(e) => {
                                  if (e.button !== 0) return;
                                  dragIndex.current = i;
                                  e.currentTarget.setPointerCapture(e.pointerId);
                                  e.currentTarget
                                    .closest("li")
                                    ?.classList.add("dragging");
                                }}
                                onPointerUp={(e) => {
                                  e.currentTarget
                                    .closest("li")
                                    ?.classList.remove("dragging");
                                  const row = document
                                    .elementFromPoint(e.clientX, e.clientY)
                                    ?.closest<HTMLElement>("[data-queue-index]");
                                  if (row && !locked)
                                    move(
                                      dragIndex.current,
                                      Number(row.dataset.queueIndex),
                                    );
                                  dragIndex.current = -1;
                                }}
                                onPointerCancel={(e) => {
                                  e.currentTarget
                                    .closest("li")
                                    ?.classList.remove("dragging");
                                  dragIndex.current = -1;
                                }}
                              >
                                <GripVertical size={17} aria-hidden="true" />
                              </button>
                            </Tooltip>
                            <Tooltip content={`${t("up")} ${filename(path)}`}>
                              <button
                                disabled={locked || i === 0}
                                aria-label={`${t("up")} ${filename(path)}`}
                                onClick={() => move(i, i - 1)}
                              >
                                <ArrowUp size={17} aria-hidden="true" />
                              </button>
                            </Tooltip>
                            <Tooltip content={`${t("down")} ${filename(path)}`}>
                              <button
                                disabled={locked || i === state.files.length - 1}
                                aria-label={`${t("down")} ${filename(path)}`}
                                onClick={() => move(i, i + 1)}
                              >
                                <ArrowDown size={17} aria-hidden="true" />
                              </button>
                            </Tooltip>
                          </div>
                        )}
                      </li>
                    ))}
                  </ol>
                )}
              </>
            )}
          </div>
          <aside className="settings">
            <fieldset disabled={locked}>
              <legend>{t("output")}</legend>
              {workspace === "unlock" && (
                <label>
                  {t("password")}
                  <input
                    type="password"
                    autoComplete="off"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                  />
                </label>
              )}
              {workspace === "convert" && mode === "markdown" && (
                <>
                  <label className="check">
                    <input
                      type="checkbox"
                      checked={merged}
                      onChange={(e) => {
                        setMerged(e.target.checked);
                      }}
                    />
                    {t("merged")}
                  </label>
                  <details>
                    <summary>{t("advanced")}</summary>
                    <p className="muted">{t("mermaid")}</p>
                    <label className="check">
                      <input
                        type="checkbox"
                        checked={options.toc}
                        onChange={(e) =>
                          setOptions({ ...options, toc: e.target.checked })
                        }
                      />
                      {t("toc")}
                    </label>
                    <label>
                      {t("header")}
                      <input
                        value={options.header}
                        onChange={(e) =>
                          setOptions({ ...options, header: e.target.value })
                        }
                      />
                    </label>
                    <label>
                      {t("footer")}
                      <input
                        value={options.footer}
                        onChange={(e) =>
                          setOptions({ ...options, footer: e.target.value })
                        }
                      />
                    </label>
                    <label className="check">
                      <input
                        type="checkbox"
                        checked={options.pageNumbers}
                        onChange={(e) =>
                          setOptions({
                            ...options,
                            pageNumbers: e.target.checked,
                          })
                        }
                      />
                      {t("pageNumbers")}
                    </label>
                  </details>
                </>
              )}
              <p className="muted">{t("automaticOutput")}</p>
              <Tooltip content={outputSource || "—"}>
                <p className="output-path" tabIndex={0}>
                  <Folder size={16} aria-hidden="true" />
                  <span>{outputSource?.replace(/[\\/][^\\/]+$/, "") || "—"}</span>
                </p>
              </Tooltip>
              {workspace !== "unlock" && (
                <label className="check">
                  <input
                    type="checkbox"
                    checked={overwrite}
                    onChange={(e) => setOverwrite(e.target.checked)}
                  />
                  {t("overwrite")}
                </label>
              )}
              {workspace === "split" && pages > 0 && (
                <>
                  <div className="section-label">
                    <h2>{t("parts")}</h2>
                    <span>{parts.length}</span>
                  </div>
                  <label>
                    {t("base")}
                    <input
                      value={prefix}
                      onChange={(e) => setPrefix(e.target.value)}
                    />
                  </label>
                  <Tooltip content={t("resetNames")}>
                    <Button
                      size="icon"
                      aria-label={t("resetNames")}
                      onClick={() => setNames({})}
                    >
                      <RotateCcw size={16} aria-hidden="true" />
                    </Button>
                  </Tooltip>
                  <div className="range-list">
                    {parts.map((r) => (
                      <div className="range" key={`${r.first}-${r.last}`}>
                        <div className="range-header">
                        <Files size={23} aria-hidden="true" />
                        <span className="range-title">
                          {t("page")} {r.first}–{r.last}{" "}
                          <small>({r.last - r.first + 1})</small>
                        </span>
                        <Tooltip content={`${t("saveOne")} ${t("page")} ${r.first}–${r.last}`}>
                          <Button
                            className="range-save"
                            size="icon"
                            aria-label={`${t("saveOne")} ${t("page")} ${r.first}–${r.last}`}
                            onClick={() => run(r)}
                          >
                            <Download size={18} aria-hidden="true" />
                          </Button>
                        </Tooltip>
                        </div>
                        <label className="range-filename">
                          <span className="sr-only">{t("rename")} {t("page")} {r.first}–{r.last}</span>
                          <input
                            value={r.name}
                            onChange={(e) =>
                              setNames({
                                ...names,
                                [`${r.first}-${r.last}`]: e.target.value,
                              })
                            }
                          />
                        </label>
                      </div>
                    ))}
                  </div>
                </>
              )}
              <button
                className="primary wide"
                disabled={
                  !state.files.length ||
                  !available ||
                  (workspace === "split" && !pages)
                }
                onClick={() => run()}
              >
                {t(workspace === "split" ? "saveAll" : "run")}
              </button>
            </fieldset>
            {job && (
              <section className="job" aria-live="polite">
                <h2>{t("status")}</h2>
                <strong>
                  {t(
                    ([
                      "preparing",
                      "running",
                      "cancelling",
                      "cancelled",
                      "failed",
                      "partial",
                      "completed",
                    ].includes(job.status)
                      ? job.status
                      : "idle") as Key,
                  )}
                </strong>
                <p>
                  {t("phase")}: {phaseText(language, job.phase)}
                </p>
                <progress
                  aria-label={t("progress")}
                  value={job.completed}
                  max={Math.max(1, job.total)}
                />
                <span>
                  {job.completed} / {job.total}
                </span>
                {job.error && (
                  <p className="error-text">
                    {errorText(language, job.error.code)}
                  </p>
                )}
                {busy(job) && (
                  <button
                    className="wide"
                    disabled={
                      job.status === "cancelling" || job.jobId === "pending"
                    }
                    onClick={() =>
                      void invoke("cancel_job", { jobId: job.jobId }).catch(
                        (e) => report(workspace, e),
                      )
                    }
                  >
                    {t("cancel")}
                  </button>
                )}
                {!busy(job) &&
                  job.items.some((i) => i.error) &&
                  workspace !== "split" && (
                    <button
                      className="wide"
                      onClick={() => run(undefined, true)}
                    >
                      {t("retry")}
                    </button>
                  )}
                <ul className="results">
                  {job.items.map((item, i) => (
                    <li key={i}>
                      <strong>{filename(item.source)}</strong>
                      {item.error ? (
                        <span className="error-text">
                          {errorText(language, item.error.code)}
                        </span>
                      ) : (
                        <span>
                          {item.output ? filename(item.output) : t("pending")}
                        </span>
                      )}
                      {item.output && (
                        <Tooltip content={`${t("open")} ${filename(item.output)}`}>
                          <button
                            aria-label={`${t("open")} ${filename(item.output)}`}
                            onClick={() => void open(item.output!)}
                          >
                            <FolderOpen size={16} aria-hidden="true" />
                          </button>
                        </Tooltip>
                      )}
                    </li>
                  ))}
                </ul>
              </section>
            )}
          </aside>
        </div>
      </main>
      <footer>
        <span>{t("local")}</span>
        <span>{state.files.length ? filename(state.files[0]) : t("none")}</span>
      </footer>
    </div>
  );
}
