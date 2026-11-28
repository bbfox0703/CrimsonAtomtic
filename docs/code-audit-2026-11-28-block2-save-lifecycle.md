# Code audit — Block 2: Save document lifecycle & data safety (2026-11-28)

> **Status: REPORT ONLY.** 依 [code-audit-2026-11-28-plan.md](code-audit-2026-11-28-plan.md)
> 的 Block 2 範圍做靜態審查；本回合 **未修改任何程式碼**，修法只列建議，實作留給後續 fix PR。
>
> - Snapshot：`dev` @ `9ba7ae4`（PR #64）。所有 `file:line` 以此 commit 為準；沒有前綴的
>   `src/...` 路徑都相對於 repo 根目錄。
> - Rust 端對照（唯讀）：`D:\Github\crimson-rs\src\c_abi\mod.rs`。只用來確認
>   `crimson_save_write_to_file` 的寫檔方式。
> - Avalonia 行為對照（唯讀）：`%USERPROFILE%\.nuget\packages\avalonia\12.2.0\lib\net10.0\Avalonia.Controls.xml`
>   （`Directory.Packages.props:8` 釘選 Avalonia 12.2.0）。
> - 範圍：計畫書 Block 2 的全部 Files，加上 `MainWindowViewModel.cs` 指定的行號範圍
>   （18–520、710–920、1033–1290、1378–1836、3234–3324、3489–3603、5354–5926）。

## 0. 範圍說明與限制

1. **測試基線未執行**：Execution plan 第 4 點要求跑 `dotnet run --project src/CrimsonAtomtic.Tests`
   並記錄測試數量。本 session 的權限設定擋下了這個指令（需要人工核准，Block 1 也遇到同樣情況），
   所以 **數量沒有記錄**。開始修 Block 2 之前請補跑一次。
2. **範圍外的程式碼只當證據引用**：部分 finding 的觸發點落在 Block 4 / 5 的檔案
   （`DyeEditorViewModel`、`DyeSlotEditorViewModel`、`SocketEditorViewModel`、`VendorBuybackViewModel`…），
   或落在 `MainWindowViewModel.cs` 中分配給其他 block 的行號範圍。這些位置只唯讀引用，用來證明
   Block 2 的生命週期問題確實會被觸發；呼叫端本身的審查見 §5 交接清單。
3. **`MainWindow.axaml.cs` 審查了全部 1,997 行**。它屬於 Block 2，但裡面有大量 Tools 對話框的
   啟動程式碼；這些程式碼對存檔生命週期的影響（dirty 傳遞、stale path）在本報告處理，各對話框
   的業務邏輯交給 Block 4 / 5。
4. **Avalonia `Shutdown` 語意**（B2-01）：判斷依據是 API 文件契約（`Shutdown` 與可取消的
   `TryShutdown` 是兩條不同路徑）。這次沒有反組譯或實機驗證，所以修正時請一併做一次實機確認。

## 1. 摘要

| 嚴重度 | 數量 | 編號 |
|---|---|---|
| bug | 7 | B2-01 – B2-07 |
| risk | 12 | B2-08 – B2-19 |
| rule violation | 6 | B2-20（Rule 3）、B2-21（Rule 6）、B2-22（Rule 7）、B2-23（Rule 4）、B2-24（Rule 12）、B2-25（UI 字串） |
| cleanup | 5 | B2-26 – B2-30 |

**最嚴重的三項**都會讓使用者 **在沒有任何提示的情況下遺失未存檔的編輯，或把編輯寫進錯誤的文件**：

- B2-01：File → Exit 繞過未存檔提示。
- B2-02：開啟另一個存檔或還原備份時，直接丟掉目前的編輯。
- B2-03：Open / Save As / Restore 之後，還開著的工具視窗會讀舊檔、寫新文件。

**建議的修正 PR 順序**：
1. **未存檔資料保護**（B2-01、B2-02、B2-06、B2-14）：統一 dirty 的來源，讓 Exit / Open / Restore /
   Discard 都經過同一個「有未存檔變更？」閘門。改動集中在 `MainWindow.axaml.cs` 與
   `MainWindowViewModel`。
2. **文件身分**（B2-03）：Load / Save As / Restore 時關閉或失效所有子視窗，或者讓 loader 在路徑
   不符時直接丟錯，而不是靜默開一個暫時 handle。
3. **寫入安全**（B2-04、B2-07、B2-08、B2-09、B2-05）：Save 指令的例外處理、temp-file + replace、
   備份失敗時的確認、外部修改偵測、正確的備份目標。
4. **還原**（B2-11、B2-12、B2-13）與 **設定持久化**（B2-17）。
5. Rule 3 / 6 / 7 / 4 / 12、UI 字串與 cleanup。

---

## 2. Findings

### B2-01 — File → Exit 呼叫 `desktop.Shutdown()`，繞過未存檔提示
- **嚴重度**：bug（資料遺失）
- **位置**：`src/CrimsonAtomtic.Ui/Views/MainWindow.axaml.cs:1989-1996`（`OnExitClick`）、`:107-174`（`OnWindowClosing`）
- **證據**：
  - 未存檔提示寫在 `Window.Closing` 裡：先設 `e.Cancel = true`，再 `await` 對話框，最後由對話框結果決定是否
    重新呼叫 `Close()`（`:116-117`）。這個流程能成立，前提是 **關閉可以被取消**。
  - `OnExitClick` 直接呼叫 `IClassicDesktopStyleApplicationLifetime.Shutdown()`。Avalonia 12.2.0 的 API 文件把
    兩條路徑分得很清楚：
    - `Shutdown`：「Shuts down the application…」（`Avalonia.Controls.xml:195-200`）。
    - `TryShutdown`：「Tries to Shutdown the application. `ShutdownRequested` event can be used to cancel the
      shutdown.」（`:131-136`）。
  - `Shutdown` 是強制路徑。它會對視窗發出 `Closing`，所以 handler 會設 `e.Cancel` 並開始 `await`，但
    lifetime 不理會取消，照樣結束 dispatcher loop。等不到對話框結果，process 就結束了。
- **重現**：載入存檔 → 改一個欄位（標題出現 `*`）→ File → Exit。預期跳出 ChangeSummary「Save before exiting?」，
  實際上 app 直接關閉，編輯全部遺失。
- **建議修法**：`OnExitClick` 改成呼叫 `Close()`（主視窗是 MainWindow，`ShutdownMode` 預設會在主視窗關閉時
  結束 app），或改用 `desktop.TryShutdown()`。修正後加一個 headless 測試或實機確認。

### B2-02 — 開啟存檔、還原備份、Review→Discard 都經由 `LoadSave`，而 `LoadSave` 不檢查未存檔變更
- **嚴重度**：bug（資料遺失）
- **位置**：`src/CrimsonAtomtic.Ui/ViewModels/MainWindowViewModel.cs:1377-1396`（`LoadSave`）；
  呼叫端 `MainWindow.axaml.cs:746-800`（Open）、`MainWindowViewModel.cs:5634`（Restore 後重新載入）
- **證據**：
  - `LoadSave` 一進來就 `Summary = loader.Load(path)`，接著 `IsDirty = false; Journal.Clear();`（`:1384-1388`）。
    `Load` 會換掉 native handle（`NativeSaveLoader.cs:97-110`），所以記憶體中的編輯會被直接丟棄。
  - Open 的流程（`MainWindow.axaml.cs:746-800`）沒有檢查 `Journal.HasUnsavedChanges` 或 `IsDirty`。
  - `RestoreFromBackupAsync` 的確認訊息（`MainWindowViewModel.cs:5597-5605`）只說「overwrites the current save
    in that slot」，沒有提到「編輯器中未存檔的修改也會被丟掉」。即使還原的是另一個 slot，`LoadSave(targetSavePath)`
    一樣會丟棄目前文件的編輯。
- **重現**：載入 A 並做幾個編輯 → File → Open Save 選 B → A 的編輯消失，沒有任何提示。
- **建議修法**：抽出 `Task<bool> ConfirmDiscardOrSaveAsync()`（同一個 ChangeSummary 對話框，Save / Discard / Cancel），
  在 Open、Restore、Exit（B2-01）、Review→Discard 之前都先呼叫。Restore 的確認文字補上「未存檔的編輯會被捨棄」。

