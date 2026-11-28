# Code audit — Block 1: Native interop boundary (2026-11-28)

> **Status: REPORT ONLY.** 依 [code-audit-2026-11-28-plan.md](code-audit-2026-11-28-plan.md)
> 的 Block 1 範圍做靜態審查；本回合 **未修改任何程式碼**，修法只列建議，實作留給後續 fix PR。
>
> - Snapshot：`dev` @ `9ba7ae4`（PR #64）。所有 `file:line` 以此 commit 為準。
> - Rust 端對照：`vendor/crimson-rs/src/c_abi/`（vendored `0a551a6`，唯讀）。抽樣比對
>   `D:\Github\crimson-rs\src\c_abi\mod.rs` 的 `SAVE_FFI_LOCK`（4410）、deferred batch
>   （4203–4385）等段落，行號與內容和 vendor 版本一致。下文 Rust 行號皆指 vendor 路徑。
> - 範圍：`src/CrimsonAtomtic.RustInterop/` 全部 26 個 `.cs` 檔與 `.csproj`（只看 C# 消費端的契約；
>   Rust 端本身不在審查範圍）。

## 0. 範圍說明與限制

1. **MainWindowViewModel.cs 行號範圍**：計畫書的 Block 1 **沒有** 分配 `MainWindowViewModel.cs`
   的任何行號範圍（那些範圍分給 Block 2–5）。本報告只把 `RunDeferred` 的三個呼叫點
   （`MainWindowViewModel.cs:2373–2442`、`:5193–5249`，以及 `DyeSlotEditorViewModel.cs:166–227`、
   `:760–809`）當作「契約的消費者證據」唯讀引用，用來證明 B1-01 / B1-02 / B1-05 的契約問題
   確實會被觸發。這些呼叫點本身的審查屬於 Block 4 / Block 5，見 §5 的交接清單。
2. **測試基線未執行**：Execution plan 第 4 點要求在快照上跑
   `dotnet run --project src/CrimsonAtomtic.Tests` 並記錄數量。本 session 的權限設定擋下了這個指令
   （需要人工核准），所以 **測試數量沒有記錄**。請在開始修 Block 1 之前補跑一次。
3. **符號對照**：C# 端有 298 個 `EntryPoint`（全部集中在 `NativeSaveLoader.cs` 的 `NativeMethods`）。
   Rust 端有 304 個字面 `extern "C" fn`，另外還有 `impl_name_only_bridge!` 巨集產生的符號。
   機械式的全量 diff 腳本需要核准才能執行，所以 **沒有跑全量 diff**。
   人工抽查了 9 個最新或最可能漂移的符號，簽章全部一致：`crimson_parser_min_gamedata_patch`、
   `crimson_parser_compatible_gamedata_minors`、`crimson_dye_color_group_info_palette_knowledge_at`、
   `crimson_item_part_prefab_load_from_bytes_with_prefab_pamt`、`crimson_item_part_prefab_merge_dlc_from_bytes`、
   `crimson_item_part_prefab_lookup_dye_slot_material`、`crimson_factionnode_lookup_display_name`、
   `crimson_faction_info_lookup_display_name`、`crimson_save_transplant_list_element`。
   遺漏的符號在執行期會變成 `EntryPointNotFoundException`，而 `KeyInfoCatalogsTests` 等 live-install 測試
   已涵蓋大部分 catalog。
4. **struct layout 對照（已逐欄比對，全部一致）**：`CrimsonPathStep` ↔ `PathStep`（8 B）、
   `CrimsonScalarBatchOp`（40 B）、`CrimsonScalarPresentBatchOp`（48 B，含 `i32` 後的 4 B padding）、
   `CrimsonListRemoveBatchOp`（32 B）、`CrimsonInventoryItemRecord`（48 B）、`CrimsonItemRecord`（64 B）、
   `CrimsonPositionedEntityRecord`（56 B）、`CrimsonCharacterRefRecord`（16 B）、
   `CrimsonPazFileEntry`（272 B）、`CrimsonItemInfoSummary`（80 B）、`CrimsonBlockInfo`（20 B）。

## 1. 摘要

| 嚴重度 | 數量 | 編號 |
|---|---|---|
| bug | 3 | B1-01, B1-02, B1-03 |
| risk | 14 | B1-04 – B1-17 |
| rule violation | 2 | B1-18（Rule 7）, B1-19（Rule 4 / analyzer 抑制） |
| cleanup | 5 | B1-20 – B1-24 |

**建議的修正 PR 順序**：
1. **B1-01 + B1-16 + B1-17 + B1-05**：deferred batch 相關的 cache 與狀態一致性，以及同一個 handle 的鎖定範圍。改動集中在 `NativeSaveLoader`。
2. **B1-02**：先修 `ISaveLoader` 文件契約，再依決策處理 Block 4 / 5 的呼叫端。
3. **B1-03 / B1-04 / B1-07 / B1-08 / B1-09**：status code 與 two-call 的機械性修正。
4. 其他 cleanup。

---

## 2. Findings

