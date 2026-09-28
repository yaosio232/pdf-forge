# PDF Merger Looping 測試提示詞

## 強制前置規則

執行任何本文件提示詞前，必須先閱讀專案根目錄 [`AGENTS.md`](AGENTS.md)，並逐項執行其中的 SPLIT-01 至 SPLIT-12。每個正式 PDF 測項至少 20 頁；分割 gate 必須使用全新 40 頁 fixture，切成 6 份（7/7/7/7/6/6），不得使用舊的三個單頁檔案替代。缺少任一測項、實際 UI 操作或證據時，結論只能是 FAIL/BLOCKED，不得回報成功。

## 1. 使用方式

先填入共用變數，再使用「主控提示詞」。如果執行環境不適合一次完成全矩陣，可依 L0～L6 分輪貼上專用提示詞。提示詞預設為 audit-only；只有貼上「缺陷修正子循環」並明確指定可修改範圍，代理才可改產品碼。

共用變數：

```text
PROJECT_ROOT = D:\Pdf_Merger
RUN_ID = 由執行者產生 YYYYMMDD-HHmmss-<revision-or-nogit>
MODE = audit
NETWORK_MODE = public-download-only
SUPPORTED_WINDOWS = Windows 10 22H2, Windows 11 企業基線
PRIMARY_DPI = 100%, 150%
PRIMARY_READERS = Edge PDF Viewer, 組織標準 Adobe Acrobat Reader
WORD_MATRIX = 無 Word, 本機已部署 Word 版本
MAX_TEST_TIME = 本輪允許時間
OUTPUT_ROOT = D:\Pdf_Merger\TestResults\<RUN_ID>
```

## 2. 主控 Loop 提示詞

