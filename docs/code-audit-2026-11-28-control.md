# Code audit 2026-11-28 — control table

每個 finding 的確認結果、修正批次與狀態。修正一筆就更新這張表（狀態與修正 commit），
同一個 commit 內一起改。報告原文：[plan](code-audit-2026-11-28-plan.md)、
Block [1](code-audit-2026-11-28-block1-interop.md) /
[2](code-audit-2026-11-28-block2-save-lifecycle.md) /
[3](code-audit-2026-11-28-block3-game-data.md) /
[4](code-audit-2026-11-28-block4-item-editors.md) /
[5](code-audit-2026-11-28-block5-world-editors.md) /
[6](code-audit-2026-11-28-block6-build-tooling.md)。

## 基準與前提

- 稽核快照：`9ba7ae4`，crimson-rs `0a551a6`。基準測試（`fad6d47`，同一份程式碼）：**453 個全過**，166 秒。
- 確認時 vendor 已 refresh 到 crimson-rs **`ebf7304`**（96 個 commit，crimson-rs 自己的稽核修正）。
  C ABI：0 個 export 移除或改名、0 個簽章改變、11 個 `repr(C)` 結構不變；新增
  `crimson_parser_supports`、`crimson_save_set_allow_hmac_rewrap` 兩個 export，以及
  −26 / −27 / −28 三個錯誤碼。C# 298 個 entry point 全數與 Rust 端一致（機械比對）。
- 用新 DLL 跑全套：mutation-version 相關的 4 個 deferred 測試失敗（契約改變，見 B1-01），其餘全過。
- 確認方式：每個 block 由一個唯讀 agent 對照現有程式碼逐條重驗；Block 5 另以 PyO3 探測 6 份存檔。

## 欄位

- **確認**：確認 = 如報告所述；部分 = 屬實但範圍不同（見備註）；未重現；上游已修；待資料 = 需要存檔掃描或實機才能判斷。
- **批次**：見下方批次表。
- **狀態**：待修 → 已修（附 commit）；待決定 = 需要使用者決定；待實機 = 需要遊戲內驗證；不修（附理由）。

## 批次

| 批次 | 主題 | 範圍 |
|---|---|---|
| P0 | vendor 更新與註解規則 | 適配 crimson-rs ebf7304（version 契約、新錯誤碼、HMAC）；註解檢查與歸零項 |
| P1 | 存檔安全 | 未存檔提示、單一 dirty 來源、Save 失敗、備份 / 還原、外部修改偵測 |
| P2 | 過期快照 | 工具視窗在換檔或同檔修改後寫錯位置：文件世代與 mutation version 檢查 |
| P3 | 多步寫入與 deferred batch | clone 後補償、batch 失敗處理、mutation gate |
| P4 | 例外處理 | async void、吞掉的例外、啟動與載入失敗 |
| P5 | 值域與正確性 | absent ≠ 0、上限、寶石判斷、各編輯器小錯 |
| P6 | 遊戲資料與快取執行緒安全 | re-bootstrap、快取原子性與版本、snapshot swap、DDS |
| P7 | UI 執行緒 I/O | 載入、掃描、設定檔移到背景 |
| P8 | UI 字串 | 三語系字串、缺鍵、字串鍵測試 |
| P9 | Magic strings（Rule 7） | 每個專案一個常數檔、錯誤碼名稱表 |
| P10 | 平台與日誌（Rule 4 / 6） | Core 介面、檔案日誌、mutex |
| P11 | 建置、CI 與 Python 工具 | 需使用者決定的項目另標 |
| P12 | 文件與註解 | 過時文件、歷史語句 ratchet 歸零 |
| P13 | 測試缺口 | |
| P14 | 結構 | 拆分大檔與重複 helper（最後做） |

## Findings


### Block 1

