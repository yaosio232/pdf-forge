// @vitest-environment jsdom
import React from "react";
import { afterEach, describe, it, expect, vi } from "vitest";
import {
  render,
  screen,
  fireEvent,
  waitFor,
  cleanup,
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
afterEach(() => {
  cleanup();
  localStorage.clear();
  vi.clearAllMocks();
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
  render(<App />);
}
describe("workspace interaction", () => {
  it("defaults to Split and exposes four workspaces without previous/next buttons", () => {
    setup();
    expect(screen.getByRole("heading", { name: "Split" })).toBeTruthy();
    for (const name of ["Merge", "Unlock", "Convert"])
      expect(screen.getByRole("button", { name })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /previous|next/i })).toBeNull();
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