### B1-01 — `LoadBlockDetails` 的 details cache 在 deferred batch 中失效（abort 後出現 ABA 舊資料）
- **嚴重度**：bug
- **位置**：`src/CrimsonAtomtic.RustInterop/NativeSaveLoader.cs:131-145`（cache 驗證）、`:169-200`（Begin/End/Abort 沒有清 cache）、`:242-257`（`RunDeferred`）
- **證據**：
  - cache 的唯一失效依據是 `mutation_version`（`NativeSaveLoader.cs:37-61, 136-141`）。
  - Rust 端在 deferred 模式下，每個 mutation **不會** bump version：scalar 走 `mod.rs:1895-1898`（"no version bump"），length-changing 走 `mod.rs:1684-1692`，batch 走 `mod.rs:2345-2373`。commit 時才 bump 一次（`mod.rs:4321`）；**abort 會把 version 還原成 begin 前的值**（`mod.rs:4356`），Rust 文件也這樣寫（`mod.rs:4227-4230`）。
  - 所以 batch 內外看到的都是同一個 version `V`，`_detailsCache` 無法分辨「batch 前」「batch 中」「abort 後」三種樹。
- **重現**（不需要並行）：
  1. `Load(path)`，此時 version = V。
  2. `BeginDeferredRedecode()`。
  3. `SetScalarField(0, 0, X)`。
  4. `LoadBlockDetails(path, 0)`：cache miss，讀到含 X 的 in-progress 樹，以 V 存入 cache。
  5. `AbortDeferredRedecode()`：Rust 樹還原，version 回到 V。
  6. `LoadBlockDetails(path, 0)`：**cache hit，回傳含 X 的已回滾資料**。UI 會顯示根本不存在的值，之後的編輯會以錯誤的現況為基準。

  反方向也成立：batch 前已快取的 block，在 batch 內 mutation 後讀取，拿到的是 batch 前的舊樹。這和 Rust 的契約「reads see the in-progress tree」（`mod.rs:4221-4223`，C# 端自己也在 `NativeSaveLoader.cs:2160-2161` 引用這句）互相矛盾。
- **連帶問題（測試失效）**：`src/CrimsonAtomtic.Tests/NativeSaveLoaderTests.cs:2113/2128` 與 `:2254/2271` 都是「batch 前 `LoadBlockDetails` → abort → 再 `LoadBlockDetails` 比對」。第二次讀取保證命中 cache 回傳同一個物件，所以 **即使 rollback 完全失效，這兩個測試也會通過**。
- **建議修法**：
  - (a) `BeginDeferredRedecode` / `EndDeferredRedecode` / `AbortDeferredRedecode`（含 `RunDeferred` 的兩條路徑）一律在 `_cacheLock` 下 `_detailsCache.Clear()`。
  - (b) batch 開啟期間完全不讀也不寫 cache。
  - (c) 或在 C# 端維護一個 epoch 計數，每次 Begin/End/Abort 都遞增，cache key 改成 `(epoch, version)`。
  - 測試方面：新增「batch 內讀取 → abort → 再讀取必須是 batch 前的值」的案例；既有兩個測試的「after」讀取改用新的 loader 實例，或先清 cache。

### B1-02 — deferred 模式下 batch 不再「all-or-nothing」，而文件推薦的 partial-success 寫法會 commit 半套操作
- **嚴重度**：bug（契約錯誤；已有真實呼叫端踩到）
- **位置**：`src/CrimsonAtomtic.RustInterop/ISaveLoader.cs:122-126`（建議在 body 內吞掉例外）、`:326-332`、`:506-513`、`:537-541`（三種 batch 都宣稱 all-or-nothing）、`NativeSaveLoader.cs:233-240`
- **證據**：
  - Rust 在 deferred 模式下：
    - `apply_length_changing_mutation`：「A mutator that errors out mid-batch leaves the tree partially mutated; the caller is expected to call abort」（`mod.rs:1672-1679, 1684-1692`）。
    - `set_scalar_fields_batch`：「the first failing op reports its index and leaves earlier ops applied」（`mod.rs:2339-2344`）。
    - `list_remove_elements_batch` 走同一個 mutator，部分套用後回傳錯誤（`mod.rs:2575-2590`）。
  - C# 契約卻同時說兩件事：batch 失敗時「the save body is left exactly as it was」，以及「swallow per-op exceptions inside body so the commit lands」。兩者在 deferred 模式下互相衝突。
  - **真實觸發點**（唯讀證據）：
    - `MainWindowViewModel.cs:2397-2430` → `ApplyPatternBv1Writes`（`:2206-2249`）。一個挑戰依序做 clone → promote `_completedTime` → `DynamicArraySetU32Elements` → `SetScalarFieldsBatch`。中途任一步失敗就 `return`，`RunDeferred` 照常 commit，結果是「clone 了、tag 改了、state 沒改」的半套 Pattern B v1 寫進存檔。該處註解（`:2234`）「The batch is all-or-nothing」在 batch 內並不成立。
    - `MainWindowViewModel.cs:5211-5249`（knowledge inject）：`ListCloneElement` 成功、隨後 `SetScalarField(_key)` 失敗並被吞掉，於是一筆 **複製自 element 0、key 也和 element 0 相同** 的重複 knowledge 被 commit。
- **建議修法**：
  1. 先修文件：`ISaveLoader` 三個 batch 方法與 `RunDeferred` 都要寫明「deferred 模式下失敗會留下部分套用；在 body 內吞例外 = 接受部分套用的樹」。
  2. 從下列做法擇一：
     - (a) 向 crimson-rs 提需求：deferred 模式下 mutator 改成先驗證再修改（atomic per-op），或提供 savepoint API。
     - (b) C# 提供 `RunDeferredPerItem(IEnumerable<Action>)`：某個 item 失敗就 abort，再重跑已成功的前綴後 commit（成本較高，但語意正確）。
     - (c) 呼叫端改為「失敗即 throw → 整批 abort」，放棄 partial-success。
  3. 交由 Block 4 / 5 逐一確認呼叫端。

