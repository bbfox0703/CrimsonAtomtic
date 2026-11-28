# Code audit — Block 5: World & progression editors (2026-11-28)

> **Status: REPORT ONLY.** 依 [code-audit-2026-11-28-plan.md](code-audit-2026-11-28-plan.md)
> 的 Block 5 範圍做靜態審查；本回合 **未修改任何程式碼**，修法只列建議，實作留給後續 fix PR。
>
> - Snapshot：`dev` @ `9ba7ae4`（PR #64）。所有 `file:line` 以此 commit 為準；只寫檔名時指
>   `src/CrimsonAtomtic.Ui/` 底下的檔案。
> - Rust 端對照（唯讀）：`D:\Github\crimson-rs\src\c_abi\mod.rs`（`apply_scalar_mutation_in_blocks` `:1738-1756`、
>   `crimson_save_set_scalar_fields_batch` 的 deferred 分支 `:2339-2374`、`crimson_save_list_clone_element` `:2656-2705`），
>   以及 `D:\Github\crimson-rs\docs\save-deferred-redecode.md`。
> - 範圍：計畫書 Block 5 的全部 Files（AbyssGates / SealedArtifactChallenge / FactionNodeEditor /
>   FactionRelationEditor / ContributionEditor / KnowledgeEditor / MountUnlock / RenameMercenary / WorldMap 的
>   ViewModel + Window；`Services/GameRegions.cs`、`FactionRelationService.cs`、`ContributionService.cs`、
>   `MountCatalog.cs`、`WorldMapAffine.cs`、`WorldMapBasemapService.cs`；`MainWindowViewModel.Contributions.cs`、
>   `MainWindowViewModel.FactionRelations.cs`），以及 `MainWindowViewModel.cs` 指定的 1836–1943、2006–3234、
>   4289–4386、4929–5059、5059–5354 五段。

## 0. 範圍說明與限制

1. **測試基線未執行**：依指示只做靜態審查，沒有跑 `dotnet run --project src/CrimsonAtomtic.Tests`，所以
   **沒有記錄測試數量**。開始修 Block 5 之前請補跑一次並記下數字。
2. **範圍外的程式碼只當證據引用**：
   - Block 5 各工具視窗的開啟與 dirty 傳遞寫在 `Views/MainWindow.axaml.cs`（Block 2 的檔案）。
   - 龍的解鎖流程中，`UnlockDragonAsync`（`MainWindowViewModel.cs:4111-4217`）與 `FillDragonHpAsync`（`:4227-4276`）
     落在 Block 4 的行號範圍內，但 Block 4 報告（§0.3）明確移交給 Block 5，因此一併審查。
   - 已由 Block 1 / Block 2 報告列出的通用問題（B1-01、B1-02、B1-05、B2-03、B2-14、B2-19、B2-20）只交叉引用，
     本報告補的是 **Block 5 特有的觸發點與後果**，不重複計數同一個根因。
3. **遊戲端行為未實機驗證**：B5-06、B5-07、B5-12、B5-14 的判斷依據是程式碼、status.md / status-archive.md
   記錄的存檔觀察與 Rust 契約，沒有進遊戲確認，也沒有掃描實際存檔。修正前請先用存檔掃描或實機測試確認。
4. 三語系字串比對：嘗試用 Python 腳本做全量比對，但執行需要權限而未執行；§3 改用 Grep 抽查（見 B5-19 與 §4）。

## 1. 摘要

| 嚴重度 | 數量 | 編號 |
|---|---|---|
| bug | 5 | B5-01 – B5-05 |
| risk | 11 | B5-06 – B5-16 |
| rule violation | 4 | B5-17（Rule 3）、B5-18（Rule 7）、B5-19（UI 字串）、B5-20（Rule 12） |
| cleanup | 4 | B5-21 – B5-24 |

**最嚴重的四項**：

- **B5-03**：多步驟寫入（Pattern B v1、knowledge inject、龍元素插入、faction relation 雙向寫入、contribution 新增）
  中途失敗時會留下半套結果：重複的 FAR tracker、重複的 knowledge `_key`、`_itemNo` 相同的物品、單向的 relation。
  其中 **只要成功筆數是 0，就不設 `IsDirty`、也不記 Journal**。之後使用者為了別的修改按 Save，這些殘留就會被寫進存檔。
- **B5-01 / B5-02**：Block 5 的工具視窗全部以 `Show(this)` 開啟（非 modal），而且都沒有過期檢查。
  - 開另一個存檔之後，Abyss Gates、Rename Mercenary、Sealed Abyss、Faction Nodes、Contributions 會用舊索引寫進新文件。
  - 同一份文件內也會出事：Sealed Abyss 對話框可以把主視窗剛標記過的挑戰 **再套一次**，多出一筆重複的 X_2 子任務；
    在主視窗 Add Item 到 Camp & Contributions 之後，Contributions 會把數量寫到 **隔壁的物品**。
- **B5-06**：clone 後的 in-place patch 只檢查欄位「在 schema 裡」，沒檢查「present」。
  - knowledge inject 會對 element 0 的 `_learnedFieldTime` / `_isNewMark` 做 in-place 寫入。依 status.md 的 absent≠0 gotcha，
    這兩個欄位很可能是 absent；若是如此，每次注入的第一個 key 都會失敗，並留下 element 0 的複本（等於 B5-03 的情況）。
  - Contribution 新增與龍的 `_mercenaryNo` 重編號也有同樣的盲點。
- **B5-04**：Abyss Gates 的 Lock / Unlock 按鈕在狀態改變後 **從不重新評估 CanExecute**。按了 Unlock 之後，
  Unlock 仍然可按、Lock 仍然灰掉，要關掉視窗重開才會恢復。

**建議的修正 PR 順序**：
1. **寫入原子性與帳務**（B5-03、B5-06、B5-07）：配合 Block 1 B1-02 的決策，per-item 失敗改成 abort，或補上補償刪除；
   任何 mutation 一旦落地就立即設 dirty 並記 Journal；clone 後的 patch 改走 presence-aware 的寫入。
2. **跨視窗一致性**（B5-01、B5-02、B5-13）：套用 Block 2 B2-03 / Block 4 B4-01 的共用 snapshot-version 與文件世代號 helper。
3. **Bug 小修**（B5-04、B5-05、B5-08、B5-09、B5-10、B5-11）。
4. **測試補齊**（B5-16）：先補 Pattern B v1（immediate + deferred）與 knowledge inject 的 live-save 測試。
5. Rule 3 / 7 / 12、UI 字串與 cleanup（B5-17 – B5-24）。

---

## 2. Findings