```text
你是 PDF 相容性、Windows WinForms UI 自動化與測試工程負責人。請在 PROJECT_ROOT=D:\Pdf_Merger 執行 PDF Merger 的完整品質循環。

開始前必須完整閱讀：
1. docs/PROGRAM_DOCUMENTATION.md
2. docs/LOOPING_TEST_PLAN.md
3. docs/LOOPING_TEST_PROMPTS.md
4. 目前原始碼、專案檔、既有 Test 素材與 git 狀態

本輪 MODE=audit。你可以建立測試程式、fixture、manifest、報告與 TestResults 產物，但不得修改產品程式 Form1.cs、Form1.Designer.cs、Logger.cs、Program.cs、Pdf_Merger.csproj，也不得聲稱已修正缺陷。

本輪 NETWORK_MODE=public-download-only。你應自行用通用 PDF 特性詞搜尋並下載公開、可合法使用的測試 PDF，不要求使用者提供真實文件。網路是單向取得公開資料：禁止上傳或對外傳送任何本機 PDF、DOC/DOCX、合併輸出、截圖、log、檔名、路徑、檔案清單、hash、內容片段、測試結果或由它們衍生的搜尋詞。禁止線上 PDF validator、轉檔、OCR、malware sandbox、雲端儲存與外部 AI 檔案分析服務。

請按 L0→L1→L2→L3→L4→L5→L6 執行。不要無限循環：
- 發現 P0 立即停止會擴大損害的測試，保存證據，再執行不會觸碰該風險的唯讀分析。
- build 被環境阻擋時，分清楚 source build 與既有 EXE；不得把舊 EXE 結果算成目前 source 通過。
- 缺少工具時，先列出需求與安全替代方法；沒有 oracle 的案例標成 BLOCKED 或 EXPLORATORY，不能算 PASS。
- 每個測試先記錄來源 SHA-256，測後再比對。輸出路徑絕不能等於來源；專門測此風險時只使用可拋棄副本並另外保存 master hash。
- Active content、Launch、JavaScript、未知 URI 不得執行。
- 不終止非本輪啟動的 WINWORD.EXE。
- 搜尋 query 只能由測試 taxonomy 產生，例如「public licensed PDF named destination test file」，不得參照任何本機內容。
- 下載只用 HTTPS GET／HEAD 或唯讀 repository fetch；發送前檢查 request 不含本機資料，redirect 後 final host 仍在核准清單。
- 優先順序為本機生成 fixture、veraPDF corpus、PDF Association 公開資源、qpdf／Apache PDFBox 官方且授權可追溯的測試資源；隨機網路 PDF 先進 quarantine，未完成授權與結構驗證不得當 golden fixture。

測試最少必須涵蓋：
- UI：啟動、拖放、合法/非法混合、排序邊界、多選、刪除、Save dialog 取消與覆寫、Processing 狀態、About、log、鍵盤、UIA 可及性、100%/150% DPI 截圖。
- PDF：基本頁數與頁序、混合頁面尺寸/旋轉、Unicode/字型/掃描圖、xref/object stream/incremental update。
- 互動：HTTP/HTTPS、mailto、內部 GoTo、named destination、遠端 GoTo、平面與巢狀書籤、Unicode 書籤；用結構、幾何與實際閱讀器三層驗證。
- 特殊功能：annotations、AcroForm 同名欄位、附件、Portfolio、layers、tagged PDF、PDF/A、加密、簽章、redaction、active content 的安全結構檢查。
- Metadata：Info、XMP、Unicode、自訂 key、舊 incremental value，並加入頁面/附件 bytes 包含 /Title、/Author 等 token 的 anti-corruption fixture。
- Word：DOC/DOCX、標題書籤、連結、表格/圖片/分節、Word 不存在、損毀或受保護文件、部分與全部轉換失敗。
- 負向與復原：假副檔名、零位元、損毀、無權限、輸出被鎖、磁碟不足模擬、輸出等於來源、程序中斷、temp 與 orphan WINWORD。
- 效能：依本機資源安全地覆蓋 10/50/100 檔與至少 1,000 頁；更大級距若資源不足需明列未執行原因。

Oracle 不得只依賴 iTextSharp。至少用另一個 PDF parser/checker 與另一個 renderer。每個輸出檢查 header/EOF、嚴格解析、頁數、順序、boxes、rotation、渲染、文字、annotations/actions、書籤、metadata、來源 hash。至少用兩種 PDF 閱讀器檢查 golden outputs。

輸出到 TestResults/<RUN_ID>/：
- environment.json
- corpus-manifest.json
- download-manifest.json
- network-egress-audit.json
- build.log
- results.json
- test-report.md
- release-gate.md
- screenshots/
- parser-reports/
- outputs/ 及 SHA-256 清單
- product-log-copy.txt（若存在）

每完成一個 L 階段，更新 test-report.md 的數量、P0/P1/P2/P3、BLOCKED、證據與下一階段決策。最終回答只摘要：Gate 結果、最嚴重 5 個問題、未覆蓋項目、報告與證據的絕對路徑。沒有證據不得說 complete、fixed、links work 或 release ready。
```

## 3. L0：環境、建置與基線提示詞

```text
在 D:\Pdf_Merger 執行 L0 基線，不修改產品碼。

1. 閱讀 docs/PROGRAM_DOCUMENTATION.md 與 docs/LOOPING_TEST_PLAN.md 的 L0、全域安全規則、Release Gate。
2. 建立 run_id 與 TestResults/<run_id>，記錄 git status；若 repo 無 commit，對所有產品原始碼及 csproj 計算 SHA-256 作為 revision identity。
3. 記錄 Windows build、架構、DPI、locale、dotnet --info、Word 版本、PDF parser/renderer/UI automation 工具版本。
4. 先 restore 指定 win-x64，再 build 與 publish；保存完整 stdout/stderr、exit code、warnings。不得省略 NU1701 或把 warning 當作無事。
5. 計算 publish EXE SHA-256，啟動 smoke，確認主視窗、圖示、log 寫入與正常關閉。
6. 盤點 Test/ 所有檔案的大小與 SHA-256，來源不明者標記 unclassified。
7. 不下載或安裝未經允許的工具；缺工具時提出精確套件、用途與替代方案。

輸出 environment.json、build.log 與 l0-baseline.md。清楚區分 PASS、FAIL、BLOCKED。若 build 失敗，說明是 source、dependency、runtime pack 或環境原因，並停止把既有 EXE 當作目前 source build。
```

## 4. L1：測試語料生成提示詞