### B1-03 — batch 的 `FailedOpIndex` 在 PANIC 或提早返回時誤報為 op 0
- **嚴重度**：bug（診斷錯誤）
- **位置**：`NativeSaveLoader.cs:540-555`、`:642-655`、`:731-744`
- **證據**：
  - Rust 的 `crimson_save_set_scalar_fields_batch` 在 `catch_unwind` 落入 `PANIC`（`mod.rs:2427`），以及 handle 為 null 等早退路徑（`mod.rs:2295-2300`）都 **不寫** `out_failed_op_index`。
  - C# 的 `out var failedIdx` 是 caller 端以 `.locals init` 零初始化的 local；LibraryImport stub 對 blittable `out` 參數不會預先寫入。
  - 結果是 `failedIdx == 0 != nuint.MaxValue`，於是例外訊息變成「failed at op 0/N」，`FailedOpIndex = 0`。呼叫端會去追查一個無辜的 op。
- **建議修法**：P/Invoke 簽章改用 `ref nuint failedOpIndex`，或改成 `nuint*` 並在呼叫前設為 `nuint.MaxValue`；另外，`rc` 為 `PANIC` / `NULL_ARG` 時一律忽略 index。

### B1-04 — two-call pattern 丟棄第二次呼叫回報的長度或筆數
- **嚴重度**：risk
- **位置**：
  - `NativeSaveLoader.cs:1513-1531`（`ReadBlockJson`：第二次呼叫用 `rented.Length`，解碼卻用第一次的 `required`）
  - `:282-294` 及另外三個 `List*`（第二次呼叫 `out _`，回傳整個 `buf`）
  - `:852-867`、`:902-917`、`:1222-1237`、`:1268-1285`
  - `NativeKeyInfoCatalogs.cs:61-72`（`NameBuffer.ReadString`）
  - `NativeItemInfoCatalog.cs:136-149`、`NativePalocCatalog.cs:153-168`、`NativeStringInfoCatalog.cs:106-119`、`NativeMainQuestChapter.cs:279-292`
- **證據**：
  - Rust 的 `SAVE_FFI_LOCK` 只保證 **單次** 呼叫是原子的（`mod.rs:4389-4410`），兩次呼叫之間其他執行緒可以插入 mutation。
  - 這種並行在本專案確實存在：`NativeSaveLoader.cs:68-70` 自己就寫了「written on the (background) mutation thread, read on the UI thread」。
  - 發生時：
    - JSON 變短 → `GetString(…, required-1)` 會把 NUL 與 pool 中殘留的位元組一起解碼，丟出 `JsonException`（不是 `CrimsonSaveException`）。
    - JSON 變長但仍塞得進 rented buffer → 第二次呼叫成功，但只解碼前 `required-1` 個位元組，得到截斷的 JSON。
    - `ListInventoryItems` / `ListAllItems` 的筆數變少 → Rust 回 `OK` 並寫入實際筆數，但 C# 丟掉這個值，**回傳結尾全零的假紀錄**（`BlockIndex=0, ItemKey=0…`），呼叫端可能據此對 block 0 發出寫入。
  - 另外 `(int)count` / `(int)required` 都是未檢查的轉型（例如 `:282`、`:1518`）。
- **建議修法**：一律使用第二次呼叫回傳的 `required` / `count`：
  - buffer 類切片成實際長度。
  - record 類在 `count > buf.Length` 時重試，在 `count < buf.Length` 時截短。
  - 第二次仍回 `BUFFER_TOO_SMALL` 時重試（設上限）。
  - 轉型改成 `checked`。

### B1-05 — handle 層級的 batch 狀態沒有 C# 端的單一寫入者保證（跨執行緒 mutation 會混入別人的 batch）
- **嚴重度**：risk
- **位置**：`NativeSaveLoader.cs:25-29`（「Avalonia's VM dispatcher is single-threaded」與實際用法不符）、`:169-200`、`:242-257`、`:1378-1391`
- **證據**：
  - 同時存在 background 寫入者（`MainWindowViewModel.cs:2373` 以 `Task.Run` 包住 `RunDeferred`）與 UI 執行緒寫入者（`DyeSlotEditorViewModel.cs:165-207`，同步的 `[RelayCommand]`）。
  - deferred 狀態是 per-handle（`mod.rs:617`），所以 batch 開啟期間 **任何執行緒** 的 mutation 都會被算進該 batch：abort 時連 UI 的編輯一起被默默丟掉，commit 時則把別人的半套操作一起寫入。
  - `Begin` / `End` / `Abort` 各自呼叫 `RequireLoaded`。若 `Load()` 在 batch 進行中換掉 handle，`End` 會打到新的 handle 而得到 `BATCH_NOT_OPEN`；舊 handle 則帶著未結束的 batch 被 Dispose。
- **建議修法**：
  - 在 `NativeSaveLoader` 加一把 session 級的 `SemaphoreSlim` 或 reentrant 鎖，`RunDeferred` 全程持有，`Load` 換 handle 時也要取得。
  - `RunDeferred` 在 Begin 時抓住 handle 實例，End / Abort 都用同一個實例。
  - batch 開啟期間，若收到其他 thread 的 mutation 就 fail fast（記錄 `Environment.CurrentManagedThreadId`）。
  - 同時更新類別 remarks。