### B5-01 — 非 modal 的 Block 5 工具視窗在開另一個存檔後，仍用舊索引寫進新文件
- **嚴重度**：bug（跨文件寫入，可能損毀存檔）。根因同 **B2-03**，這裡列出 Block 5 受影響的視窗與寫入路徑。
- **位置**：
  - 開啟點全部是 `child.Show(this)`：`Views/MainWindow.axaml.cs:1338`（Abyss Gates）、`:1360`（Mount）、`:1381`（Knowledge）、
    `:1432`（Sealed Abyss）、`:1457`（Faction Nodes）、`:1481`（Faction Relations）、`:1506`（Contributions）、`:1888`（Rename）、`:1287`（World Map）。
  - `LoadSave` 只換掉 loader 的文件並清空 Journal，不關閉也不通知子視窗：`ViewModels/MainWindowViewModel.cs:1378-1396`。
- **證據**（依各視窗保存的狀態分類）：

  | 視窗 | 保存的位置資訊 | 套用時用的 loader | 結果 |
  |---|---|---|---|
  | Abyss Gates | `_savePath` 與每列的 `(BlockIndex, Path, StateHashFieldIndex)`（`AbyssGatesViewModel.cs:84-87, 412-423`） | 直接 `_loader.SetScalarField`（`:464`） | 把舊文件的 gimmick 索引寫進新文件 |
  | Rename Mercenary | `_savePath/_topBlockIdx/_listFieldIdx/_nameFieldIdx`、`row.Index`（`RenameMercenaryViewModel.cs:70-73, 311-314`） | 直接 `_loader.SetInlineBytesField` | 改到新文件同索引的傭兵 |
  | Sealed Abyss | `CurrentChallengeContext`（block / field / element 索引，`MainWindowViewModel.cs:3202-3227`） | `_main.ApplySealedArtifactChallengesAsync` → 目前的 loader | clone 與 patch 打在新文件的錯誤元素上 |
  | Faction Nodes | `FactionNodeTarget(BlockIndex, Path, StateFieldIndex)`（`:2667-2674`） | `SetFactionNodeStatesAsync`（`:2787-2828`） | 寫到新文件同位置的 u8 |
  | Contributions（更新） | `ContributionEntry.ItemPath / StackFieldIndex`（`Services/ContributionService.cs:13-20`） | `SetScalarFieldsBatch`（`:146-149`） | 改到新文件同位置物品的 `_stackCount` |
  | Faction Relations / Knowledge / Mount | 只保存 key，套用時重新解析 | — | 新文件不會被寫錯，但畫面顯示的是舊文件的內容 |

  另外 Abyss Gates / Rename 會把 `MarkDirtyFromExternalEdit` 延到視窗關閉時才呼叫（`MainWindow.axaml.cs:1331-1337, 1881-1887`），
  所以 dirty 也會標到新文件上。
- **建議修法**：採用 B2-03 的 (a)「Load / Save As / Restore 前關閉帶 loader 的子視窗」或 (b)「文件世代號」。
  Block 5 的 VM 都經由 `_main` 或 loader 寫入，可以在 `ApplyPatternBv1Writes`、`SetFactionNodeStatesAsync`、
  `ContributionService.Apply`、`AbyssGatesViewModel.Apply`、`ApplyRename` 的入口統一檢查世代號。

### B5-02 — 同一份文件內也會過期：Sealed Abyss 重複套用、Contributions 寫到隔壁物品
- **嚴重度**：bug（靜默寫錯資料）
- **位置**：
  - `ViewModels/SealedArtifactChallengeViewModel.cs:229-285`（只在 `applied > 0` 時重掃，`:276-279`）、
    `MainWindowViewModel.cs:2326-2358`（`perBlockCount` 取自 context 的 `MissionStateListCount`）
  - `Services/ContributionService.cs:138-150`（直接用掃描時的 `ItemPath`）
- **重現 1（Sealed Abyss）**：
  1. Tools → Complete Sealed Abyss Artifact Challenges，對話框列出挑戰 C（`FollowUpAlreadyExists=false`，`MissionStateListCount=N`）。
  2. 不關對話框，在主視窗瀏覽到 C 的 catalog row，按 **Mark Challenge Complete**（`:2006-2136`）。FAR 變成 state=5，
     X_2 被 append 到 index N。
  3. 回到對話框按 **Complete selected**。context 仍說 FAR 不是 5、X_2 不存在，於是：
     - 在 index N 再 clone 一次 FAR（原本的 X_2 被往後推），並把 key 改成 X_2；
     - FAR 再設一次 state=5（tag 有 `MergeTags` 去重，`:1836-1849`）。

     結果是 **兩筆 `_key` 相同的 X_2 子任務**。
- **重現 2（Contributions）**：開著 Edit Contributions，在主視窗 Add Item 到「Camp & Contributions」容器
  （Block 4 已確認 Add Item 一律插在 index 0，見 B4-01）。這個容器後面的物品 index 全部 +1。
  回到 Contributions 修改一個既有 stack 再 Apply：`SetScalarFieldsBatch` 依舊的 `ItemPath` 把 `_stackCount` 寫到
  **原本位置的前一個物品**。驗證用的是 contribution 的 max stack，所以不會被擋。
- **建議修法**：
  - 兩個 VM 都記下 `GetMutationVersion()`，Apply 前比對；不符就重掃並要求使用者再確認一次（同 `SocketEditorViewModel.cs:1105-1126` 的作法）。
  - Sealed Abyss 在 `ApplyPatternBv1Writes` 開頭先重建 context（`TryBuildChallengeContextFromCatalogRow`），不要沿用快照。
  - Contributions 的更新改成依 `(containerKey, itemNo)` 重新定位，不要直接用位置索引。