| ID | 標題 | 嚴重度 | 確認 | 批次 | 狀態 | 修正 | 備註 |
|---|---|---|---|---|---|---|---|
| B1-01 | `LoadBlockDetails` 的 details cache 在 deferred batch 中失效（abort 後出現 ABA 舊資料） | bug | 上游已修 | P0 | 已修 | `ff24f9a` | crimson-rs ebf7304: mutation_version 在 batch 內每次 mutation 都遞增、abort 只往前；C# 的 cache 每次讀 FFI version，升 vendor 即修。四個 rollback 測試改成新契約。 |
| B1-02 | deferred 模式下 batch 不再「all-or-nothing」，而文件推薦的 partial-success 寫法會 commit 半套操作 | bug | 確認 | P3 | 已修 | `951ac21` | Rust 端已寫明 deferred 模式下前面的 op 保留；沒有 savepoint API。新增 −26/−27 與 re-decode 驗證，batch 中途失敗點更多。 |
| B1-03 | batch 的 `FailedOpIndex` 在 PANIC 或提早返回時誤報為 op 0 | bug | 確認 | P3 | 已修 | `951ac21` | NULL_ARG 提早返回與 PANIC 仍不寫 out_failed_op_index；成功時改為 usize::MAX。 |
| B1-04 | two-call pattern 丟棄第二次呼叫回報的長度或筆數 | risk | 確認 | P5 | 已修 | `559a80b` | two-call 語意不變（第二次回報實際長度/筆數）；列舉改為加鎖，不再有 data race，但兩次呼叫之間仍可能被插入。 catalog 讀取（itemInfo/paloc/stringinfo/mainquest）載入後不變，未改。 |
| B1-05 | handle 層級的 batch 狀態沒有 C# 端的單一寫入者保證（跨執行緒 mutation 會混入別人的 batch） | risk | 確認 | P3 | 已修 | `951ac21` | batch 狀態仍屬於 handle，C# 端沒有單一寫入者；與 B4-21、B5-13 同一個 mutation gate。 |
| B1-06 | catalog 的 `MergeDlcFromBytes` 與並行 lookup 不安全，C# 端沒有防護也沒有文件 | risk | 確認 | P6 | 已修 | `8577638` `f3f25a8` | Rust 文件仍寫「不能與並行 lookup 同時進行」；merge 現在可能回傳 BODY_PARSE。 merge 只在 bootstrap 時執行；P7 將 bootstrap 移出 UI 執行緒時一併處理。 以 bootstrap 期間停用視窗並關閉工具視窗達成互斥。 |
| B1-07 | 部分 catalog lookup 把所有錯誤碼默默轉成「找不到」或 0 | risk | 確認 | P4 | 已修 | `5127921` | faction get_entry / lookup_count 改為先清零 out 參數，錯誤一律讀成「找不到」。 |
| B1-08 | `GameDataLayout.TryListDir` 吞掉所有 `CrimsonSaveException` | risk | 確認 | P4 | 已修 | `5127921` | 新增更多 BODY_PARSE 來源（未知 PAMT flag），TryListDir 仍全吞。 |
| B1-09 | `NativePaverReader.TryReadFromInstall` 宣稱「Never throws」，實際會丟例外 | risk | 確認 | P4 | 已修 | `5127921` | 可改用新的 crimson_parser_supports；仍需捕捉 DllNotFound / EntryPointNotFound。 |
| B1-10 | `GameDataVersion.CompatibleMinors` 直接回傳快取中的可變陣列 | risk | 確認 | P4 | 已修 | `5127921` | 若改用 crimson_parser_supports 即自然消失。 |
| B1-11 | `ItemRecord.ToPathSteps` 在 `PathLen > 2` 時默默補零 | risk | 確認 | P4 | 已修 | `5127921` | path_len 上限仍為 2；改為超出時丟例外。 |
| B1-12 | 從 ABI 讀出的列舉值沒有驗證就直接轉型 | risk | 確認 | P4 | 已修 | `5127921` | 列舉值沒有新增；仍需驗證範圍。 |
| B1-13 | 綁定了 2.01 以後語意已錯的 3-byte dye mask ABI | risk | 確認 | P5 | 已修 | `559a80b` | 3-byte mask 保留做相容；_mask_full / _dye_slot_bits 等新函式尚未綁定。 移除 3-byte 包裝。 |
| B1-14 | PAZ 每抽取一個檔案都做兩次完整的 PAMT 解析與解密解壓 | risk | 確認 | P6 | 待上游 | `8577638` | PAZ 仍每次重新解析 PAMT；沒有 allocate-and-free API。 需 crimson-rs 提供 PAMT 快取或一次解析多檔的 API。 |
| B1-15 | `LoadBlockDetails` 持有 `_cacheLock` 期間做 1–2 秒的 FFI 與 JSON 反序列化 | risk | 確認 | P6 | 已修 | `8577638` | 純 C# 問題。 |
| B1-16 | `HasStructuralEdit` 與 deferred 狀態不同步 | risk | 確認 | P3 | 已修 | `951ac21` | 新增 −26/−27 部分失敗路徑。 |
| B1-17 | `RunDeferred` 對 abort 或 commit 失敗的處理不完整 | risk | 確認 | P3 | 已修 | `951ac21` | commit 失敗時 version 現在往前移；take() 之後的 PANIC 不變。 |
| B1-18 | Rule 7：magic strings 沒有集中管理，錯誤碼名稱表重複 8 份且內容不一致 | rule violation | 確認 | P9 | 已修 | `ff24f9a` `1836867` | 變嚴重：−26 LIST_PARTIALLY_DECODED、−27 ELEMENT_CLASS_MISMATCH、−28 HMAC_UNVERIFIED 不在常數與 8 份名稱表內。P0 先補常數與單一名稱表。 P0 建立 CrimsonErrorCode 單一名稱表並補 −26/−27/−28；其餘 magic strings 仍在 P9。 P9 完成其餘 magic strings。 |
| B1-19 | 整個專案抑制 CA1416，且理由已過時（削弱 Rule 4 的防線） | rule violation | 確認 | P10 | 已修 | `4cbcef7` | 純 C# 問題。 抑制已移除；專案內無 CA1416 警告。 |
| B1-20 | 文件與註解過時或錯置 | cleanup | 確認 | P12 | 待修 |  | ISaveLoader 的「每次成功 mutation 剛好 +1」在新版更錯；NativePaverReader「最新 export」註解過時。 |
| B1-21 | 例外訊息未本地化就直接送到 UI | cleanup | 確認 | P8 | 待修 | `6a8c473` | 純 C# 問題。 原生例外訊息直接顯示，需錯誤碼對應的本地化訊息。 |
| B1-22 | 檔案規模與重複（只談結構） | cleanup | 確認 | P14 | 待修 |  | 純結構。 |
| B1-23 | `IItemInfoCatalog` 缺少 `LookupSummary` / `LookupFlags` | cleanup | 確認 | P5 | 已修 | `559a80b` | 純 C# 問題。 |
| B1-24 | 測試缺口 | cleanup | 部分 | P13 | 待修 |  | B1-01 的 rollback 測試在新版變成有效；C# 仍沒有自己的 layout 測試。 |

### Block 2