### B1-06 — catalog 的 `MergeDlcFromBytes` 與並行 lookup 不安全，C# 端沒有防護也沒有文件
- **嚴重度**：risk
- **位置**：`NativeKeyInfoCatalogs.cs:655-677`（Knowledge）、`:1483-1505`（Character）、`:1732-1760`（FactionNode）、`:3006-3028`（Faction）、`NativeDyeCatalogs.cs:980-1000`（ItemPartPrefab）
- **證據**：
  - Rust 的 `faction_info.rs:249-250` 寫明「Not thread-safe against concurrent lookups on the same handle」；`mod.rs:4407-4409` 也說 catalog bridge **刻意不拿鎖**，理由是「immutable after load」。但 merge 會修改 handle。
  - C# 的 `_entryCount` 是非 `volatile` 欄位，在 merge 中被改寫。
  - 呼叫時機在 `LocalizationProvider.cs:745-773`（Block 3）。
- **建議修法**：在每個 `MergeDlcFromBytes` 的 XML doc 註明「必須在 catalog 發佈給其他執行緒之前呼叫」，或在 catalog 內加 `ReaderWriterLockSlim`。交由 Block 3 確認實際呼叫時機。

### B1-07 — 部分 catalog lookup 把所有錯誤碼默默轉成「找不到」或 0
- **嚴重度**：risk（違反 audit focus「every negative code becomes a typed exception」）
- **位置**：
  - `NativeKeyInfoCatalogs.cs:1250-1255`（`LookupFactionKey`：非 OK 一律 `null`）
  - `:1261-1266`（`FactionMemberCount`：非 OK 一律 `0`）
  - `:2968-2978`（`FactionInfo.GetEntryKey`：非 OK/BTS 一律 `null`，等於「列舉結束」）
  - `:3126-3132`（`LookupRelation`）
  - `NativeDyeCatalogs.cs:1078-1092`（`LookupFirstPrefabKey`：在檢查 `rc` 之前先判斷 `count == 0`，且第二次呼叫失敗也回 `null`）
- **證據**：`PANIC` / `NULL_ARG` 會被當成資料語意（未知角色、0 名成員、enumeration 提早結束）。同一個檔案裡的 `NativeGlobalGameEventInfoCatalog.LookupGroupKey`（`:2386-2399`）是正確的寫法：`NOT_FOUND` 回 `null`，其餘丟出例外。
- **建議修法**：統一成「`NOT_FOUND` / `OUT_OF_RANGE` 回 `null`，其他錯誤碼丟 `CrimsonSaveException`」；`LookupFirstPrefabKey` 先檢查 `rc` 再看 `count`。

### B1-08 — `GameDataLayout.TryListDir` 吞掉所有 `CrimsonSaveException`
- **嚴重度**：risk
- **位置**：`src/CrimsonAtomtic.RustInterop/GameDataLayout.cs:223-238`
- **證據**：註解說只是要把「install 沒有這個目錄」轉成 `null`。但 `PANIC`、`BODY_PARSE`（PAMT 毀損）、`INVALID_PATH` 也被一併吞掉。後果是 `Resolve` 默默選了 `Modern`（`:136-143`），`ResolvePalocFiles` 回 `null`（「語言不存在」），使用者看不到任何診斷。
- **建議修法**：`catch (CrimsonSaveException ex) when (ex.ErrorCode == NativeMethods.NOT_FOUND)`，其餘錯誤往上拋或記錄 log。

### B1-09 — `NativePaverReader.TryReadFromInstall` 宣稱「Never throws」，實際會丟例外
- **嚴重度**：risk（啟動路徑）
- **位置**：`src/CrimsonAtomtic.RustInterop/NativePaverReader.cs:259-283`、`:289-309`；`:152-180`
- **證據**：
  - `crimson_rs.dll` 不存在或版本過舊時，LibraryImport 會丟 `DllNotFoundException` / `EntryPointNotFoundException`，直接穿過 `TryReadFromInstall`。同檔的 `LoadParserTargetInfo` 則有接住這兩種例外。
  - `Lazy<T>`（預設 `ExecutionAndPublication`）會把 `BadImageFormatException` 等未捕捉的例外快取起來，之後每次存取 `ParserTargetMajor` 都會重拋。
- **建議修法**：在兩個 `TryRead*` 裡捕捉 `DllNotFoundException`、`EntryPointNotFoundException`、`BadImageFormatException` 並回 `null`；`LoadParserTargetInfo` 也補上 `BadImageFormatException`。

### B1-10 — `GameDataVersion.CompatibleMinors` 直接回傳快取中的可變陣列
- **嚴重度**：risk
- **位置**：`NativePaverReader.cs:131`
- **證據**：回傳的就是 `Lazy` 快取裡的 `ushort[]`，呼叫端一改就會污染整個 process 的版本閘門（`IsCompatibleWithParser`，`:220-223`）。
- **建議修法**：改回傳 `IReadOnlyList<ushort>` / `ReadOnlySpan<ushort>`，或回傳 clone。

### B1-11 — `ItemRecord.ToPathSteps` 在 `PathLen > 2` 時默默補零
- **嚴重度**：risk
- **位置**：`src/CrimsonAtomtic.RustInterop/ItemRecord.cs:113-133`
- **證據**：文件說「Honours PathLen so future longer paths still work without caller changes」，但 record 只帶兩個 step。`PathLen = 3` 時，`path[2]` 會是 `(0,0)`，mutation 打到錯誤的欄位，而且不會報錯。
- **建議修法**：`PathLen > 2` 時丟 `InvalidDataException`，比照 `QuestRollupKey.FromAbi` fail-loud 的做法（`QuestRollupKey.cs:41-49`）；文件也要同步修正。

