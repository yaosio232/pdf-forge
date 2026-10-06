import { useEffect, useRef, useState } from "react";
import {
  getDocument,
  GlobalWorkerOptions,
  type PDFDocumentProxy,
} from "pdfjs-dist/legacy/build/pdf.mjs";
import worker from "pdfjs-dist/legacy/build/pdf.worker.min.mjs?url";
import { Scissors } from "lucide-react";
import type { Key } from "./i18n";
GlobalWorkerOptions.workerSrc = worker;
function Page({
  doc,
  page,
  scale,
  onReady,
}: {
  doc: PDFDocumentProxy;
  page: number;
  scale: number;
  onReady?: () => void;
}) {
  const ref = useRef<HTMLCanvasElement>(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    let stopped = false;
    let task:
      | ReturnType<Awaited<ReturnType<PDFDocumentProxy["getPage"]>>["render"]>
      | undefined;
    void doc
      .getPage(page)
      .then((p) => {
        if (stopped || !ref.current) return;
        const canvas = ref.current;
        const viewport = p.getViewport({ scale });
        const ratio = window.devicePixelRatio || 1;
        canvas.width = Math.floor(viewport.width * ratio);
        canvas.height = Math.floor(viewport.height * ratio);
        canvas.style.width = `${viewport.width}px`;
        canvas.style.height = `${viewport.height}px`;
        task = p.render({
          canvas,
          viewport,
          transform: [ratio, 0, 0, ratio, 0, 0],
        });
        return task.promise;
      })
      .then(() => {
        if (!stopped) onReady?.();
      })
      .catch(() => {
        if (!stopped) setError(true);
      });
    return () => {
      stopped = true;
      task?.cancel();
    };
  }, [doc, page, scale]);
  return (
    <canvas ref={ref} aria-label={`${page}`} role="img" data-error={error} />
  );
}
export default function Preview({
  data,
  cuts,
  toggle,
  disabled,
  t,
  onError,
  view,
  onView,
}: {
  data: number[];
  cuts: number[];
  toggle: (page: number) => void;
  disabled: boolean;
  t: (key: Key) => string;
  onError: () => void;
  view: { page: number; scale: number };
  onView: (view: { page: number; scale: number }) => void;
}) {
  const [doc, setDoc] = useState<PDFDocumentProxy>();
  const [scale, setScale] = useState(view.scale);
  const [current, setCurrent] = useState(view.page);
  const [pageInput, setPageInput] = useState(String(view.page));
  useEffect(() => {
    setPageInput(String(current));
    onView({ page: current, scale });
  }, [current, scale]);
  const [ready, setReady] = useState<Set<number>>(new Set());
  const scroll = useRef<HTMLDivElement>(null);
  const pageRefs = useRef<Map<number, HTMLDivElement>>(new Map());
  useEffect(() => {
    let cancelled = false;
    let task: ReturnType<typeof getDocument>;
    try {
      task = getDocument({
        data: Uint8Array.from(data),
        cMapUrl: "/pdfjs/cmaps/",
        cMapPacked: true,
        standardFontDataUrl: "/pdfjs/standard_fonts/",
        wasmUrl: "/pdfjs/wasm/",
      });
    } catch {
      onError();
      return;
    }
    setDoc(undefined);
    setCurrent(view.page);
    setReady(new Set());
    void task.promise
      .then((value) => {
        if (!cancelled) setDoc(value);
      })
      .catch(() => {
        if (!cancelled) onError();
      });
    return () => {
      cancelled = true;
      void task.destroy();
    };
  }, [data]);
  useEffect(() => {
    if (!doc || !scroll.current) return;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((e) => e.isIntersecting)
          .sort((a, b) => b.intersectionRatio - a.intersectionRatio);
        if (visible[0])
          setCurrent(Number((visible[0].target as HTMLElement).dataset.page));
      },
      { root: scroll.current, threshold: [0, 0.2, 0.5, 0.8] },
    );
    pageRefs.current.get(current)?.scrollIntoView({ block: "start" });
    pageRefs.current.forEach((p) => observer.observe(p));
    return () => observer.disconnect();
  }, [doc, scale]);
  const jump = (n: number) =>
    pageRefs.current
      .get(Math.max(1, Math.min(doc?.numPages || 1, n)))
      ?.scrollIntoView({ block: "start" });
  const fit = async () => {
    if (doc && scroll.current) {
      const page = await doc.getPage(current);
      setScale(
        Math.max(
          0.2,
          Math.min(
            2,
            (scroll.current.clientWidth - 64) /
              page.getViewport({ scale: 1 }).width,
          ),
        ),
      );
    }
  };
  return (
    <section className="preview">
      <div className="preview-toolbar">
        <label>
          {t("page")}{" "}
          <input
            type="number"
            min="1"
            max={doc?.numPages || 1}
            value={pageInput}
            onChange={(e) => setPageInput(e.target.value)}
            onBlur={() => jump(Number(pageInput) || current)}
            onKeyDown={(e) => {
              if (e.key === "Enter") jump(Number(pageInput) || current);
            }}
          />
        </label>
        <span>/ {doc?.numPages || "—"}</span>
        <label>
          {t("zoom")}{" "}
          <input
            type="range"
            min="0.25"
            max="2"
            step="0.05"
            value={scale}
            onChange={(e) => setScale(Number(e.target.value))}
          />
        </label>
        <span>{Math.round(scale * 100)}%</span>
        <button onClick={() => void fit()}>{t("fit")}</button>
        <span className="muted">
          {t("rendered")} {ready.size}/{doc?.numPages || 0}
        </span>
      </div>
      {!doc ? (
        <div className="empty">{t("loading")}</div>
      ) : (
        <div className="preview-body">
          <aside className="thumbnails" aria-label={t("preview")}>
            {Array.from({ length: doc.numPages }, (_, i) => (
              <button
                key={i}
                aria-current={current === i + 1 ? "page" : undefined}
                onClick={() => jump(i + 1)}
              >
                <Page doc={doc} page={i + 1} scale={0.14} />
                <span>{i + 1}</span>
              </button>
            ))}
          </aside>
          <div className="pages" ref={scroll}>
            {Array.from({ length: doc.numPages }, (_, i) => (
              <div
                key={i}
                data-page={i + 1}
                ref={(node) => {
                  if (node) pageRefs.current.set(i + 1, node);
                  else pageRefs.current.delete(i + 1);
                }}
                className="pdf-page"
              >
                <Page
                  doc={doc}
                  page={i + 1}
                  scale={scale}
                  onReady={() =>
                    setReady((previous) => new Set(previous).add(i + 1))
                  }
                />
                <span className="page-caption">
                  {t("page")} {i + 1}
                </span>
                {i < doc.numPages - 1 && (
                  <button
                    disabled={disabled}
                    className={`scissors ${cuts.includes(i + 1) ? "active" : ""}`}
                    aria-pressed={cuts.includes(i + 1)}
                    onClick={() => toggle(i + 1)}
                  >
                    <Scissors size={18} />
                    {t(cuts.includes(i + 1) ? "uncut" : "cut")} {i + 1}
                  </button>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}