### B2-03 — 非 modal 工具視窗在 Open / Save As / Restore 之後仍持有舊路徑與舊索引：讀舊檔、寫新文件
- **嚴重度**：bug（跨文件寫入，可能造成存檔損毀）
- **位置**：
  - 子視窗以 `Show(this)` 開啟（非 modal）：`MainWindow.axaml.cs:1068, 1148, 1248, 1338, 1360, 1381, 1399, 1432, 1457, 1481, 1506, 1544, 1619, 1888, 1957` 等。
  - 子 VM 在建構時保存路徑：例如 `DyeSlotEditorViewModel.cs:263, 280`、`SocketEditorViewModel.cs:660`
    （`_loader.LoadBlockDetails(_savePath, …)`）。
  - Loader 的路徑處理：`src/CrimsonAtomtic.RustInterop/NativeSaveLoader.cs:131-155`。
- **證據**：
  - `LoadBlockDetails(savePath, …)` 在 `savePath` 與目前快取的路徑不符時，**不會丟錯**，而是靜默地從磁碟開一個
    暫時 handle 讀舊檔（`:148-154`，註解：「Open transiently and tear down at scope end」）。
  - mutation API 不帶路徑（例如 `SetScalarField(blockIndex, path, field, bytes)`，`DyeSlotEditorViewModel.cs:469-476`），
    永遠寫到 **目前** 快取的 handle。
  - `LoadSave` 與 `SaveAsAsync` 都不會關閉子視窗，也不會通知子視窗失效（`MainWindowViewModel.cs:1377-1396, 5489-5523`）。
- **重現**：
  1. 開啟 A → Tools → Edit Item Sockets（或 Dye / Vendor Buyback / Rename Mercenary），讓視窗保持開啟。
  2. 在主視窗 Open Save 選 B（或 Save As 到別的路徑）。
  3. 在工具視窗按 Apply。讀取來自 **A 的磁碟檔**，block index 與 PathStep 都是依 A 算出來的；寫入卻打進
     **B 的記憶體文件**。B 不同 block / 元素的位元組被改寫，Save 後就落地。
  4. Save As 的情況更隱蔽：之後的讀取回到 **原始未編輯的舊檔**，因此在結構性編輯之後，索引全部錯位。
- **建議修法**（擇一或並用）：
  - (a) Load / Save As / Restore 前先關閉所有帶 loader 的子視窗（或要求使用者先關閉）。
  - (b) 加一個文件世代號：`MainWindowViewModel` 每次 Load 遞增 `DocumentGeneration`，子 VM 建構時記下，
    每次讀寫前比對，不符就停用。
  - (c) Block 1 契約層：`LoadBlockDetails` 在路徑與目前 handle 不符時改為丟 `InvalidOperationException`，
    不再開暫時 handle；需要讀任意檔的呼叫端改用明確的 API。

### B2-04 — File → Save 直接綁定 `SaveCommand`：寫檔失敗會讓 process 崩潰並遺失所有編輯
- **嚴重度**：bug（崩潰後資料遺失）
- **位置**：`src/CrimsonAtomtic.Ui/Views/MainWindow.axaml:53-54`；`MainWindowViewModel.cs:5406-5427`（`SaveAsync`）；
  對照 `MainWindow.axaml.cs:126-152, 196-213`（有保護的路徑）
- **證據**：
  - `SaveAsync` 沒有 try/catch：`loader.WriteToFile` 失敗時會丟 `CrimsonSaveException`，例如 `WRITE_FAILED`
    （檔案被遊戲或 Steam Cloud 鎖住、磁碟滿、唯讀）、`BATCH_IN_PROGRESS`（工具視窗的 `RunDeferred` 還在背景執行，
    見 `crimson-rs mod.rs:4177-4183`）。
  - Menu 的 `Command="{Binding SaveCommand}"` 走 `AsyncRelayCommand.Execute`。專案沒有設定
    `AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler`，也沒有任何
    `Dispatcher.UIThread.UnhandledException` / `AppDomain.UnhandledException` handler（grep 全 `src/`：無命中）。
    例外會回拋到 dispatcher，導致 process 崩潰。
  - `MainWindow.axaml.cs:126-136` 的註解已經指出這個機制（「rethrown on the dispatcher and crashing the AOT
    process」），所以 closing / review 路徑改用 `ExecuteAsync` 加 catch。但 **最常用的 File → Save 選單沒有比照**。
  - 附帶問題：Save As 寫檔成功、但 re-anchor 的 `loader.Load(destinationPath)`（`:5513`）失敗時，`OnSaveAsClick`
    顯示的是「Save failed」（`MainWindow.axaml.cs:854-860`）。實際上檔案已經寫入，`_loadedPath` 仍指向舊路徑，
    `IsDirty` 仍是 true，訊息有誤導性。
- **建議修法**：在 `SaveAsync` / `SaveAsAsync` 內部捕捉 `CrimsonSaveException` 與 `IOException`，透過
  `AlertRequested` 回報，保持 dirty。另外，讓所有 `[RelayCommand]` async 指令都有保底：在 App 註冊
  `Dispatcher.UIThread.UnhandledException`，先寫 log（見 B2-21）並提示使用者，再決定是否 `Handled = true`。
  Save As 拆成「寫入失敗」與「寫入成功但重新載入驗證失敗」兩種訊息。

### B2-05 — 備份目標固定是 `save.save` / `lobby.save`，不是實際要被覆寫的檔案
- **嚴重度**：bug（備份不完整，而且會擠掉有效的備份）
- **位置**：`src/CrimsonAtomtic.Ui/Services/SaveBackupService.cs:120-211`（特別是 `:170-181`）、`:393-432`；
  `MainWindow.axaml.cs:822-824`（Save As 預設檔名）；`MainWindowViewModel.cs:5503`
- **證據**：
  - `BackupBeforeWrite(targetSavePath)` 只用 `targetSavePath` 判斷「檔案存在」與「推導 userId / slot」，
    實際複製的卻是同資料夾的 `BackupFileNames = ["save.save", "lobby.save"]`（`:79, 170-181`）。
  - `TryDeriveSlotCoordinates` 刻意接受任何檔名（`:404-407`：「Save As might write to a copy with a different name」）。
  - Save As 的預設檔名是 `<原名>.edited.save`（`MainWindow.axaml.cs:822-824`），並且會 re-anchor 到這個路徑
    （`MainWindowViewModel.cs:5514`）。
- **重現**：
  1. Save As 到 `slot100\save.edited.save`。之後每次 Save 都寫回這個檔案。
  2. 每次寫入前，「備份」複製的都是 **沒被動過的 `save.save`**，`save.edited.save` 本身從來沒有被備份。
     結果回報仍是 `Ok`（`:196-204`），使用者看到「Backup: slot100 @ …」。
  3. 存到第 7 次時，`PruneBackups` 依保留 6 份的規則，把 **更早、真正不同的 `save.save` 備份** 刪掉，
     留下 6 份內容相同的副本。
  4. 另一種情況：Save As 覆寫任意資料夾中已存在的 `foo.save`。`foo.save` 沒有被備份；如果該資料夾也沒有
     `save.save`，結果是 `SkippedNoSource`，且是靜默的（`MainWindowViewModel.cs:5549-5551`）。
- **建議修法**：備份 **實際的目標檔名**，再加上同資料夾的 `lobby.save`（若存在）。或者在檔名不是 `save.save` 時，
  把 `Path.GetFileName(target)` 一起放進備份清單，並記錄在 `BackupEntry.FileNames`。修正時補測試：非
  `save.save` 檔名、以及重複 Save 不得擠掉不同內容的備份（可考慮內容 hash 去重）。

### B2-06 — Add Dye 流程修改了存檔卻沒有寫 Journal，關閉時不會出現未存檔提示
- **嚴重度**：bug（資料遺失）
- **位置**：`src/CrimsonAtomtic.Ui/Views/MainWindow.axaml.cs:1074-1138`（`AddDyeRequested`）、`:1141-1147`；
  `DyeEditorViewModel.cs:105`；`MainWindowViewModel.cs:87-95`；`MainWindow.axaml.cs:111, 179`
