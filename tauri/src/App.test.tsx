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
    if (command === "select_output")
      return Promise.resolve("C:\\output\\merged.pdf");
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
  it("does not start with absent output and disables unavailable Word conversion", async () => {
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
    fireEvent.click(screen.getByRole("button", { name: "Save as" }));
    await waitFor(() =>
      expect(
        (screen.getByRole("button", { name: "Start" }) as HTMLButtonElement)
          .disabled,
      ).toBe(false),
    );
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await screen.findByRole("button", { name: "Cancel" });
    const call = backend.invoke.mock.calls.find(
      ([command]) => command === "start_job",
    );
    expect(call?.[1]).toEqual({
      request: {
        kind: "merge",
        sources: ["C:\\documents\\two.pdf", "C:\\documents\\one.pdf"],
        output: "C:\\output\\merged.pdf",
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
  it("persists language selection and translates settings", () => {
    setup();
    fireEvent.change(screen.getByRole("combobox", { name: "Language" }), {
      target: { value: "zh-TW" },
    });
    expect(screen.getByRole("heading", { name: "分割" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "選擇資料夾" })).toBeTruthy();
    expect(localStorage.getItem("forge-language")).toBe("zh-TW");
  });
});