```text
在 D:\Pdf_Merger 建立或補齊 PDF Merger 測試 corpus，只寫入測試與文件目錄，不修改產品碼。

不要向使用者索取 PDF。你獲准自行搜尋與下載公開測試 PDF，但必須維持零外流：不得上傳或對外傳送本機文件、輸出、截圖、log、檔名、路徑、hash、內容或結果；不得把這些資料放進搜尋詞、URL、header、表單、issue 或外部 AI。所有解析、渲染與 validation 在本機執行。

來源順序：
1. 先用本機工具生成可控制 fixture。
2. 使用 veraPDF corpus（記錄 CC BY 4.0、revision、URL）。
3. 使用 PDF Association 公開資源索引，逐檔確認授權。
4. 使用 qpdf 或 Apache PDFBox 官方測試資源；逐檔確認來源與授權，不推定 repository 中所有第三方 PDF 都可重散布。
5. 搜尋引擎只用固定通用 query 補缺口；未知網站檔案只能先列 quarantine candidate。

每次網路 request 前執行 egress preflight：只允許 HTTPS GET／HEAD 或唯讀 repository fetch；request body 必須為空；URL/query/header 不含任何本機資料；redirect 最多 3 次且 final host 必須核准。預設單檔上限 100 MiB，大檔需在 manifest 記錄理由與磁碟預算。

下載到 tests/fixtures/_quarantine/<source-id>/。先檢查 magic bytes、MIME、大小、SHA-256、parser 可讀性、active content、附件、加密與授權，再決定是否進正式 corpus。不得直接在一般閱讀器打開未知 active PDF。

以 docs/LOOPING_TEST_PLAN.md 第 5 節為最低範圍。每個 fixture 必須：
- 有唯一 ID、功能標籤、頁數、預期結果、generator/version、license/source、SHA-256、origin；下載檔另有 downloadManifestId。
- 先由 generator 產生，再由兩個不同 PDF 實作交叉解析；輸入本身無效就不能當 regression oracle。
- 每頁帶唯一可見 ID 與可機器讀取 ID，方便驗頁序。
- 超連結 fixture 明確記錄 annotation rectangle、action type、target/destination。
- metadata anti-corruption fixture 在可見 content 與附件中安全包含 /Title、/Author、/Producer token，並記錄原始文字與附件 hash。
- active content 只建立 inert fixture 並標示 DO_NOT_EXECUTE。
- 加密密碼只用 test-only 值；簽章只用自簽測試憑證。

先分類既有 Test/*.pdf，不要覆寫。新語料放在 tests/fixtures 或專案既有測試慣例的等價目錄，執行輸出不得混入 fixtures。

產出 corpus-manifest.json、download-manifest.json、network-egress-audit.json、fixture-validation.md、每個 fixture 的來源與 hash。download manifest 記錄 original URL、final URL、retrieved_at、ETag/Last-Modified、license、source revision、size、SHA-256 與 quarantine 結果。若某類 fixture 無合法來源或生成工具，標 BLOCKED 並列出可接受的取得方式；不要用未知網路檔案填空，也不要改向使用者索取機密樣本。
```

## 4.1 公開 PDF 網路搜尋與下載專用提示詞

```text
你只負責替 D:\Pdf_Merger 補齊公開 PDF 測試語料，不測產品、不修改產品碼，也不接收使用者文件。

目標 feature gaps={{從 corpus-manifest 計算的缺口清單}}。

隱私硬規則：
- 不讀取本機 PDF/DOC/DOCX 的內容來生成 query；不把本機檔名、路徑、客戶名稱、log、hash、截圖、錯誤內容或測試結果送出。
- 不使用 file upload、POST/PUT/PATCH、multipart form、線上 validator、converter、OCR、sandbox、cloud drive 或外部 AI 檔案分析。
- query 只能由 feature gaps 的通用名稱生成，例如 PDF/A-2b test corpus、PDF named destination sample、AcroForm duplicate field fixture。

搜尋優先官方與有授權的來源：veraPDF corpus、PDF Association resources、qpdf、Apache PDFBox。每個候選先記錄頁面 URL、直接下載 URL、維護者、license、revision 與預期 feature；授權不清楚就拒絕，不因「公開可下載」推定可作測試 corpus。

下載前驗證 HTTPS、host、空 request body、無本機資料 query/header；redirect 最多 3 次並驗 final host。預設單檔 100 MiB 上限。下載至 tests/fixtures/_quarantine/<source-id>/，計算 SHA-256，檢查 PDF magic、實際 MIME、parser、active content、附件、加密與頁數。任何 JavaScript/Launch action 只記錄，不執行。

只把授權清楚、結構可辨識、預期 feature 可建立 oracle 的檔案升級為 fixture。輸出 download-manifest.json、network-egress-audit.json、accepted.md、rejected.md。報告不得包含任何本機文件資訊。
```