### B5-03 — 多步驟寫入失敗時留下半套結果，而且成功筆數為 0 時不設 dirty、不記 Journal
- **嚴重度**：bug。commit 語意的根因見 **B1-02**；本條補充 Block 5 每個呼叫端的殘留型態與帳務缺口。
- **位置與證據**：

  | 流程 | 步驟 | 失敗時留下的東西 | dirty / Journal |
  |---|---|---|---|
  | 單筆 Mark Challenge（immediate） | clone FAR → promote `_completedTime` → 設 tags → scalar batch（`MainWindowViewModel.cs:2206-2258`） | clone 出來但 `_key` 未改的 FAR **複本**（與 FAR 同 key） | 錯誤分支 `:2129-2134` **完全不設** `IsDirty`，也不記 Journal |
  | Bulk Sealed Abyss（deferred） | 同上，body 內 `return`，`RunDeferred` 照常 commit（`:2397-2430`） | 失敗那一筆的半套寫入連同前面成功的一起 commit | 只有 `applied > 0` 才設 dirty（`:2465-2473`）；第一筆就失敗時不設 |
  | Knowledge inject（deferred） | clone element 0 → 設 `_key` → 設 `_learnedFieldTime` → 設 `_isNewMark`（`:5220-5249`） | 與 element 0 **同 key** 的複本，或 key 已改但其他欄位沿用 element 0 的元素 | `applied` 只在全部成功後 +1（`:5250`）；`UnlockAllAbyssGatesAsync`（`:5028`）與 `LearnKnowledgeAsync`（`:5325`）都只在 `applied > 0` 時設 dirty |
  | 龍元素插入（immediate） | `ListInsertElementTemplate` → 設 `_mercenaryNo` → 設 `_isMainMercenary`（`:4321-4341`） | 已插入的龍，`_mercenaryNo` 仍是模板的 640、仍是 main | `UnlockDragonAsync` 收到錯誤就直接 `return (false, …)`（`:4148-4151`），不設 dirty |
  | Faction relation | 先寫 player→faction，再寫 faction→player（`Services/FactionRelationService.cs:161-169`） | **單向** relation（服務自己的註解說遊戲要求雙向一致，`:62-63`） | `ApplyFactionRelationsAsync` 只在 `Applied + GroupsApplied > 0` 時設 dirty（`MainWindowViewModel.FactionRelations.cs:38`） |
  | Contribution 新增 | `ListCloneElement` → `SetScalarFieldsBatch`（`Services/ContributionService.cs:206-207`） | 與模板 **byte 完全相同** 的物品（`_itemNo`、`_slotNo` 重複） | `Updated + Added == 0` 時不設 dirty（`MainWindowViewModel.Contributions.cs:32`） |

  後續影響：
  - 下一次掃描時，`TryBuildChallengeContextFromCatalogRow` 找 FAR 的迴圈是「最後一個符合者勝出」（`MainWindowViewModel.cs:3077-3080`）。
    這時會挑到 **複本**，之後的寫入對象就錯了。
  - 狀態列的提示是「reload without writing to revert」。但 `IsDirty` 是 false，Save 按鈕灰掉；只要使用者之後做了任何
    其他修改再 Save，殘留就會一起寫入。
- **建議修法**：
  1. 只要任何 mutation 已落地（包括失敗前的步驟），就設 `IsDirty` 並記一筆 Journal（例如「partial: …」）。
     做法可以是比對呼叫前後的 `GetMutationVersion()` / `HasStructuralEdit`。
  2. 每個 item 的多步驟寫入要原子化：依 B1-02 的 (b) 或 (c) 處理 deferred 流程；immediate 流程補補償刪除
     （clone 失敗後 `ListRemoveElement` 移除剛插入的元素）。
  3. Faction relation 的雙向寫入放進同一個 `RunDeferred`，任一側失敗就 throw 讓整批 abort。

### B5-04 — Abyss Gates 的 Lock / Unlock 在狀態改變後不會重新評估 CanExecute
- **嚴重度**：bug（UI 狀態錯誤，會重複寫入）
- **位置**：`ViewModels/AbyssGatesViewModel.cs:624-629`（`_currentStateHash` 只有 `NotifyPropertyChangedFor`）、
  `:641-649`（`CanLock` / `CanUnlock`）、`:659-663`（`[RelayCommand(CanExecute = …)]`）；`Views/AbyssGatesWindow.axaml:111-118`
  （按鈕只綁 `Command`，沒有另外綁 `IsEnabled`）。
- **證據**：CommunityToolkit 的 `RelayCommand` 只在收到 `CanExecuteChanged` 時重新查詢。`CurrentStateHash` 改變時，
  只發出 `CanLock` / `CanUnlock` 的 PropertyChanged，沒有 `[NotifyCanExecuteChangedFor(nameof(UnlockCommand))]` /
  `LockCommand`。所以：
  - 按 Unlock 之後，Unlock 仍可按。再按一次會重寫同一個值，Journal 多一筆「Set … → Unlocked」，`IsDirty` 再設一次。
  - Lock 一直灰掉，必須關閉視窗重開才能還原。
- **建議修法**：在 `_currentStateHash` 上加 `[NotifyCanExecuteChangedFor(nameof(UnlockCommand))]` 與
  `[NotifyCanExecuteChangedFor(nameof(LockCommand))]`。另外 `Apply`（`:455-481`）入口加一道
  「目前狀態 == 預期來源狀態」的防護。

### B5-05 — Faction Nodes 的 `_isCapital` 是 bool，卻用只接受整數的 parser，★ 永遠不會顯示
- **嚴重度**：bug（顯示錯誤）
- **位置**：`ViewModels/MainWindowViewModel.cs:2759-2761`、`:4451-4465`（`TryParseScalarUInt` 只接受 `u8/u16/u32/u64` tag）；
  `ViewModels/FactionNodeEditorViewModel.cs:389`（`CapitalText`）。
- **證據**：
  - `docs/status-archive.md` 記錄的 FactionNode 欄位探勘寫的是「`_isCapital` (bool, 60)」。
  - 解碼器對 bool 的格式是 `"true <bool>"` / `"false <bool>"`（例如 `src/CrimsonAtomtic.Tests/MountUnlockMechanicsTests.cs:129`
    斷言 `"false <bool>"`）。
  - 因此 `TryParseScalarUInt(f.Value, …)` 一定回傳 false，`isCapital` 一直是 false，60 個首都節點都不會顯示 ★。
  - `FactionNodeEditorTests.Capital_*` 直接建構 `FactionNodeTarget`，繞過了這段解析，所以測試抓不到。
- **建議修法**：改用 `SaveFieldValues` 一類能處理 bool 的 helper（或在 parser 裡接受 `bool` tag），
  並補一個 live-save 測試：`ScanFactionNodes()` 至少回傳一個 `IsCapital == true` 的節點。

### B5-06 — clone 後的 in-place patch 只檢查欄位「在 schema 裡」，沒檢查「present」
- **嚴重度**：risk（觸發與否取決於存檔資料；需要掃描確認）
- **位置**：
  - Knowledge：`MainWindowViewModel.cs:5133-5142`（不論 `f.Present` 都記下 field index）、`:5236-5249`（`SetScalarField`）；
    方法註解 `:5174-5175` 卻寫著「clear `_isNewMark` **when present**」。
  - Contribution：`Services/ContributionService.cs:312-329`（`_slotNo` / `_itemNo` 不論 presence 都記 index）、`:270`（`CanClone`）、`:194-207`。
  - 龍：`MainWindowViewModel.cs:4330-4341`（`_mercenaryNo` / `_isMainMercenary` absent 或 `dragon is null` 時 **靜默跳過**）。
