# Code audit — Block 4: Item editors (2026-11-28)

> **Status: REPORT ONLY.** 依 [code-audit-2026-11-28-plan.md](code-audit-2026-11-28-plan.md)
> 的 Block 4 範圍做靜態審查；本回合 **未修改任何程式碼**，修法只列建議，實作留給後續 fix PR。
>
> - Snapshot：`dev` @ `9ba7ae4`（PR #64）。所有 `file:line` 以此 commit 為準；只寫檔名時指
>   `src/CrimsonAtomtic.Ui/` 底下的檔案。
> - Rust 端對照（唯讀）：`D:\Github\crimson-rs\src\c_abi\mod.rs`（`crimson_save_set_scalar_field_present`
>   `:2880-2954`、`toggle_one_scalar_presence_in_place` `:2961-3022`、`crimson_save_list_remove_element` `:2432-2457`）。
> - 範圍：計畫書 Block 4 的全部 Files（Sockets / Dye / VendorBuyback / FindItems 的 ViewModel + Window、
>   `Services/GemSets.cs`、`Services/KnownDyeService.cs`），以及 `MainWindowViewModel.cs` 指定的
>   922–1033、1290–1378、3324–3489、3603–4289、4386–4929 五段。

## 0. 範圍說明與限制

1. **測試基線未執行**：依指示只做靜態審查，沒有跑 `dotnet run --project src/CrimsonAtomtic.Tests`，所以
   **沒有記錄測試數量**（status.md 記載 PR #60 時為 452）。開始修 Block 4 之前請補跑一次。
2. **範圍外的程式碼只當證據引用**：Block 4 各工具視窗的開啟、dirty 傳遞與「+ Add dye」寫入流程都寫在
   `Views/MainWindow.axaml.cs`（Block 2 的檔案）。這些位置只用來證明 Block 4 的問題確實會被觸發；
   已由 Block 2 報告列出的部分（B2-06、B2-14、B2-25）只交叉引用，不重複計數。
3. **`MainWindowViewModel.cs` 3603–4289 的邊界**：這段後半（`UnlockMountAsync` `:4082`、`UnlockDragonAsync` `:4111`、
   `FillDragonHpAsync` `:4227`）屬於 mount 流程，交給 Block 5。本報告只審查 `AddItemToCurrentListAsync`、
   `GrantItemToContainerAsync`（共用的加物品配方）與 `LearnItemKnowledgeAsync`。
4. **遊戲端行為未實機驗證**：B4-05、B4-12 – B4-15 的判斷依據是程式碼、status.md 記錄的存檔觀察與 Rust 契約，
   沒有進遊戲確認。修正前請先用存檔掃描或實機測試確認。
5. 審查時在 `%TEMP%\audit4\keys.py` 寫了一個比對字串鍵的小腳本，但因權限沒有執行；§3 的三語系結果改用
   Grep 逐鍵確認。該檔案不在 repo 內，可直接刪除。

## 1. 摘要

| 嚴重度 | 數量 | 編號 |
|---|---|---|
| bug | 9 | B4-01 – B4-09 |
| risk | 14 | B4-10 – B4-23 |
| rule violation | 4 | B4-24（UI 字串缺鍵）、B4-25（UI 字串寫死）、B4-26（Rule 7）、B4-27（Rule 3） |
| cleanup | 5 | B4-28 – B4-32 |

**最嚴重的四項**：

- **B4-01**：Dye 編輯器（master + slot editor）與 Vendor Buyback 都是非 modal 視窗，卻 **沒有 mutation-version
  過期檢查**。主視窗 Add Item 一律插在 index 0（`MainWindowViewModel.cs:3744`），同一個 bag 裡後面的物品
  index 全部 +1。之後在 Dye 視窗按 Apply，會把顏色寫到隔壁的物品上，而且不會報錯。Socket 編輯器有這道檢查，這幾個視窗沒有。
- **B4-09**：Add Item / Grant 的「clone → 改 scalar → 設 `_isNewMark`」是三次各自 commit 的 FFI 呼叫。
  中間失敗時，會留下一筆與來源 **byte 完全相同** 的物品（`_slotNo` / `_itemNo` 重複，程式註解自己說這會讓遊戲載入崩潰），
  而且 `IsDirty` 與 Journal 都沒有更新。
- **B4-04 / B4-05**：`DyeSlotRow` 的「原始值」在 Apply 後從不更新。把顏色改回原值時，這次修改不會寫入存檔，
  畫面與存檔就此不一致。另外，每次 Apply 都會把所有 absent 的 scalar 以 0 寫成 present。依 status.md 的觀察，
  `_grimeOpacity` absent 代表 127，所以只改顏色也會順便改掉物品的磨損狀態。
- **B4-07**：Socket 編輯器在「重選同一顆寶石」（程式註解明說這是修復路徑）時，會先寫入 `_validSocketCount`，
  然後回傳 false。這次寫入沒有記 Journal、沒有設 dirty，也沒有同步 snapshot version，結果這個對話框之後的每次編輯
  都會被自己的過期檢查擋下來。

**建議的修正 PR 順序**：
1. **跨視窗一致性**（B4-01、B4-10、B4-11、B4-21）：把 Socket 編輯器的 `_snapshotVersion` 機制抽成共用 helper，
   套用到 Dye、Buyback、FindItems 與 bulk fill。同一個 PR 決定 mutation 的序列化策略（B4-21）。
2. **寫入原子性與帳務**（B4-02、B4-03、B4-07、B4-08、B4-09、B4-18）：任何 mutation 一旦成功就立刻記 Journal 與 dirty；
   多步驟寫入要嘛整批放進 `RunDeferred` 並在失敗時 throw（配合 Block 1 B1-02 的決策），要嘛補上補償刪除。
3. **Dye 值域**（B4-04、B4-05、B4-06、B4-14）：Apply 後重設原始值；只寫使用者實際改過的欄位；absent 的語意以存檔掃描確認。
4. **物品新增的 ID 與上限**（B4-12、B4-13、B4-30）。
5. **測試補齊**（B4-22），接著處理 UI 字串、Rule 3 / 7 與 cleanup（B4-24 – B4-32）。

---

## 2. Findings

### B4-01 — Dye 與 Vendor Buyback 沒有 snapshot 過期檢查；其他視窗做了 length-changing 編輯後，會寫到錯的物品
- **嚴重度**：bug（靜默寫錯物品）
- **位置**：`ViewModels/DyeEditorViewModel.cs:153`（`ListAllItems(out _)` 丟掉 version）、`:381-437`（row 只存位置 index）；
  `ViewModels/DyeSlotEditorViewModel.cs:166-258, 453-486`（Add / Remove / 寫入都沒有檢查）；
  `ViewModels/VendorBuybackViewModel.cs:338-382`；開啟方式 `Views/MainWindow.axaml.cs:1044-1069, 1140-1148, 1228-1248`（都是 `Show`，非 modal）
- **證據**：
  - `ISaveLoader.ListAllItems` 的契約明說位置欄位「go stale on the next length-changing mutation」，並要求呼叫端搭配
    `GetMutationVersion` 使用（`src/CrimsonAtomtic.RustInterop/ISaveLoader.cs:171-177`）。
  - Socket 編輯器完整實作了這道檢查（`SocketEditorViewModel.cs:1093-1149`），註解還引用
    `vendor/crimson-rs/docs/save-mutation-version.md`。Dye 與 Buyback 都沒有。
  - 主視窗的 Add Item 一律把 clone 插在 index 0（`MainWindowViewModel.cs:3741-3749, 3806`），同一個 `_itemList`
    裡原本所有 element 的 index 都會 +1。Remove Element（`:3489`）則會讓後面的 element 往前移。
  - Dye master 列出的範圍包含 inventory bag 裡已經染色的物品（`DyeEditorViewModel.cs:156-157`），所以兩邊會打到同一個 list。
- **重現**：Tools → Edit Item Dyes（保持開啟）→ 主視窗進入某個含已染色物品的 bag，用 Browse Items 加一個物品 →
  回到 Dye 視窗對該染色物品按 Edit → 改顏色並 Apply。寫入的路徑是 `(bag, item+0)`，實際上已經是隔壁的物品。
  只要 schema 欄位相同，`SetScalarField` 就會成功，不會有任何錯誤。