| ID | 標題 | 嚴重度 | 確認 | 批次 | 狀態 | 修正 | 備註 |
|---|---|---|---|---|---|---|---|
| B2-01 | File → Exit 呼叫 `desktop.Shutdown()`，繞過未存檔提示 | bug | 確認 | P1 | 已修 | `09394d6` | desktop.Shutdown() 會忽略 e.Cancel；改走 Close()。需實機確認一次。 需實機確認一次。 |
| B2-02 | 開啟存檔、還原備份、Review→Discard 都經由 `LoadSave`，而 `LoadSave` 不檢查未存檔變更 | bug | 確認 | P1 | 已修 | `09394d6` | Open / Restore 前沒有未存檔確認；Review→Discard 是使用者自己選的，不算缺陷。 |
| B2-03 | 非 modal 工具視窗在 Open / Save As / Restore 之後仍持有舊路徑與舊索引：讀舊檔、寫新文件 | bug | 確認 | P2 | 已修 | `30dd0ce` | 工具視窗用 Show(this)，Load / SaveAs 不關閉；與 B5-01、B4-01、B4-10、B5-02 一起用文件世代 token 修。 |
| B2-04 | File → Save 直接綁定 `SaveCommand`：寫檔失敗會讓 process 崩潰並遺失所有編輯 | bug | 確認 | P1 | 已修 | `09394d6` | Save 失敗直接讓 process 崩潰；HMAC_UNVERIFIED（−28）讓這條更容易觸發。 |
| B2-05 | 備份目標固定是 `save.save` / `lobby.save`，不是實際要被覆寫的檔案 | bug | 確認 | P1 | 已修 | `09394d6` | 備份永遠複製 save.save / lobby.save，*.edited.save 從未被備份。 |
| B2-06 | Add Dye 流程修改了存檔卻沒有寫 Journal，關閉時不會出現未存檔提示 | bug | 確認 | P1 | 已修 | `09394d6` | Add Dye 不記 Journal；根因是 B2-14 兩個 dirty 來源。 |
| B2-07 | `CommitFieldEditAsync` / `MakeFieldAbsent`：mutation 成功後的重新讀取不在 try 內 | bug | 確認 | P4 | 已修 | `5127921` | mutation 後的重新讀取在 try 外，且早於 IsDirty / Journal。 |
| B2-08 | 寫檔不是原子操作；備份失敗時仍照樣覆寫 | risk | 部分 | P1 | 已修 | `09394d6` | 上游已改為 temp + fsync + rename（半修）；C# 的「備份失敗仍照寫」與缺 OS 錯誤細節仍在。 |
| B2-09 | 存檔前沒有偵測「載入後檔案已被外部修改」，也沒有偵測遊戲是否在執行；mtime 回填會掩蓋這件事 | risk | 確認 | P1 | 已修 | `09394d6` | 存檔前不檢查外部修改；mtime 回填會掩蓋。 需實機確認一次。 |
| B2-10 | mtime 保留使用本地時間 API；備份時間戳記也用本地時間 | risk | 部分 | P1 | 已修 | `09394d6` | 只有備份資料夾用本地時間命名、可能被錯刪是真的；mtime 來回轉換在同 process 內無損，未重現。 |
| B2-11 | 還原 `Unknown` 平台的備份時，會寫進「最近使用的 launcher」的正式存檔樹 | risk | 確認 | P1 | 已修 | `09394d6` | Unknown 平台還原到 discovered[0]；註解也錯。 |
| B2-12 | 還原不是原子操作，可能讓 `save.save` 與 `lobby.save` 不同步；目前狀態不會先備份 | risk | 確認 | P1 | 已修 | `09394d6` | 還原不是原子操作，殘留舊 lobby.save；不先備份是有寫明的設計，但仍改為先移到 PreRestore。 |
| B2-13 | 還原對話框看不出平台；`ListBackups` 的 I/O 例外會讓 app 崩潰 | risk | 確認 | P4 | 已修 | `09394d6` | 還原清單看不出平台；ListBackups 的 I/O 例外會讓 app 崩潰。另：還原視窗可重複開啟。 |
| B2-14 | 「是否有未存檔變更」有兩個來源（`IsDirty` 與 `Journal`），而工具視窗只在關閉時才同步 | risk | 確認 | P1 | 已修 | `09394d6` | IsDirty 與 Journal 兩個來源；改為由 mutation version 推導。 |
| B2-15 | 通用欄位編輯器的「absent ≠ 0」語意沒有任何防護 | risk | 確認 | P5 | 已修 | `559a80b` | absent 欄位輸入 0 沒有警告；Make absent 沒有確認。 |
| B2-16 | `ScalarFieldEditing.TryEncode` 接受 NaN、Infinity，以及溢位後變成 ∞ 的浮點數 | risk | 確認 | P5 | 已修 | `559a80b` | TryEncode 接受 NaN / ∞ / 溢位成 ∞ 的值。 |
| B2-17 | `AppSettingsStore`：非原子寫入、檔案損毀後靜默重置、`TrySave` 的回傳值全被忽略 | risk | 部分 | P1 | 已修 | `09394d6` | 非原子寫入與損毀時靜默重置是真的；「全部忽略 TrySave」太廣（CustomGemSets 有檢查，WorldMap 也忽略）。 |
| B2-18 | 單一執行個體保護（Rule 2）的邊界情況 | risk | 確認 | P10 | 已修 | `4cbcef7` | Global\ mutex 沒有 try；WinExe 用 Console.Error。多使用者情境需實機。 Local\ mutex、無法開啟視為已執行、第二次啟動寫入 app log。多使用者情境未實機驗證。 |
| B2-19 | async void handler 與 fire-and-forget 的失敗不會被使用者看到 | risk | 確認 | P4 | 已修 | `5127921` | 六個 async void 工具 handler 沒有 try；SafeFireAndForget 只 Trace。 以全域處理器涵蓋。 |
| B2-20 | Rule 3：檔案 I/O 與重 FFI 在 UI 執行緒同步執行 | rule violation | 確認 | P7 | 已修 | `f3f25a8` | LoadSave 同步、備份清單在 ctor、DiscoverSaveRoots 每次全樹掃描。 備份檔複製仍在 UI 執行緒（小檔）。 |
| B2-21 | Rule 6：沒有任何日誌實作（Pre-scan #1 確認） | rule violation | 確認 | P10 | 已修 | `4cbcef7` | 沒有任何日誌實作。 FileAppLog（8 MB × 4 輪替），記錄啟動、存檔、備份、遊戲資料載入失敗與未處理例外。 |
| B2-22 | Rule 7：沒有集中的 magic-string 檔（Pre-scan #4，Block 2 範圍確認） | rule violation | 確認 | P9 | 已修 | `1836867` `4cbcef7` | 與 B1-18、B3-16、B4-26、B5-18、B6-24 合併。 VM 自組 Windows 存檔路徑併入 P10（B2-23）。 P9 集中字串；P10 的 IPlatformPaths.FallbackSaveRoot 收掉 VM 自組的存檔路徑。 |
| B2-23 | Rule 4：平台相關的路徑知識與具體實作洩漏到 ViewModel / composition root 之外 | rule violation | 確認 | P10 | 已修 | `4cbcef7` | VM 自己組 Windows 存檔路徑、直接呼叫 SteamLibraryProbe。 IPlatformPaths.FallbackSaveRoot / LooksLikeGameInstall；VM 不再直接呼叫 SteamLibraryProbe。 |
| B2-24 | Rule 12：absent 欄位的型別是由消費端用 schema TypeName 猜出來的 | rule violation | 部分 | P5 | 待上游 | `559a80b` | TypeName 別名表與 *Key→u32 猜測確實是 Rule 12 workaround；i32/u32 風險不成立，實際代價是未知 typedef 無法編輯。需上游為 absent scalar 提供 type_tag。 需 crimson-rs 回報 absent scalar 的 type_tag。 |
| B2-25 | UI 字串寫死英文，沒有放進三語系資源 | rule violation | 確認 | P8 | 已修 | `6a8c473` | 多處寫死英文。 |
| B2-26 | `MainWindow.axaml.cs`（1,997 行）中有應該放在 ViewModel / Service 的邏輯 | cleanup | 確認 | P14 | 待修 |  | 約 20 個複製貼上的工具啟動器；Add Dye 邏輯在 View。 |
| B2-27 | 過時或錯誤的註解與文件 | cleanup | 確認 | P12 | 部分 | `ff24f9a` | 多處過時註解；孤立 summary 已在 P0 由註解檢查修掉一部分。 P0 修掉孤立 summary 2 處。 |
| B2-28 | `ChangeJournal` 的執行緒安全只是表面上的 | cleanup | 確認 | P6 | 已修 | `8577638` | CollectionChanged 在呼叫端執行緒觸發；目前沒有背景呼叫端。 |
| B2-29 | 平台探測的穩健性 | cleanup | 確認 | P10 | 已修 | `4cbcef7` | IgnoreInaccessible=false，一個無權限子目錄就讓整個掃描回 null。 EnumerationOptions.IgnoreInaccessible = true。 |
| B2-30 | 測試缺口 | cleanup | 確認 | P13 | 待修 |  | 沒有 MainWindowViewModel 生命週期測試。 |

### Block 3