- **證據**：
  - Add Dye 直接呼叫 `loader.SetObjectListPresent(...)` 與 `loader.SetScalarFieldPresent(...)`（`:1092-1113`），
    成功後只呼叫 `masterVm.NotifyChildApplied()`，而這個方法只做 `IsDirty = true`（`DyeEditorViewModel.cs:105`）。
  - master 視窗關閉時呼叫的是 **不帶參數** 的 `vm.MarkDirtyFromExternalEdit()`（`:1145`）。依 `MainWindowViewModel.cs:91-94`，
    沒有參數就不會寫 Journal。
  - 關閉提示與 Review 都 **只看 `Journal.HasUnsavedChanges`**（`MainWindow.axaml.cs:111, 179`），不看 `IsDirty`。
- **重現**：Tools → Edit Item Dyes → 對某一列按「+ Add」並選一個 slot → 關閉 Dye 視窗 → 關閉主視窗。標題有 `*`，
  但沒有任何提示，app 直接關閉，新增的 dye 遺失。Review Changes 也會顯示「No pending changes」。
- **建議修法**：Add Dye 成功後寫一筆 Journal（類別 `JournalCatDye`）。更根本的做法見 B2-14：把「有未存檔變更」收斂成
  單一來源（例如以 native `mutation_version` 對照最後一次存檔時的版本），Journal 只負責描述。

### B2-07 — `CommitFieldEditAsync` / `MakeFieldAbsent`：mutation 成功後的重新讀取不在 try 內
- **嚴重度**：bug
- **位置**：`MainWindowViewModel.cs:1695-1766`（`:1751`）、`:1783-1811`（`:1804`）；綁定 `MainWindow.axaml:551, 576`
- **證據**：
  - `SetScalarField` / `SetScalarFieldPresent` 包在 try 內（`:1709-1737`, `:1792-1803`）。但下一行
    `loader.LoadBlockDetails(_loadedPath, block.Index)`（`:1751`, `:1804`）在 try 外，而且排在
    `IsDirty = true` 與 `Journal.Log` **之前**。
  - 如果重新讀取丟出 `CrimsonSaveException`（例如 deferred batch 期間的狀態，或 block 解碼失敗）：
    - 存檔已經被修改，但 `IsDirty` 與 Journal 都沒有更新，關閉時不會提示（B2-06 的同一類後果）。
    - `CommitFieldEdit` 是 `AsyncRelayCommand`，例外會回拋 dispatcher，app 崩潰。`MakeFieldAbsent` 是同步
      `RelayCommand`，例外直接從 command binding 拋出，同樣會崩潰。
- **建議修法**：mutation 成功後 **立刻** 設 `IsDirty = true` 並寫 Journal，再把重新讀取放進 try。讀取失敗時顯示
  `DetailsError`，並保留舊畫面。

### B2-08 — 寫檔不是原子操作；備份失敗時仍照樣覆寫
- **嚴重度**：risk（資料安全）
- **位置**：`MainWindowViewModel.cs:5421-5422, 5503-5504, 5531-5552`；Rust（唯讀）`D:\Github\crimson-rs\src\c_abi\mod.rs:4167-4199`
- **證據**：
  - Rust 端是 `std::fs::write(path_str, &bytes)`（`mod.rs:4193`）：直接 truncate 目的檔再寫入，沒有 temp file 加 rename。
    如果寫到一半失敗（磁碟滿、process 被殺、斷電），會留下截斷的 `save.save`。
  - 錯誤一律回 `WRITE_FAILED`（`:4195`），丟掉了 OS 錯誤細節（sharing violation、access denied、disk full），使用者看不出原因。
  - C# 端在 `BackupOutcomeKind.Failed` 時只把訊息放進 `BulkOpStatus`，然後照樣寫檔（`:5543-5548`，設計註解
    `:5417-5420`）。最壞情況是「備份失敗 + 寫到一半」，slot 損毀而且沒有任何副本。
- **建議修法**：
  - C# 端可以自行做到原子替換，不必改 Rust：`WriteToFile(target + ".tmp")` → 驗證（重新 `Load` 暫存檔，或至少檢查
    大小 > 0）→ `File.Replace(tmp, target, backupPath)` 或 `File.Move(tmp, target, overwrite: true)`。
  - 備份 `Failed` 時，用 `ConfirmRequested` 詢問「沒有備份，仍要覆寫嗎？」，預設為否。
  - 向 crimson-rs 提需求：`WRITE_FAILED` 附帶 OS error code（另開 getter，或用 `last_error` 字串）。

### B2-09 — 存檔前沒有偵測「載入後檔案已被外部修改」，也沒有偵測遊戲是否在執行；mtime 回填會掩蓋這件事
- **嚴重度**：risk（資料遺失）
- **位置**：`MainWindowViewModel.cs:5406-5427`、`:1386`、`:1420-1432`
- **證據**：
  - `_loadedFileLastWriteTime` 只在載入時記錄一次（`:1386`）。`SaveAsync` 不比對磁碟上目前的 mtime / size，直接覆寫。
  - 遊戲在編輯器開檔之後自動存檔（或 Steam Cloud 下載了較新的版本）時，Save 會用 **舊的內容加上編輯** 蓋掉新進度。
    雖然有備份會拿到新版本（B2-05 修好之後），但使用者不會知道要去還原。
  - `PreserveOriginalTimestamp` 接著把 mtime 改回 **較舊的** 載入時間（`:1428`）。在 Steam Cloud 眼中，本地檔案
    比雲端舊，可能觸發雲端版本覆蓋本地，讓編輯也一起消失；或者跳出衝突對話框。
- **建議修法**：Save 前比對 `File.GetLastWriteTimeUtc` 與檔案大小。和載入時不同就跳確認：「檔案在載入後被修改過
  （遊戲在執行？），仍要覆寫嗎？」。可另外偵測遊戲 process 是否存在（OS 相關，須透過 `Core` 介面，見 Rule 4）。

### B2-10 — mtime 保留使用本地時間 API；備份時間戳記也用本地時間
- **嚴重度**：risk
- **位置**：`MainWindowViewModel.cs:1404-1432`（`File.GetLastWriteTime` / `File.SetLastWriteTime`）；
  `SaveBackupService.cs:147`（`DateTime.Now`）、`:352-377`（依時間戳記排序後 prune）
- **證據**：
  - 本地時間在 DST 回撥的那一小時有歧義。`GetLastWriteTime` 回傳的本地時間被 `SetLastWriteTime` 轉回 UTC 時，
    可能偏移 1 小時，導致 status.md 的保證（`docs/status.md:488-490`，mtime 不變）失效。如果偏移是往後，
    檔案在 Steam Cloud 眼中變成「較新」。
  - 備份資料夾名稱用本地時間。使用者跨時區或 DST 回撥後，新備份的時間戳記可能 **早於** 既有備份。`PruneBackups`
    依時間戳記排序，剛建立的備份可能被當成最舊的刪掉；這時 `BackupOutcome.Ok` 回傳的 `Entry` 指向已刪除的資料夾。
- **建議修法**：mtime 改用 `GetLastWriteTimeUtc` / `SetLastWriteTimeUtc`。備份資料夾名稱維持同樣格式但改用 UTC
  （`ListBackups` 顯示時再轉本地）；prune 時永遠保留剛建立的那一份。

### B2-11 — 還原 `Unknown` 平台的備份時，會寫進「最近使用的 launcher」的正式存檔樹
- **嚴重度**：risk（可能覆寫錯誤的正式存檔）
- **位置**：`MainWindowViewModel.cs:738-760`（`ResolveSaveRootForBackup`）；`SaveBackupService.cs:139-144, 236-243, 393-432`
- **證據**：
  - 文件註解說「Legacy backups (`SavePlatform.Unknown`) restore into the first available save root」（`:743-745`）。
    但 legacy 備份實際上被標成 **Steam**（`SaveBackupService.cs:238-243`）。真正標成 `Unknown` 的，是「從非標準位置
    開啟的存檔」（`:139-143`）。
  - `TryDeriveSlotCoordinates` 接受任何至少有兩層父目錄的路徑（`:404-431`）。例如 `C:\Users\x\Desktop\a.save` 會得到
    userId = `x`、slot = `Desktop`；`D:\tmp\102190433\slot100\save.save` 會得到真實的 userId / slot 名稱。
  - 還原時目標是 `discovered[0]\<userId>\<slot>\save.save`，也就是 **最近修改過存檔的 launcher**（`:754-757`）。
    結果可能是：把一份來路不明的存檔寫進正式的 Steam 或 Epic slot，或在正式存檔樹下建立 `x\Desktop\save.save` 這種垃圾路徑。
  - 不同位置的 Unknown 存檔，只要最後兩層目錄名稱相同，就會共用同一個備份 bucket 並互相 prune。
