import mermaid from "mermaid";
import katex from "katex";
const renderCodes = new Set([
  "MARKDOWN_MATH",
  "MARKDOWN_MERMAID",
  "MARKDOWN_IMAGE_RENDER",
]);
const safeRenderCode = (error: unknown) =>
  typeof error === "string" && renderCodes.has(error)
    ? error
    : "MARKDOWN_PRINT";
mermaid.initialize({
  startOnLoad: false,
  securityLevel: "strict",
  theme: "default",
  suppressErrorRendering: true,
  logLevel: "fatal",
  secure: [
    "secure",
    "securityLevel",
    "startOnLoad",
    "maxTextSize",
    "suppressErrorRendering",
    "maxEdges",
    "logLevel",
  ],
});
async function renderDocument() {
  try {
    for (const node of document.querySelectorAll<HTMLElement>(
      ".math-inline,.math-block,.math-display",
    ))
      katex.render(node.textContent || "", node, {
        displayMode: !node.classList.contains("math-inline"),
        throwOnError: true,
        trust: false,
      });
  } catch {
    throw "MARKDOWN_MATH";
  }
  try {
    await mermaid.run({
      nodes: Array.from(document.querySelectorAll<HTMLElement>(".mermaid")),
      suppressErrors: false,
    });
  } catch {
    throw "MARKDOWN_MERMAID";
  }
  await document.fonts.ready;
  try {
    await Promise.all(
      Array.from(document.images).map((img) =>
        img.complete
          ? img.naturalWidth
            ? Promise.resolve()
            : Promise.reject()
          : new Promise<void>((resolve, reject) => {
              img.onload = () => resolve();
              img.onerror = () => reject();
            }),
      ),
    );
  } catch {
    throw "MARKDOWN_IMAGE_RENDER";
  }
}
let rendered: Promise<void> | undefined;
function renderMarkdown() {
  return (rendered ??= renderDocument().catch((error: unknown) => {
    throw safeRenderCode(error);
  }));
}
Object.assign(window, { renderMarkdown });
async function boot() {
  try {
    await renderMarkdown();
    Object.assign(window, { __forgeRenderState: { status: "ready" } });
  } catch (error) {
    Object.assign(window, {
      __forgeRenderState: { status: "error", error: safeRenderCode(error) },
    });
  }
}
if (document.readyState === "loading")
  document.addEventListener("DOMContentLoaded", () => void boot());
else void boot();