- **證據**：
  - in-place 寫入需要 byte range。Rust 的 `apply_scalar_mutation_in_blocks` 計算 `expected = end - start`（`mod.rs:1749-1752`）；
    absent 欄位的長度是 0，所以 deferred 模式下回 `LENGTH_MISMATCH`，immediate 模式下回 `NOT_SCALAR`（status.md gotcha
    「absent 是零」與「promote then write in place」）。
  - clone 會原樣複製來源元素，包括 presence 與 start/end（`mod.rs:2697`），所以來源 absent，複本也 absent。
  - 依 status.md gotcha「Before touching any scalar, check whether the game ever writes its zero」：`_isNewMark=false`、
    `_slotNo=0` 這類「零值」很可能以 absent 儲存。若 knowledge `_list[0]` 的 `_isNewMark` 是 absent，
    **每一次** knowledge 注入（Abyss 解鎖、Knowledge editor、龍）都會在第一個 key 失敗，並留下 B5-03 的複本。
  - 龍的情況相反：不會失敗，而是靜默保留模板的 `_mercenaryNo = 640`（`Services/MountCatalog.cs:92`）。
    這正是註解 `:4328-4329` 說會撞號的值。
- **建議修法**：
  1. 先做一次存檔掃描：`KnowledgeSaveData._list[0]` 的 `_learnedFieldTime` / `_isNewMark`、contribution 模板的 `_slotNo` / `_itemNo`
     在現有存檔裡的 presence 分佈。
  2. 寫入改成 presence-aware：present 用 `SetScalarField`；absent 時，要寫非零值就用 `SetScalarFieldPresent(true, bytes)`，
     要寫零值就保持 absent（比照 `FactionRelationService.SetScalar`，`:266-278`）。
  3. 龍的 `_mercenaryNo` 找不到時應回報錯誤，不要默默保留 640。

### B5-07 — Knowledge 注入以 element 0 為模板，未覆寫的欄位（如 `_level`）全部沿用
- **嚴重度**：risk（資料正確性）
- **位置**：`MainWindowViewModel.cs:5219-5249`；欄位說明 `:4876-4882`（`_key`、`_level` u8、`_learnedFieldTime`、`_isNewMark`）。
- **證據**：注入只 patch `_key` / `_learnedFieldTime` / `_isNewMark`，`_level` 和其他欄位都複製自 `_list[0]`。
  如果 element 0 是 level > 0 的知識，所有新注入的知識都會帶著同樣的等級，包括 Abyss 解鎖的 398 類 key、龍的 187 個 key，
  以及 Knowledge editor 的「Learn all in category」。
- **建議修法**：明確設定 `_level`（目標值需先從存檔觀察「剛學到」的知識是哪個值）；或改用 crimson-rs 的
  `make_empty_element_bytes` / element template 建立乾淨的元素，不要複製既有資料。

### B5-08 — 非 `CrimsonSaveException` 的例外會讓 process 崩潰
- **嚴重度**：risk。通用的 async void 問題見 **B2-19**，這裡列出 Block 5 的具體觸發點。
- **位置與證據**：
  - `Services/FactionRelationService.cs:209, 250, 269`：schema 缺欄位時丟 `InvalidOperationException`；`:289` 的 `RowAt` 用
    `!` 與索引存取。`Apply` 只 catch `CrimsonSaveException`（`:172, 192`）；`FactionRelationEditorViewModel.Apply`
    只有 `try/finally`（`:192-207`），例外會從 `AsyncRelayCommand` 冒到 UI 執行緒。全專案沒有全域的 `UnhandledException` handler（Grep 無結果）。
  - `TryReadFarKeyFieldIdx`（`MainWindowViewModel.cs:2150-2152`）直接 `top.Fields[...]`、`Elements![...]`，只 catch `CrimsonSaveException`。
    在 bulk 流程裡，這個呼叫位於 `RunDeferred` body 內（`:2407`）；context 過期時（B5-02）會丟 `ArgumentOutOfRangeException`，
    結果是整批 abort、例外穿出 `Task.Run`，再經 `SealedArtifactChallengeViewModel.CompleteSelected`（`:261-284`，沒有 catch）冒到 UI 執行緒。
  - `ContributionService.Scan`（`:80`）與 `FactionRelationService.Load`（`:343`）呼叫 `LoadBlockDetails` 時沒有 catch。
    `MainWindow.axaml.cs:1449, 1473, 1498` 的 `async void` handler 都沒有 try；`:1378, 1421` 同樣沒有。只有 Abyss Gates（`:1306-1318`）有包起來。
  - `OnWorldMapClick`（`MainWindow.axaml.cs:1261-1288`，同步 handler）裡的 `WorldMapViewModel` 建構子會呼叫 `ListFieldPositions`
    （`WorldMapViewModel.cs:195`），也沒有 catch。
- **建議修法**：Block 5 的 Apply / Create 路徑統一在 VM 層 catch `Exception`（至少 `InvalidOperationException` 與
  `ArgumentOutOfRangeException`），轉成狀態列訊息；並依 B2-19 加上全域 handler。

### B5-09 — Faction Relations 的 Apply 會默默改寫所有「兩側不一致」的 faction
- **嚴重度**：risk（套用範圍超出使用者的選擇）
- **位置**：`ViewModels/FactionRelationEditorViewModel.cs:169-171`、`:180-184`（確認框只顯示筆數）。
- **證據**：`changed` 的條件是 `r.NewRelation is { } n && (n.Value != r.Entry.PlayerToFaction || r.Entry.IsMismatched)`。
  `NewRelation` 的預設值就是目前的 `PlayerToFaction`（`:274`），所以只要某一列 `IsMismatched`，即使使用者沒碰它，
  Apply 也會把 faction→player 那一側改成 player→faction 的值。使用者只想改一個 faction 時，
  確認框卻寫「Set the relation with N faction(s)」，N 包含這些沒動過的列，而且沒有說明原因。
- **建議修法**：不一致的列預設不納入；另外提供一個明確的「修正不一致」動作，或在確認框裡分開列出「你改的 N 筆」與「順便修正的 M 筆」。

### B5-10 — Sealed Abyss 對話框會套用被篩選隱藏、但仍勾選的列
- **嚴重度**：risk（使用者以為只套用畫面上的列）
- **位置**：`ViewModels/SealedArtifactChallengeViewModel.cs:232`（`picked` 取自 `_allRows`）、`:200-227`
  （SelectAll / Invert 只作用在可見列）、`:318`（每一列預設勾選）。
