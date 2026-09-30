# PDF Merger Looping 完整測試計畫

### 2026-09-30 本輪非 UI 驗證例外

同日使用者要求「那你打包上傳」，本輪例外延伸至 v1.0.4 打包與 GitHub 發佈；維持非 UI 驗證範圍，發佈頁須明確揭露 UI 未驗證。

使用者指示「那你直接unit test，不開UI了」，授權本輪超連結修正透過單元／服務測試直接分割及驗證輸出，不啟動 UI。原因為 Windows 操作工具持續擷取逾時及缺少點擊座標。仍使用全新 40 頁 fixture、固定六份頁數與來源 hash 檢查，另核對原始案例第 2–24 頁所有連結。依 AGENTS.md 本輪例外回報非 UI 測試結果，UI 測項保持未驗證，不視為 UI 或全面發佈驗收通過。

## 0. 使用者指定的強制逐項測試標準

本節由根目錄 [`AGENTS.md`](AGENTS.md) 強制套用到所有後續開發。測試不能只看總數或成功訊息，必須逐項完成並保存證據：

- 每個正式 PDF 測項至少 20 頁，每頁有可辨識的豐富內容；單行或空白頁不得作主要驗收資料。
- 分割固定使用全新 40 頁 fixture，不得使用舊的三個單頁檔案替代。
- 固定切點為第 7、14、21、28、34 頁；輸出必須恰好 6 份，頁數為 7/7/7/7/6/6。
- 必須逐項執行 `AGENTS.md` 的 SPLIT-01 至 SPLIT-12，包含 40/40 預覽、主區滾輪、頁碼更新、側欄、分割點切換、縮放／Fit、來源 hash 與輸出內容。
- 任何測項未執行、失敗或缺證據，只能標示 FAIL/BLOCKED，不得回報成功。

逐項 ID、操作、預期結果與完成 gate 以 [`AGENTS.md`](AGENTS.md) 為準。

### 2026-09-28 單次發佈例外

使用者於 2026-09-28 明確要求略過驗證、直接上傳 GitHub，並在 Release 附上單檔 EXE。本次發佈因此可在 WinForms UI 驗收尚未完成時進行；原因與範圍記錄於 [`AGENTS.md`](AGENTS.md) F 節。前次執行紀錄位於 `TestResults/20260928-163832-51a9413/`：建置與 18 個單元測試通過，SPLIT-01 至 SPLIT-12 均為 `BLOCKED`。本次例外僅限該次發佈，不改動測項標準或未來的 release gate；發佈說明須揭露未完成的 UI 驗收。

同日使用者又要求本輪不驗證，並公開發佈含 About 授權文字的新 EXE。這次公開發佈沿用 [`AGENTS.md`](AGENTS.md) F 節的單次例外；新 EXE 未經 SPLIT-01 至 SPLIT-12 UI 驗收，不能回報為通過。

## 1. 目標

以可重複、可停止、可追溯的循環驗證 PDF Merger。測試不只判斷「有產生 PDF」，還要判斷頁面內容、順序、書籤、超連結、annotations、中繼資料、錯誤復原與 UI 是否符合契約。

本計畫適用兩種模式：

- **Audit loop**：只建置、測試與報告，不改產品碼。
- **Test-fix loop**：先重現並加入測試，取得明確授權後才做最小修正，再跑完整回歸。

預設採 audit loop。任何自動修正提示詞都必須明列允許修改的檔案與退出條件。

## 2. 循環模型

```text
L0 環境與基線
  ↓
L1 建立／補齊測試語料與 manifest
  ↓
L2 UI 與一般功能 smoke
  ↓
L3 PDF 結構、內容與特殊功能矩陣
  ↓
L4 Word、錯誤、權限、復原與安全邊界
  ↓
L5 效能、穩定性、資源與重複執行
  ↓
L6 全量回歸、證據封存、Release Gate
  ├─ PASS → 結束並出具 release report
  ├─ FAIL 且只審核 → 出具缺陷清單後停止
  └─ FAIL 且授權修正 → 一缺陷一測試一修正，回到 L2
```

每輪必須建立唯一 `run_id`，建議格式 `YYYYMMDD-HHmmss-<commit-or-nogit>`。每輪輸出放在 `TestResults/<run_id>/`；原始測試語料保持唯讀，輸出另放，不覆寫來源。