| ID | 標題 | 嚴重度 | 確認 | 批次 | 狀態 | 修正 | 備註 |
|---|---|---|---|---|---|---|---|
| B3-01 | 重新 bootstrap 不會重載 PALOC：新安裝的 bridge 配上舊安裝的語言表 | bug | 確認 | P6 | 已修 | `8577638` | 重新 bootstrap 不清 catalog / PALOC / mission map；DLC 會被重複 merge。 |
| B3-02 | `EnsurePortraitList` race：同時開始的頭像載入，大多數拿到 null 並被永久快取 | bug | 確認 | P6 | 已修 | `8577638` | 頭像清單 race，null 被永久快取。最容易重現。 |
| B3-03 | 圖示 / 頭像快取寫入不是原子操作；寫壞的檔案會被當成「已快取」，永遠不重抽 | bug | 確認 | P6 | 已修 | `8577638` | 圖示 / 頭像快取寫入非原子，壞檔永遠不重抽。 |
| B3-04 | 換掉 icon / portrait provider 後，開著的非 modal picker 仍綁著已 Dispose 的 Bitmap | risk | 部分 | P2 | 已修 | `30dd0ce` | Dispose 沒有 _disposed 旗標；Avalonia 畫已 Dispose 的 Bitmap 的行為需執行時確認。 |
| B3-05 | `LocalizationProvider` 被背景執行緒讀取，同時又在 UI 執行緒上被修改 | risk | 確認 | P6 | 已修 | `8577638` | 背景讀、UI 寫同一批 Dictionary；改 immutable snapshot。 |
| B3-06 | DDS 解碼器的尺寸運算會整數溢位，並且在驗證長度之前就配置記憶體 | risk | 確認 | P6 | 已修 | `8577638` | DDS 尺寸運算溢位、先配置後驗證；只在遊戲檔損毀時觸發。 |
| B3-07 | 快取沒有版本或失效機制：遊戲更新後，圖示與頭像永遠是舊的 | risk | 確認 | P6 | 已修 | `8577638` | 快取沒有版本戳記，遊戲更新後不會重抽。 |
| B3-08 | Character Picker 的 pick mode 寫入 lo24 key，丟掉原值的 cat byte | risk | 待資料 | P5 | 待資料 | `559a80b` | 程式確實寫 lo24；characterinfo 現在有 32-bit hashed key，報告建議的修法會寫壞。需用實際存檔確認。 picker 寫入 characterinfo 列本身的鍵（hashed 列為 32 位元）；60 筆高位元組非 0 的鍵是否帶 cat byte 仍待比對。 |
| B3-09 | 三個複製按鈕的 handler 是沒有 try/catch 的 `async void`，剪貼簿例外會讓 process 崩潰 | risk | 確認 | P4 | 已修 | `5127921` | 另有第四處 FindItemsWindow。 |
| B3-10 | 每個載入過的語言都常駐記憶體，切換次要語言會累積 | risk | 確認 | P6 | 已修 | `8577638` | 必須在 B3-05 之後做，否則 use-after-dispose。 |
| B3-11 | `GameDataLayout.DlcNames` 的檔案系統例外沒有被捕捉，可能讓 app 啟動失敗 | risk | 確認 | P4 | 已修 | `5127921` | DlcNames 的檔案系統例外沒有捕捉，可能讓啟動失敗。 |
| B3-12 | `LevelGimmickSceneObjectInfoKey` 先查 gimmickinfo 表，兩個 namespace 的數值範圍重疊（Rule 12） | risk | 待資料 | P5 | 待資料 |  | 兩個 TypeName 已共用 PALOC byte 0x00，可能本來就是同一個 namespace；需資料比對。 |
| B3-13 | 沒有任何測試斷言「最新存檔的每個 key TypeName 都能解析」 | risk | 確認 | P13 | 待修 |  | 沒有「最新存檔每個 TypeName 都能解析」的測試。 |
| B3-14 | Rule 4：`UiLanguageService` 直接 P/Invoke `kernel32!GetUserDefaultUILanguage` | rule violation | 確認 | P10 | 已修 | `4cbcef7` | kernel32 P/Invoke 在 Ui；fallback 被 InvariantGlobalization 弄壞。 Core 的 IOsUiLanguage（Win32 LCID / POSIX locale 變數），不依賴 CultureInfo。 |
| B3-15 | Rule 3：同步 I/O 與長時間工作跑在 UI 執行緒 | rule violation | 確認 | P7 | 已修 | `f3f25a8` | bootstrap、語言載入、180k 掃描、圖示解碼都在 UI 執行緒。 圖示在 converter 解碼一次後快取，未改。 |
| B3-16 | Rule 7：magic string 散落各處，沒有集中檔案 | rule violation | 確認 | P9 | 已修 | `1836867` | 合併至 magic-strings 批次。 |
| B3-17 | UI 字串寫死在 C#，沒有進三語系 `Strings/*.axaml` | rule violation | 確認 | P8 | 部分 | `6a8c473` | 合併至 UI 字串批次。 ItemPicker 內部分字串未改。 |
| B3-18 | `FormatPair` 對只有 internal name 的 key 顯示「X / X」 | cleanup | 確認 | P5 | 已修 | `559a80b` | 只有 internal name 的 key 顯示「X / X」。 |
| B3-19 | 重複的 helper 與死碼 | cleanup | 確認 | P14 | 待修 |  | LookupUiResourceString 三份、死碼。 |
| B3-20 | XML doc 錯置、孤立，以及過時的註解 | cleanup | 確認 | P12 | 部分 | `ff24f9a` | 孤立 summary 在 P0 已修（4 處）；剩 Avalonia 版本與計數文字。 |
| B3-21 | `LocalizationProvider.cs`（2,569 行）的結構：可以按職責拆分，DLC 表也重複載入 | cleanup | 確認 | P14 | 待修 |  | DLC 表 merge 後又載入成 resolver；拆分 LocalizationProvider。 |
| B3-22 | 所有載入失敗都靜默吞掉，沒有任何診斷資訊；`UiLanguageService` 用了裸 `catch` | cleanup | 確認 | P4 | 已修 | `5127921` | 16 個空的 catch；改收集 LoadFailures。 |

### Block 4