- **同類情況**：
  - 同一個物品可以開出多個 `DyeSlotEditorWindow`，因為每按一次 Edit 就開一個新視窗（`MainWindow.axaml.cs:1044-1069`）。
    在 A 視窗 Add / Remove slot 之後，B 視窗的 `SlotIndex` 就錯位了。B 的 `RemoveSlot` 還會用過時的 `Slots.Count == 1`
    （`DyeSlotEditorViewModel.cs:239`）判斷要把整個 list 設為 absent，可能把 A 剛加的 slot 一起刪掉。
  - Vendor Buyback 的 Remove 用的是過時的 `BuybackElementIdx`，會刪掉另一筆售出物品（`VendorBuybackViewModel.cs:343-346`）。
    VM 只會修正 **自己** 刪除造成的位移（`:354-370`）。
- **建議修法**：把 Socket 編輯器的 `_snapshotVersion` / `IsSnapshotStale` / `ResyncSnapshotVersion` 抽成共用 helper
  （例如 `SnapshotGuard`），讓 `DyeEditorItemRow` 帶著建立時的 version。Dye slot editor 與 Buyback 每次寫入前都要比對，
  自己寫入後再重新同步。版本不符時拒絕寫入，並提示使用者重開視窗。同一個物品只允許開一個 slot editor
  （可以用 dictionary 記住已開的視窗並把它帶到前景）。補一個測試：開著 VM 時在主 loader 上做一次 `ListCloneElement`，
  之後的 Apply 必須被拒絕。

### B4-02 — 「+ Add dye」流程分兩步寫入、失敗時不回復；找不到 `_dyeSlotNo` 時仍回報成功
- **嚴重度**：bug
- **位置**：`Views/MainWindow.axaml.cs:1074-1138`（`AddDyeRequested`）、`:1165-1195`（`ResolveDyeSlotNoFieldIndex`）
- **證據**：
  - 第一步 `SetObjectListPresent`（`:1092-1096`）與第二步 `SetScalarFieldPresent(_dyeSlotNo)`（`:1110-1113`）是兩次獨立的
    length-changing commit，沒有包進 `RunDeferred`，失敗時也沒有補償。
  - 第二步失敗時會跳到 `:1128-1135` 顯示錯誤並 `return`，但第一步已經把 list 物化了。結果是一個 `_dyeSlotNo` absent 的元素
    （= slot 0，status.md:94-97），也就是 class doc 說「pulled the original + Add UX back out」的那種錯誤形狀
    （`DyeEditorViewModel.cs:17-19`）。這條路徑也不會設 dirty。
  - `ResolveDyeSlotNoFieldIndex` 回傳 null 時（`:1108`），第二步被 **靜默跳過**，然後照樣執行
    `NotifyChildApplied()` + `RefreshAsync()`（`:1136-1137`），也就是回報成功。使用者選了 slot 4，存檔裡卻是 slot 0。
  - 沒有寫 Journal 的問題已列為 **B2-06**（Block 2），本項不重複計數。
- **建議修法**：把整個流程移到 VM（與 `DyeSlotEditorViewModel.AddSlot` 的 index==0 分支共用，見 B4-29）。
  `_dyeSlotNo` 找不到時視為失敗。第二步失敗時用 `SetObjectListPresent(false)` 回復第一步，或者兩步都在同一個
  `RunDeferred` 內完成並在失敗時 throw，讓整批 abort。只要有任何寫入落地，就立刻記 Journal 與 dirty。

### B4-03 — `DyeSlotEditorViewModel.AddSlot`：batch 外先 clone，後續失敗時留下一份完整複製的 element 0
- **嚴重度**：bug（Block 1 交接項）
- **位置**：`ViewModels/DyeSlotEditorViewModel.cs:175-183`（clone / present，各自 commit）、`:184-188`（`LoadElement` 為 null 時直接 return）、
  `:189-207`（`RunDeferred` 清掉欄位）、`:215-221`（通用 catch）、`:222-224`（dirty / Journal 只在完全成功時執行）
- **證據**：
  - `ListCloneElement(sourceIndex: 0, destinationIndex: index)` 會立即 commit。接下來的 `RunDeferred` 只要有任何一個 op 丟出例外，
    `RunDeferred` 就會 abort 並 rethrow（`ISaveLoader.cs:111-127`）。abort 只回滾 batch 內的變更，batch 前的 clone 會留下來。
  - 留下的是 element 0 的 byte-identical 複本：`_dyeSlotNo`、RGB、theme 全都相同，等於同一個 dye slot number 出現兩次。
    通用 catch 只顯示錯誤並 `LoadSlots()`（`:219`），使用者會看到兩列一樣的 slot。
  - `LoadElement` 回傳 null 的路徑（`:184-188`）同樣在 clone 之後直接 return，也不會設 dirty。
  - 這兩條路徑都 **沒有** 設 `IsDirty`，也沒有寫 Journal。存檔已經被結構性修改（`HasStructuralEdit` = true），但關閉時不會提示。
- **建議修法**：clone 之後的任何失敗，都用 `ListRemoveElement(index)`（index==0 的情況用 `SetObjectListPresent(false)`）補償回復，
  補償本身失敗時要記 Journal 並設 dirty，同時告訴使用者存檔目前的狀態。另一種做法是向 crimson-rs 要一個
  「clone + 清除欄位」的單一 ABI，讓整個動作在一次呼叫內完成。

### B4-04 — `DyeSlotRow` 的原始值在 Apply 後不會更新：改回原值時不會寫入，畫面與存檔不一致
- **嚴重度**：bug（靜默遺失寫入）
- **位置**：`ViewModels/DyeSlotEditorViewModel.cs:518-523`（`_orig*` 全是 `readonly`）、`:729-751`（差異比對）、`:753-757`、`:807-808`
- **證據**：
  - `Apply()` 一律拿目前的值和建構時的 `_orig*` 比對，成功後不會把 `_orig*` / `_orig*Present` 更新成剛寫入的值，
    而且這些欄位是 `readonly`，也無法更新。
  - **重現**：R 原本是 10（present）→ 改成 200 → Apply（存檔變成 200）→ 改回 10 → Apply。這時 `R != _origR` 為 false，
    所以不會寫入，畫面顯示「No pending changes.」。**存檔仍是 200，畫面卻顯示 10**。
  - 反過來，之後每次 Apply 都會重寫這次 session 中改過的所有欄位，並再記一次 Journal（`LogSlotApplied`，`:406-412`）。
  - 原本 absent 的欄位第一次 Apply 後已經是 present，但之後的 Apply 仍以 `wasPresent=false` 走
    `SetScalarFieldPresent(makePresent: true)`（`:467-472`）。這條路能正確覆寫，是因為 Rust 的 **實作** 會覆寫值
    （`mod.rs:3009-3015`）。但 Rust 的 **文件**（`mod.rs:2902-2905`）與 status.md 的 gotcha（`status.md:365-366`）
    都寫「present(1) on an already-present field is a no-op」。只要哪天實作改成和文件一致，這裡就會變成靜默遺失寫入。
- **建議修法**：Apply 成功的欄位，要把 `_orig*` 與 `_orig*Present` 更新成剛寫入的值（改成可寫欄位，或在 Apply 後重新
  `LoadSlots()`，但後者會丟掉其他列未套用的編輯）。另外向 crimson-rs 回報 `mod.rs:2902-2905` 的文件與實作不一致，
  請他們擇一修正，並同步更新 status.md 的 gotcha。

### B4-05 — Dye Apply 會把所有 absent 的 scalar 以 0 寫成 present；`_grimeOpacity` absent ≠ 0
- **嚴重度**：bug（改到使用者沒碰的欄位；值域語意錯誤）
- **位置**：`ViewModels/DyeSlotEditorViewModel.cs:623-660`（absent 讀成 0）、`:729-738`（`|| !_orig*Present` 會強制寫入）、
  `:713-721`（`ApplyPickedColor` 只把 A 補成 0xFF）；`Views/DyeSlotEditorWindow.axaml:101`（Grime 允許 -128..127）