## 3. 全域安全規則

1. 測試輸出不得指向任何來源檔；每次先解析絕對路徑並確認不同。
2. 所有產物只寫入工作區測試目錄或新建的安全暫存目錄。
3. Agent 可以自行搜尋並下載公開測試 PDF，但網路只用於取得公開資料；所有本機測試資料與結果遵守「零外流」。
4. 禁止對外傳送本機 PDF、DOC/DOCX、合併輸出、截圖、log、檔名、目錄、檔案清單、hash、內容片段或測試結果，包括上傳到線上 validator、轉檔器、OCR、沙箱、雲端儲存、issue tracker 或外部 AI。
5. 搜尋 query、URL、HTTP header、表單與 repository issue 不得包含本機檔名、路徑、客戶／專案名稱、log 內容或從本機文件衍生的字串；只能使用固定的通用 PDF 特性詞。
6. 取得 corpus 時只允許下載型的 GET／HEAD 或唯讀 repository fetch；禁止附帶本機檔案的 POST／PUT／PATCH、multipart/form-data 與任何 upload API。必須檢查 redirect 後的最終 host。
7. 不在真實機密文件上做破壞性、密碼或惡意 action 測試。
8. JavaScript、Launch、外部檔案與 URI action 只檢查物件結構；不得執行未知 action。
9. URI 點擊使用本機可控 HTTP listener 或保留的 `https://example.invalid/...`，不得依賴公網成功。
10. 加密測試密碼固定使用測試專用字串並記在 manifest，不沿用真實密碼。
11. 簽章測試只使用自簽測試憑證；預期合併後原簽章失效，不宣稱保留有效性。
12. 每輪前後記錄並清理由該輪啟動的 `WINWORD.EXE`，不可終止使用者原本的 Word 程序。
13. 發現本機資料外送嘗試、P0 資料遺失、輸出來源覆寫或任意檔案寫入風險時立即停止該輪並保存本機證據；證據本身不得外傳。

## 4. 測試環境矩陣

至少維護下列環境欄位；release 前需覆蓋主要支援組合，非主要組合可採 pairwise：

| 維度 | 選項 |
|---|---|
| Windows | Windows 10 22H2、Windows 11 當前企業基線 |
| 架構 | x64；x86／ARM64 標為非支援驗證 |
| DPI | 100%、125%、150%、200% |
| 解析度 | 1366×768、1920×1080、2560×1440 |
| 主題 | Light、Dark 系統主題、高對比 |
| 語系 | zh-TW、en-US；再選一個逗號小數／不同 code page 語系 |
| Word | 無 Word、Microsoft 365 Desktop、組織實際部署版本 |
| 權限 | 一般使用者、輸出唯讀目錄、長路徑開啟／關閉 |
| 路徑 | 本機 NTFS、UNC／網路分享、OneDrive 類同步目錄（若在支援範圍） |
| PDF 閱讀器 | Edge／Chrome PDF Viewer、Adobe Acrobat Reader 或組織標準閱讀器 |

每個結果必須記錄 OS build、DPI、Word 版本、EXE hash、來源版本、測試工具版本。

## 5. 測試語料設計

### 5.0 公開網路語料取得與零外流

Agent 應自行補齊測試 corpus，不要求使用者提供真實文件。取得順序如下：

1. 優先使用本機生成 fixture，因為預期結構、授權與安全性最清楚。
2. 再使用有明確授權與維護者的官方公開 corpus。
3. 最後才用搜尋引擎尋找特定缺口；隨機搜尋結果只能進隔離區，完成來源、授權與結構驗證前不得成為 golden fixture。

優先來源白名單：

| 來源 | 用途 | 條件 |
|---|---|---|
| `https://github.com/veraPDF/veraPDF-corpus` | PDF/A、PDF/UA、ISO 32000 原子測試檔 | 記錄 CC BY 4.0 歸屬、revision 與檔案 URL |
| `https://pdfa.org/resources/` | PDF Association 公開技術測試資源索引 | 逐項確認下載頁與授權，不把網頁本身視為檔案授權 |
| `https://github.com/qpdf/qpdf` | 結構、xref、object stream、加密相關生成／測試參考 | 優先用工具本機生成；匯入 repository fixture 時保存 revision 與 Apache-2.0 資訊 |
| Apache PDFBox 官方 repository 的 test resources | 渲染、字型、旋轉與 parser edge cases | 個別 fixture 仍須確認來源／授權；不因 repository 為 Apache 專案就推定每份第三方 PDF 都可重散布 |