| ID | 標題 | 嚴重度 | 確認 | 批次 | 狀態 | 修正 | 備註 |
|---|---|---|---|---|---|---|---|
| B4-01 | Dye 與 Vendor Buyback 沒有 snapshot 過期檢查；其他視窗做了 length-changing 編輯後，會寫到錯的物品 | bug | 確認 | P2 | 已修 | `30dd0ce` | Dye 完全成立；Buyback 較窄（只在同一商店的 sold list 被動到時）。 |
| B4-02 | 「+ Add dye」流程分兩步寫入、失敗時不回復；找不到 `_dyeSlotNo` 時仍回報成功 | bug | 確認 | P3 | 已修 | `951ac21` | Add dye 兩步寫入、失敗不回復、找不到 _dyeSlotNo 仍回報成功。 |
| B4-03 | `DyeSlotEditorViewModel.AddSlot`：batch 外先 clone，後續失敗時留下一份完整複製的 element 0 | bug | 確認 | P3 | 已修 | `951ac21` | AddSlot 在 batch 外 clone，失敗時留下複本。 |
| B4-04 | `DyeSlotRow` 的原始值在 Apply 後不會更新：改回原值時不會寫入，畫面與存檔不一致 | bug | 確認 | P5 | 已修 | `559a80b` | _orig* 不更新；第二次 Apply 的 present(1) 在新版 crimson-rs 是 no-op（G-03）。 |
| B4-05 | Dye Apply 會把所有 absent 的 scalar 以 0 寫成 present；`_grimeOpacity` absent ≠ 0 | bug | 確認 | P5 | 已修 | `559a80b` | absent channel 被寫成 present 0；grime absent 的意義需存檔掃描確認。 存檔掃描確認 grime 一律 absent、alpha 255。 |
| B4-06 | `DyeSlotRow.Apply` 部分失敗時，錯誤訊息被「Applied: …」蓋掉 | bug | 確認 | P3 | 已修 | `951ac21` | LastError 被「Applied」蓋掉；IsDirty 在 batch 內設定。 |
| B4-07 | `ApplyGemPick` 開啟 socket 後回傳 false：自己的寫入讓之後的編輯全被擋下，也沒有記帳 | bug | 確認 | P3 | 已修 | `951ac21` | 開了 socket 卻回傳 false，之後的編輯全被擋。 |
| B4-08 | Apply-Set 的 commit 失敗會回滾存檔，但 row 狀態與 Journal 仍宣稱已套用 | bug | 確認 | P3 | 已修 | `951ac21` | commit 失敗回滾存檔，但 row 狀態與 Journal 仍宣稱已套用。 |
| B4-09 | Add Item / Grant：clone 與修改欄位不是原子操作，失敗時留下會讓遊戲崩潰的重複物品 | bug | 確認 | P3 | 已修 | `951ac21` | clone / batch / new-mark 三次獨立 commit；上游 G-08 讓 batch 內 clone 後寫入變可行。 |
| B4-10 | Find Items 的「Go」不檢查 snapshot 是否過期，會導航到別的物品 | risk | 確認 | P2 | 已修 | `30dd0ce` | Find Items 的 Go 不檢查 IsStale。 |
| B4-11 | Bulk fill 在「掃描 → 確認對話框 → 套用」之間沒有重新檢查 version | risk | 確認 | P2 | 已修 | `30dd0ce` | Bulk fill 確認對話框前後不檢查 version。 |
| B4-12 | `_itemNo` 唯一性的範圍不一致：Add Item / Grant 只看同一個 list，Contributions 看整個 block | risk | 確認 | P3 | 已修 | `951ac21` | _itemNo 唯一性範圍不一致。 |
| B4-13 | Stack fill 忽略 iteminfo 的 `ApplyMaxStackCap` 旗標，會寫出超過上限的數量 | risk | 部分 | P5 | 已修 | `559a80b` | 只會把已超過上限的 stack 往上取整，不會把未滿的推過上限；仍應尊重 ApplyMaxStackCap。 |
| B4-14 | 選 slot 0 時把 `_dyeSlotNo` 明確寫成 0；遊戲對 slot 0 的寫法是 absent | risk | 待資料 | P5 | 已修 | `559a80b` | 程式確實寫 present 0；遊戲是否接受需掃描存檔。 存檔掃描確認 slot 0 一律 absent。 |
| B4-15 | Vendor Buyback 刪除最後一筆時留下 present 但長度為 0 的 list；Remove 也沒有確認 | risk | 待資料 | P5 | 已修 | `559a80b` | 刪最後一筆留下 present 空 list 是否合法需掃描存檔；沒有確認對話框是真的。 存檔掃描確認從不出現 present 空 list；未加確認對話框。 |
| B4-16 | 自訂寶石組可以存任意 u32；Apply-Set 會把非寶石寫進 socket | risk | 確認 | P5 | 已修 | `559a80b` | 自訂寶石組可存任意 u32，空格會讓後面的寶石左移。 |
| B4-17 | 判斷「寶石」有兩套標準：picker 用 string_key prefix，內建組用 `item_type` / `category_info`（Rule 12） | risk | 確認 | P5 | 部分 | `559a80b` | 兩套寶石判斷標準；目前未觀察到實際分歧。 驗證改用 canonical 集合；picker 仍用前綴。 |
| B4-18 | Socket 的其他非原子寫入序列 | risk | 確認 | P3 | 已修 | `951ac21` | Change 分支兩次獨立 SetScalarField。 |
| B4-19 | Apply-Set 在呼叫 `ApplyGemPick` 前就跳過同一顆寶石，在範圍外的 slot 永遠不會被修復 | risk | 確認 | P3 | 已修 | `951ac21` | Apply-Set 遇同一顆寶石就跳過，範圍外的 slot 永遠不會修復。 |
| B4-20 | `async void` 路徑的例外沒有完整捕捉 | risk | 部分 | P4 | 已修 | `5127921` | AddDye lambda 與 OnEditItemSocketsClick 只捕捉 CrimsonSaveException；InvalidOperation 路徑大多被前置條件擋住。 以全域處理器涵蓋。 |
| B4-21 | 背景執行緒的多步 mutation 可能與 UI 執行緒的 `RunDeferred` 交錯 | risk | 確認 | P3 | 已修 | `951ac21` | Add Item / Grant 在 Task.Run 多步寫入，與 UI 執行緒 RunDeferred 可能交錯。 |
| B4-22 | 測試缺口：deferred 模式的 dye 寫入、Socket 修復路徑、過期檢查與多個工具都沒有測試 | risk | 確認 | P13 | 待修 |  | 多個工具沒有測試。 |
| B4-23 | 掃描時讀取失敗的 block 被靜默跳過，結果不完整也不告知使用者 | risk | 確認 | P4 | 已修 | `5127921` | 掃描讀取失敗的 block 被靜默跳過。 Dye 主清單只避免重複讀取，未顯示數量。 |
| B4-24 | Rule：五個 UI 字串鍵在三個語系字典中都不存在 | rule violation | 確認 | P8 | 已修 | `ff24f9a` | 五個字串鍵三語系都缺；P0 補上並加鍵值測試。 |
| B4-25 | Rule：UI 字串寫死在 C#（含原生例外訊息直接顯示） | rule violation | 確認 | P8 | 部分 | `6a8c473` | 寫死英文與原生例外訊息直接顯示。 新增訊息皆已進字典；工具視窗內既有英文狀態與原生例外訊息未全面改。 |
| B4-26 | Rule 7：magic string 沒有集中；錯誤碼寫成 `-16` 字面值 | rule violation | 確認 | P9 | 已修 | `1836867` | 合併至 magic-strings 批次。 |
| B4-27 | Rule 3：同步檔案 I/O 與耗時的 FFI 跑在 UI 執行緒 | rule violation | 確認 | P7 | 部分 | `f3f25a8` | 設定檔 I/O 與 FFI 在 UI 執行緒。 Socket/Buyback 掃描已移到背景；設定檔 I/O 與 dye slot editor（含 brush）未改。 |
| B4-28 | 文件漂移與錯置的 XML doc | cleanup | 確認 | P12 | 部分 | `ff24f9a` | 孤立 summary 在 P0 已修（SocketEditor）；其餘過時說明。 |
| B4-29 | 重複的 helper 與死碼 | cleanup | 確認 | P14 | 待修 |  | 重複 helper 與死碼。 |
| B4-30 | Add Item / Grant 寫死 scalar 的 byte 寬度，沒有依 type tag 編碼 | cleanup | 確認 | P3 | 已修 | `951ac21` | scalar byte 寬度寫死。 |
| B4-31 | 結構與大小 | cleanup | 確認 | P14 | 待修 |  | 純結構。 |
| B4-32 | 其他 UX / 正確性小問題 | cleanup | 確認 | P5 | 已修 | `559a80b` | 500 筆上限在排序前截斷等小問題。 |

### Block 5