- **證據**：
  - 每個條件都是 `(值不同 || !原本 present)`。所以只要某個 channel 原本 absent，**任何一次 Apply** 都會把它以畫面上的預設值 0
    寫成 present，即使使用者只改了 theme 或材質。
  - status.md:109-113 記錄的實機觀察：111147 三個部位的 `_grimeOpacity` 是 85 / 101 / **absent**，對應遊戲 UI 的耐久 33 / 20 / 0，
    符合「repair = 100 − grime × 100 / 127，absent = 127」。依這個模型，第三個部位按一次 Apply（例如只換顏色），
    grime 會從 absent（127）變成 0，耐久從 0% 變成 100%。這是使用者沒有要求的修改，而且 Journal 只會寫「Grime」，看不出影響。
  - 用 `AddSlot` 或「+ Add」新增的 slot，所有欄位都是 absent（`:201-205`）。使用者只要選 theme 再 Apply，
    就會寫入 R=G=B=0、A=0、Grime=0。其中 A=0 的語意未知，可能是透明。
  - 這正是 status.md「ABSENT is a value」gotcha（`status.md:333-343`）要避免的情況：先確認遊戲會不會寫出 0，
    如果不會，absent 就是另一個值。
- **建議修法**：
  - 只寫使用者實際改過的欄位。用每個欄位的 dirty flag 取代 `|| !present`。
  - 欄位 absent 時，UI 應顯示「(absent)」而不是 0（例如 `NumericUpDown` 綁 nullable）。
  - 以存檔掃描確認 `_grimeOpacity` / `_dyeColorA` 的值域與 absent 語意，必要時請 crimson-rs 在 schema 端標註 absent 的預設值
    （Rule 12：語意應落在 parser / schema 層，不要在 UI 端猜）。
  - Grime 的輸入範圍應依確認後的值域收窄。

### B4-06 — `DyeSlotRow.Apply` 部分失敗時，錯誤訊息被「Applied: …」蓋掉
- **嚴重度**：bug（誤報成功）
- **位置**：`ViewModels/DyeSlotEditorViewModel.cs:782-787`（失敗時設定 `LastError` 並結束 lambda）、`:798-808`
- **證據**：只有在「第一個寫入就失敗」（`appliedLabels.Count == 0`）時才會保留錯誤訊息。如果第 3 個欄位失敗、前 2 個成功，
  `:808` 會把 `LastError` 改成「Applied: R, G.」，失敗的欄位與錯誤碼完全看不到。Journal 也只記成功的欄位。
- **建議修法**：失敗時把成功與失敗分開顯示，例如「Applied: R, G. Failed: Material — <err>」。這條 partial-success 路徑要依
  Block 1 B1-02 的決策重新檢視：是否改成整批 abort。

### B4-07 — `ApplyGemPick` 開啟 socket 後回傳 false：自己的寫入讓之後的編輯全被擋下，也沒有記帳
- **嚴重度**：bug
- **位置**：`ViewModels/SocketEditorViewModel.cs:900-903`（先執行 `TryEnsureSocketOpened`）、`:904-908`（同一顆寶石）、
  `:913-921`（iteminfo 未載入）、`:961-968`（寶石寫入失敗）、`:978-990`（`IsDirty` / Journal / `ResyncSnapshotVersion` 只在完全成功時執行）
- **證據**：
  - `TryEnsureSocketOpened` 在 slot 不在已開啟範圍內時會真的寫入：`SetScalarFieldPresent` 或 `SetScalarField`（`:1176-1203`），
    而且會更新所有同一物品的 row（`:1222-1229`）。
  - 接下來三個 early return 都在 `ResyncSnapshotVersion()` 之前：
    1. **同一顆寶石**（`:904-908`）。註解 `:895-899` 明說「re-picking the gem already there is the obvious way a user tries to
       trigger that repair」，也就是說這正是設計好的修復路徑。
    2. `ResolveGemEndurance` 回傳 null（iteminfo 未載入）。
    3. 寶石寫入丟出 `CrimsonSaveException`。
  - 非 deferred 模式下，這次寫入會讓 mutation version +1，但 `_snapshotVersion` 沒有跟著更新。所以 **下一次** 任何編輯都會被
    `RejectIfSnapshotStale` 擋下，顯示「The save was modified in another window」（`:1137-1149`），使用者只能關閉再重開。
  - 這次寫入沒有記 Journal、沒有設 `IsDirty`。狀態列還顯示「Same gem as current — no write performed.」（`:906`），與事實相反。
    如果這是唯一的修改，關閉主視窗時不會有任何提示（Journal 是空的，`MainWindow.axaml.cs:111`）。
- **建議修法**：
  - 先做不會寫入的檢查（`ResolveGemEndurance`），再執行 `TryEnsureSocketOpened`。
  - `TryEnsureSocketOpened` 回傳「是否有寫入」。有寫入時，不管後續結果如何都要 `IsDirty = true`、記一筆 Journal
    （例如「Opened socket N on X」）、`ResyncSnapshotVersion()`。
  - 同一顆寶石且確實做了修復時，狀態列改為「Re-opened socket N」。
  - 補測試：同一顆寶石重選在已開啟範圍外的 slot，必須寫入 count、記 Journal，而且下一次 Fill 不能被擋下。

### B4-08 — Apply-Set 的 commit 失敗會回滾存檔，但 row 狀態與 Journal 仍宣稱已套用
- **嚴重度**：bug（Block 2 交接項）
- **位置**：`ViewModels/SocketEditorViewModel.cs:546-588`；batch 內呼叫的 `ApplyGemPick` 會修改 row 與 Journal（`:972-990`）；
  `TryEnsureSocketOpened` 會更新同物品的所有 row（`:1222-1229`）
- **證據**：
  - `EndDeferredRedecode` 失敗時，handle 會「auto-rolled-back to the pre-begin snapshot」（`ISaveLoader.cs:68-77`）。
  - 但 batch 內每個成功的 slot 都已經執行了 `row.SetFilled(...)`、`IsDirty = true`、`_journal.Log(...)`（`:974-984`），
    同物品的 row 也已標成 `ValidSocketCountPresent = true`、`ValidSocketCountRangeStale = true`。commit 失敗的 catch（`:581-588`）
    只改了狀態列文字。
  - 結果：
    - Journal 列出根本不存在的變更。關閉主視窗時會提示「有未存檔變更」，Save 寫出去的卻是回滾後的樹。
    - row 以為 slot 已經有寶石，下一次 Fill 會走 Change 分支，對 absent 欄位做 in-place 寫入，得到 `NOT_SCALAR`。
    - row 以為 `_validSocketCount` 已經 present，`TryEnsureSocketOpened` 會走 stale-range 的 demote + promote 分支。
  - Block 2 交接（B2 §5）指出這裡「沒有寫 Journal 也沒有設 dirty」。實際情況正好相反：**多寫了** Journal、**多設了** dirty。
- **建議修法**：進入 batch 前先保存每個受影響 row 的狀態，commit 失敗時還原；或者直接把對話框標成失效，要求使用者重開。
  Journal 不要在 batch 內逐 slot 記錄，改成 commit 成功後彙總記一筆。

### B4-09 — Add Item / Grant：clone 與修改欄位不是原子操作，失敗時留下會讓遊戲崩潰的重複物品
- **嚴重度**：bug（資料完整性）
- **位置**：`ViewModels/MainWindowViewModel.cs:3802-3828`（`AddItemToCurrentListAsync`）、`:3858-3864`；
  `:3994-4019`、`:4033-4036`（`GrantItemToContainerAsync`）
- **證據**：
  - 流程是三次各自 commit 的 FFI 呼叫：`ListCloneElement` → `SetScalarFieldsBatch`（`_itemKey` / `_stackCount` / `_slotNo` /
    `_itemNo` / `_transferredItemKey`）→ `SetScalarFieldPresent` 或 `SetScalarField`（`_isNewMark`）。
  - batch 失敗時（例如 schema 寬度與寫死的 byte 數不符，見 B4-30），clone 已經落地，而且與來源 byte 完全相同。同一個方法的註解
    （`:3718-3723`）明說「duplicate `_slotNo` / duplicate `_itemNo` … crashes the game on load」。
  - 錯誤分支只設定 `BulkOpStatus`，**不設** `IsDirty`、**不寫** Journal（`:3858-3864`、`:4033-4036`）。這筆壞資料會留在記憶體中的存檔，
    下一次任何編輯後的 Save 都會把它寫出去，而 Journal 完全沒有記錄。
  - 只有 `_isNewMark` 那一步失敗時，物品其實已經完整加入，畫面卻顯示「Add failed」。使用者重試就會多出一個物品。