### B1-12 — 從 ABI 讀出的列舉值沒有驗證就直接轉型
- **嚴重度**：risk（低）
- **位置**：`ItemRecord.cs:104`（`ContainerKind`）、`PositionedEntityRecord.cs:116`（`PositionKind`）、`NativeDyeCatalogs.cs:1113`（`DyeSlotCountSource`）
- **證據**：Rust 若新增 kind，C# 會得到一個 `switch` 沒有處理的值，靜默落入 default 分支。同層的 `QuestRollupKey.FromAbi` 遇到未知值會直接丟例外，兩邊做法不一致。
- **建議修法**：加上 `Enum.IsDefined` 檢查（泛型版本是 AOT-safe），或改用 switch 映射，未知值就丟例外。

### B1-13 — 綁定了 2.01 以後語意已錯的 3-byte dye mask ABI
- **嚴重度**：risk（Rule 12 精神：不要讓上層再踩一次 schema 陷阱）
- **位置**：`NativeDyeCatalogs.cs:673-695`（`LookupSlotMask`）、`:755-779`（`LookupSlotExtraLayerMask`）；宣告在 `NativeSaveLoader.cs:3568-3572`、`:3591-3595`
- **證據**：
  - Rust 的 `part_prefab_dye_slot_info.rs:355-363` 寫明「**Wrong meaning since 2.01** … these first 3 bytes are the flags of dye slots 0, 1 and 2, not of the slot's three channels」，正確的 API 是 `_mask_full` / `_active_channels` / `_dye_slot_bits`。extra layer 的版本見 `:707-710`。vendor 的 `CLAUDE.md` 也把這點列為長期須知。
  - C# 的 XML doc 仍寫「3 raw mask bytes for a slot」。目前只有測試在用（`KeyInfoCatalogsTests.cs:429, 491`）。
- **建議修法**：改綁 `crimson_part_prefab_dye_slot_info_lookup_slot_mask_full`、`…_extra_layer_mask_full`（兩者都是 two-call 介面），需要舊語意時改用 `…_active_channels`；3-byte 版本移除或標 `[Obsolete]`。

### B1-14 — PAZ 每抽取一個檔案都做兩次完整的 PAMT 解析與解密解壓
- **嚴重度**：risk（效能）
- **位置**：`src/CrimsonAtomtic.RustInterop/NativePazExtractor.cs:19-52`、`:56-91`
- **證據**：Rust 的 `crimson_paz_extract_file`（`paz.rs:141-206`）在 size query 時就會讀檔、解析 PAMT、完整 `paz::extract_file`；第二次呼叫全部重做一遍。啟動時會抽取數十張表（iteminfo 約 6 MB、PALOC 每個語言多個檔），成本加倍。
- **建議修法**：
  - (a) 先用 `ListDir` 拿到的 `PazFileEntry.UncompressedSize` 或一個猜測大小配置 buffer，單次呼叫，只在 `BUFFER_TOO_SMALL` 時重試。
  - (b) 向 crimson-rs 要求 allocate-and-free 介面（`…_extract_file_alloc` + `crimson_buf_free`）。

### B1-15 — `LoadBlockDetails` 持有 `_cacheLock` 期間做 1–2 秒的 FFI 與 JSON 反序列化
- **嚴重度**：risk（Rule 3：UI 凍結）
- **位置**：`NativeSaveLoader.cs:131-146`（鎖內呼叫 `ReadBlockDetails`）、`:1378-1391`（每個 mutation 都會 `lock (_cacheLock)`）
- **證據**：
  - 註解說大 block 讀一次要 1–2 秒（`:51-53`）。這段期間 UI 執行緒上任何 `RequireLoaded`，包括 `DyeSlotEditorViewModel.cs:166` 那類同步 command，都會卡在鎖上。
  - `LoadBlockDetails` 的 `CancellationToken` 只在進入時檢查一次（`:119`）。
  - 整個 `ISaveLoader` 只有同步 API（`ISaveLoader.cs:17, 24`），Rule 3 是否守住完全取決於每個呼叫端記得 `Task.Run`。
- **建議修法**：
  - 鎖內只抓 handle 與查 cache，FFI 和反序列化移到鎖外；回填 cache 時再上鎖，並重新比對 version。
  - 考慮提供 async facade（`LoadBlockDetailsAsync`），或在 `ISaveLoader` 文件標明「blocking；呼叫端必須離開 UI 執行緒」。

### B1-16 — `HasStructuralEdit` 與 deferred 狀態不同步
- **嚴重度**：risk
- **位置**：`NativeSaveLoader.cs:64-77`、`:659`、`:748`、`:191-200`
- **證據**：
  - (a) batch 內做了 structural 編輯後 abort，旗標仍然是 `true`，存檔時會多跳一次警告（偏保守、無害）。
  - (b) 反向是真正的漏洞：deferred 模式下，`SetScalarFieldsPresentBatch` / `ListRemoveElementsBatch` 部分套用後丟例外，`MarkStructuralEdit()` 不會執行。若呼叫端照 B1-02 的建議吞掉例外並 commit，**長度已改變卻沒有旗標**，使用者就不會收到遊戲 loader overflow 的警告（`ISaveLoader.cs:100-107`）。
- **建議修法**：Begin 時記下旗標，Abort 時還原；deferred 模式下任何 structural 呼叫，不論成敗都設旗標（寧可多警告）。

