import { useEffect, useLayoutEffect, useRef, useState } from "react";
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
  const [size, setSize] = useState<{width:number;height:number}>();
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
        const natural = p.getViewport({scale:1});
        setSize(previous => previous?.width === natural.width && previous.height === natural.height ? previous : {width:natural.width,height:natural.height});
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
    <canvas ref={ref} aria-label={`${page}`} role="img" data-error={error}
      style={size ? {width:size.width*scale,height:size.height*scale} : undefined} />
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
  const scaleRef=useRef(scale);scaleRef.current=scale;
  const currentRef=useRef(current);currentRef.current=current;
  const anchor=useRef<{canvas:HTMLCanvasElement;x:number;y:number;clientX:number;clientY:number}|undefined>(undefined);
  const pan=useRef<{id:number;x:number;y:number;left:number;top:number}|undefined>(undefined);
  const [panning,setPanning]=useState(false);
  const zoom=(value:number,clientX?:number,clientY?:number)=>{
    const container=scroll.current;
    const next=Math.max(0.25,Math.min(2,value));
    if (!container || next===scaleRef.current) return;
    const area=container.getBoundingClientRect();
    const x=clientX ?? area.left+container.clientWidth/2;
    const y=clientY ?? area.top+container.clientHeight/2;
    const pages=[...pageRefs.current.values()];
    const page=pages.find(p=>{const r=p.getBoundingClientRect();return r.top<=y && r.bottom>=y;}) ?? pageRefs.current.get(currentRef.current);
    const canvas=page?.querySelector("canvas");
    if(canvas){const rect=canvas.getBoundingClientRect();anchor.current={canvas,x:(x-rect.left)/(rect.width||1),y:(y-rect.top)/(rect.height||1),clientX:x,clientY:y};}
    scaleRef.current=next;setReady(new Set());setScale(next);
  };
  useLayoutEffect(()=>{
    const point=anchor.current;anchor.current=undefined;
    if(point && scroll.current && point.canvas.isConnected){
      const rect=point.canvas.getBoundingClientRect();
      scroll.current.scrollLeft+=rect.left+point.x*rect.width-point.clientX;
      scroll.current.scrollTop+=rect.top+point.y*rect.height-point.clientY;
    }
  },[scale]);
  useEffect(()=>{
    const container=scroll.current;if(!container)return;
    const wheel=(event:WheelEvent)=>{
      if(!event.ctrlKey)return;
      event.preventDefault();
      const units=event.deltaMode===1 ? 16 : event.deltaMode===2 ? container.clientHeight : 1;
      zoom(scaleRef.current*Math.exp(-Math.max(-500,Math.min(500,event.deltaY*units))*0.002),event.clientX,event.clientY);
    };
    container.addEventListener("wheel",wheel,{passive:false});
    return ()=>container.removeEventListener("wheel",wheel);
  },[doc]);
  const stopPan=()=>{pan.current=undefined;setPanning(false);};
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
    setScale(view.scale);
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
  }, [doc]);
  const jump = (n: number) =>
    pageRefs.current
      .get(Math.max(1, Math.min(doc?.numPages || 1, n)))
      ?.scrollIntoView({ block: "start" });
  const fit = async () => {
    if (doc && scroll.current) {
      const page = await doc.getPage(current);
      zoom(
        Math.max(
          0.25,
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
    <section className="preview" onKeyDown={event=>{
      if(!event.ctrlKey)return;
      if(["+","=","-","0"].includes(event.key)){
        event.preventDefault();zoom(event.key==="0" ? 1 : scaleRef.current*(event.key==="-" ? 1/1.1 : 1.1));
      }
    }}>
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
            onChange={(e) => zoom(Number(e.target.value))}
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
              <div className="thumbnail-item" key={i}>
              <button className="thumbnail-page"
                key={i}
                aria-label={`${t("page")} ${i+1}`}
                aria-current={current === i + 1 ? "page" : undefined}
                onClick={() => jump(i + 1)}
              >
                <Page doc={doc} page={i + 1} scale={0.14} />
                <span>{i + 1}</span>
              </button>
              {i<doc.numPages-1 && <button
                className={`thumbnail-cut ${cuts.includes(i+1)?"active":""}`}
                disabled={disabled} aria-pressed={cuts.includes(i+1)}
                aria-label={`${t(cuts.includes(i+1)?"uncut":"cut")} ${i+1}`}
                title={`${t(cuts.includes(i+1)?"uncut":"cut")} ${i+1}`}
                onClick={()=>toggle(i+1)}><Scissors size={17} aria-hidden="true"/></button>}
              </div>
            ))}
          </aside>
          <div className={`pages ${panning?"panning":""}`} ref={scroll} tabIndex={0}
            aria-label={t("preview")}
            onPointerDown={event=>{
              if(event.button!==1)return;event.preventDefault();
              pan.current={id:event.pointerId,x:event.clientX,y:event.clientY,left:event.currentTarget.scrollLeft,top:event.currentTarget.scrollTop};
              event.currentTarget.setPointerCapture(event.pointerId);setPanning(true);event.currentTarget.focus({preventScroll:true});
            }}
            onPointerMove={event=>{
              const drag=pan.current;if(!drag || drag.id!==event.pointerId)return;
              if(!(event.buttons&4)){stopPan();return;}
              event.preventDefault();event.currentTarget.scrollLeft=drag.left+drag.x-event.clientX;event.currentTarget.scrollTop=drag.top+drag.y-event.clientY;
            }}
            onPointerUp={event=>{if(pan.current?.id===event.pointerId){stopPan();if(event.currentTarget.hasPointerCapture(event.pointerId))event.currentTarget.releasePointerCapture(event.pointerId);}}}
            onPointerCancel={stopPan} onLostPointerCapture={stopPan}
            onAuxClick={event=>{if(event.button===1)event.preventDefault();}}>
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