- **建議修法**：
  - (a) clone 之後的任何失敗都用 `ListRemoveElement(dstIdx)` 補償，補償失敗時明確提示並記 Journal / 設 dirty。
  - (b) 或者把三步放進同一個 `RunDeferred` 並在失敗時 throw，讓整批 abort。要先確認 deferred 模式下對 clone 出來的 element
    做 in-place 寫入是合法的，也就是 byte range 沿用來源的長度；status.md「deferred batch changes which primitives are valid」
    要求這點必須有測試證明。
  - `_isNewMark` 的失敗應降級成警告，不要判定整個操作失敗。
  - 兩個方法共用同一份配方（見 B4-29），修一次即可。

### B4-10 — Find Items 的「Go」不檢查 snapshot 是否過期，會導航到別的物品
- **嚴重度**：risk
- **位置**：`ViewModels/FindItemsViewModel.cs:72`（`RequestGoto`）、`:142-161`（`IsStale` 只是個屬性，沒有人在導航前檢查）；
  `Views/MainWindow.axaml.cs:1536-1554`；`ViewModels/MainWindowViewModel.cs:123-146`（只檢查 block 是否存在）
- **證據**：對話框設計上會一直開著（`MainWindow.axaml.cs:1551-1552`）。在 B4-01 描述的 Add Item 插在 index 0 之後，
  `row.Record` 的 `ItemElementIndex` 已經指向另一個物品，`NavigateToInventoryItemAsync` 只做邊界檢查就導航過去。使用者以為看到的是剛搜尋的物品，
  接著在主視窗編輯，就會改到錯的物品。另外，`IsStale` 的 XML doc 說它是「Used by the Refresh button's CanExecute」，
  但 `RefreshCommand` 根本沒有 CanExecute（`:165`），而且 `IsStale` 只在 Refresh 時發出一次 PropertyChanged，畫面上的狀態不會即時更新。
- **建議修法**：`RequestGoto` 前先檢查 `IsStale`。過期時自動 Refresh，再用 `(InventoryKey, ItemNo)` 重新定位該列，
  找不到就提示。footer 改用定時器或 mutation 事件更新。

### B4-11 — Bulk fill 在「掃描 → 確認對話框 → 套用」之間沒有重新檢查 version
- **嚴重度**：risk
- **位置**：`ViewModels/MainWindowViewModel.cs:3361-3377` → `:3404` → `:3428-3443`（`BulkFillItemListMaxStackAsync`）；
  `:4733-4791` → `:4809` → `:4818-4829`（`FillAllStacksAcrossInventoriesAsync`）
- **證據**：
  - 候選清單是在等待確認對話框 **之前** 建好的，依據是主視窗的 nav frame cache（`parent.Elements`）或背景掃描。等待確認期間，
    其他非 modal 工具視窗仍可操作（`ShowDialog` 只停用 owner），可以做 length-changing 修改。
  - 主視窗的 `ElementsFrame` 本身也可能早就過期：工具視窗修改 loader 後不會通知主視窗刷新（B4-01）。
  - `SetScalarFieldsBatch` 只驗證型別與長度，所以寫到同 class 的另一個物品時一樣會成功，`_stackCount` 被覆寫成別人的上限值。
- **建議修法**：掃描前記下 `GetMutationVersion()`，套用前比對，不符就重新掃描或中止。主視窗的 nav frame 也應記錄建立時的 version，
  在 Add Item / Bulk fill / Remove 之前檢查（與 Block 2 B2-14 的單一 dirty 來源一起設計）。

### B4-12 — `_itemNo` 唯一性的範圍不一致：Add Item / Grant 只看同一個 list，Contributions 看整個 block
- **嚴重度**：risk
- **位置**：`ViewModels/MainWindowViewModel.cs:3724-3727`、`:3956-3958`、`:4419-4443`（`ScanItemListMaxes` 只掃傳入的 list）；
  對照 `Services/ContributionService.cs:186, 251-263`（「max `_itemNo` across the whole block」）、`docs/dev-log.md:275-276`
- **證據**：Add Item 的註解說 `_itemNo` 是「game uses this as a persistent ID」，重複會讓遊戲載入崩潰（`:3720-3723`）。
  但新值只取 **目前這個 `_itemList`** 的最大值 + 1。另一個 bag 只要已經有 `max+1`，就會撞號。後寫的 `ContributionService`
  已改用整個 InventorySaveData block 的最大值，兩條路徑不一致。
- **建議修法**：抽成共用的 `NextItemNo(BlockDetails inventoryBlock)`，至少掃整個 InventorySaveData block。另外請 crimson-rs 或用存檔掃描確認
  `_itemNo` 的唯一範圍是否還包含裝備、傭兵與商店清單。如果是，就需要一個 save-wide 的 `max_item_no` ABI。

### B4-13 — Stack fill 忽略 iteminfo 的 `ApplyMaxStackCap` 旗標，會寫出超過上限的數量
- **嚴重度**：risk（值域）
- **位置**：`ViewModels/MainWindowViewModel.cs:4639-4696`（`TryComputeTargetStack`，其中 `:4675-4683` 向上取整到 max 的倍數）、
  `:4518-4595`；旗標定義見 `ViewModels/FindItemsViewModel.cs:441-442`（「Caps runtime stack count to max_stack_count」）
- **證據**：max ≤ 100 的物品會被取整到 max 的倍數（例如 120 → 150，max = 50），這是刻意的「多疊」設計。但帶有
  `ApplyMaxStackCap` 的物品，遊戲執行期會把數量限制在 `max_stack_count`。寫入 150 的結果可能是被截斷，也可能是不被接受，
  目前沒有驗證。`StackFillCapTests` 只測純數學，不涵蓋這個旗標。
- **建議修法**：`TryBuildSingleCandidate` 讀 `LookupItemInfoSummary(itemKey).Flags`，有 `ApplyMaxStackCap` 時目標值最多是 max。
  補一個測試案例，並用存檔確認遊戲本身是否寫過超過 max 的數量。

### B4-14 — 選 slot 0 時把 `_dyeSlotNo` 明確寫成 0；遊戲對 slot 0 的寫法是 absent
- **嚴重度**：risk（未經實機驗證的存檔形狀）
- **位置**：`ViewModels/DyeSlotEditorViewModel.cs:193-199`；`Views/MainWindow.axaml.cs:1110-1113`
- **證據**：status.md:94-97 與 :110-111 記錄：遊戲把 slot 0 存成 `_dyeSlotNo` absent（「absent(0) / 1 / 7」）。兩個新增路徑都會把 0
  寫成 present。status.md:220 也記載 slot 的新增與移除「Not checked in-game」。依「ABSENT is a value」的經驗，explicit 0 可能是
  遊戲從來不會寫出的形狀。
- **建議修法**：先掃描存檔，確認 `_dyeSlotNo` 是否出現過 present 0。如果從未出現：選 slot 0 時把欄位保持或改為 absent
  （clone 的情況要 demote）。

### B4-15 — Vendor Buyback 刪除最後一筆時留下 present 但長度為 0 的 list；Remove 也沒有確認
- **嚴重度**：risk
- **位置**：`ViewModels/VendorBuybackViewModel.cs:338-382`；Rust `mod.rs:2432-2450`（`list_remove_element` 只把 count 減 1，不會轉成 absent）
- **證據**：Dye 的 `RemoveSlot` 刻意在最後一個元素時改用 `SetObjectListPresent(false)`，理由是「which is how the game stores an
  undyed item」（`DyeSlotEditorViewModel.cs:229-242`）；Faction relation 也這樣做（dev-log.md:288-289）。Buyback 沒有做這個處理，
  `_storeSoldItemDataList` 會變成 present + count 0，這個形狀是否合法沒有驗證。另外 Remove 直接執行，不像主視窗的 `RemoveElementAsync`
  會先確認。