- **緩解**：確認對話框會顯示 `Target:`（`:5597-5605`），使用者有機會看出問題。
- **建議修法**：`Unknown` 備份記下原始完整路徑（例如在備份資料夾寫一個 `origin.txt`），還原時寫回原路徑；或改成用
  檔案選擇器讓使用者指定目標。同時修正 `:738-746` 的註解。

### B2-12 — 還原不是原子操作，可能讓 `save.save` 與 `lobby.save` 不同步；目前狀態不會先備份
- **嚴重度**：risk
- **位置**：`SaveBackupService.cs:327-342`；`MainWindowViewModel.cs:5613-5627`
- **證據**：
  - 只複製備份裡存在的檔案（`entry.FileNames`）。如果備份只有 `save.save`，但 slot 目前有 `lobby.save`，舊的
    `lobby.save` 會被留下。`SaveBackupService.cs:75-78` 自己也說過「restoring just one risks a desync」。
  - 檔案逐一 `File.Copy(overwrite:true)`，中途失敗時兩個檔案會來自不同時間點（`:318-325` 已承認這一點）。
  - 還原前 **不備份目前狀態** 是刻意設計（`:13-20`、`MainWindowViewModel.cs:5613-5617`）。但目前的磁碟狀態常常包含
    「遊戲自己存的進度」，這些進度從未經過編輯器的 Save，因此 **不在任何備份裡**。還原一旦按錯，這份進度就永久消失。
  - 沒有檢查遊戲是否在執行。
