# PDF Forge

PDF Forge 是本機優先的 Windows PDF 工具，提供分割、合併、預覽，以及使用已知密碼批次移除 PDF 加密。

原始碼、測試證據與可交付執行檔分開存放；發佈版輸出至 `dist/PDF Forge/`，不納入 Git。

## 開發前必讀：逐項測試標準

所有後續開發都必須遵守 [`AGENTS.md`](AGENTS.md)。正式 PDF 測項每個至少 20 頁；分割固定使用全新 40 頁 PDF，切成 6 份（7/7/7/7/6/6）；並逐項完成主瀏覽區、滾輪、頁間切點、側欄、縮放與輸出完整性測試。未完成逐項測試與證據，不得回報成功。

- [`PROGRAM_DOCUMENTATION.md`](PROGRAM_DOCUMENTATION.md)：程式目的、架構、UI、每個方法、資料流、相依套件、錯誤處理、限制與目前建置基線。
- [`LOOPING_TEST_PLAN.md`](LOOPING_TEST_PLAN.md)：涵蓋 UI、多種 PDF、超連結、書籤、表單、加密、簽章、Word 轉換、效能、復原能力，以及 Agent 自行取得公開 PDF 時的單向下載與零外流規則。
- [`LOOPING_TEST_PROMPTS.md`](LOOPING_TEST_PROMPTS.md)：主控、公開語料取得、分輪執行、缺陷修正、回歸與 release gate 提示詞。

文件基準日期：2026-09-24。文件以工作區當日的原始碼為準；`After`、`Form1.cs.copy` 與既有 EXE 只視為歷史／建置產物，不代表目前原始碼行為。