- **證據**：所有列預設都勾選。使用者輸入關鍵字篩到只剩 1 列，按 Complete selected，實際套用的是 **全部** 被勾選的列
  （最多約 141 筆）。確認框只顯示總數（`:243-250`），看不出包含了隱藏列。Faction Nodes（`FactionNodeEditorViewModel.cs:197`）
  也是同一套邏輯，但它預設不勾選，風險較低。
- **建議修法**：套用範圍改成「勾選 ∩ 可見」，或在確認框寫出「其中 K 筆目前被篩選隱藏」；也可以考慮預設不勾選。

### B5-11 — Mark Challenge 的警告文字仍宣稱「至少持有一件 SA 遺物才會啟用」，但這道檢查已經移除
- **嚴重度**：risk（使用者依賴一道不存在的防護）
- **位置**：`Resources/Strings/en.axaml:643`（`MarkChallengeConfirmWarnings`）與 ja / zh-TW 對應鍵；fallback 文字
  `MainWindowViewModel.cs:2047-2052`；doc 註解 `:1856-1858, 1861-1874, 1985-1990`。檢查移除的位置：`:3123-3128`、`:3151-3156`。
- **證據**：警告框寫著「The button only enables when you hold at least one SA artifact (any variant)」。但
  `TryBuildChallengeContextFromCatalogRow` 自 2026-05-17 起已經不看物品欄（註解 `:3123-3128`），`HoldsAnySealedAbyssArtifact()` 也已刪除。
  使用者會以為按鈕亮起就表示「手上有遺物」。
- **建議修法**：三語系同步改寫這段警告；同時更新 `IsCurrentChallengeMarkable` 與 `MarkCurrentChallengeCompleteAsync` 的 doc。

### B5-12 — Rename Mercenary：清空名稱寫成「present + 長度 0」，長度也沒有上限
- **嚴重度**：risk（遊戲端的格式未驗證）
- **位置**：`ViewModels/RenameMercenaryViewModel.cs:307-338`；契約 `src/CrimsonAtomtic.RustInterop/ISaveLoader.cs:627-631`。
- **證據**：
  - `ISaveLoader` 文件寫明：「Empty newBytes writes count = 0 + empty payload — does NOT make absent」，而且 absent 欄位會被 promote 成 present。
    所以對一個從沒自訂名稱（absent）的傭兵按 Apply（Apply 永遠可按，`:521-531`），結果是 absent → present(count 0)。
    遊戲對「沒有自訂名稱」寫的是哪一種格式，目前沒有證據；status.md 的 absent≠0 gotcha 正好就是這類情況。
  - 名稱沒有 byte 長度上限，也沒有字元過濾。遊戲 UI 或緩衝區的上限未知。
  - 每次 Apply 都是 length-changing 寫入（會設 `HasStructuralEdit`），同步在 UI 執行緒上做 re-decode。
- **建議修法**：
  - 清空時改用 make-absent（需要 crimson-rs 的 inline_bytes make-absent 能力，或沿用 `SetScalarFieldPresent` 一類的 API）。
  - 名稱未變（等於讀回的值）時不寫入。
  - 依存檔觀察到的最大長度設上限，並做 UTF-8 byte 計數驗證。

### B5-13 — Mount 對話框允許多列同時解鎖，工具視窗之間的 mutation 也沒有序列化
- **嚴重度**：risk。跨執行緒的 batch 問題見 **B1-05**。
- **位置**：`ViewModels/MountUnlockViewModel.cs:69-94, 130`（`IsBusy` 是每一列各自的）；`MainWindowViewModel.cs:4111-4217`。
- **證據**：快速點兩個 sigil 列時，兩個 `UnlockMountAsync` 會交錯 await。兩者都對 Quest Artifacts 容器
  計算插入位置與 max `_itemNo` / `_slotNo`，可能算出相同的編號。龍的流程在 `await InsertDragonElementAsync` 之後才
  `ResolveKnowledgeList`，期間若 Knowledge editor 也在注入，兩邊會用同一個 `BaselineCount` 當 append index，
  在 deferred batch 裡互相覆寫 `newIdx` 位置的 patch。
- **建議修法**：在 `MainWindowViewModel` 加一個全域的 mutation gate（例如 `SemaphoreSlim(1)`），所有 Block 4 / 5 的寫入都經過它；
  Mount 對話框改用對話框層級的 `IsBusy`。

### B5-14 — 尚未實機驗證的寫入與啟發式判斷
- **嚴重度**：risk（status.md 已列為未驗證）
- **位置與證據**：
  - Relation group 寫入：`Services/FactionRelationService.cs:188` 寫死 `(ushort)g.RelationGroupKey`。寬度取自註解（`:76`），
    沒有從 schema 的 `MetaSize` 讀取；欄位若不是 2 bytes，會回 `LENGTH_MISMATCH`，而且是在 relation 已經寫完之後
    （B5-03 的半套狀態）。group 對 stance 的影響尚未實機確認（status.md:64-65）。
  - Abyss Gate 的 region / altitude 欄：`AbyssGatesViewModel.cs:94`（`SkyMinHeight = 1400f` 來自單一存檔 slot102 的 258 筆資料）、
    `:434-449`（從 level 名稱猜 region）；status.md:64-65 列為未實機驗證。這兩欄只用於顯示與篩選，不影響寫入。
  - Pattern B v1 只在 Shield II / Spear I / Hooves II / Slash III 驗證過（`MainWindowViewModel.cs:2053-2056`）；
    bulk 一次可以套用約 141 筆，而 broad scan 模式還會納入非 SA 任務（`:2299-2305`）。
- **建議修法**：relation group 的寬度改從 `DecodedFieldRow.MetaSize` 取得；其餘項目照 status.md 的 backlog 做實機驗證，
  並把結果寫回 status.md。

### B5-15 — 掃描時吞掉讀取錯誤，可能讓 `_completedTime` watermark 偏低，也讓清單少項目
- **嚴重度**：risk
- **位置**：`MainWindowViewModel.cs:2871-2872`（`ScanMaxMissionCompletedTime`）、`:2573-2574`（`ScanBulkSealedArtifactCandidates`）、
  `:2724-2725`（`ScanFactionNodes`）；`AbyssGatesViewModel.cs:256-263`、`:248-251`。
- **證據**：上述位置都是 `catch (CrimsonSaveException) { continue; }`。
  - 只要有一個 QuestSaveData block 讀取失敗，watermark 就可能低於真正的最大值，違反註解（`:2851-2856`）
    「must sort after every prior `_completedTime`」的前提。
  - 清單類掃描則會少列項目，使用者只會看到「no candidates」，不會知道有錯誤。
