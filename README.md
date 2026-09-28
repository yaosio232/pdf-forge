# PDF Forge

PDF Forge 是 Windows 桌面 PDF 工具。你可以用它預覽並分割 PDF、依指定順序合併文件，或在知道密碼的情況下批次產生未加密的 PDF 副本。檔案在本機處理。

原始碼公開在 [GitHub](https://github.com/yaosio232/pdf-forge)；Windows 單檔 EXE 可從 [Releases](https://github.com/yaosio232/pdf-forge/releases) 下載。

## 可以做什麼

- **分割 PDF**：開啟一份 PDF，瀏覽頁面並在想切開的頁面後按剪刀。右側會顯示各份文件的頁碼範圍，也能在儲存前修改檔名。
- **合併文件**：加入 PDF、DOC 或 DOCX，調整順序後輸出成一份 PDF。合併 Word 文件需要電腦已安裝桌面版 Microsoft Word。
- **批次解除 PDF 加密**：選取多份 PDF，輸入它們共用的已知密碼，再選擇輸出資料夾。程式不會猜測密碼，原始檔案不會被修改。

## 開始使用

在 Windows 上開啟 `PDF Forge.exe`。若你是從原始碼執行，請先安裝 .NET 6 SDK，再於專案目錄執行：

```powershell
dotnet run --project .\Pdf_Merger.csproj
```

程式有三個分頁，開啟時會先顯示 **Split PDF**：

1. **分割**：按 **Open PDF** 或拖入 PDF；用滑鼠滾輪瀏覽頁面，也可以點縮圖或輸入頁碼跳頁。按頁面之間的剪刀加入切點，再按一次可取消。確認右側的分割結果與檔名後，按 **Save ... split PDFs** 選擇儲存位置。
2. **合併**：切到 **Merge PDFs**，按選檔按鈕或拖入 PDF／Word 文件；用上下移動按鈕調整順序，接著按 **Merge PDF** 選擇輸出檔名。
3. **解除加密**：切到 **Unlock PDFs**，加入加密 PDF，輸入已知密碼並選擇輸出資料夾，最後按 **Unlock PDFs**。所有選取的檔案會使用同一組密碼。

分割時的 **−**、**+** 和 **Fit** 可調整預覽大小。儲存分割結果時，檔案會使用右側清單中的名稱，放在所選位置的資料夾內。請在儲存前確認檔名與輸出位置。

## 從原始碼建置

本專案使用 C#、WinForms 與 .NET 6，主要目標是 Windows x64。建置與執行測試：

```powershell
dotnet restore .\Pdf_Merger.sln
dotnet build .\Pdf_Merger.sln --no-restore
dotnet test .\Pdf_Merger.sln --no-build --logger "console;verbosity=minimal"
```

若要建立可交付的單檔執行檔：

```powershell
dotnet publish .\Pdf_Merger.csproj -c Release -r win-x64 --self-contained
```

發佈產物由 .NET 放在 `bin/Release/` 下的 `publish` 目錄；`dist/` 是專案另行整理交付檔案時使用的目錄，不納入 Git。

## 開源授權

PDF Forge 的原始程式碼以 [GNU Affero General Public License v3.0（AGPL-3.0-only）](LICENSE.md) 開源。你可以使用、研究、修改與再散布，但散布修改版或提供網路服務時，須遵守 AGPLv3 的原始碼與授權告知義務。本工具產生或處理的 PDF，不會只因使用本工具而自動套用 AGPLv3。

PDF Forge 使用 [iTextSharp 5.5.13.4](https://www.nuget.org/packages/iTextSharp/5.5.13.4)，它採 AGPLv3／商業授權雙軌；也使用採 MIT 授權的 BouncyCastle.Cryptography。這些相依套件保留各自的著作權與授權條款，詳見 [授權與第三方聲明](NOTICE.md)。本專案不提供保固。

## 專案文件

- [程式文件](PROGRAM_DOCUMENTATION.md)：架構、資料流與實作細節。部分內容以舊版程式為基準，查閱現況時請以原始碼為準。
- [開發與驗收規範](AGENTS.md)：修改專案後必須執行的測項與證據要求。
- [完整測試計畫](LOOPING_TEST_PLAN.md)：PDF、UI、Word、效能與發佈驗收的測試範圍。
- [測試執行提示詞](LOOPING_TEST_PROMPTS.md)：分輪測試與回歸流程。
- [授權與第三方聲明](NOTICE.md)：專案授權範圍、主要相依套件與原始碼取得方式。

測試規範要求以全新 40 頁 PDF 實際操作 WinForms 分割介面，逐項驗證預覽、切點與六份輸出。建置或單元測試通過，不能取代這些 UI 驗收結果。