搜尋僅可使用與本機資料無關的通用字串，例如：

```text
public licensed PDF named destination test file
open source AcroForm duplicate field test PDF
CC licensed PDF attachment annotation test corpus
official PDF 2.0 xref stream test suite
```

嚴禁把來源檔名、視窗截圖 OCR、錯誤訊息、客戶名稱或文件文字拼進 query。不得使用會把本機檔案上傳的線上 PDF repair、validator、malware scanner、OCR 或 AI 分析服務；所有 parser、renderer、validator 與防毒檢查均在本機執行。

下載流程：

1. 解析 URL，確認 scheme 為 HTTPS、host 在本輪核准清單、沒有 credentials 或本機資料 query。
2. 預設單檔上限 100 MiB、redirect 最多 3 次；效能大檔需在 manifest 另行核准大小與磁碟預算。
3. 下載到 `tests/fixtures/_quarantine/<source-id>/`，不得直接放入 golden corpus。
4. 驗證 magic bytes、實際 MIME、大小、SHA-256、parser 可讀性、active content、附件及加密狀態。
5. 記錄原始 URL、最終 URL、取得時間、HTTP ETag／Last-Modified（若有）、license、source revision、大小與 SHA-256 到 `download-manifest.json`。
6. 完成授權與結構驗證後才複製到正式 fixture；保留原始 hash，所有後續處理離線執行。
7. 網路存取 log 只記公開 URL 與下載結果；若意外包含本機敏感資訊，立即停止且只在本機受限目錄處理該 log。

### 5.1 Manifest

每個 corpus 檔案需有 manifest 記錄：

```json
{
  "id": "PDF-LINK-URI-001",
  "file": "pdf/link-uri.pdf",
  "sha256": "<computed hash>",
  "pages": 2,
  "features": ["uri-link", "ascii-text"],
  "expected": {
    "uri": "https://example.invalid/pdf-merger-test",
    "bookmarkCount": 0,
    "encrypted": false
  },
  "generator": "<tool and version or licensed fixture source>",
  "license": "generated-test-fixture",
  "origin": "generated-local-or-downloaded-public",
  "downloadManifestId": null
}
```

`sha256` 與 generator 在建立語料時寫入真實值，不可用猜測值。下載檔必須以 `downloadManifestId` 連回來源、授權與取得紀錄。已存在的 `Test/1.pdf`、`2.pdf`、`3.pdf`、`111111.pdf` 先分類與建立 manifest；在來源不明前不得視為唯一 golden files，也不得上傳到外部服務協助分類。

### 5.2 基本頁面與渲染

| ID 群組 | 必備選項 | 主要驗證 |
|---|---|---|
| PDF-BASIC | 1 頁、2 頁、多頁、空白頁 | 頁數與順序 |
| PDF-TEXT | ASCII、繁中、簡中、日文、韓文、RTL、emoji | 文字顯示與可擷取性 |
| PDF-IMAGE | JPEG、PNG alpha、黑白掃描、彩色掃描 | 視覺 diff、透明度 |
| PDF-FONT | 內嵌／部分內嵌／未內嵌、CID font | 字型與 glyph |
| PDF-PAGEBOX | MediaBox、CropBox、BleedBox、TrimBox 不同 | 頁面邊界保持 |
| PDF-ROTATE | 0/90/180/270 度 | 旋轉與方向 |
| PDF-SIZE | A4、Letter、Legal、A3、自訂超寬／超長、混合尺寸 | 每頁尺寸保持 |
| PDF-COLOR | RGB、CMYK、Gray、ICC profile、透明混色 | 視覺與色彩資訊 |
| PDF-VERSION | 1.4、1.5、1.7、2.0 可取得樣本 | 能否讀取及輸出版本 |
| PDF-XREF | 傳統 xref、xref stream、object stream、incremental update | 結構相容性 |
| PDF-LINEAR | linearized PDF | 合併成功；輸出是否仍線性化只記錄不預設 |

### 5.3 超連結、目的地與書籤