- **建議修法**：先掃描存檔，確認遊戲是否寫過空的 present `_storeSoldItemDataList`。如果沒有，刪除最後一筆時改用
  `SetObjectListPresent(false)`。Remove 前加上確認（或提供 Undo）。

### B4-16 — 自訂寶石組可以存任意 u32；Apply-Set 會把非寶石寫進 socket
- **嚴重度**：risk
- **位置**：`ViewModels/CustomGemSetsEditorViewModel.cs:61-83`；`ViewModels/SocketEditorViewModel.cs:1258-1266`（未知 key 的耐久回退成 65535）；
  對照 `Services/GemSets.cs:23-28`
- **證據**：
  - `BuiltInGemSets` 的註解明確警告：「a mistyped key would otherwise be written into a socket silently, since the save format accepts
    any u32 as a gem」。內建組因此逐一驗證過，但自訂組只檢查「是否為非 0 的 u32」（`:68-76`）。
  - Item picker 有 gem prefix 過濾（`MainWindow.axaml.cs:1972-1975`），Apply-Set 卻繞過了這層過濾。
  - 空白格會被壓縮（`:67, 75`）：使用者把第 2 格留空、第 3 格填寶石，那顆寶石會被套到 slot 1，而不是 slot 2。
- **建議修法**：Save 時以 iteminfo 驗證每個 key（與 `BuiltInGemSets_ContainOnlyRealGems` 測試用同一套判斷，見 B4-17）。
  空白格保留位置，例如存成 0，代表「這個 slot 不動」，Apply 時跳過。

### B4-17 — 判斷「寶石」有兩套標準：picker 用 string_key prefix，內建組用 `item_type` / `category_info`（Rule 12）
- **嚴重度**：risk
- **位置**：`ViewModels/SocketEditorViewModel.cs:100-113`（`GemStringKeyPrefixes`，依據是 1.06 的 189 筆清單）；
  `Services/GemSets.cs:23-25`（「`item_type == 74 && category_info == 2501`, 190 entries on game 2.00」）
- **證據**：picker 用的 prefix 是從 1.06 推出來的啟發式規則，而 iteminfo 已經有權威欄位。兩者如果有出入（新寶石換了命名，
  或非寶石剛好用了相同 prefix），picker 就會漏掉或多出項目。這屬於「用消費端的字串判斷取代 gamedata 欄位」。
- **建議修法**：在 `LocalizationProvider` 或 iteminfo bridge 提供 `IsGem(itemKey)`（以 item_type / category_info 判斷），
  picker、自訂組驗證與測試都改用它。

### B4-18 — Socket 的其他非原子寫入序列
- **嚴重度**：risk
- **位置**：`ViewModels/SocketEditorViewModel.cs:955-958`（Change：兩次獨立的 `SetScalarField`）、`:938-947`（Fill：
  `SetScalarFieldsPresentBatch`，在 Apply-Set 的 deferred batch 內執行）、`:1186-1198`（stale range：先 demote 再 promote）
- **證據**：
  - Change 分支先寫 `_itemKey`、再寫 `_currentEndurance`。第二步失敗時，新寶石已經寫入，耐久卻還是舊值，例如 65535 留在上限 100 的寶石上。
    這正是 class doc 說「engine rejects」的形狀（`:84-96`）。row 也沒有更新（`:961-968` 直接 return）。
  - Block 1 B1-02 指出：deferred 模式下 batch 失敗會留下部分套用的結果。Apply-Set 裡 Fill 的兩個 op 如果只成功第一個，
    就會 commit 出「`_currentEndurance` present、`_itemKey` absent」的 slot（mask 0x01），而 `ApplyGemPick` 吞掉例外（`:961-968`），
    整批照樣 commit。
  - stale-range 分支先 demote，promote 失敗時，`_validSocketCount` 會被留在 absent，但這是在 batch 內，所以後續寶石照樣寫入。
- **建議修法**：Change 改用 `SetScalarFieldsBatch`（immediate 模式下是 all-or-nothing）。deferred 內的失敗改成 throw，讓整批 abort
  （或採用 B1-02 的新 API）。

### B4-19 — Apply-Set 在呼叫 `ApplyGemPick` 前就跳過同一顆寶石，在範圍外的 slot 永遠不會被修復
- **嚴重度**：risk
- **位置**：`ViewModels/SocketEditorViewModel.cs:555-558`；對照 `:895-903`
- **證據**：已填寶石但在 `_validSocketCount` 範圍外的 slot，會被遊戲整個物品重新封印（`:887-893`）。單一 Pick 刻意把
  `TryEnsureSocketOpened` 放在同寶石判斷之前，就是為了修復這種情況。Apply-Set 卻先用 `continue` 跳過，最後還回報
  「no changes (every targeted slot already matches)」（`:607-611`）。
- **建議修法**：Apply-Set 對已填相同寶石的 slot，仍然執行「開啟 socket」的檢查與寫入（B4-07 修好之後，可以直接交給 `ApplyGemPick` 處理）。

### B4-20 — `async void` 路徑的例外沒有完整捕捉
- **嚴重度**：risk（process 崩潰）
- **位置**：`Views/MainWindow.axaml.cs:1074`（`AddDyeRequested += async row => …`，`Action<T>` 實際上是 async void）、`:1087-1135`
  只捕捉 `CrimsonSaveException`；`:1902-1935`（`OnEditItemSocketsClick` 同樣只捕捉 `CrimsonSaveException`）；
  `:1389-1400`（`OnKnownDyesClick` 沒有 try）
- **證據**：`ListAllItems` / `LoadBlockDetails` 在沒有載入存檔時會丟 `InvalidOperationException`（`ISaveLoader.cs:157-159, 207-209`）。
  `ResolveDyeSlotNoFieldIndex` → `LoadBlockDetails`（`:1171`），以及 `SocketEditorViewModel.TryCreate` → `ListAllItems`，
  都可能丟出這個例外，在 async void 中就會讓 process 崩潰。`KnownDyesViewModel.CreateAsync` 的 `Task.Run` 例外（`KnownDyesViewModel.cs:63`）
  同理。Block 2 的 B2-19 已列出 Block 5 的同類問題。
- **建議修法**：工具視窗的 handler 統一包一層 `try { … } catch (Exception ex) { await ShowAlert… }`，或者讓這些 handler 改走 B2-19 建議的共用 helper。

### B4-21 — 背景執行緒的多步 mutation 可能與 UI 執行緒的 `RunDeferred` 交錯
- **嚴重度**：risk（機率低）
- **位置**：`ViewModels/MainWindowViewModel.cs:3802-3828, 3994-4019`（`Task.Run` 內的三步寫入）；
  `ViewModels/DyeSlotEditorViewModel.cs:189, 778`、`ViewModels/SocketEditorViewModel.cs:546`（UI 執行緒的 `RunDeferred`）
- **證據**：deferred batch 是 handle 全域的狀態，而且不能巢狀（`ISaveLoader.cs:57-59`）。Rust 端的鎖只序列化 **單一呼叫**。
  Add Item 在背景執行 clone / batch / present 的空檔，使用者可以在非 modal 的 Dye 或 Socket 視窗按 Apply。這時：
  - 背景的後續呼叫會落進 UI 端開的 batch。UI 端一旦 abort，就會連 Add Item 的寫入一起回滾，而 Add Item 照樣回報成功。
  - 或者 UI 端的 `BeginDeferredRedecode` 碰上別人的 batch，丟出 `BATCH_IN_PROGRESS`。
- **建議修法**：主 VM 提供單一的 async mutation gate（`SemaphoreSlim`），所有寫入（含工具視窗）都經過它。
  或者在 Block 1 的 `NativeSaveLoader` 讓「batch 開啟中、而且來自別的執行緒」的呼叫直接失敗。

### B4-22 — 測試缺口：deferred 模式的 dye 寫入、Socket 修復路徑、過期檢查與多個工具都沒有測試
- **嚴重度**：risk
- **位置**：`src/CrimsonAtomtic.Tests/`（Block 4 相關測試只有 `SocketEditorTests`、`DyeSlotNumberTests`、`KnownDyeServiceTests`、
  `StackFillCapTests`）