| ID | 標題 | 嚴重度 | 確認 | 批次 | 狀態 | 修正 | 備註 |
|---|---|---|---|---|---|---|---|
| B5-01 | 非 modal 的 Block 5 工具視窗在開另一個存檔後，仍用舊索引寫進新文件 | bug | 確認 | P2 | 已修 | `30dd0ce` | 與 B2-03 同修。Faction Relations / Knowledge / Mount 在套用時依 key 重新解析，只會顯示舊資料。 |
| B5-02 | 同一份文件內也會過期：Sealed Abyss 重複套用、Contributions 寫到隔壁物品 | bug | 確認 | P2 | 已修 | `30dd0ce` | 另：SA 對話框 applied==0 時不重掃，重試會重複 clone FAR。 |
| B5-03 | 多步驟寫入失敗時留下半套結果，而且成功筆數為 0 時不設 dirty、不記 Journal | bug | 確認 | P3 | 已修 | `951ac21` | 單筆 Mark 失敗不設 dirty；bulk / knowledge 在 RunDeferred 內 return 會 commit 半套。 |
| B5-04 | Abyss Gates 的 Lock / Unlock 在狀態改變後不會重新評估 CanExecute | bug | 確認 | P5 | 已修 | `559a80b` | Lock / Unlock 不重新評估 CanExecute。 |
| B5-05 | Faction Nodes 的 `_isCapital` 是 bool，卻用只接受整數的 parser，★ 永遠不會顯示 | bug | 確認 | P5 | 已修 | `559a80b` | _isCapital 是 bool，61 個節點為 true，★ 永遠不顯示（存檔探測確認）。 |
| B5-06 | clone 後的 in-place patch 只檢查欄位「在 schema 裡」，沒檢查「present」 | risk | 部分 | P3 | 已修 | `951ac21` | 5 份存檔中模板欄位都是 present；只缺 presence 檢查。 |
| B5-07 | Knowledge 注入以 element 0 為模板，未覆寫的欄位（如 `_level`）全部沿用 | risk | 部分 | P5 | 待實機 | `559a80b` | _level 是 TLevel u32（不是 u8），_list[0] 永遠是 key 1 level 2；遊戲自己的寫法是 level 1、時間與新標記 absent。注入配方已在實機確認可用，需實機 A/B 才能改。 |
| B5-08 | 非 `CrimsonSaveException` 的例外會讓 process 崩潰 | risk | 確認 | P4 | 已修 | `5127921` | 沒有全域例外處理。 |
| B5-09 | Faction Relations 的 Apply 會默默改寫所有「兩側不一致」的 faction | risk | 確認 | P5 | 已修 | `559a80b` | Apply 會默默改寫所有兩側不一致的 faction。 |
| B5-10 | Sealed Abyss 對話框會套用被篩選隱藏、但仍勾選的列 | risk | 確認 | P5 | 已修 | `30dd0ce` | 套用被篩選隱藏但仍勾選的列。 P2 一併修正。 |
| B5-11 | Mark Challenge 的警告文字仍宣稱「至少持有一件 SA 遺物才會啟用」，但這道檢查已經移除 | risk | 確認 | P8 | 已修 | `6a8c473` | 三語系警告文字仍寫「需持有遺物」。 |
| B5-12 | Rename Mercenary：清空名稱寫成「present + 長度 0」，長度也沒有上限 | risk | 確認 | P5 | 已修 | `559a80b` | _mercenaryName 98 筆中 95 筆 absent；清空寫成 present 長度 0、沒有長度上限。 |
| B5-13 | Mount 對話框允許多列同時解鎖，工具視窗之間的 mutation 也沒有序列化 | risk | 確認 | P3 | 已修 | `951ac21` | IsBusy 只到列；兩次快速點擊可能算出相同 _itemNo。 |
| B5-14 | 尚未實機驗證的寫入與啟發式判斷 | risk | 待資料 | P5 | 待實機 | `559a80b` | _relationGroupKey 寬度目前正確（146 筆 u16）；群組態度、Abyss 欄位、bulk Pattern B 仍待實機。 |
| B5-15 | 掃描時吞掉讀取錯誤，可能讓 `_completedTime` watermark 偏低，也讓清單少項目 | risk | 確認 | P4 | 已修 | `5127921` | 掃描吞讀取錯誤，_completedTime watermark 可能偏低。 SA 預覽清單未顯示略過數量。 |
| B5-16 | 測試缺口 | risk | 確認 | P13 | 待修 |  | 多個 world 工具沒有測試。 |
| B5-17 | Rule 3：UI 執行緒上的同步 FFI 與檔案 I/O | rule violation | 部分 | P7 | 已修 | `f3f25a8` | LoadBlockDetails 有快取；同步工作在 Rename、WorldMap 點陣圖、KnowledgeEditor。 |
| B5-18 | Rule 7：magic strings 分散且重複（Pre-scan #4 在 Block 5 範圍確認） | rule violation | 確認 | P9 | 已修 | `1836867` | 合併至 magic-strings 批次。 |
| B5-19 | UI 字串寫死英文；三語系字典的鍵一致，但用詞不一致 | rule violation | 確認 | P8 | 部分 | `6a8c473` | 另 zh-TW 混用阿比斯之門 / 深淵之門。 狀態標籤與 zh-TW 用詞已改；skip reason 仍為英文診斷字串。 |
| B5-20 | Rule 12：消費端的 workaround | rule violation | 確認 | P5 | 已修 | `559a80b` | WorldMap 以 " / " 切名稱；TryParseScalarUInt 複製三份。 |
| B5-21 | 過時或錯置的註解與文件 | cleanup | 確認 | P12 | 部分 | `ff24f9a` | 孤立 summary 在 P0 已修（SA bulk）；其餘過時說明。 |
| B5-22 | Skip reason 用英文子字串分類 | cleanup | 確認 | P8 | 已修 | `6a8c473` | 以英文子字串分類 skip reason。 |
| B5-23 | 結構：Sealed Abyss 佔 `MainWindowViewModel.cs` 約 1,300 行（Pre-scan #5 確認） | cleanup | 確認 | P14 | 待修 |  | 純結構。 |
| B5-24 | 零碎項目 | cleanup | 部分 | P4 | 已修 | `5127921` | return default 那處不可達；FAR 重讀與 catch-all 屬實。 FAR 重讀保留（B1-01 已修後無害）。 |

### Block 6

