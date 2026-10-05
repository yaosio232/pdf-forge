import { describe, it, expect } from "vitest";
import { ranges, reorder, acceptFiles, busy, systemLanguage } from "./model";
import { dictionary, errorText } from "./i18n";
describe("range selection", () => {
  it("preserves all 40 pages in six nonoverlapping ranges", () => {
    const result = ranges(40, [7, 14, 21, 28, 34], "report");
    expect(result.map((r) => r.last - r.first + 1)).toEqual([7, 7, 7, 7, 6, 6]);
    expect(
      result.flatMap((r) =>
        Array.from({ length: r.last - r.first + 1 }, (_, i) => r.first + i),
      ),
    ).toEqual(Array.from({ length: 40 }, (_, i) => i + 1));
  });
  it("ignores duplicate and invalid boundaries", () =>
    expect(ranges(20, [0, 20, 21, 7, 7, -1, 1.5], "report")).toEqual([
      { first: 1, last: 7, name: "report_01.pdf" },
      { first: 8, last: 20, name: "report_02.pdf" },
    ]));
});
describe("queue", () => {
  it("reorders without changing original queue", () => {
    const input = ["a", "b", "c"];
    expect(reorder(input, 0, 2)).toEqual(["b", "c", "a"]);
    expect(input).toEqual(["a", "b", "c"]);
  });
  it("accepts supported formats and deduplicates", () =>
    expect(acceptFiles("merge", ["a.pdf", "b.DOCX", "x.exe", "a.pdf"])).toEqual(
      ["a.pdf", "b.DOCX"],
    ));
});
describe("localization", () => {
  it("has matching complete dictionaries", () => {
    expect(Object.keys(dictionary.en).sort()).toEqual(
      Object.keys(dictionary["zh-TW"]).sort(),
    );
    for (const text of Object.values(dictionary["zh-TW"]))
      expect(text.length).toBeGreaterThan(0);
  });
  it("uses system default and stable fallback code", () => {
    expect(systemLanguage("zh-Hant-TW")).toBe("zh-TW");
    expect(systemLanguage("fr-FR")).toBe("en");
    expect(errorText("zh-TW", "UNKNOWN")).toContain("(UNKNOWN)");
    expect(errorText("en", "WORD_UNAVAILABLE")).toContain("Microsoft Word");
  });
});
describe("job locks", () => {
  it("locks only active states", () => {
    for (const status of ["preparing", "running", "cancelling"])
      expect(busy({ status } as never)).toBe(true);
    for (const status of ["completed", "failed", "partial", "cancelled"])
      expect(busy({ status } as never)).toBe(false);
  });
});