- **建議修法**：還原前把目前的檔案移到一個獨立、不計入保留上限的 `PreRestore\` 資料夾（只保留最近 1 份），這樣就
  不會擠掉一般備份，原本的設計顧慮也能消除。複製改成 temp + replace，並在備份沒有 `lobby.save` 時刪除或改名殘留的
  `lobby.save`（或至少提示）。

### B2-13 — 還原對話框看不出平台；`ListBackups` 的 I/O 例外會讓 app 崩潰
- **嚴重度**：risk
- **位置**：`RestoreFromBackupViewModel.cs:27-34, 75`；`RestoreFromBackupWindow.axaml:39-41`；
  `SaveBackupService.cs:219-303`；`MainWindow.axaml.cs:988-1004`
- **證據**：
  - 列表只顯示 `SlotLabel = "{userId} / {slot}"`，沒有平台欄。同一個 userId / slot 同時存在於 Steam 與 Epic 時，
    兩列看起來一模一樣（平台只出現在確認對話框）。這正是計畫書提到的「userId collisions across launchers」在 UI 上的表現。
  - `ListBackups` 與 `ScanSlotDirs` 的 `Directory.EnumerateDirectories` 與 `new FileInfo(p).Length` 都沒有 try
    （`:227, 255, 268, 271, 286`）。如果某個備份資料夾沒有權限，或在列舉時被刪掉，ctor 會直接丟例外。呼叫端
    `OnRestoreFromBackupClick` 是同步 void handler（`:994`），例外就會讓 app 崩潰。
  - 列表在 UI 執行緒同步建立（Rule 3，見 B2-20）。
- **建議修法**：新增「Platform」欄（三語系字串）。`ListBackups` 對每個子目錄 try/catch，跳過無法讀取的項目。
  VM 改用 async factory，在背景執行緒列舉。

### B2-14 — 「是否有未存檔變更」有兩個來源（`IsDirty` 與 `Journal`），而工具視窗只在關閉時才同步
- **嚴重度**：risk
- **位置**：`MainWindowViewModel.cs:87-95, 1429, 1524-1527`；`MainWindow.axaml.cs:111, 137-163, 1141-1147, 1241-1247, 1331-1337, 1881-1887, 1950-1956`
- **證據**：
  - Save 的 `CanExecute` 看 `IsDirty`（`:1429`）。關閉提示與 Review 看 `Journal`。
  - Sockets / Rename / Vendor Buyback / Abyss Gates 這幾個視窗在 Apply 時就寫 Journal，但主 VM 的 `IsDirty`
    要等子視窗 **關閉** 時才被設定。
  - 因此工具視窗還開著時：
    - 標題沒有 `*`，File → Save 是灰的。
    - 關閉主視窗時 ChangeSummary 列出這些編輯；使用者按 Save 後 `SaveCommand.CanExecute(null)` 為 false，
      存檔被靜默跳過（`:139-142`）。接著 `Journal.HasUnsavedChanges` 仍為 true，於是直接 `return`（`:157-160`），
      沒有任何訊息，視窗維持開啟。使用者不知道為什麼沒存。
  - 反方向是 B2-06：`IsDirty` 為 true 但 Journal 是空的。
- **建議修法**：子 VM 在 Apply 時直接呼叫 `MarkDirtyFromExternalEdit(category, summary)`（帶描述），不要等到關閉。
  或者讓 `IsDirty` 改由 `loader.GetMutationVersion() != _savedMutationVersion` 推導，作為唯一來源。

### B2-15 — 通用欄位編輯器的「absent ≠ 0」語意沒有任何防護
- **嚴重度**：risk（寫出遊戲從不產生的狀態）
- **位置**：`MainWindowViewModel.cs:1722-1731, 1783-1811`；`FieldRowViewModel.cs:85-98`；
  `MainWindowViewModel.cs:1256-1259`（提示文字）
- **證據**：
  - status.md 的 gotcha（`docs/status.md:333-343`）：`_validSocketCount` 的 absent 就是「0」，遊戲從不寫出 present-0；
    absent 與 present 是兩個語意不同的狀態。
  - 通用編輯器中，對 absent 欄位輸入 `0` 再按 Apply，會走 `SetScalarFieldPresent(makePresent:true, [0])`，寫出 present-0。
    提示只有「(absent — Apply makes present)」，沒有說明「0 不等於 absent」。
  - 「Make absent」按鈕對任何 present scalar 都會顯示（`:1787`），包括 `_itemKey` 這種語意上必定存在的欄位。
- **建議修法**：對 absent 欄位輸入 0（或型別預設值）時跳出警告，並建議「保持 absent」。「Make absent」改成需要確認，
  並說明這是 expert 操作。可以在 `ScalarFieldEditing` 加一個 `IsZeroValue(tag, bytes)` 輔助函式。

### B2-16 — `ScalarFieldEditing.TryEncode` 接受 NaN、Infinity，以及溢位後變成 ∞ 的浮點數
- **嚴重度**：risk
- **位置**：`src/CrimsonAtomtic.SaveModel/ScalarFieldEditing.cs:295-315, 346`
- **證據**：`float.TryParse` / `double.TryParse` 搭配 `NumberStyles.Float` 會接受 `"NaN"`、`"Infinity"`、`"∞"`。
  .NET Core 3.0 之後，`"1e39"` 對 f32 會回傳 true，值為 +∞。這些值會原封不動寫進存檔的座標、四元數或倍率欄位，
  可能讓遊戲出問題。
- **建議修法**：parse 後檢查 `float.IsFinite`，不合格時回傳錯誤訊息。f32x3 / f32x4 的每個分量也一樣檢查。
  補單元測試。

### B2-17 — `AppSettingsStore`：非原子寫入、檔案損毀後靜默重置、`TrySave` 的回傳值全被忽略
- **嚴重度**：risk（使用者資料遺失：自訂 gem sets、安裝路徑覆寫、免責聲明接受紀錄）
- **位置**：`src/CrimsonAtomtic.Ui/Services/AppSettings.cs:164-198`；呼叫端 `MainWindowViewModel.cs:529-531, 598-600, 663-664, 776-783, 1036-1038, 1082-1084`、
  `DisclaimerDialog.axaml.cs:66-68`
- **證據**：
  - `TrySave` 用 `File.Create`（先 truncate）再序列化。中途失敗會留下空檔或截斷的 JSON。
  - `Load` 的 `catch { return new AppSettings(); }` 把損毀檔案當成「沒有設定」。下一次任何 `TrySave(existing with {X})`
    都會把 **預設值加上單一欄位** 寫回去，其他欄位（`custom_gem_sets`、`game_install_root` 等）永久消失。
  - 所有呼叫端都忽略 `TrySave` 的 `bool`。`SetGameInstallRoot` 即使沒有寫成功也回傳 `true`（`:530-560`），
    使用者會以為設定已保存。
  - 每個呼叫端都是「Load → with → TrySave」各做一次 read-modify-write，沒有集中的 store 實例。
- **建議修法**：寫入改成 temp + `File.Move(overwrite:true)`。`Load` 遇到 `JsonException` 時把壞檔改名成
  `settings.json.corrupt-<ts>` 再回傳預設值。`TrySave` 失敗時提示使用者（至少 `SetGameInstallRoot` 要回報）。
  長期可以改成單一 `SettingsService` 實例，在記憶體中保存目前設定。

### B2-18 — 單一執行個體保護（Rule 2）的邊界情況
- **嚴重度**：risk
- **位置**：`src/CrimsonAtomtic.Ui/Program.cs:11, 16-24, 48-52, 69-75`
- **證據**：
  - `Global\CrimsonAtomtic.v1` 是跨 session 的命名空間。快速切換使用者時，另一位使用者的 instance 會擋住目前使用者。
    如果該 mutex 的 DACL 不允許目前使用者開啟，`new Mutex(...)` 會丟 `UnauthorizedAccessException`；`Main` 沒有接住，
    app 啟動就崩潰。
  - 已有 instance 時，訊息寫到 `Console.Error`（`:22`），但 WinExe 沒有 console，使用者什麼都看不到。也不會把既有視窗帶到前景。
  - 非 Windows 平台使用 `NullSingleInstanceGuard`，永遠成功（`:69-75`）。對 cross-platform 目標來說，Rule 2 在
    Linux / macOS 沒有被執行；兩個 instance 可能同時寫同一份存檔、備份樹與設定檔。
- **建議修法**：改用 `Local\` 命名空間（單一 instance 的範圍本來就是同一個使用者），並捕捉
  `UnauthorizedAccessException` / `WaitHandleCannotBeOpenedException`。第二個 instance 用 OS 訊息框提示，或透過
  named pipe 通知第一個 instance 啟動視窗（實作放在 `Core` 介面之後）。非 Windows 平台補上 lock-file 實作。

### B2-19 — async void handler 與 fire-and-forget 的失敗不會被使用者看到
- **嚴重度**：risk
- **位置**：
  - `MainWindow.axaml.cs:1370-1382, 1389-1400, 1413-1433, 1441-1458, 1465-1482, 1490-1507`：`await XxxViewModel.CreateAsync(...)` 沒有 try。
  - `MainWindow.axaml.cs:997`：`RestoreFromBackupAsync(entry).SafeFireAndForget()` 沒有傳 `onError`。
  - `src/CrimsonAtomtic.Ui/Services/TaskExtensions.cs:49`：預設只呼叫 `Trace.TraceError`。
  - `App.axaml.cs:127-142`：async lambda 也沒有 try。
  - `MainWindowViewModel.cs:240`：只捕捉 `InvalidOperationException`。
- **證據**：
  - `CreateAsync` 內部會做 FFI 掃描，存檔被卸載或解碼失敗時會丟 `CrimsonSaveException`。在 async void 裡，這會讓 app 崩潰。
    相比之下，`OnEditItemSocketsClick` 與 `OnEditAbyssGatesClick` 都有 catch（`:1913-1935, 1306-1318`），各處不一致。
  - `SafeFireAndForget` 的預設路徑是 `Trace`。專案沒有任何 `TraceListener` 或檔案 logger（B2-21），所以 Restore 中
    非 IO 類的例外，例如 `ArgumentException`、`SaveBackupService.Restore` 以外的錯誤，會被完全吞掉，使用者看不到任何訊息。
  - `NavigateToItemByKeyAsync` 沒有接 `CrimsonSaveException`，只能交給 `SafeFireAndForget` 的 Trace 路徑。
- **建議修法**：所有 Tools handler 統一用一個 `RunToolAsync(Func<Task>)` 包裝，捕捉例外並顯示 alert。
  `SafeFireAndForget` 的預設 handler 改成寫 log 並更新狀態列。

### B2-20 — Rule 3：檔案 I/O 與重 FFI 在 UI 執行緒同步執行
- **嚴重度**：rule violation（Rule 3）
- **位置與證據**（不使用 `.Result` / `.Wait()`，grep 在 `src/CrimsonAtomtic.Ui` 無命中；問題在同步呼叫）：
  - `LoadSave` 是同步的 `[RelayCommand]`：`loader.Load(path)`（`MainWindowViewModel.cs:1384`）會做 ChaCha20 + LZ4 + 解析
    整份存檔，再加上 `PersistLoadedSavePlatform` 的設定 I/O（`:1395`）。`MainWindow.axaml.cs:785-786` 的註解直接說明
    它是同步的。
  - `SaveAsync` / `SaveAsAsync`：備份的 `File.Copy` 加上 `WriteToFile` 加上 Save As 的重新 `Load`，都在第一個 `await`
    之後於 UI 執行緒執行（`:5421-5423, 5503-5513`）。
  - `CommitFieldEditAsync` 的 `await Task.CompletedTask // no awaits remain`（`:1708`）：mutation 與 `LoadBlockDetails`
    都在 UI 執行緒。依 `:1468-1471` 的註解，大 block 讀一次要 1–2 秒。`MakeFieldAbsent:1795-1804`、
    `RemoveElementAsync:3552`、`RefreshSelectedBlockSilently:5381`、`SetSecondaryLanguage:608` 同理。
    搭配 Block 1 的 B1-15（持鎖期間），UI 會凍結。
  - `NavigateToItemByKeyAsync:231`：`ListInventoryItems` 同步執行。
  - `RestoreFromBackupViewModel` ctor 的 `ListBackups()`（`:31`）。
  - `DefaultOpenSaveStartingPath`（`:691-704`）→ `DiscoverSaveRoots` → 遞迴 `EnumerateFiles(..., AllDirectories)`
    （`WindowsPlatformPaths.cs:189`），每次按 Open 都會執行。
  - 啟動時 `localization.TryBootstrapFromGameRoot(paths.GameInstallRoot)`（`App.axaml.cs:46`）：PAZ / PALOC 解析，
    在視窗出現之前同步執行。`SetGameInstallRoot` 也一樣（`MainWindowViewModel.cs:535`）。
  - 屬性 getter 讀磁碟：`IsUiLanguageAuto`（`:635-636`）；欄位初始化讀磁碟：`_fontSize`（`:1051-1053`）。
- **建議修法**：`LoadSave` 改成 `async Task`，`Load` 放到 `Task.Run`。Save / Save As 的備份與寫檔放到 `Task.Run`，
  期間設定 `IsBusy` 停用編輯。`CommitFieldEdit` 系列比照 `LoadSelectedBlockAsync`（`:1493-1516`）的寫法。
  `AppSettings` 改成記憶體快取（B2-17），不要在 getter 裡讀檔。

### B2-21 — Rule 6：沒有任何日誌實作（Pre-scan #1 確認）
- **嚴重度**：rule violation（Rule 6）
- **位置**：`src/CrimsonAtomtic.Core/IPlatformPaths.cs:42-43`；`src/CrimsonAtomtic.Ui/Platform/WindowsPlatformPaths.cs:53`；
  `Program.cs:35`（`LogToTrace()`）；`TaskExtensions.cs:49`