| ID | 標題 | 嚴重度 | 確認 | 批次 | 狀態 | 修正 | 備註 |
|---|---|---|---|---|---|---|---|
| B6-01 | `dump_challenges.py` 只把 missioninfo body 餵給 C ABI；2.06 上 0 筆輸出且 exit 0 | bug | 部分 | P11 | 已修 | `354681e` | 升 vendor 後從靜默變成 exit 1；仍需傳入 header。 傳入 missioninfo header（2.06 上 6,872 列 / 2,260 挑戰），0 列 exit 1。仍經 ctypes，待 B6-25。 |
| B6-02 | `dump_catalogs.py` 在 2.06 上輸出空的 `string_key`；`extract_keycases.py` 把 `""` 當成已解析 | bug | 確認 | P11 | 已修 | `354681e` | 仍缺 header；"" 被當成已解析。 dump_catalogs 傳 iteminfo header（7,068/7,068 有 string_key）；extract_keycases 把 "" 視為未解析。 |
| B6-03 | Debug 模式建出 Rust debug DLL，但 C# 專案永遠複製 `target\release` | bug | 確認 | P11 | 已修 | `354681e` | Debug 建 Rust debug，C# 永遠複製 release。 Rust 一律 release；C# build 在 DLL 缺少或與 ref 不符時發 warning（Directory.Build.targets）。 |
| B6-04 | CI 的 vendor 步驟：`git checkout $CRIMSON_RS_REF` 失敗會被下一行的成功蓋掉 | bug | 確認 | P11 | 已修 | `354681e` | 潛在問題（目前 ref 是 main）。 scripts/checkout_crimson_rs.ps1：每個 git 步驟檢查 exit code，最後比對 HEAD 與 ref。 |
| B6-05 | Single-file 回歸檢查只發 warning；`release.yml` 卻說會 throw，且 dumpbin 檢查在 CI 不會執行 | bug | 確認 | P11 | 已修 | `354681e` | 回歸檢查只發 warning。 三種回歸都 throw；dumpbin 經 vswhere 找到，CI 也會跑 import 檢查。 |
| B6-06 | Release 不可重現：crimson-rs 跟 `main`、cargo 沒有 `--locked`、SHA 沒有寫進產出物 | risk | 確認 | P11 | 已修 | `354681e` | 需決定 crimson-rs 釘版策略。 使用者決定：釘 vendored SHA。 vendor/crimson-rs.ref（update_vendors 寫入、CI checkout）、cargo --locked、SHA 寫進 InformationalVersion 與 release notes。 |
| B6-07 | CI 的 Rust toolchain 沒有釘版本（`dtolnay/rust-toolchain@stable`） | risk | 確認 | P11 | 已修 | `354681e` | 需決定 Rust 版本（可能需上游加 rust-toolchain.toml）。 使用者決定：釘 Rust 版本。 RUST_TOOLCHAIN 1.100.1，dtolnay/rust-toolchain 以 SHA 釘。 |
| B6-08 | CI 的 .NET SDK（`10.0.x`）不讀 `global.json`；ILCompiler pin 會隨 runner 更新觸發 NU1109 | risk | 確認 | P11 | 已修 | `354681e` | 需決定 SDK 釘版。 使用者決定：SDK 由 global.json 決定。 setup-dotnet global-json-file；global.json 10.0.404 rollForward disable。 |
| B6-09 | 供應鏈：actions 用可變 tag/branch 引用，且擁有 `contents: write`；NuGet / pip 沒有 lock | risk | 確認 | P11 | 已修 | `354681e` | 需決定：actions 以 SHA 釘版、權限縮小、lock file。 使用者決定：actions 以 SHA 釘、預設唯讀權限、lock file。 actions 以 SHA 釘、預設 contents: read（release job 才有 write）、NuGet lock file + locked mode、pip constraints。 |
| B6-10 | 沒有 PR CI；release workflow 也不跑測試（Pre-scan #6 確認） | risk | 確認 | P11 | 已修 | `354681e` | 需決定：是否新增 PR CI。 使用者決定：新增 PR CI。 新增 ci.yml（C# 測試 + tools 測試）；release build job 也跑測試。首次 GitHub 執行待確認。 |
| B6-11 | `update_vendors.ps1` 的 dirty 檢查只看 working tree；vendor clone 內的本地 commit 會被 `reset --hard` 靜默丟掉 | risk | 確認 | P11 | 已修 | `354681e` | vendor clone 內的本地 commit 會被 reset --hard 丟掉。 reset 前檢查 origin..HEAD；-Force 加 git clean -fd，說明已更正。 |
| B6-12 | Vendor 更新後不重建也不提示：`.pyd` 與 `crimson_rs.dll`/`.lib` 會停在舊版 | risk | 確認 | P11 | 已修 | `354681e` | vendor 更新後不提示重建。 HEAD 變動時印出重建步驟；build_rust 寫 .crimson-rs-sha，C# build 比對 ref。 |
| B6-13 | `gamedata_layout._read_pamt` 吞掉所有例外，錯誤訊息誤導成「遊戲沒安裝？」 | risk | 確認 | P11 | 已修 | `354681e` | 上游同一支腳本也有相同問題。 只接 OSError/ValueError，改拋 LookupError 並保留原因。 |
| B6-14 | Build 腳本宣稱支援 `linux-x64` / `osx-arm64`，實際流程只支援 Windows | risk | 確認 | P11 | 已修 | `354681e` | 宣稱支援 linux / osx，實際只有 Windows。 ValidateSet 只留 win-x64；build_rust 非 Windows 直接 throw；NativeLibrary 加 win RID 條件。 |
| B6-15 | `build_number.txt`：每次本機 build 都 +1 並污染 working tree；CI 的 tag pin 對實際 tag 格式永遠不會觸發 | risk | 部分 | P11 | 已修 | `354681e` | 行為有寫明；但 v20260531 tag 會讓 VerBuild 超過 65535。需決定 build number 策略。 使用者決定：成功建置後才 +1，CI 擋超過 65535 的 tag。 成功後才 +1（-p:VerBuild）；csproj 與 CI 擋 >65535；CI 檢查 vX.YY.ZZ 與 csproj 一致。 |
| B6-16 | `build_rust.ps1` 不控制 `CARGO_TARGET_DIR`；`#requires -Version 7` 允許 stderr 語意不同的 7.0/7.1 | risk | 確認 | P11 | 已修 | `354681e` | CARGO_TARGET_DIR 不受控；#requires 7 應為 7.2。 CARGO_TARGET_DIR 呼叫期間固定為 vendor target；#requires 7.2。 |
| B6-17 | Build / test 會帶入遙測套件，CI 沒有設定 opt-out（需確認） | risk | 確認 | P11 | 已修 | `354681e` | 需決定遙測 opt-out。 使用者決定：CI 設遙測 opt-out。 CI 設 DOTNET_CLI / TESTINGPLATFORM / AVALONIA 遙測 opt-out。Avalonia.BuildServices 保留。 |
| B6-18 | 從遊戲擷取的資產 `sample_bc3_32x32.dds` 被 commit 進 repo | risk | 確認 | P11 | 已修 | `354681e` | 需決定：改合成 fixture 或在 data-policy 加例外。 使用者決定：改合成 fixture。 改用測試內產生的 BC3 輸入 + 逐像素 BC3 測試；data-policy 加「fixture 一律合成」。 |
| B6-19 | `.gitignore` 的 `!tests/fixtures/**/save.save` 指向不存在的目錄，且與「存檔不進公開 repo」相衝 | risk | 確認 | P11 | 已修 | `354681e` | .gitignore 指向不存在目錄。 刪除 negation，改為全域 *.save。 |
| B6-20 | Rule 9：`dump_challenges.py` 在 import 時重設 stdout、以寫死的絕對路徑載入 DLL | rule violation | 確認 | P11 | 已修 | `354681e` | import 時重設 stdout、寫死 DLL 路徑。 stdout 重設與 DLL 載入移入 main()；DLL 路徑來自 common/crimson_abi.py 或 --dll。 |
| B6-21 | Rule 9：`tools/inspect/sa-investigations/` 的 8 支腳本都沒有 usage，無參數就直接執行 | rule violation | 確認 | P11 | 已修 | `354681e` | 需決定：刪除或補 usage。 使用者決定：刪除。 資料夾刪除；發現寫入 P11 dev-log 條目。 |
| B6-22 | Rule 9（跨平台）：`extract_save.py` 建 parser 時就呼叫 `save_root()`，非 Windows 上 `--help` 也會 crash | rule violation | 確認 | P11 | 已修 | `354681e` | 已重現：LOCALAPPDATA 未設時 --help 崩潰。 help 文字用 SAVE_ROOT_DISPLAY，不在建 parser 時呼叫 save_root()。 |
| B6-23 | Rule 10：同一張圖的衍生格式與複本被 commit | rule violation | 確認 | P11 | 已修 | `354681e` | 需決定：重複圖檔保留一份。 使用者決定：只留一份。 刪除 img/Logo.jpg、img/MainIcon.ico（與 Assets 同 blob）；img/README.md + data-policy 例外。 |
| B6-24 | Rule 7：路徑與常數散落各處，沒有集中定義 | rule violation | 確認 | P9 | 已修 | `1836867` `354681e` | DLL 路徑重複七處；與 magic-strings 批次合併。 MSBuild 已統一；PowerShell 腳本與 Python 工具的路徑在 P11。 P9 MSBuild；P11 scripts/paths.ps1 與 tools/common/crimson_abi.py。 |
| B6-25 | tools 規則 6：在 Python 裡做 byte 層級解析，並繞過 PyO3 直接用 ctypes 呼叫 C ABI | rule violation | 確認 | P11 | 待修 |  | 需上游提供 missioninfo PyO3 binding。 需請上游提供 missioninfo PyO3 binding。 |
| B6-26 | `docs/architecture.md` 與現況大幅脫節（Pre-scan #2 確認並補充） | cleanup | 確認 | P12 | 待修 |  | architecture.md 大幅脫節。 |
| B6-27 | `README.md` / `scripts/README.md` / root `CLAUDE.md` 的建置說明與實際不符 | cleanup | 確認 | P12 | 待修 |  | README / scripts/README 建置說明不符。 |
| B6-28 | `tools/` 各 README 的清單與實際檔案不一致 | cleanup | 確認 | P12 | 待修 |  | tools README 清單不符。 |
| B6-29 | 其他文件中的過時事實（`target-py`、DataGrid、game 1.10） | cleanup | 確認 | P12 | 待修 |  | status.md 等過時事實。 |
| B6-30 | `Directory.Packages.props` 註解錯位；測試專案把一長串 transitive 套件提升成直接引用 | cleanup | 確認 | P11 | 已修 | `354681e` | 需決定：精簡測試專案的 transitive 引用。 使用者決定：精簡。 測試專案只留 xunit.v3 / Test.Sdk / runner；註解移到正確位置；ILCompiler 註解去歷史。 |
| B6-31 | `<NoWarn>IL2104;IL3053</NoWarn>` 的理由已過時，是否仍需要沒有驗證 | cleanup | 部分 | P12 | 待修 |  | NoWarn 在 2026-08-18 已重新驗證仍需要；只有註解過時。 |
| B6-32 | `ClaudeMdSizeTests` 先走完整棵樹才過濾，且沒有跳過 `.venv` | cleanup | 確認 | P13 | 待修 |  | ClaudeMdSizeTests 先走完整棵樹。 |
| B6-33 | Build 腳本的小問題 | cleanup | 確認 | P11 | 已修 | `354681e` | build.cmd 退回 PS 5.1 等小問題。 build.cmd 無 pwsh 直接報錯；刪未用變數；Clean 失敗發 warning；Invoke-Step 先歸零。runtime 參數不加（只有 win-x64）。 |
| B6-34 | `.gitignore` 的矛盾與冗餘規則 | cleanup | 確認 | P11 | 已修 | `354681e` | .gitignore 重複規則。 刪重複規則、.vscode 策略統一、/Backup*/ 只作用於根目錄。 |
| B6-35 | `tools/` 衛生問題：沒有測試、錯誤路徑 exit 0、handle 洩漏、`sys.path` 指錯位置 | cleanup | 部分 | P11 | 已修 | `354681e` | tools 沒有測試、錯誤路徑 exit 0；paloc 失敗路徑其實有釋放 handle。 tools/tests（60）、exit code、handle try/finally、setup_python_env 檢查 3.12+。ruff 未進 CI（既有 finding）。 |
| B6-36 | `release.yml` 的過時註解與 `workflow_dispatch` 的邊界情況 | cleanup | 確認 | P11 | 已修 | `354681e` | 過時註解；workflow_dispatch 在 tag ref 上會走 release 步驟。 註解改寫；release 步驟條件加 github.event_name == 'push'。 |

