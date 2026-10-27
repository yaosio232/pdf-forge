// @vitest-environment jsdom
import React from "react";
import { afterEach, beforeEach, describe, it, expect, vi } from "vitest";
import {
  render,
  screen,
  fireEvent,
  waitFor,
  cleanup,
  act,
} from "@testing-library/react";
const backend = vi.hoisted(() => ({ invoke: vi.fn() }));
vi.mock("@tauri-apps/api/core", () => ({ invoke: backend.invoke }));
vi.mock("@tauri-apps/api/event", () => ({
  listen: vi.fn(() => Promise.resolve(() => {})),
}));
vi.mock("@tauri-apps/api/webview", () => ({
  getCurrentWebview: () => ({
    onDragDropEvent: () => Promise.resolve(() => {}),
  }),
}));
vi.mock("./Preview", () => ({ default: () => <div>Local PDF preview</div> }));
import App from "./App";
import { TooltipProvider } from "./components/ui/tooltip";
beforeEach(() => {
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
});
afterEach(() => {
  cleanup();
  localStorage.clear();
  vi.clearAllMocks();
  vi.unstubAllGlobals();
});
function setup() {
  localStorage.setItem("forge-language", "en");
  backend.invoke.mockImplementation((command: string) => {
    if (command === "capabilities")
      return Promise.resolve({ word: false, webview2: true, version: "2.0.0" });
    if (command === "select_files")
      return Promise.resolve([
        "C:\\documents\\one.pdf",
        "C:\\documents\\two.pdf",
      ]);
    return Promise.resolve();
  });
  return render(
    <TooltipProvider>
      <App />
    </TooltipProvider>,
  );
}
describe("workspace interaction", () => {
  it("cancels superseded metadata and never reads the stale document", async () => {
    setup();
    const base = backend.invoke.getMockImplementation()!;
    let resolveOld!: (value: { pages: number; encrypted: boolean }) => void;
    let selection = 0;
    backend.invoke.mockImplementation((command: string, args: unknown) => {
      if (command === "select_files") return Promise.resolve([`C:\\documents\\${++selection}.pdf`]);
      if (command === "pdf_info") return (args as {path: string}).path.endsWith("1.pdf")
        ? new Promise(resolve => { resolveOld = resolve; })
        : Promise.resolve({pages: 40, encrypted: false});
      if (command === "read_pdf") return Promise.resolve([1, 2]);
      return base(command, args);
    });
    fireEvent.click(screen.getByRole("button", {name: "Add files"}));
    await waitFor(() => expect(resolveOld).toBeTypeOf("function"));
    const oldRequest = backend.invoke.mock.calls.find(([command]) => command === "pdf_info")![1] as {requestId: string};
    expect(oldRequest.requestId).toBeTypeOf("string");
    fireEvent.click(screen.getByRole("button", {name: "Add files"}));
    await screen.findByRole("button", {name: "Save this range Page 1–40"});
    expect(backend.invoke).toHaveBeenCalledWith("cancel_pdf_info", {requestId: oldRequest.requestId});
    await act(async () => resolveOld({pages: 999, encrypted: false}));
    expect(backend.invoke.mock.calls.filter(([command]) => command === "read_pdf").map(([,args]) => args)).toEqual([{path: "C:\\documents\\2.pdf"}]);
    expect(screen.queryByRole("alert")).toBeNull();
  });
  it("clearing a loading PDF cancels its request and discards its eventual result", async () => {
    setup();
    const base = backend.invoke.getMockImplementation()!;
    let resolveInfo!: (value: {pages: number; encrypted: boolean}) => void;
    backend.invoke.mockImplementation((command: string, args: unknown) => command === "pdf_info"
      ? new Promise(resolve => {resolveInfo = resolve;}) : base(command, args));
    fireEvent.click(screen.getByRole("button", {name: "Add files"}));
    await waitFor(() => expect(resolveInfo).toBeTypeOf("function"));
    const request = backend.invoke.mock.calls.find(([command]) => command === "pdf_info")![1];
    fireEvent.click(screen.getByRole("button", {name: "Clear document"}));
    expect(backend.invoke).toHaveBeenCalledWith("cancel_pdf_info", {requestId: request.requestId});
    await act(async () => resolveInfo({pages: 40, encrypted: false}));
    expect(backend.invoke.mock.calls.some(([command]) => command === "read_pdf")).toBe(false);
    expect(screen.queryByRole("button", {name: /Save this range/})).toBeNull();
  });
  it("unmounting cancels a pending metadata request", async () => {
    const view = setup();
    const base = backend.invoke.getMockImplementation()!;
    backend.invoke.mockImplementation((command: string, args: unknown) => command === "pdf_info" ? new Promise(() => {}) : base(command, args));
    fireEvent.click(screen.getByRole("button", {name: "Add files"}));
    await waitFor(() => expect(backend.invoke.mock.calls.some(([command]) => command === "pdf_info")).toBe(true));
    const request = backend.invoke.mock.calls.find(([command]) => command === "pdf_info")![1];
    view.unmount();
    expect(backend.invoke).toHaveBeenCalledWith("cancel_pdf_info", {requestId: request.requestId});
  });
  it("shows a keyboard tooltip for the compact log action", async () => {
    setup();
    fireEvent.focus(screen.getByRole("button", { name: "Open logs" }));
    expect((await screen.findByRole("tooltip")).textContent).toContain("Open logs");
  });
  it("keeps the workspace heading semantic while moving help and file choice into labeled icon controls", async () => {
    setup();
    expect(screen.getByRole("heading", { name: "Split" }).className).toContain("sr-only");
    const help = screen.getByRole("button", {
      name: "Scroll through your PDF and choose scissors between pages.",
    });
    fireEvent.focus(help);
    expect((await screen.findByRole("tooltip")).textContent).toContain("Scroll through your PDF");
    expect(screen.getByRole("button", { name: "Add files" }).querySelector(".lucide-file-plus2")).toBeTruthy();
  });
  it("uses a compact globe language selector and the requested open-lock icon",async()=>{
    setup();
    const selector=screen.getByRole("combobox",{name:"Language"});
    expect(selector.parentElement?.querySelector(".lucide-globe")).toBeTruthy();
    expect(selector.parentElement?.querySelector(".lucide-languages")).toBeNull();
    expect(selector.parentElement?.getAttribute("title")).toBeNull();
    fireEvent.change(selector,{target:{value:"zh-TW"}});
    await waitFor(()=>expect(localStorage.getItem("forge-language")).toBe("zh-TW"));
    const unlock=screen.getByRole("button",{name:"解除密碼"});
    expect(unlock.querySelector(".lucide-lock-open")).toBeTruthy();
  });
  it("split cards keep editable filenames and save the exact range from an icon button",async()=>{
    setup();const base=backend.invoke.getMockImplementation()!;
    backend.invoke.mockImplementation((command:string,args:unknown)=>command==="pdf_info" ? Promise.resolve({pages:40,encrypted:false}) : command==="read_pdf" ? Promise.resolve([1,2]) : command==="start_job" ? Promise.resolve("split-card") : command==="get_job" ? Promise.resolve({jobId:"split-card",status:"completed",phase:"completed",completed:1,total:1,items:[],error:null}) : base(command,args));
    fireEvent.click(screen.getByRole("button",{name:"Add files"}));
    const save=await screen.findByRole("button",{name:"Save this range Page 1–40"});
    expect(
      screen.getByRole("button", {
        name: "Reset all filenames from the current prefix",
      }),
    ).toBeTruthy();
    const card=save.closest(".range")!;
    expect(card.querySelector(".lucide-files")).toBeTruthy();
    expect(save.querySelector(".lucide-download")).toBeTruthy();
    fireEvent.change(card.querySelector("input")!,{target:{value:"renamed.pdf"}});
    fireEvent.click(save);
    await waitFor(()=>expect(backend.invoke.mock.calls.find(([command])=>command==="start_job")?.[1]).toEqual({request:{kind:"split",source:"C:\\documents\\one.pdf",directory:"",ranges:[{first:1,last:40,name:"renamed.pdf"}],overwrite:false}}));
  });
  it("defaults to Split and exposes four workspaces without previous/next buttons", () => {
    setup();
    expect(screen.getByRole("heading", { name: "Split" })).toBeTruthy();
    for (const name of ["Merge", "Unlock", "Convert"])
      expect(screen.getByRole("button", { name })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /previous|next/i })).toBeNull();
  });
  it("switches from the empty drop state to a compact loaded workspace", async () => {
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    expect(document.querySelector(".work-surface.empty-state")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Add files" }));
    await waitFor(() =>
      expect(document.querySelector(".work-surface.has-files")).toBeTruthy(),
    );
    expect(document.querySelector(".work-surface.empty-state")).toBeNull();
  });
  it("keeps merge inputs when switching workspace and supports keyboard reorder/delete", async () => {
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    fireEvent.click(screen.getByRole("button", { name: "Add files" }));
    await screen.findByRole("button", { name: "Move down one.pdf" });
    fireEvent.click(screen.getByRole("button", { name: "Move down one.pdf" }));
    expect(screen.getAllByRole("listitem")[0].textContent).toContain("two.pdf");
    fireEvent.click(screen.getByRole("button", { name: "Unlock" }));
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    expect(
      screen.getByRole("button", { name: "Move up one.pdf" }),
    ).toBeTruthy();
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    fireEvent.click(screen.getByRole("button", { name: "Remove selected" }));
    expect(screen.queryByText("two.pdf")).toBeNull();
  });
  it("keeps source paths compact and exposes them through accessible tooltips", async () => {
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    fireEvent.click(screen.getByRole("button", { name: "Add files" }));
    const source = await waitFor(() => {
      const file = document.querySelector<HTMLElement>(".queue-file");
      expect(file).toBeTruthy();
      return file!;
    });
    expect(source.getAttribute("title")).toBeNull();
    expect(document.querySelector(".queue-select small")).toBeNull();
    fireEvent.focus(source);
    expect((await screen.findByRole("tooltip")).textContent).toContain(
      "C:\\documents\\one.pdf",
    );
    expect(document.querySelector(".output-path")?.getAttribute("title")).toBeNull();
    expect(document.querySelector(".output-path .lucide-folder")).toBeTruthy();
  });
  it("keeps the automatic output folder reachable by keyboard", async () => {
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    fireEvent.click(screen.getByRole("button", { name: "Add files" }));
    const output = await waitFor(() => {
      const path = document.querySelector<HTMLElement>(".output-path");
      expect(path).toBeTruthy();
      return path!;
    });
    expect(output.tabIndex).toBe(0);
    fireEvent.focus(output);
    expect((await screen.findByRole("tooltip")).textContent).toContain(
      "C:\\documents\\one.pdf",
    );
  });
  it("does not start without inputs and disables unavailable Word conversion", async () => {
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    expect(
      (screen.getByRole("button", { name: "Start" }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Convert" }));
    await waitFor(() =>
      expect(screen.getByText(/Microsoft Word is unavailable/)).toBeTruthy(),
    );
    expect(
      (screen.getByRole("button", { name: "Start" }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
  });
  it("submits ordered inputs without overwrite and keeps cancel available while locked", async () => {
    setup();
    const base = backend.invoke.getMockImplementation()!;
    backend.invoke.mockImplementation((command: string, args: unknown) =>
      command === "start_job"
        ? Promise.resolve("job-1")
        : command === "get_job"
          ? Promise.resolve({
              jobId: "job-1",
              status: "running",
              phase: "processing",
              completed: 0,
              total: 2,
              items: [],
              error: null,
            })
          : base(command, args),
    );
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    fireEvent.click(screen.getByRole("button", { name: "Add files" }));
    await screen.findByRole("button", { name: "Move down one.pdf" });
    fireEvent.click(screen.getByRole("button", { name: "Move down one.pdf" }));
    await waitFor(() =>
      expect(
        (screen.getByRole("button", { name: "Start" }) as HTMLButtonElement)
          .disabled,
      ).toBe(false),
    );
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await screen.findByRole("button", { name: "Cancel" });
    expect(
      backend.invoke.mock.calls.some(
        ([command]) => command === "select_output",
      ),
    ).toBe(false);
    const call = backend.invoke.mock.calls.find(
      ([command]) => command === "start_job",
    );
    expect(call?.[1]).toEqual({
      request: {
        kind: "merge",
        sources: ["C:\\documents\\two.pdf", "C:\\documents\\one.pdf"],
        output: "",
        overwrite: false,
      },
    });
    expect(
      (screen.getByRole("button", { name: "Add files" }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(backend.invoke).toHaveBeenCalledWith("cancel_job", {
      jobId: "job-1",
    });
  });
  it.each(["unlock", "markdown"] as const)(
    "keeps the %s retry destination aligned with its first submitted source without changing inputs/settings",
    async (operation) => {
      setup();
      const extension = operation === "unlock" ? "pdf" : "md";
      const sources = [
        `C:\\first\\one.${extension}`,
        `C:\\second\\two.${extension}`,
      ];
      const base = backend.invoke.getMockImplementation()!;
      let started = 0;
      let selections = 0;
      backend.invoke.mockImplementation(
        (command: string, args: { jobId?: string }) => {
          if (command === "select_files")
            return Promise.resolve(
              selections++ === 0 ? sources : [`C:\\third\\three.${extension}`],
            );
          if (command === "start_job")
            return Promise.resolve(`retry-${++started}`);
          if (command === "get_job")
            return Promise.resolve(
              args.jobId === "retry-1"
                ? {
                    jobId: "retry-1",
                    status: "partial",
                    phase: "committing",
                    completed: 1,
                    total: 2,
                    error: null,
                    items: [
                      {
                        source: sources[0],
                        output: "C:\\first\\one.pdf",
                        pages: 20,
                        error: null,
                      },
                      {
                        source: sources[1],
                        output: null,
                        pages: null,
                        error: { code: "TIMEOUT", detail: "" },
                      },
                    ],
                  }
                : {
                    jobId: "retry-2",
                    status: "completed",
                    phase: operation,
                    completed: 1,
                    total: 1,
                    items: [
                      {
                        source: sources[1],
                        output: "C:\\second\\two.pdf",
                        pages: 20,
                        error: null,
                      },
                    ],
                    error: null,
                  },
            );
          return base(command, args);
        },
      );
      fireEvent.click(
        screen.getByRole("button", {
          name: operation === "unlock" ? "Unlock" : "Convert",
        }),
      );
      if (operation === "markdown") {
        fireEvent.click(screen.getByRole("button", { name: "Markdown → PDF" }));
        fireEvent.change(screen.getByLabelText("Header"), {
          target: { value: "Retained header" },
        });
      } else
        fireEvent.change(screen.getByLabelText("Shared password"), {
          target: { value: "Retained password" },
        });
      fireEvent.click(screen.getByRole("button", { name: "Add files" }));
      await waitFor(() =>
        expect(document.querySelectorAll(".queue li")).toHaveLength(2),
      );
      fireEvent.click(
        document.querySelectorAll<HTMLInputElement>(".queue input")[0],
      );
      fireEvent.click(screen.getByRole("button", { name: "Start" }));
      await screen.findByRole("button", { name: "Retry failed files" });
      expect(document.querySelector(".output-path")?.textContent).toBe(
        "C:\\first",
      );
      fireEvent.click(
        screen.getByRole("button", { name: "Retry failed files" }),
      );
      await waitFor(() =>
        expect(document.querySelector(".output-path")?.textContent).toBe(
          "C:\\second",
        ),
      );
      const requests = backend.invoke.mock.calls
        .filter(([command]) => command === "start_job")
        .map(([, args]) => args.request);
      expect(requests[0].sources).toEqual(sources);
      expect(requests[1].sources).toEqual([sources[1]]);
      expect(document.querySelectorAll(".queue li")).toHaveLength(2);
      expect(
        document.querySelectorAll<HTMLInputElement>(".queue input")[0].checked,
      ).toBe(true);
      await waitFor(() =>
        expect(
          (
            screen.getByRole("button", {
              name: "Add files",
            }) as HTMLButtonElement
          ).disabled,
        ).toBe(false),
      );
      fireEvent.click(screen.getByRole("button", { name: "Add files" }));
      await waitFor(() =>
        expect(document.querySelectorAll(".queue li")).toHaveLength(3),
      );
      expect(document.querySelector(".output-path")?.textContent).toBe(
        "C:\\first",
      );
      expect(document.querySelector(".results")?.textContent).toContain(
        "two.pdf",
      );
      fireEvent.click(screen.getByRole("button", { name: "Remove selected" }));
      expect(document.querySelector(".output-path")?.textContent).toBe(
        "C:\\second",
      );
      if (operation === "markdown") {
        fireEvent.click(
          screen.getByRole("button", { name: "Move down two.md" }),
        );
        expect(document.querySelector(".output-path")?.textContent).toBe(
          "C:\\third",
        );
      }
      expect(document.querySelector(".results")?.textContent).toContain(
        "two.pdf",
      );
      if (operation === "markdown")
        expect(requests[1].options).toEqual({
          toc: false,
          header: "Retained header",
          footer: "",
          pageNumbers: true,
        });
      else expect(requests[1].password).toBe("Retained password");
      expect(
        backend.invoke.mock.calls.some(
          ([command]) => command === "select_output",
        ),
      ).toBe(false);
    },
  );
  it("persists language selection and translates settings", () => {
    setup();
    fireEvent.change(screen.getByRole("combobox", { name: "Language" }), {
      target: { value: "zh-TW" },
    });
    expect(screen.getByRole("heading", { name: "分割" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "選擇資料夾" })).toBeNull();
    expect(localStorage.getItem("forge-language")).toBe("zh-TW");
  });
});
