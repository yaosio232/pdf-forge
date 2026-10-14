// @vitest-environment jsdom
import React from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, waitFor, within } from "@testing-library/react";
import { translator } from "./i18n";
vi.mock("pdfjs-dist/legacy/build/pdf.worker.min.mjs?url", () => ({ default: "local-worker" }));
vi.mock("pdfjs-dist/legacy/build/pdf.mjs", () => ({
  GlobalWorkerOptions: {},
  getDocument: () => ({ destroy: vi.fn(), promise: Promise.resolve({ numPages: 40, getPage: async () => ({
    getViewport: ({ scale }: { scale: number }) => ({ width: 600 * scale, height: 800 * scale }),
    render: () => ({ promise: Promise.resolve(), cancel: vi.fn() }),
  }) }) }),
}));
import Preview from "./Preview";
import { TooltipProvider } from "./components/ui/tooltip";
beforeEach(() => {
  vi.stubGlobal("IntersectionObserver", class { observe() {} disconnect() {} });
  HTMLElement.prototype.scrollIntoView = vi.fn();
  HTMLElement.prototype.setPointerCapture = vi.fn();
  HTMLElement.prototype.releasePointerCapture = vi.fn();
  HTMLElement.prototype.hasPointerCapture = () => true;
  vi.stubGlobal("PointerEvent", MouseEvent);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
async function setup(cuts: number[] = []) {
  const toggle=vi.fn(), onView=vi.fn();
  const props={ data: [1,2,3], cuts, toggle, disabled:false, t:translator("en"), onError:vi.fn(), view:{page:1,scale:1}, onView };
  const result=render(previewTree(props));
  await waitFor(() => expect(result.container.querySelectorAll(".pdf-page")).toHaveLength(40));
  await waitFor(() => expect(result.container.querySelector(".pdf-page canvas")?.getAttribute("style")).toContain("600px"));
  return {...result,props,toggle,onView,pages:result.container.querySelector(".pages") as HTMLDivElement};
}
function previewTree(props: React.ComponentProps<typeof Preview>) {
  return <TooltipProvider><Preview {...props}/></TooltipProvider>;
}
it("Ctrl wheel zooms the PDF, prevents browser zoom, and leaves ordinary scrolling alone",async()=>{
  const {pages,onView}=await setup();
  const regular=new WheelEvent("wheel",{deltaY:-100,bubbles:true,cancelable:true});fireEvent(pages,regular);
  expect(regular.defaultPrevented).toBe(false);
  expect(onView.mock.calls.at(-1)?.[0].scale).toBe(1);
  const ctrl=new WheelEvent("wheel",{deltaY:-100,ctrlKey:true,bubbles:true,cancelable:true});fireEvent(pages,ctrl);
  expect(ctrl.defaultPrevented).toBe(true);
  expect(onView.mock.calls.at(-1)?.[0].scale).toBeGreaterThan(1);
  fireEvent.wheel(pages,{deltaY:100,ctrlKey:true});
  expect(onView.mock.calls.at(-1)?.[0].scale).toBeLessThan(1.2);
});
it("middle drag pans both axes and pointer release stops panning",async()=>{
  const {pages}=await setup();pages.scrollTop=300;pages.scrollLeft=200;
  fireEvent.pointerDown(pages,{button:1,buttons:4,clientX:200,clientY:200});
  fireEvent.pointerMove(pages,{buttons:4,clientX:170,clientY:160});
  expect(pages.scrollLeft).toBe(230);expect(pages.scrollTop).toBe(340);
  fireEvent.pointerUp(pages,{button:1});fireEvent.pointerMove(pages,{clientX:150,clientY:100});
  expect(pages.scrollTop).toBe(340);
});
it("zoom keeps the document point beneath the cursor and honors keyboard/reset limits",async()=>{
  const {pages,container,onView}=await setup();pages.scrollTop=100;pages.scrollLeft=0;
  const canvas=container.querySelector(".pdf-page canvas") as HTMLCanvasElement;
  canvas.getBoundingClientRect=()=>({left:-pages.scrollLeft,top:-pages.scrollTop,width:parseFloat(canvas.style.width),height:parseFloat(canvas.style.height)} as DOMRect);
  fireEvent.wheel(pages,{deltaY:-100,ctrlKey:true,clientX:200,clientY:200});
  const scale=onView.mock.calls.at(-1)![0].scale;
  expect(pages.scrollLeft).toBeCloseTo(200*(scale-1));
  expect(pages.scrollTop).toBeCloseTo(300*scale-200);
  fireEvent.keyDown(pages,{key:"0",ctrlKey:true});expect(onView.mock.calls.at(-1)![0].scale).toBe(1);
  for(let i=0;i<20;i++)fireEvent.wheel(pages,{deltaY:-500,ctrlKey:true});
  expect(onView.mock.calls.at(-1)![0].scale).toBe(2);
  for(let i=0;i<20;i++)fireEvent.wheel(pages,{deltaY:500,ctrlKey:true});
  expect(onView.mock.calls.at(-1)![0].scale).toBe(0.25);
});
it("cancelled middle drag releases pan state and processing disables both sets of scissors",async()=>{
  const {pages,container,rerender,props}=await setup();pages.scrollTop=120;
  fireEvent.pointerDown(pages,{button:1,buttons:4,clientX:100,clientY:100});
  fireEvent.pointerCancel(pages);fireEvent.pointerMove(pages,{buttons:4,clientX:100,clientY:20});
  expect(pages.scrollTop).toBe(120);expect(pages.classList.contains("panning")).toBe(false);
  rerender(previewTree({ ...props, disabled: true }));
  expect([...container.querySelectorAll<HTMLButtonElement>(".thumbnail-cut,.scissors")].every(button=>button.disabled)).toBe(true);
});
it("left thumbnail scissors cover every page gap and share cuts with the main preview",async()=>{
  const {container,toggle,rerender,props}=await setup();const thumbnails=within(container.querySelector(".thumbnails") as HTMLElement);
  expect(thumbnails.getAllByRole("button",{name:/Split after page/})).toHaveLength(39);
  fireEvent.click(thumbnails.getByRole("button",{name:"Split after page 7"}));expect(toggle).toHaveBeenCalledWith(7);
  rerender(previewTree({ ...props, cuts: [7] }));
  expect(thumbnails.getByRole("button",{name:"Remove split after page 7"}).getAttribute("aria-pressed")).toBe("true");
  expect(within(container.querySelector(".pages") as HTMLElement).getByRole("button",{name:"Remove split after page 7"}).getAttribute("aria-pressed")).toBe("true");
  expect(thumbnails.queryByRole("button",{name:"Split after page 40"})).toBeNull();
});
it("keeps empty page gaps actionable but only shows scissors after selecting them", async () => {
  const { container, rerender, props } = await setup();
  const thumbnails = within(container.querySelector(".thumbnails") as HTMLElement);
  const pages = within(container.querySelector(".pages") as HTMLElement);
  expect(thumbnails.getAllByRole("button", { name: /Split after page/ })).toHaveLength(39);
  expect(thumbnails.getByRole("button", { name: "Split after page 7" }).querySelector(".lucide-scissors")).toBeNull();
  expect(pages.getByRole("button", { name: "Split after page 7" }).querySelector(".lucide-scissors")).toBeNull();
  rerender(previewTree({ ...props, cuts: [7] }));
  expect(thumbnails.getByRole("button", { name: "Remove split after page 7" }).querySelector(".lucide-scissors")).toBeTruthy();
  expect(pages.getByRole("button", { name: "Remove split after page 7" }).querySelector(".lucide-scissors")).toBeTruthy();
});
it("does not change cuts for a middle-button page-gap click", async () => {
  const { container, toggle } = await setup();
  const pages = within(container.querySelector(".pages") as HTMLElement);
  fireEvent.click(
    pages.getByRole("button", { name: "Split after page 7" }),
    { button: 1 },
  );
  expect(toggle).not.toHaveBeenCalled();
});