- **證據**：
  - `RunDeferred` 的三個呼叫點中，只有 Socket Apply-Set 有測試（`SocketEditorTests.cs:513, 631, 694`）。
    `DyeSlotEditorViewModel` 的兩個 batch（`AddSlot` `:189`、`Apply` `:778`）完全沒有測試。`DyeSlotNumberTests` 只測 slot number 查詢。
    status.md:366-369 寫得很清楚：「Any code path that only ever runs immediate is untested against the deferred contract」。
  - 下列路徑也沒有測試：`ApplyGemPick` 的同寶石修復與自身寫入後的 snapshot（B4-07）、stale gate、commit 失敗（B4-08）。
  - `VendorBuybackViewModel`、`FindItemsViewModel`、`AddItemToCurrentListAsync` / `GrantItemToContainerAsync`、
    `CustomGemSetsEditorViewModel`、`KnownDyesViewModel`、`DyeEditorViewModel` 都沒有任何測試引用。grep 只在
    `NativeSaveLoaderTests.cs:1728` 找到一個常數引用。
- **建議修法**：優先補上：Dye Apply / AddSlot 在 live save 上的 round-trip（含 absent 欄位與連續兩次 Apply，對應 B4-04 / B4-05）、
  Add Item 失敗補償（B4-09）、過期拒絕（B4-01）、`ApplyMaxStackCap`（B4-13）。

### B4-23 — 掃描時讀取失敗的 block 被靜默跳過，結果不完整也不告知使用者
- **嚴重度**：risk（低）
- **位置**：`ViewModels/SocketEditorViewModel.cs:656-667`（失敗的 block 不放進 cache，同一個 block 的每筆記錄都會重試一次）、
  `ViewModels/DyeEditorViewModel.cs:158-169`、`ViewModels/VendorBuybackViewModel.cs:147-155`、`ViewModels/MainWindowViewModel.cs:4743-4751`
- **證據**：這些 catch 只是 `continue`，對話框照常開啟，計數看起來正常，但少了一整個 block 的物品。Bulk fill 的確認訊息與
  「Nothing to fill」也會因此誤導使用者。
- **建議修法**：記錄失敗的 block 數量，顯示在狀態列（例如「2 block(s) could not be read」）。失敗結果也要放進 cache，避免重複讀取。

### B4-24 — Rule：五個 UI 字串鍵在三個語系字典中都不存在
- **嚴重度**：rule violation（UI 字串）
- **位置**：`ViewModels/DyeSlotEditorViewModel.cs:217`（`DyeSlotAddFailed`）、`:224`（`JournalDyeSlotAdded`）、`:226`（`DyeSlotAdded`）、
  `:255`（`JournalDyeSlotRemoved`）、`:257`（`DyeSlotRemoved`）
- **證據**：用 Grep 逐鍵比對 `Resources/Strings/en.axaml` / `ja.axaml` / `zh-TW.axaml`，這五個鍵三份都沒有（Block 4 查詢的其他
  110+ 個鍵都存在）。`UiText.Format` 會使用英文 fallback，所以 ja / zh-TW 使用者看到的是英文，Journal 也會記成英文。
  這是 2026-11-27 新增 slot 增刪功能時漏掉的。
- **建議修法**：三份字典都補上這五個鍵。另外建議加一個測試：掃描 `UiText.Get/Format("<key>"` 的字面值，斷言每個鍵在三份字典中都存在
  （比只比較三份字典彼此一致更能抓到這類遺漏）。

### B4-25 — Rule：UI 字串寫死在 C#（含原生例外訊息直接顯示）
- **嚴重度**：rule violation（UI 字串）
- **位置**（代表性，非全部）：
  - `SocketEditorViewModel.cs:220, 222`（`FilterCountText`）、`:435`（「slot / slots」）、`:452, 480`（「Custom Set (…)」）、
    `:915, 1143`（`LastError`）、`:1620`（「(empty)」）、`:1639, 1641`（`StateLabel`：「Filled」、「Open」、「Closed (will open on Fill)」…）
  - `DyeSlotEditorViewModel.cs:148-149, 601`（「Slot {0}」）、`:755, 794-795, 808`、`:848`（「sub」）；`DyeSlotPickerViewModel.cs:62-63`
  - `DyeEditorViewModel.cs:74-82`（`ResultsCountText` 的四種狀態）
  - `FindItemsViewModel.cs:78`、`:121-129`、`:140`、`:323`
  - `VendorBuybackViewModel.cs:279`（「Store {0}」）
  - `CustomGemSetsEditorViewModel.cs:80, 131`；`Services/GemSets.cs:38-68`（「Built-in Set N」會顯示在下拉選單）；
    `Views/CustomGemSetsEditorWindow.axaml:62-78`（「ItemKey #N」）
  - `MainWindowViewModel.cs:3884-3934, 4035`（Grant 的錯誤訊息會顯示在 Mount 對話框）
  - 原生 `ex.Message` 直接進 UI：`DyeSlotEditorViewModel.cs:250, 481`、`VendorBuybackViewModel.cs:350-351`、
    `SocketEditorViewModel.cs:966, 1210`（Block 1 B1-21）
  - `OpenGemPicker` 的「Pick」與 tooltip（`MainWindow.axaml.cs:1977-1978`）已列為 B2-25，不重複計數。
- **建議修法**：全部改用 `UiText.Get/Format`，並補齊三語系字典。原生錯誤改為「已翻譯的錯誤碼說明 + (code N)」，`ex.Message` 只放在 tooltip 或 log。

### B4-26 — Rule 7：magic string 沒有集中；錯誤碼寫成 `-16` 字面值
- **嚴重度**：rule violation
- **位置**：
  - schema 欄位與 class 名稱寫成字面值：`DyeSlotEditorViewModel.cs:193, 201, 339-347, 636-657`、`Views/MainWindow.axaml.cs:1193`、
    `MainWindowViewModel.cs:927, 3620, 3691, 3697, 3711, 3886, 3901, 3912, 3927, 3929, 3951-3952, 4402-4405, 4432, 4436,
    4532-4539, 4739, 4755, 4767`
  - 每個檔案各自定義 private const（`SocketEditorViewModel.cs:56-75`、`VendorBuybackViewModel.cs:38-53`、`DyeEditorViewModel.cs:32-34`），
    其中 `"_itemKey"` 至少定義了 3 次
  - 錯誤碼：`DyeSlotEditorViewModel.cs:209`、`Views/MainWindow.axaml.cs:1116` 都寫 `ex.ErrorCode == -16`。對應常數
    `NativeMethods.NOT_FOUND` 是 `internal`（`src/CrimsonAtomtic.RustInterop/NativeSaveLoader.cs:1722, 1743`），UI 專案用不到。
    Block 3 §4 已記錄這項交接。
- **建議修法**：`CrimsonAtomtic.Ui` 加一個集中、有註解的 `SaveSchemaNames`（欄位與 class 名稱）。RustInterop 公開
  `CrimsonErrorCode` 常數（Block 1 B1-18）。UI 改成 `ex.ErrorCode == CrimsonErrorCode.NotFound`。

### B4-27 — Rule 3：同步檔案 I/O 與耗時的 FFI 跑在 UI 執行緒
- **嚴重度**：rule violation（檔案 I/O）；risk（FFI 造成 UI 凍結）
- **位置**：
  - 同步檔案 I/O：`CustomGemSetsEditorViewModel.cs:85-87`（`AppSettingsStore.Load` + `TrySave`）、`:105`；
    `MainWindowViewModel.cs:66-70`（`LoadCustomGemSets`，由 `MainWindow.axaml.cs:1925` 與 `SocketEditorWindow.axaml.cs:32` 在 UI 執行緒呼叫）
  - UI 執行緒上的 FFI：`SocketEditorViewModel.TryCreate`（`ListAllItems` + 每個 block 一次 `LoadBlockDetails`，`MainWindow.axaml.cs:1920`）；
    `ApplyGemPick` / `ApplyClear` / `TryEnsureSocketOpened` 的 length-changing 呼叫（介面文件說每次約 1 秒，`ISaveLoader.cs:522-527`；
    在從未開過 socket 的物品上 Fill 一次就是兩次）；`DyeSlotEditorViewModel` 建構子與 `AddSlot` / `RemoveSlot`（Block 1 交接項）；
    `VendorBuybackViewModel.TryCreate:150`；mutation 後的重新讀取 `MainWindowViewModel.cs:3449, 3832, 4024`，以及 `:3894`（Grant 的讀取）