| ID | 案例 | 預期驗證 |
|---|---|---|
| LINK-URI | HTTP、HTTPS、含 query／fragment／Unicode URL | `/URI` 值與 annotation rectangle 保留 |
| LINK-MAIL | `mailto:` 含 subject/body | action 字串完整 |
| LINK-GOTO | 同一 PDF 跨頁 GoTo | 合併後指向正確位移頁 |
| LINK-NAMED | named destination、同名 named destination 來自不同來源 | 目的地不衝突、每個連結正確 |
| LINK-REMOTE | GoToR 外部 PDF | action 保留但不自動開啟 |
| LINK-FILE | 相對路徑與 Unicode 檔名 | 字串保留；不執行 |
| LINK-MULTI | 同頁多連結、重疊 rectangle、頁面旋轉後連結 | 數量、位置、目標 |
| BOOKMARK-FLAT | 多來源各一層書籤 | 頁碼依來源 offset 修正 |
| BOOKMARK-NEST | 3 層以上、開啟／收合狀態 | 階層、標題、樣式、目的地 |
| BOOKMARK-NONPAGE | URI action 或 named action bookmark | action type 與內容 |
| BOOKMARK-UNICODE | 繁中、日文、emoji 標題 | 文字不亂碼 |

超連結驗證分三層：

1. **結構層**：解析每頁 annotation 的 subtype、rectangle、action 與 destination。
2. **幾何層**：將頁面 rasterize，確認 link rectangle 仍覆蓋預期文字／圖形。
3. **互動層**：在至少兩個 PDF 閱讀器人工點擊；外部 URI 指向本機 listener，內部連結核對實際落點。

### 5.4 特殊 PDF 功能

| 群組 | 選項 | 判定方式 |
|---|---|---|
| Annotations | highlight、text note、stamp、ink、free text | subtype、內容、座標、appearance |
| AcroForm | text、checkbox、radio、combo、button；兩來源同名 field | 欄位數、名稱、值、appearance、可操作性 |
| XFA | 靜態／動態樣本（若合法取得） | 明確記錄支援或失敗，不默認保留 |
| Embedded files | file attachment annotation、document embedded file | Name tree、檔名、內容 hash |
| Portfolio | collection PDF | collection dictionary 與附件；若不支援要有明確錯誤 |
| Layers | Optional Content Groups 預設顯示／隱藏 | OCG dictionary 與視覺狀態 |
| Tagged PDF | headings、table、alt text | StructTreeRoot 與 tag tree 完整性 |
| PDF/A | PDF/A-1b、2b 或組織使用版本 | veraPDF 類工具驗證前後合規；不得只看 metadata |
| Signatures | approval、certification、timestamp 測試簽章 | 合併後不得聲稱原簽章有效；UI／報告需揭露 |
| Encryption | user password、owner password、禁止複製／列印、AES 不同版本 | 現況預期無密碼介面而失敗；不得產生假成功檔 |
| Redaction | 已套用 redaction 與僅有 redaction annotation | 已刪內容不可回復；未套用者狀態明確 |
| Multimedia/3D | 有樣本才測 | 結構保留或明確列為不支援，不執行 active content |
| JavaScript | document/page/action JS | 只檢查是否存在與產品政策；不執行 |

### 5.5 中繼資料專用語料

至少建立：

- Info 中每個標準 key 都有 ASCII 值。
- Info 與 XMP 含繁中、emoji、超長文字、空字串。
- 自訂 Info key；預期不因清除標準 key 被誤刪。
- XMP 包含 Dublin Core、PDF/A identification 與自訂 namespace。
- 頁面可見文字或 content stream 刻意包含 `/Title (KEEP_VISIBLE)`、`/Author` 等 token，用來偵測 raw-byte 清除誤傷。
- 附件 bytes 刻意包含相同 token，確認附件不被修改。
- incremental update 中舊 Info 與新 Info 並存，確認敏感舊值不可用一般或 forensic 字串搜尋找回。

驗收要同時檢查：結構化 Info、XMP、原始 bytes 敏感值、頁面視覺、文字擷取與附件 hash。

### 5.6 Word 語料

- DOC 與 DOCX 各一份簡單文字。
- 中文／日文／emoji、不同字型、缺字字型。
- 標題樣式與 3 層導覽書籤。
- HTTP、mailto、內部書籤連結。
- 表格、圖片、透明圖片、頁首頁尾、頁碼、註腳、分節、橫向頁。
- 公式、SmartArt、圖表、嵌入物件與 tracked changes。
- 密碼保護、受保護檢視、損毀檔、開啟時巨集提示。
- 極長文件與含外部連結文件。
- Word 未安裝、Word 首次啟動尚未完成、COM busy、檔案被另一程序鎖住。