## 5. L2：UI 完整檢測提示詞

```text
對 PDF Merger 執行 L2 WinForms UI 檢測。先閱讀 docs/LOOPING_TEST_PLAN.md 第 6 節。不得修改產品碼。

自動化至少涵蓋：
1. 啟動、關閉、主視窗標題、控制項存在、Enabled/Visible、初始清單。
2. 單一/多個合法拖放、合法+非法混合、全部非法、資料夾、無副檔名、假 PDF。
3. 第一/中間/最後項上移下移、邊界、連續 50 次排序刪除交錯、Ctrl/Shift 多選、全部刪除。
4. 空清單 Merge、Save dialog 取消、預設檔名/filter、既有輸出覆寫、成功/警告/error dialog。
5. 合併時 MERGE PDF 變 Processing... 且 disabled；完成或失敗後必須恢復。
6. About 內容、Agree、Open Log Folder；不要讓 explorer 子程序干擾其他測試。
7. Tab/Shift+Tab、Enter/Space、UI Automation Name/ControlType/Focusable；記錄重複 TabIndex 的實際結果。
8. 100% 與 150% DPI、1366x768 與 1920x1080 截圖；檢查重疊、截字、亂碼、glyph、高對比。

原生 drag-drop 必須走端到端 OLE/滑鼠路徑；若工具做不到，該項標人工，不可用反射直接填 sourceFiles 冒充 UI 測試。截圖 diff 要遮罩時間與動態路徑。每個失敗保存 screenshot、UIA tree、步驟與產品 log。

輸出 ui-results.json、ui-report.md、screenshots/、automation.log。最後按 P0-P3 分級，不要把現有行為自動當成正確設計。
```

## 6. L3A：超連結與書籤提示詞

```text
專注測試 PDF Merger 的超連結、目的地與書籤。不得只看成功 MessageBox，也不得相信固定字串「Links are working!」。

建立最小但完整矩陣：HTTP、HTTPS、mailto、URI query/fragment/Unicode、同文件 GoTo、named destination、不同來源同名 destination、GoToR、相對外部檔名、旋轉頁連結、多連結/重疊 rectangle；平面、三層巢狀、Unicode、URI action 書籤。

對每個輸出做三層驗證：
1. 結構：逐頁列出 Link annotation、Rect、A/S、URI、Dest，列出 outline tree 與最終頁碼。
2. 幾何：renderer 產圖並疊上 link rectangles，確認位置仍覆蓋預期內容。
3. 互動：用 Edge 與另一個核准閱讀器點擊。外部 URI 只連本機 listener 或檢查攔截到的 target；active/unknown action 不執行。

特別驗證第二、第三來源的內部連結與書籤 page offset，及兩來源 named destination 衝突。任何跳錯頁、target 改變、annotation 消失、閱讀器修復警告列 P1。

輸出 link-bookmark-results.json、link-map-before-after.csv、overlay screenshots、reader-checklist.md 與最小失敗 fixture IDs。
```

## 7. L3B：特殊 PDF 與中繼資料提示詞

```text
專注測試 PDF Merger 的特殊 PDF 功能與 metadata 清除。先備份 fixture hashes，不修改來源或產品碼。

覆蓋 annotations、AcroForm（含不同來源同名 field）、附件、Portfolio、layers、tagged PDF、PDF/A、測試簽章、各類加密、redaction 與 inert active content。每類要先定義預期為 preserve、reject-with-clear-error 或 unsupported；沒有契約就標 UNDETERMINED，不可因輸出存在而 PASS。

metadata 必測：標準 Info、XMP、Unicode、超長值、自訂 key、incremental update 舊值。加入頁面可見文字、content bytes、附件 bytes 包含 /Title、/Author、/Subject、/Keywords、/Creator、/Producer、/Trapped 的 anti-corruption fixtures。

輸出驗證：
- 至少兩個不同 parser 無結構錯誤。
- 頁面 render 與文字擷取保持；token 附近內容不能消失。
- 附件 hash 保持。
- 標準敏感 metadata 與 XMP 依產品政策清除，raw bytes 不可搜到測試秘密值。
- 自訂非敏感 key 的結果如實記錄。
- 簽章不得宣稱仍有效；PDF/A/UA 必須由專用 validator 判定。

若 BlankPdfInfoEntries 類 raw byte 行為造成任何頁面、附件或 object 損壞，列 P0，保存輸入/輸出 hash、最小 fixture、parser error、render diff，停止擴大測試。

輸出 special-pdf-results.json、metadata-report.md、validator reports、before-after object inventory 與 visual diffs。
```