### B1-17 — `RunDeferred` 對 abort 或 commit 失敗的處理不完整
- **嚴重度**：risk（低）
- **位置**：`NativeSaveLoader.cs:250-256`
- **證據**：
  - abort 丟出 `CrimsonSaveException` 時被吞掉（`:253`），batch 可能仍開著卻沒有任何訊號，之後 `WriteToFile` 會一直得到 `BATCH_IN_PROGRESS`。
  - abort 丟出其他例外（例如 B1-05 情境下 handle 被換掉後的 `InvalidOperationException` / `ObjectDisposedException`）時，會蓋掉原本的例外。
  - `End` 回傳 `MUTATION_INVALID` 以外的錯誤（例如 `PANIC`）時，Rust 已經先 `take()` 了 state（`mod.rs:4297`），樹可能處於不明狀態，C# 卻只當成一般失敗。
- **建議修法**：abort 改成 `catch (Exception abortEx)`，用 `AggregateException` 或 `Data` 帶回原例外；失敗後呼叫 `IsDeferredRedecodeOpen` 並把結果附在例外上；End 回 `PANIC` 時建議 UI 重新載入。

### B1-18 — Rule 7：magic strings 沒有集中管理，錯誤碼名稱表重複 8 份且內容不一致
- **嚴重度**：rule violation
- **位置**：
  - `NativeSaveLoader.cs:1626-1656`（唯一完整的一份）
  - `NativeKeyInfoCatalogs.cs:83-95`
  - `NativeItemInfoCatalog.cs:432-444`
  - `NativePalocCatalog.cs:236-249`（名為 `NativeSaveLoader_ErrorName`，doc 宣稱「Re-exposes NativeSaveLoader's private error-name table」，實際上是精簡過的子集）
  - `NativePazExtractor.cs:128-140`
  - `NativeStringInfoCatalog.cs:184-196`
  - `NativeMainQuestChapter.cs:308-317`
  - `NativeSideQuestFaction.cs`（同樣的模式）
  - `"0.pamt"` 字面值：`GameDataLayout.cs:111`（UI 端另有多處）
- **證據**：catalog 路徑遇到 `UNSUPPORTED_VERSION(-6)`、`TOO_SMALL`、`BAD_MAGIC`、`NOT_OBJECT_LIST` 時，訊息只會顯示「UNKNOWN(-6)」。本專案沒有任何集中的 constants 檔，與 Pre-scan #4 吻合。
- **建議修法**：新增一個 `NativeErrorCodes.cs`（常數加上 `Name(int)`，含完整註解），全部改用它；`GameDataLayout` 增加 `PamtFileName = "0.pamt"` 常數。

### B1-19 — 整個專案抑制 CA1416，且理由已過時（削弱 Rule 4 的防線）
- **嚴重度**：rule violation（latent）
- **位置**：`src/CrimsonAtomtic.RustInterop/CrimsonAtomtic.RustInterop.csproj:12-14`（另有 `:8-9`）
- **證據**：CA1416 是平台相容性 analyzer，會抓 Windows-only API。註解說是「placeholders we'll wire up to the real C ABI later」，但 C ABI 早已接好。抑制它之後，若有人在 RustInterop 誤用 OS 專屬 API，build 不會報錯，而 Rule 4 要求 OS 呼叫必須經過 `Core` 介面。`:8-9` 的「byte* … (and only there)」也已不實，unsafe 早就遍布全專案。
- **建議修法**：移除 `NoWarn CA1416`，確認 build 乾淨（`TreatWarningsAsErrors` 已開）；更新 `AllowUnsafeBlocks` 的說明。

### B1-20 — 文件與註解過時或錯置
- **嚴重度**：cleanup
- **位置與內容**：
  - `NativePaverReader.cs:21`、`:62-63`、`:115-121`：仍寫 target 是 2.00 / minor 0 / commit `0f2363b`，實際是 2.06。判斷邏輯本身是從 ABI 讀取的，所以只有文字錯誤。
  - `NativeDyeCatalogs.cs:138-151`：`PaletteAt` 的 `<summary>` 被擠到 `PaletteKnowledgeAt` 上（一個成員有兩段 summary）；`:928-938` `ResolvedItemCount` 的 summary 錯掛在 `LoadFromBytes` overload 上；`:467-472`「Dye editor v1 doesn't consume this bridge yet」已過時。
  - `NativeKeyInfoCatalogs.cs:7-28`：檔頭寫「Six small bridges」，實際有 27 個 catalog。
  - `ItemInfoSummary.cs:127-128`：測試名稱寫成 `NativeItemInfoSummaryTests.Layout_MatchesRustAbi`，實際是 `ItemInfoCatalogTests.ItemInfoSummary_Layout_MatchesRustAbi`。
  - `ISaveLoader.cs:27-33`：「bumps by exactly 1 on every successful mutation」在 batch 內不成立（連帶 B1-01）。
  - `CharacterRefRecord.cs:51`、`NativePaverReader.cs:247`：`cref` 指向 Ui 層的 `LocalizationProvider`，本 assembly 解析不到，也違反分層方向。
- **建議修法**：一次性的文件整理 PR。

### B1-21 — 例外訊息未本地化就直接送到 UI
- **嚴重度**：cleanup（跨區 UI 字串規則；實際顯示屬 Block 4）
- **位置**：`CrimsonSaveException.Message` 是英文開發者訊息（例如 `NativeSaveLoader.cs:440-442`），`DyeSlotEditorViewModel.cs:217-218` 原樣放進 `StatusMessage`。
- **建議修法**：interop 層保留 `ErrorCode`，UI 端依錯誤碼查三語系 `Strings/*.axaml`，開發者訊息只放 tooltip 或 log。RustInterop 本身沒有直接使用 UI 字串，三語系檢查見 §4。