### 5.7 路徑與名稱

- 空格、繁中、日文、emoji、`# % & +`、多個句點、大／小寫副檔名。
- 同名不同資料夾、同檔重複拖入、hard link／symlink（環境允許時）。
- 接近 260 字元與啟用 long path 後超過 260 字元。
- 本機、UNC、網路暫斷、唯讀、無權限、磁碟空間不足。
- 輸出已存在、輸出被鎖住、輸出目錄不存在。
- **輸出等於第一／中間／最後一個來源**：預期產品拒絕且來源 hash 不變；現況可能失敗，列 P0。

## 6. UI 測試矩陣

### 6.1 啟動與版面

- 單一 instance 與多 instance 啟動。
- 圖示、標題、所有文字、字型、顏色、按鈕可見且不重疊。
- 100/125/150/200% DPI 與三種解析度截圖比較。
- 視窗縮放、最大化、最小化、移到不同 DPI 螢幕。
- 亂碼、截字、ellipsis、垃圾桶 glyph、箭頭 glyph。
- 高對比、螢幕閱讀器、UI Automation tree、焦點框。

### 6.2 拖放

- 單一合法檔、同時多個合法檔、合法與非法混合、全部非法。
- 大／小寫副檔名、無副檔名、資料夾、捷徑、假副檔名、零位元 PDF。
- 拖入順序與 UI 順序一致；重複與同名行為可解釋。
- DragEnter effect 對無效項目是否造成錯誤期待。

### 6.3 清單操作

- 第一項上移、最後一項下移不越界。
- 中間項連續上／下移，`sourceFiles` 與顯示順序一致。
- 多選上下移目前應不動；必須記錄為設計限制或缺陷，不能誤判為成功。
- 刪除第一、最後、中間、多個非連續、全部、零選取。
- 排序與刪除交錯 50 次後再合併，頁序仍正確。
- 鍵盤選取、Ctrl/Shift 多選、Tab／Shift+Tab 順序。

### 6.4 合併互動

- 空清單警告。
- SaveFileDialog 預設檔名、PDF filter、取消、覆寫確認。
- 合併時按鈕停用與 `Processing...`；嘗試關閉視窗、重複點擊、拖入、切換焦點。
- 成功、部分 Word 失敗、fatal error、所有 Word 失敗的訊息與按鈕恢復。
- 長時間工作時視窗回應、Windows「Not Responding」、可否安全取消。
- About 內容、滾動、Agree、Open Log Folder、父視窗 modal 行為。

### 6.5 UI 自動化與人工檢查分工

- 自動化建議採 UI Automation 3 相容框架（例如 FlaUI UIA3）控制按鈕、清單與 dialog。
- WinForms 原生拖放若 UIA 無法可靠模擬，可用受控 helper 發送 OLE drag-drop；不得用直接改 private field 取代端到端測試。
- 每個 DPI 保存 baseline screenshot；只對穩定區域做像素／感知 diff，時間與路徑文字需遮罩。
- 使用 Accessibility Insights 或 Inspect 類工具檢查 Name、ControlType、IsKeyboardFocusable 與 tab traversal。
- 真實 drag/drop、閱讀器點擊與高對比視覺至少保留人工 checklist。

## 7. PDF 輸出 Oracle

單一「閱讀器打得開」不足以判定成功。每個輸出至少執行：

1. **檔案層**：存在、非零、`%PDF-` header、`%%EOF`、SHA-256。
2. **結構層**：嚴格 parser 可開啟；xref、object stream、trailer 無錯；可用 qpdf `--check` 或同級工具交叉驗證。
3. **頁面層**：頁數等於來源頁數總和；每頁 MediaBox／CropBox／rotation 符合 manifest。
4. **順序層**：每個 fixture 在可見頁首放唯一 ID，文字擷取或 QR／圖像標記驗證序列。
5. **視覺層**：以固定版本 renderer 轉圖，對來源頁與輸出對應頁做感知 diff；容許閾值由 corpus 類型設定。
6. **文字層**：逐頁文字／Unicode 正規化結果與預期一致。
7. **互動層**：annotations、URI、GoTo、named destination、書籤、表單、附件逐一比對。
8. **隱私層**：Info／XMP 標準欄位已清除，敏感測試字串不可在 raw bytes 中找到；自訂非敏感資料是否保留按產品契約判定。
9. **閱讀器層**：至少兩種閱讀器無修復警告地打開；內部／外部連結依 checklist 操作。
10. **來源完整性**：合併前後每個來源 SHA-256 完全不變。