## 8. L4：Word、負向與復原提示詞

```text
執行 PDF Merger 的 Word 轉換、負向輸入、權限與復原測試。只處理測試檔，不修改產品碼。

Word 矩陣：DOC/DOCX 簡單文字、Unicode、標題書籤、HTTP/內部連結、表格、圖片、頁首頁尾、分節/橫向、公式、tracked changes、受保護/損毀；另測無 Word、COM busy、檔案鎖住、部分轉換失敗與全部失敗。

PDF/檔案負向矩陣：零位元、假副檔名、截斷、錯誤 xref、加密、讀取無權限、輸出目錄無權限、輸出鎖住、網路暫斷、已存在輸出、長路徑。磁碟不足必須用受控 quota/虛擬磁碟或 mock，不可真的耗盡系統磁碟。

P0 專項：用三組可拋棄來源副本分別測「輸出等於第一/中間/最後來源」。測試前另存 master 與 hashes，放在隔離目錄。期望產品拒絕且來源 hash 不變；若現況截斷或改寫，立即停止該項並列 P0。

每案例檢查：MessageBox 是否誠實、按鈕是否恢復、部分輸出是否被清理/隔離、temp 增量、僅本輪啟動的 WINWORD 是否退出、產品 log 是否有 context、其他成功來源是否符合明確政策。

輸出 word-negative-results.json、recovery-report.md、process-snapshots、temp-before-after 與 source-integrity.csv。
```

## 9. L5：效能與穩定性提示詞

```text
在資源安全前提下執行 PDF Merger 效能與耐久測試。先完成 L2 smoke；任何來源修改風險未排除時不得開始。

級距：1/2/10/50/100/500 檔、10/100/1000/5000 頁、1/50/250 MB 與資源允許時 1 GB 單檔；同一程序重複 20 次、重啟 50 次、混合 Word 1/10/50 份。若機器資源不足，依序縮小最高級距並記錄原因，不得讓系統磁碟耗盡。

每個場景 warm-up 後重跑 3 次取中位數，記錄 wall time、CPU、working set、private bytes、handle count、UI responsiveness、輸出大小、temp 增量、WINWORD 數量。驗證輸出不因壓力而少頁、錯序或失去連結。

第一個可信執行建立 baseline；已有核准 baseline 時，比較退化百分比。超過 20% 且無環境解釋列 release blocker。OOM、hang、handle 持續增長、orphan process、來源修改一律 fail。

輸出 performance.csv、performance-report.md、資源時間序列與所有失敗場景最小重現方式。
```

## 10. 缺陷修正子循環提示詞

```text
你現在獲准修正 PDF Merger 的單一缺陷：
DEFECT_ID={{缺陷編號}}
EVIDENCE={{報告與產物絕對路徑}}
ALLOWED_PRODUCT_FILES={{允許修改的精確檔案}}

先使用 systematic debugging：從證據與原始碼找根因，不憑猜測改碼。接著使用 test-driven development：
1. 用最小 fixture 重現。
2. 先新增會因正確原因失敗的自動測試，執行並保存 failure。
3. 提出根因、修正邊界與不會處理的相鄰問題。
4. 只在 ALLOWED_PRODUCT_FILES 做最小修正，不改文案/架構等無關內容。
5. 執行新測試、同功能群組、L2 smoke；P0/P1 再跑全部 golden PDF、link/bookmark、metadata anti-corruption 與來源 hash。
6. 更新 test report、PROGRAM_DOCUMENTATION 的現況敘述及已知限制。
7. 在宣稱完成前執行 verification-before-completion，列出實際命令、exit code、passed/failed 數與未執行項。

如果同一 DEFECT_ID 連續三次修正仍失敗，停止修改，保留工作區與證據，回報根因假設、三次差異、仍缺的資訊與 2-3 個人工決策選項。不得以跳過測試、放寬 assertion 或刪除 fixture 取得綠燈。
```

