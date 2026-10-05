// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from "vitest";
import { waitFor } from "@testing-library/react";
const parsers = vi.hoisted(() => ({ math: vi.fn(), diagram: vi.fn() }));
vi.mock("katex", () => ({ default: { render: parsers.math } }));
vi.mock("mermaid", () => ({
  default: { initialize: vi.fn(), run: parsers.diagram },
}));
type RendererWindow = Window & {
  renderMarkdown: () => Promise<void>;
  __forgeRenderState?: { status: string; error?: string };
};
const rendererWindow = window as unknown as RendererWindow;
beforeEach(() => {
  vi.resetModules();
  vi.clearAllMocks();
  parsers.math.mockImplementation(() => {});
  parsers.diagram.mockResolvedValue(undefined);
  document.body.innerHTML = "";
  delete rendererWindow.__forgeRenderState;
  Object.defineProperty(document, "fonts", {
    configurable: true,
    value: { ready: Promise.resolve() },
  });
  Object.defineProperty(document, "readyState", {
    configurable: true,
    value: "complete",
  });
});
describe("renderer diagnostics remain bounded", () => {
  it("does not expose math parser document text in rejection or readiness", async () => {
    document.body.innerHTML = '<span class="math-inline">sample</span>';
    parsers.math.mockImplementation(() => {
      throw new Error("PRIVATE_DOCUMENT_BODY math parse failed");
    });
    await import("./renderer");
    await expect(rendererWindow.renderMarkdown()).rejects.toBe("MARKDOWN_MATH");
    await waitFor(() =>
      expect(rendererWindow.__forgeRenderState).toEqual({
        status: "error",
        error: "MARKDOWN_MATH",
      }),
    );
  });
  it("does not expose diagram parser document text in rejection or readiness", async () => {
    document.body.innerHTML = '<div class="mermaid">sample</div>';
    parsers.diagram.mockRejectedValue(
      new Error("PRIVATE_DOCUMENT_BODY Mermaid parse failed"),
    );
    await import("./renderer");
    await expect(rendererWindow.renderMarkdown()).rejects.toBe(
      "MARKDOWN_MERMAID",
    );
    await waitFor(() =>
      expect(rendererWindow.__forgeRenderState).toEqual({
        status: "error",
        error: "MARKDOWN_MERMAID",
      }),
    );
  });
  it("classifies image decoding failure separately from invalid local path", async () => {
    document.body.innerHTML = "<img>";
    Object.defineProperty(document.images[0], "complete", { value: true });
    Object.defineProperty(document.images[0], "naturalWidth", { value: 0 });
    await import("./renderer");
    await expect(rendererWindow.renderMarkdown()).rejects.toBe(
      "MARKDOWN_IMAGE_RENDER",
    );
    await waitFor(() =>
      expect(rendererWindow.__forgeRenderState).toEqual({
        status: "error",
        error: "MARKDOWN_IMAGE_RENDER",
      }),
    );
  });
  it("keeps successful rendering idempotent", async () => {
    document.body.innerHTML = '<span class="math-inline">sample</span>';
    await import("./renderer");
    await rendererWindow.renderMarkdown();
    await rendererWindow.renderMarkdown();
    expect(parsers.math).toHaveBeenCalledTimes(1);
    expect(parsers.diagram).toHaveBeenCalledTimes(1);
    expect(rendererWindow.__forgeRenderState).toEqual({ status: "ready" });
  });
});
