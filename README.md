# PDF Forge

PDF Forge 是 Windows 桌面 PDF 工具。你可以用它預覽並分割 PDF、依指定順序合併文件，或在知道密碼的情況下批次產生未加密的 PDF 副本。檔案在本機處理。

原始碼公開在 [GitHub](https://github.com/yaosio232/pdf-forge)。各版本交付檔案與待驗範圍請見 [Releases](https://github.com/yaosio232/pdf-forge/releases)。

## 可以做什麼

- **分割 PDF**：開啟一份 PDF，瀏覽頁面並在想切開的頁面後按剪刀。右側會顯示各份文件的頁碼範圍，也能在儲存前修改檔名。
- **合併文件**：加入 PDF、DOC 或 DOCX，調整順序後輸出成一份 PDF。合併 Word 文件需要電腦已安裝桌面版 Microsoft Word。
- **批次解除 PDF 加密**：選取多份 PDF，輸入它們共用的已知密碼，再選擇輸出資料夾。程式不會猜測密碼，原始檔案不會被修改。
- **PDF 轉 Word**：將一份完整 PDF 轉成可編輯的 DOCX，需要桌面版 Microsoft Word。所有處理在本機完成；排版可能改變，掃描頁可能保留為圖片，未提供 OCR。
- **批次 Markdown 轉 PDF**：一次選取或拖入多份 `.md`／`.markdown`，各自產生 PDF。使用已安裝的 Edge 或 Chrome，不需要 Word；支援標題、段落、清單、表格與程式碼區塊。第一版不顯示圖片，也不執行原始 HTML。

## 開始使用

主要可攜交付必須是包含 .NET、Poppler、原生 DLL、字型和第三方授權的單一 Windows EXE；程式可自行解開至私人快取。舊版 ZIP 需要完整解壓並保留旁邊的 `poppler` 資料夾，並非單檔版。實際交付與驗收狀態以對應 Release 說明為準。從本分支原始碼執行需 Windows 10／11 x64、.NET SDK 10.0.403 和可用的 Poppler renderer；Word 轉換另需桌面版 Word：

```powershell
dotnet run --project .\Pdf_Merger.csproj
```

程式有四個分頁，開啟時會先顯示 **Split PDF**：

1. **分割**：按 **Open PDF** 或拖入 PDF；用滑鼠滾輪瀏覽頁面，也可以點縮圖或輸入頁碼跳頁。按頁面之間的剪刀加入切點，再按一次可取消。確認右側的分割結果與檔名後，按 **Save ... split PDFs** 選擇儲存位置。
2. **合併**：切到 **Merge PDFs**，按選檔按鈕或拖入 PDF／Word 文件；用上下移動按鈕調整順序，接著按 **Merge PDF** 選擇輸出檔名。
3. **解除加密**：切到 **Unlock PDFs**，加入加密 PDF，輸入已知密碼並選擇輸出資料夾，最後按 **Unlock PDFs**。所有選取的檔案會使用同一組密碼。
4. **檔案轉換**：切到 **Convert Files**（Ctrl+W），選擇 **PDF → Word** 或 **Markdown → PDF (batch)**。PDF 模式選取或拖入一份 PDF，再按 **Convert to Word** 選擇 DOCX 路徑；Markdown 模式可以多選或拖入多份 Markdown，再按 **Convert to PDF** 選擇輸出資料夾。既有輸出需確認覆寫；同名 Markdown 必須先改名。成功後可按 **Open output folder** 開啟匯出資料夾。

轉換期間可按 **Cancel conversion** 停止工作，或關閉主視窗，程式會停止這次工作、清理暫存檔後退出。Word 轉換五分鐘逾時、Markdown 每檔兩分鐘逾時；取消／失敗的檔案不會取代既有輸出，批次先前成功的檔案會保留。Word 使用獨立程序；不會終止原有 Word。**Open log folder** 在轉換期間仍可使用，log 記錄啟動、開啟、儲存及清理階段。Word 若出現首次啟動、啟用或轉換提示，可取消後先處理 Word 設定再重試。加密 PDF 請先解除加密，完成後請檢查 Word 文件內容與排版。

Markdown 在隔離的本機瀏覽器設定檔中列印，禁止讀取外部圖片、字型與執行腳本，不上傳文件；圖片（含本機圖片）會省略。Edge／Chrome 必須另外安裝，程式不會自動安裝瀏覽器。

分割時的 **−**、**+** 和 **Fit** 可調整預覽大小。儲存分割結果時，檔案會使用右側清單中的名稱，放在所選位置的資料夾內。請在儲存前確認檔名與輸出位置。

## 從原始碼建置

單檔交付先以 `scripts/Publish-Portable.ps1 -StageOnly` 準備並核對完整 payload（其餘 Poppler 參數沿用下方範例），再執行 `scripts/Publish-SingleFile.ps1 -PayloadDirectory <payload 路徑>`。第二步需 Rust/Cargo，會檢查封裝內容、執行 launcher 測試，並產生 `dist/v1.0.5.2/PDF-Forge-1.0.5.2-windows-x64-portable.exe`。外層啟動器靜態連結 runtime，啟動後在 `%LOCALAPPDATA%/PDFForge/portable` 的版本／payload 雜湊私人快取中核對並解開內部檔案；無需保留旁置 DLL 或 resources。Cargo 內部版本 `1.0.5+2` 對應 Windows 交付版本 `1.0.5.2`。啟動器及其 Rust／LLVM 相依套件的原始授權保存在 `licenses/portable-launcher/`；必要的 Microsoft runtime `LICENSE.DOCX` 屬第三方授權，不是使用者文件。

本專案使用 C#、WinForms 與 .NET 10（SDK 10.0.403、自包含執行環境 10.0.14），支援 Windows 10／11 x64，不再支援 Windows 7／8.1。建置與執行測試：

iTextSharp 5.5.13.6 使用 .NET Framework 相容資產，還原／建置會保留 `NU1701` 警告；建置與單元測試不代表所有文件或 UI 相容性已驗收。

```powershell
dotnet restore .\Pdf_Merger.sln
dotnet build .\Pdf_Merger.sln --no-restore
dotnet test .\Pdf_Merger.sln --no-build --logger "console;verbosity=minimal"
```

若要建立 .NET 自包含執行檔：

```powershell
dotnet publish .\Pdf_Merger.csproj -c Release -r win-x64 --self-contained
```

發佈產物由 .NET 放在 `bin/Release/` 下的 `publish` 目錄；`dist/` 不納入 Git。`scripts/Publish-Portable.ps1` 可用 `-StageOnly` 準備供外層單檔 launcher 封裝的乾淨 payload；預設模式僅建立補充 ZIP。它要求 Poppler runtime、解開後的 conda package `info` 目錄與 upstream source archive：

```powershell
.\scripts\Publish-Portable.ps1 `
  -PopplerRoot C:\path\to\poppler `
  -PopplerPackageInfoDirectory C:\path\to\expanded\info `
  -PopplerSourceArchive C:\path\to\poppler-26.09.0.tar.xz `
  -PopplerPackageArchive C:\path\to\poppler-26.09.0-h924501e_0.conda `
  -BuildDate 2026-10-27
```

腳本固定使用 .NET SDK 10.0.403／runtime 10.0.14，並驗證 Poppler package/source SHA-256、授權、feedstock revision、recipe 與 Windows patches；原始碼與 recipe 僅供建置端核對，不混入交付套件。`BuildDate` 同時固定 EXE metadata 與 ZIP entry timestamps。裸 EXE 與含 sidecar 檔案的 ZIP 均未達單檔交付標準；交付前還須驗證實際單檔 EXE。

## 開源授權

PDF Forge 的原始程式碼以 [GNU Affero General Public License v3.0（AGPL-3.0-only）](LICENSE.md) 開源。你可以使用、研究、修改與再散布，但散布修改版或提供網路服務時，須遵守 AGPLv3 的原始碼與授權告知義務。本工具產生或處理的 PDF，不會只因使用本工具而自動套用 AGPLv3。

PDF Forge 使用 [iTextSharp 5.5.13.6](https://www.nuget.org/packages/iTextSharp/5.5.13.6)，它採 AGPLv3／商業授權雙軌；也使用採 MIT 授權的 BouncyCastle.Cryptography，以及 BSD-2-Clause 授權的 Markdig 1.3.2。這些相依套件保留各自的著作權與授權條款，詳見 [授權與第三方聲明](NOTICE.md)。本專案不提供保固。

本分支釘選 [Poppler](https://poppler.freedesktop.org/) 26.09.0（GPL-2.0-or-later），Windows binary 為 conda-forge `poppler=26.09.0=h924501e_0`；套件與原始碼雜湊、授權及原生相依清單見 [NOTICE.md](NOTICE.md)、[來源說明](licenses/Poppler-26.09.0-SOURCE.md) 與 [相依清單](licenses/Poppler-26.09.0-DEPENDENCIES.md)。預覽程序有時間與記憶體限制，但 Poppler 26.09.0 的 Splash DoS 問題 CVE-2026-93653 尚未由上游修補。

## 專案文件

- [程式文件](PROGRAM_DOCUMENTATION.md)：架構、資料流與實作細節。部分內容以舊版程式為基準，查閱現況時請以原始碼為準。
- [開發與驗收規範](AGENTS.md)：修改專案後必須執行的測項與證據要求。
- [完整測試計畫](LOOPING_TEST_PLAN.md)：PDF、UI、Word、效能與發佈驗收的測試範圍。
- [測試執行提示詞](LOOPING_TEST_PROMPTS.md)：分輪測試與回歸流程。
- [授權與第三方聲明](NOTICE.md)：專案授權範圍、主要相依套件與原始碼取得方式。

測試規範要求以全新 40 頁 PDF 實際操作 WinForms 分割介面，逐項驗證預覽、切點與六份輸出。建置或單元測試通過，不能取代這些 UI 驗收結果。
