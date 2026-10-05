import { build } from "esbuild";
import "./build-notices.mjs";
import { mkdir, readFile, writeFile, cp } from "node:fs/promises";
await mkdir("public/markdown", { recursive: true });
await build({
  entryPoints: ["scripts/renderer.ts"],
  outfile: "public/markdown/renderer.js",
  bundle: true,
  format: "iife",
  target: "es2022",
  minify: true,
});
let css = await readFile("node_modules/katex/dist/katex.min.css", "utf8");
for (const match of [...css.matchAll(/url\((fonts\/[^)]+)\)/g)]) {
  const bytes = await readFile(`node_modules/katex/dist/${match[1]}`);
  const mime = match[1].endsWith("woff2")
    ? "font/woff2"
    : match[1].endsWith("woff")
      ? "font/woff"
      : "font/ttf";
  css = css.replace(
    match[0],
    `url(data:${mime};base64,${bytes.toString("base64")})`,
  );
}
await writeFile(
  "public/markdown/renderer.css",
  css +
    "\n.mermaid svg{max-width:100%;height:auto}.math-block,.math-display{text-align:center;overflow-wrap:anywhere}",
);
await cp("node_modules/pdfjs-dist/cmaps", "public/pdfjs/cmaps", {
  recursive: true,
});
await cp(
  "node_modules/pdfjs-dist/standard_fonts",
  "public/pdfjs/standard_fonts",
  { recursive: true },
);
await cp("node_modules/pdfjs-dist/wasm", "public/pdfjs/wasm", {
  recursive: true,
});
