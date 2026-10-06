import assert from "node:assert/strict";
import { readFile, writeFile, mkdir, readdir } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import { createCanvas } from "@napi-rs/canvas";
const fixture = path.resolve(
  process.argv[2] || "../TestResults/functional-core/source40.pdf",
);
const output = path.resolve(
  process.argv[3] || "../TestResults/functional-core/pdfjs",
);
const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
const bytes = await readFile(fixture);
const before = hash(bytes);
// Exercise the same legacy module used by Preview, on a runtime without the new Promise API.
Promise.withResolvers = undefined;
const { getDocument, version } =
  await import("pdfjs-dist/legacy/build/pdf.mjs");
assert.equal(
  typeof Promise.withResolvers,
  "function",
  "PDF.js legacy must restore its required Promise API",
);
const task = getDocument({
  data: new Uint8Array(bytes),
  cMapUrl: path.resolve("public/pdfjs/cmaps") + "/",
  cMapPacked: true,
  standardFontDataUrl: path.resolve("public/pdfjs/standard_fonts") + "/",
  wasmUrl: path.resolve("public/pdfjs/wasm") + "/",
  useSystemFonts: false,
});
const doc = await task.promise;
assert.equal(
  doc.numPages,
  40,
  "Fresh functional fixture must contain 40 pages",
);
await mkdir(output, { recursive: true });
const pages = [];
try {
  for (let number = 1; number <= doc.numPages; number++) {
    const page = await doc.getPage(number);
    const content = await page.getTextContent();
    const text = content.items.map((item) => item.str || "").join(" ");
    const id = `FORGE-PAGE-${String(number).padStart(2, "0")}`;
    assert.ok(text.includes(id), `Page ${number} must contain its unique ID`);
    assert.ok(text.length > 100, "Page must contain substantive content");
    const viewport = page.getViewport({ scale: 0.25 });
    const canvas = createCanvas(
      Math.ceil(viewport.width),
      Math.ceil(viewport.height),
    );
    await page.render({ canvas, viewport }).promise;
    const pixels = canvas
      .getContext("2d")
      .getImageData(0, 0, canvas.width, canvas.height).data;
    let ink = 0;
    for (let i = 0; i < pixels.length; i += 4)
      if (
        pixels[i + 3] > 0 &&
        Math.min(pixels[i], pixels[i + 1], pixels[i + 2]) < 230
      )
        ink++;
    assert.ok(
      ink > 100,
      `Page ${number} must render visible text/table content`,
    );
    const png = canvas.toBuffer("image/png");
    pages.push({
      page: number,
      id,
      textLength: text.length,
      visiblePixels: ink,
      renderSha256: hash(png),
    });
    if (number === 1) await writeFile(path.join(output, "first-page.png"), png);
    page.cleanup();
  }
  assert.equal(
    new Set(pages.map((page) => page.renderSha256)).size,
    40,
    "Every rendered page must be distinct",
  );
  const assets = await readdir("dist/assets");
  assert.ok(
    assets.some(
      (name) => name.startsWith("pdf.worker.min-") && name.endsWith(".mjs"),
    ),
    "Packaged PDF worker must exist",
  );
  for (const folder of ["cmaps", "standard_fonts", "wasm"])
    assert.ok(
      (await readdir(`dist/pdfjs/${folder}`)).length > 0,
      `Packaged ${folder} must exist`,
    );
  assert.equal(
    hash(await readFile(fixture)),
    before,
    "Read/render must not modify source",
  );
  const report = {
    status: "PASS",
    kind: "real-pdfjs-function-check-no-ui",
    version,
    pages: 40,
    sourceSha256: before,
    legacyPromiseCompatibility: true,
    packagedAssets: true,
    results: pages,
  };
  await writeFile(
    path.join(output, "results.json"),
    JSON.stringify(report, null, 2) + "\n",
  );
  console.log(
    JSON.stringify({
      status: "PASS",
      pages: 40,
      rendered: 40,
      sourceUnchanged: true,
      legacyPromiseCompatibility: true,
      packagedAssets: true,
      report: path.join(output, "results.json"),
    }),
  );
} finally {
  await doc.destroy();
}