### B1-22 — 檔案規模與重複（只談結構）
- **嚴重度**：cleanup
- **證據**：
  - `NativeKeyInfoCatalogs.cs`：27 個幾乎相同的 catalog，各配一個 `SafeHandle` 子類別；`GetEntry` 的 two-call 至少複製 10 次；`NameBuffer.ReadString` 每次呼叫都配置一個 closure 與 delegate。
  - `NativeSaveLoader.cs`：同時塞了 `NativeSaveLoader`、`CrimsonSaveHandle`、`CrimsonSaveException`，以及涵蓋全部 298 個符號的 `NativeMethods`。
- **建議（僅結構，不在稽核期間重構）**：
  - `NativeMethods` 依 surface 拆成 partial 檔：`NativeMethods.Save.cs` / `.Paloc.cs` / `.Paz.cs` / `.ItemInfo.cs` / `.KeyInfo.cs` / `.Dye.cs` / `.Quest.cs`。
  - `NativeSaveLoader` 拆成 partial：`.Deferred.cs` / `.Structural.cs` / `.Batch.cs` / `.Enumerations.cs`。
  - catalog 用一個泛型 `OwnedNativeHandle<TRelease>`，或用 source generator 產生，消除 27 份 SafeHandle 樣板；two-call 收斂成一個 `TwoCall.ReadUtf8(...)`（改用 `static` lambda 搭配 state 參數，避免每次配置）。
  - 拆分邊界很乾淨：各 surface 之間只透過 `NativeHandle` / `InternalSlotInfoHandle` 這類 internal accessor 互相引用。

### B1-23 — `IItemInfoCatalog` 缺少 `LookupSummary` / `LookupFlags`
- **嚴重度**：cleanup
- **位置**：`IItemInfoCatalog.cs:15-125` 與 `NativeItemInfoCatalog.cs:297-333`
- **證據**：需要 summary 的消費端只能依賴具體類別，介面形同虛設。
- **建議修法**：補進介面，或明確說明「這兩個方法只在具體類別上提供」。

### B1-24 — 測試缺口
- **嚴重度**：cleanup（測試覆蓋）
- **證據**：
  - 有 layout 測試的只有 `ItemRecord`、`PositionedEntityRecord`、`ItemInfoSummary`（`NativeSaveLoaderTests.cs:1546, 1632`、`ItemInfoCatalogTests.cs:139`）。
  - 缺 `InventoryItemRecord`(48)、`CharacterRefRecord`(16)、`PazFileEntry`(272)、`PathStep`(8)，以及三個 batch op struct（40 / 48 / 32，`internal`，可用 `InternalsVisibleTo` 或從 RustInterop 內部斷言）。
  - 沒有測到的情境：B1-01（batch 內讀取再 abort）、B1-03（PANIC 時的 index）、B1-02（deferred 模式下 batch 部分失敗）。
- **建議修法**：補上述測試（`Unsafe.SizeOf<T>()` 加 `Marshal.OffsetOf`，後者只在測試專案使用，不受 AOT 規則限制）。

---

## 3. Audit focus 逐項結論

| 項目 | 結論 |
|---|---|
| Handle lifetime（SafeHandle、dispose/reload、跨執行緒） | **大致正確**。每個 native handle 都是 `SafeHandle`，`ReleaseHandle` 呼叫對應的 `*_free`，`FromOwnedPointer` 在 rc 非 OK 時不會包裝；`Load` 先建新 handle 再換掉舊的，失敗時 dispose 新 handle（`NativeSaveLoader.cs:84-110`）。LibraryImport 對 `SafeHandle` 參數會 AddRef，Dispose 競態最壞是 `ObjectDisposedException`，不會 use-after-free。Rust 端 `SAVE_FFI_LOCK` 讓 save ABI 單次呼叫在記憶體層面安全。**問題在語意層的跨執行緒**：B1-05（batch 混入、handle 中途被換）、B1-06（catalog merge）。 |
| Two-call 字串 / buffer | 單執行緒下正確（NUL 處理、`required <= 1` 視為空字串、ArrayPool 歸還都沒問題）。**大小在兩次呼叫之間改變時處理錯誤**：B1-04。長度溢位：未檢查的 `(int)` 轉型，見 B1-04。 |
| Status code 對應 | save 路徑完整：每個非 OK 都轉成 `CrimsonSaveException`，`NOT_FOUND` / `OUT_OF_RANGE` 有明確的 null 語意。**例外**：B1-07（5 處吞錯）、B1-08（GameDataLayout）、B1-09（paver 回 null 時吞掉 rc，屬於刻意設計，但「never throws」不成立）、B1-03（index 誤報）。錯誤名稱表殘缺：B1-18。 |
| Deferred batch 契約 | commit on return、abort on throw 已實作且有測試。**巢狀** batch 由 Rust 回 `BATCH_IN_PROGRESS`（有測試 `:2171`）。**batch 內不能用的 primitive**（status.md「promote then write in place」）完全沒寫進 `ISaveLoader` 文件（`:43-66`、`:111-127`）——應補上「batch 內對剛 promote 的欄位只能用 `present(0)` 加 `present(1, value)`，不能用 `SetScalarField`」。嚴重問題見 B1-01、B1-02、B1-16、B1-17。 |
| AOT safety | **符合**。全部使用 `[LibraryImport]`，字串一律 `StringMarshalling.Utf8`，對應 Rust 的 `CStr::to_str`（UTF-8），編碼一致；沒有 `DllImport`、反射或 `Marshal.PtrToStructure`（grep 確認）；`IsAotCompatible=true` 加上 `TreatWarningsAsErrors=true`（`Directory.Build.props:7, 14`）。`AllowUnsafeBlocks` 是整個專案啟用，但用量限於 `fixed` / `byte*` 互操作，可接受；註解過時（B1-19）。`ReadU16` 等 delegate 指向 source-generated 方法，沒有 AOT 問題。小風險：Windows 路徑若含孤立 surrogate，UTF-8 marshaller 會把它換成 U+FFFD，結果變成 `IO` 錯誤而不是崩潰。 |
| 版本閘門 | **符合**。`(major, minor)`、patch floor、compatible minors 全部從 ABI 讀取（`NativePaverReader.cs:164-167`）；`IsCompatibleWithParser` 沒有使用字面常數；ABI 缺失時 fail-closed（空集合會判定為不相容）。問題只在 B1-09、B1-10，以及文件過時（B1-20）。 |
| 檔案大小與拆分 | 可以乾淨拆分，見 B1-22。 |