- **證據**：同一類工作在其他地方已經放進 `Task.Run`（`DyeEditorViewModel.RefreshAsync:114`、Add Item `:3802`、Fill All `:4733`），
  做法不一致。
- **建議修法**：設定讀寫改成 async API。工具視窗的初次掃描與每次寫入，都包成 `await Task.Run(...)`，並搭配 B4-21 的 mutation gate；
  寫入期間停用 UI 按鈕。

### B4-28 — 文件漂移與錯置的 XML doc
- **嚴重度**：cleanup
- **位置與內容**：
  - `SocketEditorViewModel.cs:230-231`：說物品以 `(BlockIndex, BagIndex, ItemIndex)` 識別，實際用的是 5-tuple（`:1575-1576`）。
  - `SocketEditorViewModel.cs:1045-1092`：`TryEnsureSocketOpened` 的 `<remarks>` 掛在 `IsSnapshotStale` 上，形成兩個 `<summary>`。
    `TryEnsureSocketOpened` 本身（`:1151`）沒有文件。
  - `SocketEditorViewModel.cs:374-377`：說「no filled sockets」時回傳 null，實際上是完全沒有 socket row 時才回傳 null（`:393-396`）。
  - `DyeSlotPickerWindow.axaml.cs:9-14`：還寫著「slots `0..N-1`」，實際是稀疏的 slot number。
  - `FindItemsViewModel.cs:142-146`：`IsStale` 宣稱被 Refresh 的 CanExecute 使用（見 B4-10）。
  - `CustomGemSetsEditorViewModel.cs:94-96`：存檔成功訊息叫使用者「Close and reopen the Sockets editor」，但 `SocketEditorWindow.axaml.cs:36-42`
    已經會自動刷新。
  - status.md:365-366 與 Rust 文件 `mod.rs:2902-2905` 都說 present(1) 是 no-op，與 Rust 實作不符（見 B4-04）。
- **建議修法**：逐項修正文件；status.md 的 gotcha 等 crimson-rs 確定語意後再更新。

### B4-29 — 重複的 helper 與死碼
- **嚴重度**：cleanup
- **位置與內容**：
  - `DescendToItem` 有兩份相同的實作：`SocketEditorViewModel.cs:690-715`、`DyeEditorViewModel.cs:202-227`。
  - `TryParseScalarUInt` 有四份：`SocketEditorViewModel.cs:1322`、`DyeEditorViewModel.cs:302`、`VendorBuybackViewModel.cs:384`、
    `MainWindowViewModel.cs:4451`（`SaveFieldValues` 已有類似 helper）。
  - 英文名 + 次要語言名的解析有三份：Socket `:1292-1305`、Buyback `:283-293`、DyeEditor `:274-283`。
  - 「Slot {0} ({1})」有兩份：`DyeSlotEditorViewModel.cs:146-149`、`DyeSlotPickerViewModel.cs:58-65`。
  - 自訂寶石組標籤的建構有兩份：`SocketEditorViewModel.cs:446-457, 474-485`。
  - 「+ Add dye」：`MainWindow.axaml.cs:1074-1138` 與 `DyeSlotEditorViewModel.AddSlot` 的 index==0 分支做的是同一件事。
  - Add Item 與 Grant 的 clone-and-patch 配方重複（`MainWindowViewModel.cs:3603-3865` 與 `3879-4049`）。
  - `ItemFlagChipModel.LookupUiResourceString`（`FindItemsViewModel.cs:389-397`）重複了 `UiText.Get`。
  - 死碼：`MainWindowViewModel.cs:3686-3702` 收集的 `sourceTransferred` / `sourceItemKeyValue` 只在 `:3793-3794` 被 `_ =` 丟掉。
- **建議修法**：抽成 `ItemPathHelpers` / `ItemNameResolver` / `ItemCloneRecipe` 等共用 helper。死碼直接刪除。

### B4-30 — Add Item / Grant 寫死 scalar 的 byte 寬度，沒有依 type tag 編碼
- **嚴重度**：cleanup（是 B4-09 的觸發條件之一）
- **位置**：`MainWindowViewModel.cs:3754-3757`（`ushort` slotNo、`ulong` stackCount / itemNo）、`:3978-3981`；
  對照 stack fill 的 `ScalarFieldEditing.TryEncode(scTag, …)`（`:4581-4585`）
- **證據**：只要遊戲修改了 `_slotNo` 或 `_stackCount` 的寬度，batch 就會因為 `LENGTH_MISMATCH` 失敗，而且這時 clone 已經落地（B4-09）。
  stack fill 用 type tag 編碼，不會有這個問題。
- **建議修法**：寫入前從來源 element 讀出每個欄位的 type tag，改用 `ScalarFieldEditing.TryEncode`。編碼失敗時在 clone **之前** 就中止。

### B4-31 — 結構與大小
- **嚴重度**：cleanup
- **證據 / 建議**：
  - `SocketEditorViewModel.cs`（1,737 行）裡有四個型別（VM、`SocketRow` `:1346`、`GemSetTargetItem` `:1685`、`GemSetOption` `:1710`），
    可以拆檔。
  - `MainWindowViewModel.cs` 在 Block 4 範圍的五段合計約 1,400 行，可以比照現有的 `MainWindowViewModel.Contributions.cs` /
    `.FactionRelations.cs`，移到 `MainWindowViewModel.Items.cs`（Add Item / Grant / Learn knowledge）與 `.StackFill.cs`。
  - Dye 與 Socket 的業務流程（`MainWindow.axaml.cs:1013-1195, 1902-1987`）應移回 VM（與 Block 2 B2-26 的拆分一起做）。

### B4-32 — 其他 UX / 正確性小問題
- **嚴重度**：cleanup
- **位置與內容**：
  - `DyeEditorViewModel.cs:180`：達到 500 筆時，在 **排序前** 就 `break`。依掃描順序，已染色的物品可能被未染色的擠掉，而 `ResultsCountText`
    沒有「capped」提示。
  - `DyeSlotEditorWindow.axaml.cs:35-42`：沒有選 theme 時，「Pick…」不做任何事、也沒有提示。新增的 slot（B4-03 / B4-02）預設就沒有 theme，
    而且不需要 theme 的自由取色器（`DyePalettePickerViewModel.UseCustomColor`）也跟著被擋住。
  - `SocketEditorViewModel.cs:596-606`：Apply-Set 有失敗時不更新狀態列，畫面可能還停在後面某個 slot 的成功訊息。
  - `MainWindowViewModel.cs:1290-1370`：「Fill to max stack」對物品 block 裡的任何整數欄位都可以用（例如選了 `_endurance`），
    雖然只是預填文字框，但容易誤導。應限定 `_stackCount`。
  - `LearnItemKnowledgeAsync`（`:4058-4074`）在 `GetLearnedKnowledgeKeys()` 回傳 null（存檔沒有 knowledge list）時，靜默略過學習，也沒有提示。

---

## 3. Cross-cutting checklist 結果

| 項目 | 結果 | 對應 |
|---|---|---|
| Rule 3 — async I/O、不在 UI 執行緒阻塞 | ❌ 違反 | B4-27（另有 B4-21 的交錯風險）。Block 4 檔案中沒有 `.Result` / `.Wait()` |
| Rule 4 — OS 呼叫在 `Core` 介面後 | ✅ 通過。Block 4 檔案沒有 P/Invoke、registry 或 OS 指令；剪貼簿走 Avalonia `TopLevel.Clipboard`（`FindItemsWindow.axaml.cs:35-39`） | — |
| Rule 5 — 不使用 reflection API | ✅ 通過。沒有 `typeof(...).Get*` / `Activator` / reflection JSON；binding 都有 `x:DataType`；`TryGetResource` 不是 reflection | — |
| Rule 7 — magic string 集中 | ❌ 違反 | B4-26 |
| Rule 12 — 不在消費端繞過 parser 問題 | ⚠️ 兩處風險 | B4-17（寶石判斷用 string_key prefix）、B4-05（absent 語意在 UI 端以 0 猜測）。`IsPlayerEditableItem` 的 prefix 判斷是已記錄的刻意設計（Block 3 已說明），不列入 |
| 例外：不能用空 catch 掩蓋寫入失敗 | ⚠️ | 沒有字面上的空 catch 吞掉寫入錯誤，但有「捕捉了錯誤，卻留下部分寫入且沒記帳」的情況：B4-03、B4-07、B4-08、B4-09。純讀取的靜默略過見 B4-23 |
| UI 字串進三語系 `Strings/*.axaml` | ❌ 有遺漏 | B4-24（缺鍵）、B4-25（寫死） |
| 三語系字典 key-for-key 一致 | ✅（Block 3 已逐鍵確認，852 / 852 / 852） | 但三份 **同時** 缺少 B4-24 的五個鍵，所以「三份一致」不代表「程式用到的鍵都存在」 |