- **建議修法**：watermark 掃描遇到錯誤時應中止 Mark 並回報；清單掃描則把「略過 N 個 block」顯示在狀態列。

### B5-16 — 測試缺口
- **嚴重度**：risk（test coverage）
- **證據**（Grep `src/CrimsonAtomtic.Tests`）：
  - 兩個 `RunDeferred` 呼叫端（Sealed Abyss bulk `MainWindowViewModel.cs:2397`、knowledge `:5211`）**都沒有測試**；
    單筆 Mark（immediate）也沒有。status.md 的 gotcha 明說「Any code path that only ever runs immediate is untested against the deferred contract」，
    這兩處正好是 deferred 專屬路徑。
  - `KnowledgeEditorTests` 只測 `CategoryFor`；`FactionNodeEditorTests` 只測 label 與 row 建構（因此抓不到 B5-05）。
  - `SealedArtifactChallengeViewModel`、`RenameMercenaryViewModel`、`WorldMapViewModel`、`SetFactionNodeStatesAsync`、
    `UnlockAllAbyssGatesAsync` 都沒有測試（確認計畫書所列）。
  - `MountUnlockMechanicsTests` 自稱「mirrors InsertDragonElementAsync … minus the VM glue」（`:12-13`），也就是把 VM 的步驟重寫一份。
    測試用 `!` 假設欄位存在（`:92-97`），VM 卻是靜默跳過（B5-06），兩者的行為已經不同。
- **建議修法**：
  - 把 Pattern B v1 與 knowledge inject 抽成可以直接對 `ISaveLoader` 呼叫的 service（比照 `ContributionService`），
    在 `LiveSaves` 每個 schema 上跑 immediate 與 deferred 兩種模式，寫檔、重載後驗證結果。
  - 加 B5-05 的 `_isCapital` 測試，以及 B5-04 的 CanExecute 測試。
  - Mount 測試改成呼叫抽出來的 service，不要自己重寫一份步驟。

### B5-17 — Rule 3：UI 執行緒上的同步 FFI 與檔案 I/O
- **嚴重度**：rule violation（CLAUDE.md Rule 3）。通用部分見 **B2-20**。
- **位置與證據**：
  - `IsCurrentChallengeMarkable` / `CurrentChallengeMarkTooltip` 是 property getter，每次讀取都會
    `LoadBlockDetails` + 兩次 `DynamicArrayGetU32Elements` FFI，再線性掃描整個 `_missionStateList`（約 4,000 筆，`MainWindowViewModel.cs:3061-3087`）。
    `NotifyMarkChallengeStateChanged`（`:5872-5878`）每次導航會觸發三次求值（IsMarkable、Tooltip、CanExecute）。
  - 單筆 Mark 的 `TryReadFarKeyFieldIdx`（`:2093`）在 UI 執行緒上執行。
  - `AbyssGatesViewModel.Apply`（`:464`）同步呼叫 `SetScalarField`（每次都 re-decode，約 25 ms）。
  - `RenameMercenaryViewModel.TryCreate`（`:105-208`）同步呼叫 `LoadBlockDetails` 加 N 次 `GetInlineBytesField`；`ApplyRename`（`:314`）是同步的 length-changing 寫入。
  - `KnowledgeEditorViewModel.CreateAsync` 的 `main.GetLearnedKnowledgeKeys()`（`:95`）與 `LearnAsync` 的重新同步（`:298`）
    在 UI 執行緒上讀取約 1,740 個元素。
  - World Map：`OnWorldMapClick` 同步讀 settings，並把底圖解碼成 Bitmap（約 108 MB，`MainWindow.axaml.cs:1270-1272`）；
    VM 建構子同步呼叫 `ListFieldPositions`（`WorldMapViewModel.cs:172, 195`）；`SetBasemap` 與 `PersistPathToSettings`（`:320-360`）同步做檔案 I/O。
    `TrySave` 的回傳值被忽略（同 B2-17）。
- **建議修法**：getter 改成快取的屬性，在導航或寫入後用 `Task.Run` 重算；其餘改用 `await Task.Run(...)`，
  World Map 開啟時先顯示視窗，再非同步載入底圖。

### B5-18 — Rule 7：magic strings 分散且重複（Pre-scan #4 在 Block 5 範圍確認）
- **嚴重度**：rule violation（CLAUDE.md Rule 7）
- **證據**：
  - `"MissionStateData"` 在 `MainWindowViewModel.cs` 出現 6 次（`:1905, 2586, 2915, 2971, 3003, 3064`）；
    `"QuestSaveData"` / `"_missionStateList"` 各 2 次（`:2568, 2578, 2866, 2875`）。
  - `"FactionSaveData"` 同時出現在 `MainWindowViewModel.cs:2719` 與 `Services/FactionRelationService.cs:87`；
    `"MercenaryClanSaveData"` / `"_mercenaryDataList"` 同時出現在 `MainWindowViewModel.cs:4325, 4363, 4371` 與 `RenameMercenaryViewModel.cs:45, 48`。
  - `"Sealed_Abyss_Artifact"`、`"Challenge_SealedArtifact_"`（`:2509, 2536`）、`VisibleTagHash`（`:1822`）、
    Abyss gate hash（`AbyssGatesViewModel.cs:53-55`）、`AbyssGateKnowledgePrefixes`（`MainWindowViewModel.cs:4862-4868`）分散在各檔。
  - `MountCatalog.cs:49-51` 自稱「Centralized magic-strings file (Mandatory rule 7)」，但它只集中了 mount 的常數，不是整個專案的集中檔。
- **建議修法**：建立一個 Ui 專案的 `SaveSchemaNames`（class / field 名稱）與 `GameDataConstants`（hash、prefix、key），
  並加上註解；Block 5 的 VM 與 service 改為引用它。與 B1-18 / B2-22 合併成同一個 PR。