## 11. L6：全量回歸與 Release Gate 提示詞

```text
對目前 PDF Merger 候選版本執行最終 L6，不修改產品碼。

輸入：最近一次成功 build/publish、全部 fixture manifest、歷史 P0/P1 regressions、最近核准效能 baseline。

1. 驗證 source revision/hash 與 EXE SHA-256 對應。
2. 重跑全部 L2 smoke、歷史 P0/P1、一般 PDF、link/bookmark 全矩陣、metadata anti-corruption、Word 基本成功/失敗與來源完整性。
3. 對 golden outputs 用兩個 parser、固定 renderer、兩種閱讀器。
4. 比較 compiler warnings；NU1701 等既有警告仍要出現在風險摘要。
5. 套用 docs/LOOPING_TEST_PLAN.md 第 11 節每一條 Release Gate，逐條附證據路徑。
6. 審查 download-manifest.json 與 network-egress-audit.json：每個下載 fixture 可追溯來源／授權，且沒有任何本機資料外送、upload request 或線上檔案分析服務。
7. 產生 results.json、test-report.md、release-gate.md、artifact-sha256.txt。

結論只能是 PASS、FAIL 或 BLOCKED：
- PASS：所有 gate 有證據且 P0/P1 為 0。
- FAIL：產品行為不符合 gate。
- BLOCKED：環境/fixture/oracle 不足，且不能安全判斷。

最後回答格式：
Gate: <PASS|FAIL|BLOCKED>
Revision/EXE hash: <值>
Counts: <passed/failed/blocked/unsupported, P0-P3>
Top issues: <最多五項>
Coverage gaps: <未執行或無 oracle>
Evidence: <test-report.md 與 release-gate.md 絕對路徑>

不得用「大致正常」「看起來可以」或固定成功 MessageBox 取代證據。
```

## 12. 單輪續跑提示詞

當一輪因時間或環境中斷，用下列提示詞續跑，避免重做已完成項目：

```text
延續 PDF Merger 測試 run_id={{RUN_ID}}。先讀取 TestResults/{{RUN_ID}}/environment.json、results.json、test-report.md、release-gate.md 與 artifact hashes，並確認現有產品 source hash、EXE hash、fixture hashes 沒有改變。

若 hashes 相同：只從報告「Next Loop」的第一個未完成測試繼續，不重跑已完成且證據完整的昂貴案例；但在最終 L6 必須重跑規定的 smoke 與 P0/P1 regression。

若任何產品或 fixture hash 改變：建立新的 run_id，不得把舊 PASS 沿用到新版本；舊 run 僅作歷史證據。

保持原 MODE 與安全規則。完成後原子更新 results.json 與 Markdown 報告，列出本次新增執行、未執行、失敗及下一步。
```

## 13. 報告審查提示詞

```text
只審查、不執行也不修改。閱讀 PDF Merger 的 docs/LOOPING_TEST_PLAN.md、本輪 environment.json、corpus-manifest.json、results.json、test-report.md、release-gate.md 與 artifact hash 清單。

檢查：
- 每個 PASS 是否有可定位證據，而非只靠成功 dialog。
- source、EXE、fixture hashes 是否一致。
- 超連結是否做結構/幾何/互動三層驗證。
- metadata 是否做結構、raw bytes、視覺、文字、附件 hash。
- 是否交叉使用非 iTextSharp parser/renderer。
- P0 輸出等於來源是否使用隔離副本且檢查來源 hash。
- BLOCKED/UNSUPPORTED 是否被錯算為 PASS。
- Release Gate 是否逐條滿足。
- 下載語料是否有來源／授權／final URL／hash；egress audit 是否證明只有公開資料下載，沒有本機資料上傳或進入搜尋詞。

輸出 review-findings.md。每項 finding 給 severity、引用的檔案/測試 ID、缺少的證據與最小補測建議。若證據不足，結論必須降為 BLOCKED，不可推測通過。
```