測試工具與產品使用相同 iTextSharp 時可能共享盲點，所以 parser／renderer 至少一種必須是不同實作。

## 8. 各輪執行內容與 Gate

### L0：環境與基線

輸入：乾淨工作區狀態、SDK、已安裝工具。

步驟：

1. 記錄 `git status`；若無 commit，記為 `nogit` 並對原始碼計算 hash。
2. 記錄 `dotnet --info`、Word、Windows、DPI、測試工具版本。
3. restore、build、publish；保存完整 log 與警告。
4. 檢查可執行檔啟動、圖示、log 寫入。
5. 為所有來源語料計算 SHA-256。

Gate：build/publish 若失敗，後續只可用已知版本 EXE 做探索測試，報告不得把結果歸屬於目前 source。

### L1：Corpus 建立與自我驗證

1. 先以本機 generator 建立可控制 fixture，再依 5.0 白名單自行搜尋／下載缺少的公開 PDF；不得要求使用者交付真實文件。
2. 下載前執行 egress preflight，確認 request 沒有本機資料、只做 GET／HEAD／唯讀 fetch，且 redirect 最終 host 仍被允許。
3. 將下載檔放入 quarantine，建立 `download-manifest.json` 並驗證來源、授權、hash、格式與 active content。
4. 由兩個獨立 parser 驗證輸入本身有效；解析與 validation 全部在本機執行。
5. 產生 corpus manifest、license/source、hash、視覺 baseline，並連回 download manifest。
6. 特殊 action 保持 inert；fixture 目錄設唯讀或每輪先驗 hash。

Gate：沒有來源／授權、fixture 未知、發生本機資料外送或沒有 oracle 時不得進入 golden corpus；未知功能只能列 exploratory，不得作 pass/fail 依據。

### L2：UI 與 Smoke

執行啟動、一般拖放、排序、刪除、取消、兩個簡單 PDF 合併、About、log。覆蓋至少 100% 與 150% DPI。

Gate：啟動失敗、來源被修改、一般兩檔合併錯序、UI 無法恢復為 P0/P1，停止進入昂貴矩陣。

### L3：PDF 特性矩陣

分批執行基本頁面、連結／書籤、annotations／form、metadata、安全／合規。測試組合採：

- 每個 feature 單獨至少一次。
- P0/P1 feature 的兩兩 pairwise 組合。
- 固定全功能 golden 組合：一般文字 + 旋轉 + 內外連結 + 巢狀書籤 + Unicode metadata。
- 每次正式 release 加一輪全部 fixture 依固定順序合併。

Gate：任何結構損毀、錯頁連結、頁面內容遺失或來源修改不得進入 release。

### L4：Word、負向、權限與復原

覆蓋 Word 存在／不存在、部分轉換失敗、全部失敗、損毀／加密 PDF、無權限、輸出衝突、磁碟空間、程序中斷。確認暫存檔、WINWORD、部分輸出與按鈕狀態。

Gate：不得留下產品啟動的孤兒 Word 程序；錯誤不得顯示假成功；來源永不改變。

### L5：效能與穩定性

| 場景 | 建議級距 |
|---|---|
| 檔案數 | 1、2、10、50、100、500 |
| 總頁數 | 10、100、1,000、5,000 |
| 單檔大小 | 1 MB、50 MB、250 MB、1 GB（資源允許時） |
| 連續循環 | 同一程序 20 次、重新啟動 50 次 |
| 混合 Word | 1、10、50 份 DOCX |

記錄 wall time、CPU、working set、private bytes、handle count、輸出大小、temp 增量。不要先設虛構門檻；第一個穩定 release 建立 baseline，之後 release gate 採「不得比核准 baseline 劣化超過 20%」，同機且重跑三次取中位數。

Gate：OOM、hang、無限增長、無法關閉、孤兒程序或來源修改一律 fail。

### L6：全量回歸與證據封存