## 4. Cross-cutting checklist（Block 1 範圍）

| 規則 | 結論 |
|---|---|
| Rule 3 async | RustInterop 只有同步 API（FFI 本身會阻塞）。專案內沒有 `.Result` / `.Wait()`（grep 無命中）。**風險**：B1-15（持鎖阻塞 UI）；呼叫端同步執行 mutation 交給 Block 4（`DyeSlotEditorViewModel.cs:165-207`）。`GameDataLayout.DlcNames` 有同步檔案 I/O（`:102-115`），由呼叫端負責放到背景執行緒。 |
| Rule 4 OS 呼叫 | RustInterop 沒有直接的 OS API（只用 `File` / `Directory` / `Path`，都是跨平台）。但 CA1416 被抑制，防線變弱，見 B1-19。 |
| Rule 5 無反射 | 符合（見 §3 AOT）。 |
| Rule 7 magic strings | **違反**，見 B1-18。 |
| Rule 12 foundation over workaround | interop 層沒有發現「在消費端繞過 parser 錯誤」的寫法；`ItemInfoLoadFromBytes` 遇到 `BODY_PARSE` 時回退到 body-only loader（`NativeItemInfoCatalog.cs:80-83`），是 Rust 端文件化的版本分流，不算 workaround。B1-13 屬於「沒跟上 parser 端的修正」。 |
| 空 `catch` 掩蓋寫入失敗 | 沒有空 catch。`RunDeferred:253` 吞掉 abort 失敗（B1-17）；`GameDataLayout:230-237` 範圍過寬（B1-08）。`WriteToFile`（`NativeSaveLoader.cs:921-942`）的錯誤都會往上拋。 |
| UI 字串三語系 | RustInterop 不引用任何 UI 資源鍵。順帶檢查 `en.axaml` / `ja.axaml` / `zh-TW.axaml` 的 `x:Key=` 數量，三者都是 **852**（只比了數量；逐鍵比對屬 Block 3）。interop 的英文例外訊息會被送到 UI，見 B1-21。 |

## 5. 交接給其他 block

- **Block 4**：
  - `DyeSlotEditorViewModel.cs:165-207` 在 UI 執行緒同步呼叫 length-changing FFI（Rule 3）。
  - 同一處：batch **外** 先做 `SetObjectListPresent` / `ListCloneElement`，若 batch 內失敗並 abort，已加入的空 dye element 會殘留（`:178-182` 對上 `:189-207`）。
  - `:776-797` 的 partial-success 寫法要依 B1-02 的決策重新檢視。
  - UI 顯示 `ex.Message`（B1-21）。
- **Block 5**：`MainWindowViewModel.cs:2397-2430` 與 `:5211-5249` 這兩個 `RunDeferred` 呼叫點會 commit 半套操作（B1-02）；`:2234` 註解錯誤。
- **Block 3**：`LocalizationProvider.cs:745-773` 呼叫 DLC merge 的時機與並行 lookup 的關係（B1-06）；PALOC 多檔逐一迭代的邏輯目前放在 UI 端（因為 `LookupDisplayName` 只接受單一 `NativePalocCatalog`），可評估把這個 facade 下沉到 interop 層。
- **Block 2**：`WriteToFile` 的原子性（Rust 端是直接寫目的地路徑）與備份順序；B1-15 的 UI 凍結。

## 6. Pre-scan observations 查證結果

| # | 觀察 | 與 Block 1 相關？ | 查證結果 |
|---|---|---|---|
| 1 | Rule 6 logs 未實作 | 否（Block 2） | RustInterop 沒有任何 logger 呼叫，與觀察一致；完整查證由 Block 2 負責。 |
| 2 | architecture.md 漂移 | 否（Block 6） | 未查。 |
| 3 | `UiLanguageService` 的 Rule 4 | 否（Block 3） | 未查。 |
| 4 | Rule 7 沒有集中的 magic-string class | **是** | **確認屬實（就 RustInterop 而言）**：沒有集中的常數檔；錯誤碼名稱表重複 8 份且內容不一致；`"0.pamt"` 是字面值；PALOC lo32 常數散在各 catalog 類別（有命名，但沒有集中）。見 B1-18。 |
| 5 | 檔案大小熱點 | **是**（兩個檔案） | **確認**：`NativeSaveLoader.cs` 實際 3,683 行（計畫書寫 3,682，差 1 行應是行尾計算方式不同），內含 298 個 `EntryPoint`（計畫書寫「~300」）；`NativeKeyInfoCatalogs.cs` 3,247 行，內含 27 個 catalog 加 27 個 SafeHandle 類別。拆分建議見 B1-22。 |
| 6 | CI 只跑 release | 否（Block 6） | 未查。 |