- **證據**：
  - 在 `src/` grep `LogDirectory`：只有介面、Windows 實作、測試 fake 三處定義，**沒有任何讀取它的程式碼**。
  - grep `ILogger|Serilog|StreamWriter|Trace.TraceError|Debug.WriteLine`：只有 `TaskExtensions.cs:49` 的
    `Trace.TraceError`，而且沒有掛任何 listener。
  - `ChangeJournal` 只存在記憶體（`ChangeJournal.cs:33`）。
  - 規則要求的 `%LOCALAPPDATA%\CrimsonAtomtic\Logs\<category>\<process>\`、4 檔輪替、單檔 8 MB 都沒有實作。
  - 影響：B2-04、B2-19 的失敗、備份失敗（`BulkOpStatus` 會被下一個動作覆蓋）、還原結果，事後都無從追查。
- **建議修法**：在 `Core` 定義 `IAppLog`（例如 `Info` / `Warn` / `Error(category, message, ex)`），Ui 層實作一個 AOT-safe
  的輪替檔案寫入器（不需要第三方套件）。至少記錄 Save / Save As / Backup / Restore 的結果、未處理例外，以及
  `SafeFireAndForget` 的失敗。

### B2-22 — Rule 7：沒有集中的 magic-string 檔（Pre-scan #4，Block 2 範圍確認）
- **嚴重度**：rule violation（Rule 7）
- **證據**（`src/` 中 grep `class *Constants|MagicStrings|Names|Literals|Keys`：無命中）：
  - 路徑與檔名：`"save.save"` 出現在 `SaveBackupService.cs:79, 341`、`MainWindowViewModel.cs:5595`、`WindowsPlatformPaths.cs:189`；
    `"Pearl Abyss", "CD", "save"` 在 `MainWindowViewModel.cs:719-721` 又寫了一次（`WindowsPlatformPaths.cs:35-46`
    已經有一份）；`"CrimsonAtomtic"` 出現在 `WindowsPlatformPaths.cs:51` 與 `MainWindowViewModel.cs:1143`；
    `"Logs"` 在 `WindowsPlatformPaths.cs:53`；`"Backups"` 在 `SaveBackupService.cs:72`；mutex 名稱在 `Program.cs:11`；
    `"settings.json"` / `"window-state.txt"` 散在各自的類別。
  - Schema 字串：`"fixed_prefix"` / `"fixed_suffix"` 在 `ScalarFieldEditing.cs:73`、`ElementRowViewModel.cs:210, 215, 282`、
    `MainWindowViewModel.cs:1315, 4533, 4539`；欄位名稱 `"_inventorylist"`、`"_itemList"`、`"_storeDataList"`、
    `"_storeSoldItemDataList"`（`MainWindowViewModel.cs:172, 189, 317, 335`）、`"_dyeSlotNo"`（`MainWindow.axaml.cs:1193`）、
    `"_stackCount"`、`"ItemKey"`（`ElementRowViewModel.cs:90, 209, 214`）。
  - 錯誤碼字面值：`ex.ErrorCode == -16`（`MainWindow.axaml.cs:1116`），應該用具名常數 `NOT_FOUND`。
- **建議修法**：每個專案一個 `*Names.cs`：Ui 放 `SaveFileNames`、`AppPaths`、`SchemaFieldNames`、`FieldKinds`；Core 放
  平台目錄名稱。錯誤碼使用 Block 1 B1-18 建議的集中常數。

### B2-23 — Rule 4：平台相關的路徑知識與具體實作洩漏到 ViewModel / composition root 之外
- **嚴重度**：rule violation（Rule 4，輕度）
- **位置**：`MainWindowViewModel.cs:8, 525, 719-721`；`App.axaml.cs:40`；`WindowsPlatformPaths.cs:2, 155`
- **證據**：
  - VM 自己組出 Windows 的存檔路徑 `LocalApplicationData\Pearl Abyss\CD\save`（`:719-721`），沒有透過 `IPlatformPaths`。
  - VM `using CrimsonAtomtic.Ui.Platform`，並直接呼叫靜態的 `SteamLibraryProbe.LooksLikeCrimsonDesertInstall`（`:525`）。
  - `App.axaml.cs:40` 不論在什麼 OS 都 `new WindowsPlatformPaths()`；非 Windows 平台沒有選擇其他實作的分支。
  - 反向依賴：`WindowsPlatformPaths`（平台層）依賴 `Ui.Services.AppSettingsStore`（`:2, 155`）。
  - 正面確認：mutex 已經放在 `ISingleInstanceGuard` 之後（`Program.cs:16-18`），存檔根目錄的探測也透過 `IPlatformPaths`。
- **建議修法**：`IPlatformPaths` 加上 `DefaultSaveRoot`（或 `FallbackSaveRoot`）與 `IsValidGameInstall(string)`；
  composition root 依 `OperatingSystem.IsWindows()` 選擇實作；把 `GameInstallRoot` 覆寫值改由建構子注入，不要讓
  `WindowsPlatformPaths` 自己去讀 `AppSettingsStore`。

### B2-24 — Rule 12：absent 欄位的型別是由消費端用 schema TypeName 猜出來的
- **嚴重度**：rule violation（Rule 12）
- **位置**：`src/CrimsonAtomtic.SaveModel/ScalarFieldEditing.cs:76-152`（特別是 `:142-150`）；`FieldRowViewModel.cs:52-55, 85-98`
- **證據**：
  - 註解寫明「Rust erases the underlying scalar kind to `FieldKind::Absent` on absent rows」（`ScalarFieldEditing.cs:78-81`、
    `FieldRowViewModel.cs:52-55`）。C# 因此維護一張 TypeName → tag 的別名表，並用「`*Key` 而且 `metaSize == 4` 就當 u32」的
    啟發式判斷。
  - 這是在消費端補 parser 輸出缺少的資訊。啟發式猜不出 signedness：一個 4-byte 的 `*Key` 如果其實是 i32，會被當成 u32 寫入；
    新的 typedef 名稱則會被靜默判定為不可編輯。
- **建議修法**：向 crimson-rs 提需求：`get_block_json` 對 absent scalar 也輸出 `type_tag`（Rust 端解析 schema 時已經知道
  基礎型別）。C# 改讀這個欄位，保留現有啟發式只作為舊 DLL 的 fallback，之後移除。

### B2-25 — UI 字串寫死英文，沒有放進三語系資源
- **嚴重度**：rule violation（cross-cutting「UI strings in all three `Resources/Strings/*.axaml`」）
- **位置**：
  - 檔案選擇器：`MainWindow.axaml.cs:765, 770, 828, 834, 1820`（`"Open Crimson Desert save"`、`"Save files"`、
    `"Save Crimson Desert save as…"`、`"Select Crimson Desert install folder"`）。
  - `ConfirmDialog.axaml.cs:53`（`"OK"`）；`MainWindow.axaml:168`（`"14 (default)"`）；`ElementRowViewModel.cs:270`
    （`"Fill stack"` / `"Fill stacks"`，由 `MainWindow.axaml:666` 綁定）；`MainWindow.axaml.cs:1977-1978`
    （`"Pick"`、gem picker tooltip）。
  - VM 狀態文字：`MainWindowViewModel.cs:449-459`（Icons…）、`:473-487`（Localization…）、`:1126-1128`
    （`"verified"` / `"FAILED"` / `"bytes"`）、`:1211, 1219`（`"(none)"`）、`:1227, 1239-1242`（`"of"`、`"element(s)"`）、
    `:1259`（`"(absent — Apply makes present)"`）、`:1282`（`"max stack: "`）、`:5748`。
  - 錯誤訊息直接顯示 `ex.Message` 加上 `(code N)`（例如 `:156, 1505, 1735`），這屬於 Block 1 的 B1-21。
- **正面確認**：Block 2 程式碼透過 `UiText` / `FindResource` / `DynamicResource` 引用的 59 個鍵（Save / Restore / Backup /
  ChangeSummary / Structural warning / Remove / Journal / Menu 等），在 `en.axaml`、`ja.axaml`、`zh-TW.axaml` 都存在
  （各 59 筆命中）。逐鍵完整比對屬於 Block 3。
- **建議修法**：把上述字串搬進三份資源檔。`ElementRowViewModel.FillButtonLabel` 改成在 XAML 依
  `IsSingleFillCandidate` 切換兩個 `DynamicResource`。

### B2-26 — `MainWindow.axaml.cs`（1,997 行）中有應該放在 ViewModel / Service 的邏輯
- **嚴重度**：cleanup（結構）
- **證據**：
  - **存檔 mutation 寫在 View 裡**：Add Dye 的兩步驟寫入加上 `ResolveDyeSlotNoFieldIndex`（`:1074-1138, 1165-1195`）。
    這些應該是 VM 方法，也因此漏了 Journal（B2-06）且沒有測試。
  - **視窗位置邏輯重複**：`:19-595` 約 580 行，與 `WindowRestoreState` / `ManagedWindowRestore` 幾乎相同。註解
    （`WindowRestoreState.cs:16-17`、`ManagedWindowRestore.cs:39-41`）說是刻意不遷移，但這樣有兩份需要同步維護的
    Win32 / Avalonia workaround。
  - **約 20 個 Tools handler 複製貼上**：開視窗、`Closed` 時 `MarkDirtyFromExternalEdit()`、錯誤處理寫法各不相同（B2-19）。
    應該收斂成一個 `ToolWindowHost` 或 `OpenToolAsync<TVm, TWindow>` 輔助函式，統一處理 dirty 傳遞（B2-14）、
    stale document（B2-03）與例外。
  - View 直接讀設定：`OnWorldMapClick:1270`。
- **建議修法**：依上述三組拆出（Add Dye → `DyeEditorViewModel`；位置 → 讓 MainWindow 也用 `WindowRestoreState`，
  只在外面包一層持久化；Tools 啟動 → 共用 helper）。這是結構性重構，建議等 B2-01 到 B2-07 修完之後再做。

### B2-27 — 過時或錯誤的註解與文件
- **嚴重度**：cleanup
- **證據**：
  - `MainWindowViewModel.cs:1189-1193` 說「Scalar editing is permitted only at depth == 1… IsEditable is forced false on
    deeper frames」，與 `:5675-5677`（「Every scalar is editable at any depth」）以及實際行為矛盾。
  - `MainWindowViewModel.cs:821-825` 說 `IsDirty`「set whenever a successful CommitFieldEditCommand…」，但實際上有十幾個設定點。
  - `MainWindowViewModel.cs:498-521` 連續三個 `<summary>`，前兩個沒有對應的成員（屬於 `SetSecondaryLanguage` /
    `RefreshIconCache`）。`MainWindow.axaml.cs:973-979` 有一段孤立的 Browse Items 說明，掛在 Restore handler 上。
  - `MainWindowViewModel.cs:738-746`：「Legacy backups (`Unknown`)」與實際情況不符（見 B2-11）。
  - `SaveBackupService.cs:41-42` 說 legacy 備份「are pruned on their natural retention cycle」，但 `:348-350` 說
    「Does NOT prune legacy」。實際上舊版 layout 永遠不會再寫入，所以 **永遠不會被 prune**，會無限期保留。
  - `Program.cs:8-10`「A versioned GUID prefix」：名稱 `Global\CrimsonAtomtic.v1` 裡沒有 GUID。
  - `AppSettings.cs:102-111`：`UiLanguage` 的說明是依 `CultureInfo.CurrentUICulture` 自動偵測，但實際走 Win32
    `GetUserDefaultUILanguage`（`App.axaml.cs:65-72`）。
  - `WalkPath` 用位置索引 `current.Fields[(int)step.FieldIndex]`（`:3239-3243`），而 `ResolveDyeSlotNoFieldIndex` 用
    `f.FieldIndex == step.FieldIndex` 查找（`MainWindow.axaml.cs:1176`）。兩者都隱含「Fields 是依 FieldIndex 排列的密集
    列表」這個假設，但只有前者依賴它。建議統一，並在 `BlockDetails` 文件寫明。
- **建議修法**：逐一更正。legacy 備份可以在 `ListBackups` 時一次性搬到 `Steam\` 底下，或提供清理選項。

### B2-28 — `ChangeJournal` 的執行緒安全只是表面上的
- **嚴重度**：cleanup（目前的呼叫端都在 UI 執行緒，尚未觸發）
- **位置**：`src/CrimsonAtomtic.Ui/Services/ChangeJournal.cs:21-28, 72-79`；`ChangeSummaryDialog.axaml.cs:60`
- **證據**：註解說 lock 是為了保護「background mutation worker calls Log」這個情境。但 `ObservableCollection.Add` 在 lock 內，
  會在 **呼叫端的執行緒** 觸發 `CollectionChanged`；開著的 ChangeSummaryDialog 直接綁定 `journal.Entries`，背景執行緒
  觸發時 Avalonia 會丟出跨執行緒例外。UI 端的列舉也沒有拿 lock。
- **建議修法**：明確規定只能在 UI 執行緒呼叫（`Dispatcher.UIThread.VerifyAccess()`），移除沒有作用的 lock；或在 `Log` 內
  `Dispatcher.UIThread.Post` 轉送。

### B2-29 — 平台探測的穩健性
- **嚴重度**：cleanup
- **位置**：`WindowsPlatformPaths.cs:184-207`；`SteamLibraryProbe.cs:49-53, 100-124`
- **證據**：
  - `TryFindMostRecentSaveMtime` 的註解說「Swallows IO/permission errors per-file」，但 `AllDirectories` 列舉時只要遇到一個
    無權限的子目錄，`MoveNext` 就會丟例外，被外層 catch 接住後回傳 `null`。結果是整個平台被排到最後，而不是只略過一個檔案。
    可以改用 `new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }`。
  - Steam 根目錄只試 `C:\Program Files (x86)\Steam` 與 `C:\Program Files\Steam`。註解說使用者可以手動設定覆寫，可以接受；
    但 Steam 裝在其他磁碟的使用者會在首次啟動時落入 no-localization 狀態。可以考慮透過 `Core` 介面讀取 registry
    `HKCU\Software\Valve\Steam\SteamPath`。
  - `GameInstallRoot` 是 property，但每次存取都會讀設定檔加上探測磁碟（`:150-175`）。改成方法（`ResolveGameInstallRoot()`）
    比較能反映成本。

### B2-30 — 測試缺口
- **嚴重度**：cleanup（測試覆蓋）
- **證據**：Block 2 的五個測試檔都只測服務層與純函式。`MainWindowViewModel` 的存檔生命週期 **完全沒有測試**。
  `SaveBackupServiceTests` 的 23 個案例（`SaveBackupServiceTests.cs:57-376`）沒有涵蓋：
  - 非 `save.save` 檔名的目標（B2-05）。
  - `Restore` 時備份沒有 `lobby.save` 但 slot 有（B2-12）。
  - `Unknown` 平台還原的目標（B2-11）。
  - 時鐘倒退後的 prune（B2-10）。
  - `ListBackups` 遇到無權限目錄（B2-13）。
- **建議修法**：VM 層用 fake `ISaveLoader` 加暫存目錄的 `IPlatformPaths`（測試已有 `FakePlatformPaths`，
  `SaveBackupServiceTests.cs:395-420`），補上以下測試：
  - Load / Save / Save As 的 mtime 保留與 re-anchor。
  - dirty 閘門（B2-01、B2-02）。
  - 寫檔失敗時保持 dirty（B2-04）。
  - 重新讀取失敗時仍然標記 dirty（B2-07）。
  - `AppSettingsStore` 遇到損毀檔案（B2-17）。
  - `ScalarFieldEditing` 的 NaN / Infinity（B2-16）。

---

## 3. Audit focus 逐項結論

| 項目 | 結論 |
|---|---|
| 寫入原子性；disk-full / 檔案鎖定 / 遊戲執行中 | **不原子**：Rust 直接 `std::fs::write`（B2-08）。disk-full 或檔案鎖定時，`WRITE_FAILED` 沒有細節；從 File → Save 觸發會讓 app 崩潰（B2-04）。沒有偵測遊戲執行或外部修改（B2-09）。 |
| 每條寫入路徑之前都有備份 | 寫磁碟的路徑只有三條（grep `WriteToFile` / `Restore(`）：`SaveAsync:5421`（有備份）、`SaveAsAsync:5503`（有備份，但檔名不是 `save.save` 時備到錯的檔案，B2-05）、Restore（刻意不備份，B2-12）。工具視窗只改記憶體，不直接寫磁碟。備份失敗時照樣寫（B2-08）。 |
| 原始 mtime 保留與 Save As re-anchor | **已實作且邏輯正確**：Save 與 Save As 都在寫入後回填載入時的 mtime（`:5423, 5509`），Save As 再 `Load` 新路徑並重新記錄 mtime（`:5513-5515`），與 status.md 一致。問題：使用本地時間 API（B2-10）、回填會掩蓋外部修改（B2-09）、re-anchor 失敗時訊息誤導（B2-04）。 |
| Dirty tracking、關閉提示、`ChangeJournal` 完整性 | 主編輯器的每個 mutation 都會設 dirty 並寫 Journal（`:1753-1759, 1806-1808, 3563-3566`，以及 Block 4 / 5 的十多處）。**缺口**：Exit 繞過提示（B2-01）、Open / Restore 不檢查（B2-02）、Add Dye 沒寫 Journal（B2-06）、重新讀取失敗時漏標（B2-07）、兩個來源不同步（B2-14）。 |
| absent 與 zero 語意 | 通用編輯器在路由上正確：absent 走 `SetScalarFieldPresent`，present 走 `SetScalarField`（`:1722-1731`），符合「promote 與 in-place 寫入不能互換」。但對使用者沒有任何防護（B2-15）；型別推斷是消費端的 workaround（B2-24）。 |
| 依 `SavePlatform` 的備份保留與還原；跨 launcher 的 userId 衝突 | 備份存放在 `{platform}\{userId}\{slot}`，所以 **跨 launcher 不會互相覆蓋或 prune**（有測試 `:86-140`）。保留是在新備份寫入後才 prune，順序正確（`:192-194`）。**問題**：`Unknown` 還原目標（B2-11）、UI 看不出平台（B2-13）、還原不同步（B2-12）、legacy 備份永不清理（B2-27）、本地時間排序（B2-10）。 |
| Rule 2（mutex） | 有實作（Windows 具名 mutex，透過 `ISingleInstanceGuard`）。邊界情況見 B2-18。 |
| Rule 3（UI 執行緒上的 async I/O） | **違反**，見 B2-20。 |
| Rule 4（OS 呼叫放在 `Core` 介面之後） | 大致遵守（`IPlatformPaths`、`ISingleInstanceGuard`）。洩漏處見 B2-23。 |
| Rule 6（logs） | **未實作**，見 B2-21。 |
| `MainWindow.axaml.cs` code-behind 大小 | 1,997 行。應搬移的邏輯見 B2-26。 |

## 4. Cross-cutting checklist（Block 2 範圍）

| 規則 | 結論 |
|---|---|
| Rule 3：async，不在 UI 執行緒用 `.Result` / `.Wait()` | `.Result` / `.Wait()` / `GetResult`：grep 無命中。但同步 I/O 與 FFI 很多，見 **B2-20**。對話框都是 async（`ShowDialog` 加 `await`）。 |
| Rule 4：OS 呼叫在 `Core` 介面之後 | 見 **B2-23**。`Program.cs` 的 mutex 有透過介面；`File.Get/SetLastWriteTime` 與 `Directory.*` 屬於跨平台 BCL，不算違反。 |
| Rule 5：不用反射 | **符合**。JSON 全部走 source-generated context（`SaveModelJsonContext`、`AppSettingsJsonContext`、`EpicManifestJsonContext`）；`Enum.GetNames<T>()` / `Enum.TryParse<T>` 是泛型 AOT-safe 版本；`MainWindowViewModel.cs:2, 1170` 的 `using System.Reflection` 只用於 `Assembly.GetEntryAssembly()?.GetName().Version`，AOT 下可用；Regex 用 `[GeneratedRegex]`（`SteamLibraryProbe.cs:163`）；MVVM 用 CommunityToolkit 的 source generator。 |
| Rule 7：magic strings | **違反**，見 **B2-22**。 |
| Rule 12：不在消費端繞過 parser 問題 | 見 **B2-24**（absent 欄位型別推斷）。其他地方沒有發現。 |
| 空 `catch` 掩蓋寫入失敗 | 存檔寫入本身沒有被吞（`WriteToFile` 的錯誤會往上拋，但沒人接，見 B2-04）。**會吞掉寫入失敗的地方**：`AppSettingsStore.TrySave` 的 bare `catch` 加上所有呼叫端忽略回傳值（B2-17）；`WindowStateStore.Save` 的 bare `catch`（`WindowStateStore.cs:63-66`，只影響視窗位置，可以接受）；備份 `Failed` 只更新狀態列（B2-08）；`PreserveOriginalTimestamp` 吞掉 IO 錯誤（`:1430-1431`，屬於刻意的 best-effort，可以接受，但應該寫 log，見 B2-21）；`Program.cs:62` 的 `ReleaseMutex`（可以接受）。 |
| UI 字串三語系 | Block 2 引用的 59 個資源鍵三語系都有。寫死在程式碼裡的英文字串見 **B2-25**。 |

## 5. 交接給其他 block

- **Block 1**：
  - `NativeSaveLoader.LoadBlockDetails` 在路徑不符時靜默開暫時 handle（`:148-154`），是 B2-03 的根因之一。建議把契約改成丟錯。
  - `WriteToFile` 的 `WRITE_FAILED` 沒有 OS 錯誤細節（B2-08）。
  - B1-15（持鎖期間阻塞 UI）在 B2-20 列出的 UI 執行緒呼叫點會被放大。
- **Block 3**：`App.axaml.cs:46` 與 `MainWindowViewModel.cs:535` 在 UI 執行緒同步 bootstrap localization（B2-20）；
  Pre-scan #3（`UiLanguageService` 的 Rule 4）不在本 block 查證。
- **Block 4**：
  - Add Dye 的寫入流程在 code-behind 且沒有寫 Journal（B2-06、B2-26，`MainWindow.axaml.cs:1074-1138`）。
  - `SocketEditorViewModel.cs:581-588`：commit 失敗時沒有寫 Journal 也沒有設 dirty，但提示「Reload the save without writing
    to revert」。要和 Block 1 的 B1-01 / B1-02 一起確認 abort 之後樹的實際狀態。
  - 各工具 VM 持有 `_savePath`，屬於 B2-03 的受影響端；dirty 只在關閉時傳遞，見 B2-14。
  - `OpenGemPicker` 的寫死字串（B2-25）。
- **Block 5**：Knowledge / Known Dyes / Sealed Artifacts / Faction Nodes / Faction Relations / Contributions 的
  `CreateAsync` 在 async void handler 裡沒有 try（B2-19）；這些視窗同樣受 B2-03 影響。
- **Block 6**：Pre-scan #2 的事實部分已順帶確認（見 §6）；文件修正由 Block 6 處理。

## 6. Pre-scan observations 查證結果

| # | 觀察 | 與 Block 2 相關？ | 查證結果 |
|---|---|---|---|
| 1 | Rule 6 logs 未實作 | **是** | **確認屬實**。`IPlatformPaths.LogDirectory`（`IPlatformPaths.cs:43`）只有 `WindowsPlatformPaths.cs:53` 與測試 fake（`SaveBackupServiceTests.cs:402, 420`）實作，沒有任何讀取它的程式碼；`src/` 中沒有 `ILogger`、Serilog 或自訂 writer；唯一的錯誤輸出是 `TaskExtensions.cs:49` 的 `Trace.TraceError`，而且沒有 listener；`ChangeJournal` 只存在記憶體。見 B2-21。 |
| 2 | architecture.md 漂移 | 部分（事實面） | **事實部分確認**：`src/CrimsonAtomtic.Core/` 只有 `IPlatformPaths.cs` 與 `ISingleInstanceGuard.cs` 兩個介面檔；設定持久化在 `Ui/Services/AppSettings.cs`，備份保留在 `Ui/Services/SaveBackupService.cs`；`SaveModel` 只有兩個檔案（`SaveSummary.cs`、`ScalarFieldEditing.cs`），沒有 edit model 或 undo/redo。文件本身的比對與修正交給 Block 6。 |
| 3 | `UiLanguageService` 的 Rule 4 | 否（Block 3） | 未查。 |
| 4 | Rule 7：沒有集中的 magic-string class | **是** | **確認屬實（Block 2 範圍）**：grep 沒有任何常數集中類別；mutex 名稱（`Program.cs:11`）、`"Logs"`（`WindowsPlatformPaths.cs:53`）都是字面值；`"save.save"`、`"Pearl Abyss\CD\save"`、schema 欄位名稱、錯誤碼 `-16` 都重複散落在各處。見 B2-22。 |
| 5 | 檔案大小熱點 | **是**（兩個檔案） | **確認**：`MainWindowViewModel.cs` 共 5,926 行（最後一行 `:5926` 是 `BreadcrumbItem`），與計畫書一致；`MainWindow.axaml.cs` 共 1,997 行，與計畫書一致。拆分建議見 B2-26。 |
| 6 | CI 只跑 release | 否（Block 6） | 未查。 |