---

## 4. 交接清單（其他 block 的位置，本報告只當證據）

| 位置 | 問題 | 交給 |
|---|---|---|
| `D:\Github\crimson-rs\src\c_abi\mod.rs:2902-2905` 對照 `:3009-3015` | `set_scalar_field_present(1)` 對已 present 欄位：文件說 no-op，實作會覆寫值（B4-04） | crimson-rs（經 Block 1） |
| `NativeSaveLoader.cs:1722, 1743` | `NativeMethods.NOT_FOUND` 等錯誤碼是 `internal`，UI 只能寫 `-16`（B4-26） | Block 1 |
| `ISaveLoader.cs:111-127` 與 B1-02 | deferred 模式下 batch 失敗只套用一部分；Block 4 受影響的呼叫點：`SocketEditorViewModel.cs:546-579, 938-947`、`DyeSlotEditorViewModel.cs:189-207, 778-790`（B4-06、B4-18） | Block 1 決策 → Block 4 修正 |
| `Views/MainWindow.axaml.cs:1074-1138` | Add Dye 沒寫 Journal（B2-06）；分兩步寫入不回復（B4-02） | Block 2 / 4 |
| `Views/MainWindow.axaml.cs:1060-1066, 1141-1147, 1241-1247, 1950-1956` | 工具視窗只在關閉時把 dirty 傳回主視窗（B2-14）。開著 Socket / Buyback 視窗時 Save 被停用，關閉提示選「Save」會靜默略過 | Block 2 |
| `MainWindowViewModel.cs:123-146, 274-314` | `NavigateTo*` 只檢查 block 是否存在，不檢查 mutation version（B4-10） | Block 2 |
| `MainWindowViewModel.cs:4082-4289` | Mount / dragon 流程在 Block 4 指定範圍內，但屬於 Block 5 | Block 5 |
| `DyeEditorViewModel.cs:114`、`KnownDyesViewModel.cs:63`、`MainWindowViewModel.cs:4733-4791` | 在背景執行緒讀取 `LocalizationProvider`（B3-05 的讀取端）；Fill All 掃描也會在背景呼叫 `GetItemMaxStackCount` | Block 3 決定執行緒模型後一併處理 |

---

## 5. Pre-scan observations 與 Block 4 Audit focus 驗證結果

### 5.1 Pre-scan observations

| # | 計畫書敘述 | 與 Block 4 相關？ | 驗證結果 |
|---|---|---|---|
| 1 | Rule 6 logs 未實作 | 否（Block 2） | 未查（Block 2 已確認） |
| 2 | architecture.md 漂移 | 否（Block 6） | 未查 |
| 3 | `UiLanguageService` 的 Rule 4 | 否（Block 3） | 未查（Block 3 已確認） |
| 4 | Rule 7：沒有集中的 magic-string class | **是** | **在 Block 4 確認屬實**。schema 欄位名稱在 `DyeSlotEditorViewModel`、`MainWindowViewModel`、`MainWindow.axaml.cs` 以字面值重複出現；Socket / Buyback / DyeEditor 各自用 private const 重複定義；錯誤碼 `-16` 出現兩次。見 B4-26 |
| 5 | 檔案大小熱點 | **是** | **確認**：`SocketEditorViewModel.cs` 1,737 行（Read 工具顯示 1,738，最後一行是行尾換行，與計畫書一致），內含四個型別；`MainWindowViewModel.cs` 的 Block 4 五段約 1,400 行；`DyeSlotEditorViewModel.cs` 867 行，與計畫書一致。拆分建議見 B4-31 |
| 6 | CI 只跑 release | 否（Block 6） | 未查 |

### 5.2 計畫書 Block 4 的待驗證敘述

| 計畫書敘述 | 驗證結果 |
|---|---|
| 「`RunDeferred` callers here are `SocketEditorViewModel` and `DyeSlotEditorViewModel` (two sites)」 | **確認屬實**。Ui 專案中的 `RunDeferred(` 呼叫：`SocketEditorViewModel.cs:546`；`DyeSlotEditorViewModel.cs:189`（`AddSlot`）與 `:778`（`Apply`，透過 `:421` 的 `LoaderRunDeferred`）。另外兩處 `MainWindowViewModel.cs:2397, 5211` 屬於 Block 5 |
| 「Socket Apply-Set has a test; check whether the dye-slot batches do」 | **確認**：Socket Apply-Set 有 `SocketEditorTests.cs:513, 631, 694`；dye-slot 的兩個 batch **沒有任何測試**（B4-22） |
| 「No dedicated test file found for `VendorBuyback`, `FindItems` or `AddItemToCurrentListAsync`」 | **確認屬實**，而且範圍更大：`DyeSlotEditorViewModel`、`DyeEditorViewModel`、`CustomGemSetsEditorViewModel`、`KnownDyesViewModel`、`GrantItemToContainerAsync` 也都沒有測試（B4-22） |
| 物品識別用 5-tuple，不用 `(Block, Bag, Item)` | **Socket 正確**：`SocketRow.ItemIdentity` / `GemSetTargetItem.ItemIdentity`（`:1575-1576, 1700-1701`）在過濾、Apply-Set 目標、count 更新（`:254-285, 303-318, 427-438, 511-518, 1222-1229`）都使用 5-tuple。Dye / Buyback / FindItems 不以三元組當集合鍵，而是攜帶完整路徑，所以沒有發現三元組碰撞。只剩 `SocketEditorViewModel.cs:230-231` 的文件還寫三元組（B4-28）。這些路徑的另一個問題是「過期」，見 B4-01 |
| `_currentEndurance` 對照 `iteminfo.max_endurance` | **正確**：Fill / Change 都寫入寶石自己的 `max_endurance`（`:913-958`）；iteminfo 未載入時拒絕寫入（`:1260-1263`）；只有未知 key 才回退成 65535（`:1264-1265`）。殘餘風險：Change 不是原子操作（B4-18）；自訂組可能寫入未知 key，拿到 65535（B4-16） |
| stack 上限 | `TryComputeTargetStack` 的兩種模式與 `BulkFillCap` 有測試（`StackFillCapTests`），行為與確認訊息一致。缺口：`ApplyMaxStackCap`（B4-13）；掃描與套用之間有 TOCTOU（B4-11） |
| dye slot number（稀疏，0..11） | 新增的選項來自 `LookupDyeSlotNumbers`（`DyeSlotEditorViewModel.cs:143`、`MainWindow.axaml.cs:1076`），不會提供 prefab 沒有的號碼；`_dyeSlotNo` 以 i8 讀取、以 byte 寫入，0..11 的範圍內沒問題。風險：slot 0 被明確寫成 0（B4-14）；找不到欄位時會靜默落到 slot 0（B4-02） |
| `_grimeOpacity` | **有問題**：absent 被當成 0，任何一次 Apply 都會把它寫成 present 0；UI 允許 -128..127（B4-05） |
| Add Item：缺少 knowledge 時學習 | **已實作**：`AddItemToCurrentListAsync:3851`、`GrantItemToContainerAsync:4045` → `LearnItemKnowledgeAsync`（`:4058-4074`），只在加入成功後執行，並把結果附在狀態列。缺口：存檔沒有 knowledge list 時靜默略過（B4-32）；加入本身的原子性問題（B4-09） |
| Add Item：把 DLC 物品加進沒有 DLC 的存檔 | **程式碼沒有任何 DLC 檢查或警告**，與 status.md:79-81 記載的使用者決定（2026-11-07）一致；仍然 **沒有實機驗證**，維持 backlog 狀態 |