1. 重新跑 L2 smoke、所有過往 P0/P1 regression、固定 golden 組合。
2. 確認無新增 compiler warning；既有 NU1701 必須明列風險，不能隱藏。
3. 產生 machine-readable JSON、JUnit/TRX（若框架支援）、Markdown summary、screenshots、logs、output hashes。
4. 對失敗分類：產品缺陷、測試缺陷、環境缺陷、已接受限制。
5. 套用 Release Gate。

## 9. 缺陷優先級

| 等級 | 定義 | 例子 |
|---|---|---|
| P0 | 資料遺失、安全邊界、來源被改、錯檔成功 | 輸出覆寫來源、metadata 清除破壞頁面 |
| P1 | 核心功能錯誤、輸出不可用、頁序／連結錯 | 內部連結跳錯頁、一般 PDF 無法合併 |
| P2 | 特殊功能遺失、明顯 UX／可及性問題 | 表單消失、200% DPI 控制項重疊 |
| P3 | 文案、輕微視覺或 log 改善 | 訊息不一致、非阻斷對齊問題 |

每個缺陷至少記錄 run_id、環境、fixture IDs、來源／輸出 hash、重現步驟、預期、實際、截圖或 parser 證據、log、最小重現組合。

## 10. Test-fix 子循環

每次只處理一個缺陷：

1. 以最小 fixture 穩定重現。
2. 建立會失敗的自動測試；如果只能人工重現，建立可重複 checklist 與證據。
3. 執行測試確認是預期原因失敗。
4. 做最小修正，不順便重構無關區域。
5. 跑新測試、同功能群組、L2 smoke。
6. P0/P1 修正必跑全部 L3 golden 與來源 hash 檢查。
7. 更新限制、測試 manifest 與報告。
8. 若同一缺陷連續三次修正仍失敗，停止自動修改，出具根因與選項供人工決策。

## 11. Release Gate

只有全部成立才能標記「可發佈」：

- build 與 publish 成功，來源與 binary 對得上。
- L2 smoke 100% 通過。
- P0 = 0；P1 = 0。
- P2 若保留，必須有明確支援政策、使用者可見限制與核准紀錄。
- 一般 PDF 的頁數、頁序、視覺、文字、頁面 box／rotation 全數通過。
- HTTP/HTTPS、mailto、內部 GoTo、named destination、平面與巢狀書籤全數通過。
- Metadata fixture 通過結構、raw bytes、視覺、文字與附件 hash 五項檢查。
- 所有來源 SHA-256 不變。
- 無產品造成的 orphan WINWORD、不可清理 temp 或殘留錯誤輸出。
- 至少兩種閱讀器打開 golden outputs 無修復警告。
- 效能沒有超過核准 baseline 20% 的未解釋退化。
- 報告含環境、工具版本、EXE hash、fixture hash、失敗與已知限制。
- `download-manifest.json` 可追溯每個網路 fixture 的公開來源、最終 URL、授權、取得時間、大小與 SHA-256。
- Network egress audit 顯示只有公開語料的 GET／HEAD／唯讀 fetch；沒有本機檔案、內容、檔名、路徑、log、hash、截圖或結果離開測試環境。
- 沒有使用需上傳檔案的線上 validator、轉檔、OCR、malware sandbox、雲端儲存或外部 AI 服務。

如果尚未建立某特殊功能的產品契約，結果必須是「unsupported／undetermined」，不能以「檔案產生成功」當作通過。

## 12. 每輪報告格式

```markdown
# PDF Merger Test Run <run_id>

- Source revision/hash:
- EXE SHA-256:
- OS / DPI / locale:
- Word version:
- Test tools:
- Mode: audit | test-fix
- Network mode: public-download-only
- Download manifest:
- Egress audit:

## Summary
- Passed:
- Failed:
- Blocked:
- Unsupported:
- P0/P1/P2/P3:

## Gate Result
PASS | FAIL | BLOCKED

## Failures
| ID | Severity | Fixture | Expected | Actual | Evidence |

## Source Integrity
| Fixture | SHA-256 before | SHA-256 after | Same |

## Artifacts
- Build log
- Machine-readable results
- Screenshots
- Parser reports
- Output PDFs and hashes
- Product log

## Next Loop
- Exact failed tests to rerun
- Required decision or authorized fix
```

對應的可直接使用提示詞見 [`LOOPING_TEST_PROMPTS.md`](LOOPING_TEST_PROMPTS.md)。
