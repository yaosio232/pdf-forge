export type Workspace = "split" | "merge" | "unlock" | "convert";
export type Language = "zh-TW" | "en";
export type Range = { first: number; last: number; name: string };
export type Options = {
  toc: boolean;
  header: string;
  footer: string;
  pageNumbers: boolean;
};
export type Request =
  | {
      kind: "split";
      source: string;
      directory: string;
      ranges: Range[];
      overwrite: boolean;
    }
  | { kind: "merge"; sources: string[]; output: string; overwrite: boolean }
  | { kind: "unlock"; sources: string[]; password: string; directory: string }
  | { kind: "pdfToWord"; source: string; output: string; overwrite: boolean }
  | {
      kind: "markdown";
      sources: string[];
      output: string;
      merged: boolean;
      overwrite: boolean;
      options: Options;
    };
export type JobEvent = {
  jobId: string;
  status: string;
  phase: string;
  completed: number;
  total: number;
  items: {
    source: string;
    output: string | null;
    pages: number | null;
    error: { code: string; detail: string } | null;
  }[];
  error: { code: string; detail: string } | null;
};
export const busy = (job?: JobEvent) =>
  !!job && ["preparing", "running", "cancelling"].includes(job.status);
export const filename = (path: string) => path.split(/[\\/]/).pop() || path;
export const stem = (path: string) => filename(path).replace(/\.[^.]+$/, "");
export function ranges(pages: number, cuts: number[], base: string): Range[] {
  let first = 1;
  return [
    ...new Set(cuts.filter((x) => Number.isInteger(x) && x > 0 && x < pages)),
  ]
    .sort((a, b) => a - b)
    .concat(pages)
    .map((last, i) => {
      const r = {
        first,
        last,
        name: `${base}_${String(i + 1).padStart(2, "0")}.pdf`,
      };
      first = last + 1;
      return r;
    });
}
export function reorder<T>(items: T[], from: number, to: number) {
  const result = [...items];
  if (from < 0 || to < 0 || from >= items.length || to >= items.length)
    return result;
  const [item] = result.splice(from, 1);
  result.splice(to, 0, item);
  return result;
}
export const acceptFiles = (kind: string, paths: string[]) => [
  ...new Set(
    paths.filter((p) =>
      (kind === "merge"
        ? /\.(pdf|docx?)$/i
        : kind === "markdown"
          ? /\.(md|markdown)$/i
          : /\.pdf$/i
      ).test(p),
    ),
  ),
];
export function systemLanguage(locale: string): Language {
  return locale.toLowerCase().startsWith("zh") ? "zh-TW" : "en";
}