### B5-19 — UI 字串寫死英文；三語系字典的鍵一致，但用詞不一致
- **嚴重度**：rule violation（Cross-cutting「UI strings in all three Resources/Strings/*.axaml」；同 B2-25）
- **寫死、沒有進資源字典的字串**：
  - Faction node 狀態：`MainWindowViewModel.cs:2683-2700`（"Undiscovered" … "Lost"），在 combo 與 grid 都會顯示。
  - Abyss gate 狀態：`AbyssGatesViewModel.cs:651-657`（"Locked (untouched)" 等）、`:419`（"(no owner-level name)"）、`:390`。
  - Knowledge：`KnowledgeEditorViewModel.cs:345`（"Learned" / "Not learned"）、`:33-34`（"All" / "Other" 類別）。
  - Rename：`RenameMercenaryViewModel.cs:436`（"Animal" / "Mercenary"）。
  - World Map：`WorldMapViewModel.cs:108-110, 218-219, 228-230, 241, 245`；`WorldMapWindow.axaml.cs:112`（"Image files"）。
  - Sealed Abyss 的 skip reason 全是英文字串（`MainWindowViewModel.cs:2920-3111`），會直接顯示在 tooltip
    （`:1933-1939`）與對話框；`"Disabled:"`、`"(unknown reason)"` fallback（`:1938-1939`）也是。
  - `"No save loaded."`（`:5306`、`:4115`、`:4294`）、`"Dragon element insert failed: …"`（`:4350`）、`"Unsupported unlock kind"`（`:4101`）。
  - Service 層的例外訊息會直接進狀態列（`FactionRelationService.cs:209, 250, 269`）。
  - `%LOCALAPPDATA%\CrimsonAtomtic\Backups\` 以字面路徑寫在三語系的警告文字裡（`SaConfirmBody`、`MarkChallengeConfirmWarnings`），
    在 Linux / macOS 上不正確；實際路徑應該取自 `IPlatformPaths`。
- **三語系鍵同步（已驗證）**：
  - Block 5 程式碼引用的鍵抽查 25 個，en / ja / zh-TW 三檔都有。
  - Block 5 相關前綴（Abyss / Sa / Faction / Rel / Contrib / Knowledge / Mount / Dragon / RenameMerc / WorldMap / Mark / BulkMark / Journal*）的鍵數，三檔都是 298。
  - 格式字串的 placeholder 數一致（`MarkChallengeConfirmDetail` {0}–{10}、`AbyssConfirmBody`、`SaNoCandidatesNoneEligible`、`BulkMarkFailed`、`KnowledgeInjectFailed`）。
- **用詞不一致**：`zh-TW.axaml:772`（`AbyssConfirmBody`）同一段文字裡混用「阿比斯之門」與「深淵之門」。
- **建議修法**：上列字串全部移進三語系字典（`FactionNodeEditorTests` 斷言英文 label 的部分要一起改）；skip reason 改成 enum
  加本地化 label（也解決 B5-22）；路徑改用 `{0}` 參數由 `IPlatformPaths` 填入；統一 zh-TW 的「Abyss」譯名。

### B5-20 — Rule 12：消費端的 workaround
- **嚴重度**：rule violation（CLAUDE.md Rule 12）
- **證據**：
  - `WorldMapViewModel.ResolveNamePair`（`:274-296`）把 `ResolveByFieldTypeName` 已經格式化好的 `"English / Secondary"` 字串，
    再用 `" / "` 拆回兩種語言（註解自己寫了「Simpler approach for now」）。名稱本身含有 `" / "` 時會拆錯。
    正確做法是由 `LocalizationProvider` 提供 per-language 的 API。
  - `AbyssGatesViewModel.ExtractStringValue`（`:497-517`）用解析顯示字串的方式取得 `_ownerLevelName`；隨後又用 `GetInlineBytesField` 覆寫（`:396-408`）。
    前者只是 fallback，但仍是解析格式化字串的作法。
  - `TryParseScalarUInt` 解析格式化值的作法，在三個檔案各寫了一份（`MainWindowViewModel.cs:4451`、`AbyssGatesViewModel.cs:519`、
    `RenameMercenaryViewModel.cs:353`），而且都不支援 bool（B5-05 的根因）。專案裡已經有 `SaveFieldValues.TryU64`（`FactionRelationService` 用的就是它）。
- **建議修法**：`LocalizationProvider` 新增 `ResolveNamePairByFieldTypeName`；三份 `TryParseScalarUInt` 統一改用 `SaveFieldValues`，並補上 bool 支援。

### B5-21 — 過時或錯置的註解與文件
- **嚴重度**：cleanup
- **證據**：
  - `RenameMercenaryViewModel.cs:32-37, 430-432` 說「no symmetric getter … read-side FFI is the natural next iteration」，
    但 `:186` 已經在用 `GetInlineBytesField`。
  - `MainWindowViewModel.cs:2234-2236` 說「The batch is all-or-nothing」，在 deferred 模式下不成立（見 B1-02、`mod.rs:2339-2344`）。
  - `:2267-2291` 是一段孤立的 `<summary>/<remarks>`，描述已經移除的 Tools 命令。它緊接著 `ScanSealedArtifactCandidates` 自己的 `<summary>`（`:2292`），
    導致同一個成員有兩段 summary。
  - `:4921-4925` 說「~3 length-changing FFI calls per missing key … a few seconds」，但注入已經改用 deferred batch（`:5195-5208`）。
  - `IsCurrentChallengeMarkable` / `MarkCurrentChallengeCompleteAsync` 的 doc 仍描述已移除的 held-artifact gate（見 B5-11）。
- **建議修法**：隨對應的 fix PR 一併更新。

### B5-22 — Skip reason 用英文子字串分類
- **嚴重度**：cleanup（但很容易在本地化時壞掉）
- **位置**：`MainWindowViewModel.cs:2624-2631`
- **證據**：`skippedNoFar` / `skippedOther` 的分類靠 `skipReason.Contains("artifact never picked up")` 等子字串比對。
  只要有人修改或本地化 `:3023, 3025, 3089` 的訊息，統計就會默默錯掉。
- **建議修法**：`TryBuildChallengeContextFromCatalogRow` 改成回傳 `SkipReason` enum 加參數，顯示時再本地化。

### B5-23 — 結構：Sealed Abyss 佔 `MainWindowViewModel.cs` 約 1,300 行（Pre-scan #5 確認）
- **嚴重度**：cleanup（只談結構）
- **證據**：
  - `MainWindowViewModel.cs` 共 5,926 行（`:5926` 為最後一行）。Block 5 的區段約 1,860 行，其中 Sealed Abyss
    （`:1820-3227`）約 1,300 行，包含 `FactionNodeTarget`、`FactionNodeStates` 等巢狀型別（`:2667-2701`）。
  - 同一個 Block 裡的 Contributions 與 Faction Relations 已經採用「partial 檔 + Service」的拆法
    （`MainWindowViewModel.Contributions.cs`、`ContributionService.cs`），可以直接沿用。
- **建議修法**：抽出 `SealedAbyssService`（Pattern B v1、掃描、context 建立）與 `KnowledgeInjectService`（`ResolveKnowledgeList`、
  `ApplyKnowledgeInjectAsync`），VM 只保留 partial wrapper。這也讓 B5-16 的測試可以直接對 service 撰寫。

### B5-24 — 零碎項目
- **嚴重度**：cleanup
- **證據與建議**：
  - `TryReadFarKeyFieldIdx`（`:2144-2163`）在 apply 時再讀一次 block 只為了取得 FAR 的 `_key` index。context 建立時
    （`:3092-3100`）就能一起記下，順便消除 B1-01（deferred 期間讀到 cache 舊資料）的暴露面。目前取的是 schema index，
    所以沒有造成錯誤，但沒有必要。
  - `ScanBulkSealedArtifactCandidates` 在 `_loadedPath is null` 時回傳 `default`（`:2492-2495`），`Candidates` / `SkipDetails` 會是 null，
    與其他分支回傳空集合不一致。
  - `WorldMapBasemapService.TryLoad`（`:35-42`）用沒有型別的 `catch` 吞掉所有例外（包括 `OutOfMemoryException`），
    `SetBasemap` 失敗時也沒有給使用者任何回饋（`WorldMapViewModel.cs:322-326`）。
  - `WorldMapViewModel.UpdateCursorCoords`（`:416-417`）在 `DisplaySide == 0` 時會除以 0（`OnWindowOpened` 取不到 screen 時，`DisplaySide` 維持 800，實際風險很低）。

---

## 3. Audit focus 逐項結論

| 計畫書 Audit focus | 結論 |
|---|---|
| Sealed Abyss 只寫 pre-claim 形狀（Pattern B v1），Pattern A 已從所有路徑移除 | **確認**。`ApplyPatternBv1Writes`（`:2185-2265`）只寫 `farPath` 與 `newPath`；catalog row 與 twin 只讀不寫（`twinPath` 只用於 `DynamicArrayGetU32Elements`，`:3028-3032`；`CatalogElementIdx` 從未用於寫入）。`src/` 裡的「Pattern A」字樣都屬於不相干的 resolver 命名（`NativeKeyInfoCatalogs.cs:1533` 等）。但半套寫入的風險仍在（B5-03）。 |
| `RunDeferred` 呼叫端（≈2397、≈5211）是否有測試 | **兩處都沒有**（B5-16）。逐一對照 deferred 契約：Pattern B v1 的 `_completedTime` absent 時走 `SetScalarFieldPresent(true, ctBytes)`，present 時才進 in-place batch（`:2219-2225, 2243-2248`）；clone 的 patch 依賴 clone 複製來源的 start/end（`mod.rs:2697`），因此 **避開了**「promote then write in place」。knowledge 注入則因為沒檢查 presence，可能 **踩到同一類問題**（B5-06）。兩處都採 partial-success commit（B1-02 / B5-03）。 |
| Captured-element 模板以欄位名稱、而非 index 跨 schema 對應 | **確認**。只有龍的元素是跨存檔帶入，走 `ListInsertElementTemplate`（CRET，依名稱重建；`MountCatalog.cs:76-100`），插入後也用 `FindFieldByName` 取 index（`:4325-4341`）。`MountUnlockMechanicsTests` 對每個 live schema 都跑一次。Rename Mercenary 不搬移元素；knowledge / contribution / Pattern B 的 clone 都在同一份存檔內，沒有跨 schema 問題。 |
| Faction relation-group 編輯與 Abyss gate 欄位尚未實機驗證 | **確認仍未驗證**（B5-14）；另外發現 relation group 的寬度寫死為 u16。 |
| `SealedArtifactChallengeViewModel`、`RenameMercenaryViewModel`、`WorldMapViewModel` 沒有專屬測試 | **確認**（B5-16）。 |

## 4. Cross-cutting checklist（Block 5 範圍）

| 項目 | 結果 |
|---|---|
| Rule 3 — async I/O、UI 執行緒不阻塞 | **違反**：B5-17。Block 5 沒有 `.Result` / `.Wait()`（Grep 確認），但有大量同步 FFI 與檔案 I/O 跑在 UI 執行緒上。 |
| Rule 4 — OS 呼叫走 Core 介面 | **未發現違反**。World Map 的檔案選擇走 Avalonia `StorageProvider`；`File.Exists` 是跨平台 API。只有字串裡的 `%LOCALAPPDATA%` 字面路徑屬於呈現問題（B5-19）。 |
| Rule 5 — AOT、無反射 | **未發現違反**。Block 5 檔案沒有 `typeof` / `Activator` / `System.Reflection`（`MainWindowViewModel.cs:2` 的 `using System.Reflection` 屬於 Block 2 的版本號讀取）。 |
| Rule 7 — magic strings 集中 | **違反**：B5-18。 |
| Rule 12 — 不在消費端做 workaround | **違反（輕微）**：B5-20。 |
| 例外處理：沒有空的 catch 隱藏寫入失敗 | 寫入路徑沒有空 catch。但有「部分寫入後吞掉錯誤且不設 dirty」（B5-03），讀取路徑也有吞錯（B5-15、B5-24），另有非 `CrimsonSaveException` 會讓 process 崩潰（B5-08）。 |
| UI 字串三語系 | 鍵同步 **通過**（抽樣 25 鍵、相關前綴三檔各 298 鍵、placeholder 一致）。但有大量寫死英文的字串（B5-19）。 |

## 5. Pre-scan observations 驗證結果

| # | 觀察 | Block 5 的驗證結果 |
|---|---|---|
| 1 | Rule 6（log）看起來沒有實作 | 屬於 Block 2（見 B2-21）。Block 5 的所有失敗只寫進狀態列與 Journal，沒有任何檔案 log，**與觀察一致**。 |
| 2 | Architecture doc drift（13 個 ViewModel `using CrimsonAtomtic.RustInterop`） | 屬於 Block 6。在本 snapshot 用 `^using CrimsonAtomtic\.RustInterop;` Grep，ViewModels 目錄共 **12 個檔案**，其中 Block 5 有 3 個（`AbyssGatesViewModel`、`RenameMercenaryViewModel`、`WorldMapViewModel`）加上 `MainWindowViewModel`。另外 `Services/FactionRelationService.cs`、`ContributionService.cs` 也直接依賴 RustInterop。「ViewModel 不知道 FFI」這個說法 **不成立**；數字差 1，可能是計數方式不同，請 Block 6 報告核對。 |
| 3 | `UiLanguageService` 的 Rule 4 | 不在 Block 5 範圍。 |
| 4 | Rule 7：沒有集中的 magic-string class | **在 Block 5 範圍確認**（B5-18）。`MountCatalog` 自稱集中檔，但只涵蓋 mount。 |
| 5 | Size hot spots：`MainWindowViewModel.cs` 5,926 行 | **確認** 5,926 行。Block 5 佔約 1,860 行，其中 Sealed Abyss 約 1,300 行（B5-23）。 |
| 6 | CI 只跑 release | 不在 Block 5 範圍。 |