### 其它 issue（確認過程中發現）

| ID | 標題 | 嚴重度 | 確認 | 批次 | 狀態 | 修正 | 備註 |
|---|---|---|---|---|---|---|---|
| X-01 | crimson-rs −28 HMAC_UNVERIFIED：載入時 HMAC 失敗的存檔，Save 會被拒絕 | bug | 確認 | P0 | 已修 | `ff24f9a` | 來自 crimson-rs ebf7304；C# 不檢查 HmacOk，搭配 B2-04 會崩潰。 |
| X-02 | NativeItemInfoCatalog 把 _with_header 的 BODY_PARSE 退回 body-only 載入（Rule 12） | risk | 確認 | P4 | 已修 | `5127921` | 上游 header 檢查變嚴，退回後物品全部沒有名稱且不報錯。 |
| X-03 | status.md 的 gotcha「promote then write in place」與 set_object_list_present(1) 語意已過時 | cleanup | 確認 | P0 | 已修 | `ff24f9a` | 上游 G-08 / G-03 改變行為。 |
| X-04 | ResolveDyeSlotCount 的意義改為 dye-slot bit 數；LookupSlotCount 是 sub-mesh 數 | cleanup | 確認 | P12 | 待修 |  | 目前沒有 UI 呼叫 ResolveDyeSlotCount；更新 C# 文件。 |
| X-05 | Transplant 在同一個 handle 內已允許，C# 端的禁止可放寬 | cleanup | 確認 | P3 | 已修 | `951ac21` | 上游 G-15。 |
| X-06 | FindItemsWindow 使用 ThemeAccentBrush2 / ThemeBorderLowBrush / ThemeForegroundBrush，Fluent 主題沒有定義 | bug | 確認 | P8 | 已修 | `6a8c473` | 字串鍵比對時發現；需確認畫面上是否失色。 |
| X-07 | 還原備份視窗是非 modal、fire-and-forget，可重複開啟並同時還原 | bug | 確認 | P1 | 已修 | `09394d6` | Block 2 確認時發現。 |
| X-08 | 註解規則（docs/code-comments.md）與 CommentRulesTests：行號、稽核/PR/commit id、重複 summary 歸零；歷史語句 ratchet | rule | 確認 | P0/P12 | 部分 | `ff24f9a` | 使用者要求，參考 UE5CEDumper。 P0：行號/ID/重複 summary 歸零，歷史語句 ratchet 基準 43 行（22 檔）。 |

## 統計（確認後）

- Findings：168 筆（risk 75, cleanup 35, bug 32, rule violation 26），另有其它 issue 8 筆。
- 確認結果：確認 146, 部分 16, 待資料 5, 上游已修 1。
- 待使用者決定：12 筆（皆在 P11）。

