# Code audit — Block 3: Game-data resolution, localization & assets (2026-11-28)

> **Status: REPORT ONLY.** 依 [code-audit-2026-11-28-plan.md](code-audit-2026-11-28-plan.md)
> 的 Block 3 範圍做靜態審查；本回合 **未修改任何程式碼**，修法只列建議，實作留給後續 fix PR。
>
> - Snapshot：`dev` @ `9ba7ae4`（PR #64）。所有 `file:line` 以此 commit 為準；`src/...` 路徑相對於 repo 根目錄，
>   只寫檔名時指 `src/CrimsonAtomtic.Ui/` 底下的檔案。
> - Rust 端對照（唯讀）：`D:\Github\crimson-rs\src\c_abi\mod.rs:128`（`NOT_FOUND = -16`）。
> - 範圍：計畫書 Block 3 的全部 Files，加上 `MainWindowViewModel.cs` 指定的 522–710、1943–2006 兩段。

## 0. 範圍說明與限制

1. **測試基線未執行**：本回合依指示只做靜態審查，沒有跑 `dotnet run --project src/CrimsonAtomtic.Tests`，
   所以 **沒有記錄測試數量**。Block 1 / 2 也遇到同樣情況，開始修 Block 3 之前請補跑一次。
2. **「Resolver coverage」無法靜態量測**：計畫書要求統計最新存檔中，每個 TypeName 有多少個解析不到的
   distinct key。這必須實際載入存檔與遊戲資料，靜態審查做不到。本報告只檢查程式碼路徑與測試有沒有覆蓋
   （見 B3-13），沒有量出數字。
3. **範圍外的程式碼只當證據引用**：`MainWindow.axaml.cs`（Block 2）、`DyeEditorViewModel` / `KnownDyeService`
   （Block 4）、`GameDataLayout.cs` / `NativeDyeCatalogs.cs`（Block 1）只唯讀引用，用來證明 Block 3 的問題確實會被
   觸發。這些位置本身的修正見 §4 交接清單。
4. **Avalonia 執行期行為**（B3-04、B3-09）：判斷依據是程式碼與 API 契約，沒有實機驗證。修正時請一併做實機確認。

## 1. 摘要

| 嚴重度 | 數量 | 編號 |
|---|---|---|
| bug | 3 | B3-01 – B3-03 |
| risk | 10 | B3-04 – B3-13 |
| rule violation | 4 | B3-14（Rule 4）、B3-15（Rule 3）、B3-16（Rule 7）、B3-17（UI 字串） |
| cleanup | 5 | B3-18 – B3-22 |

**最嚴重的三項**：

- **B3-01**：Tools → Set Game Install Folder 重新 bootstrap 時，PALOC catalog 與幾個 lazy cache **沒有清掉**。
  新安裝的 key-table bridge 會配上舊安裝的語言表，名稱可能解析錯誤。
- **B3-02**：`PortraitProvider.EnsurePortraitList` 有 race。第一次開 Browse Characters 時，大部分列會被
  記成「沒有頭像」，直到重開 app 都不會恢復。
- **B3-03**：圖示 / 頭像快取檔寫入不是原子操作。寫到一半的檔案會被當成「已快取」，之後永遠不會重新抽取。

**建議的修正 PR 順序**：
1. **重新 bootstrap 正確性**（B3-01、B3-04、B3-11）：先把 `LocalizationProvider` 的狀態重設集中成一個
   `Reset()`，再處理 Set Game Install Folder 對開著的子視窗的影響。
2. **執行緒安全**（B3-05、B3-02）：決定 `LocalizationProvider` 的執行緒模型（不可變快照 swap，或全面加鎖），
   同一個 PR 修掉 portrait 的 race。
3. **資產快取**（B3-03、B3-06、B3-07）：原子寫入、DDS 尺寸檢查、快取版本戳記。
4. **資料正確性**（B3-08、B3-12）與 **coverage 測試**（B3-13）。
5. Rule 3 / 4 / 7、UI 字串（B3-14 – B3-17）與 cleanup。

---

## 2. Findings

### B3-01 — 重新 bootstrap 不會重載 PALOC：新安裝的 bridge 配上舊安裝的語言表
- **嚴重度**：bug（名稱解析錯誤或缺漏；舊資源一直佔用到 app 結束）
- **位置**：`Services/LocalizationProvider.cs:549-640`（`TryBootstrapFromGameRoot`）、`:1151-1156`（`TryLoadCatalog`
  的 early return）、`:1887-1891`（`_missionNameToKey`）、`:642-648` / `:1079-1094`（失敗時保留舊 bridge）、
  `:745-773`（DLC merge）；呼叫端 `ViewModels/MainWindowViewModel.cs:522-561`（`SetGameInstallRoot`）
- **證據**：
  - `TryBootstrapFromGameRoot` 只重設了 `_factionMembers` 與 `_itemDyeJoin`（`:556-562`），**沒有清掉**
    `_catalogs`、`_palocParts`、`_namesByLang`、`_missionNameToKey`。
  - 結尾的 `TryLoadCatalog(DefaultLanguage)`（`:637`）一進來就檢查 `if (_catalogs.ContainsKey(langCode)) return true;`
    （`:1153-1156`）。第二次 bootstrap 時 `"eng"` 已經在字典裡，所以 **完全不會從新安裝重新抽取 PALOC**。
    `_languageSources` 重新探測過（`:1098`），但 catalog 還是舊的。
  - key-table bridge 每次都換成新安裝的版本（`TryLoadNicheBridge` 先 `slot?.Dispose()` 再指派，`:1089-1090`），
    結果是 **新 bridge 的 hash-hop 查舊 PALOC**（`DisplayOrFallback`，`:2406-2429`）。
  - 新安裝缺某張表、或抽取失敗時，`slot` 保留 **上一個安裝的 handle**（`:1089` 只在成功時才換掉）。
    `TryBootstrapItemInfo` 等函式在 `0008/0.pamt` 不存在時直接 `return`（`:645-648`、`:668-671`、`:907-910`…），
    舊的 `_itemInfo` 也一樣留著。
  - 更隱蔽的情況：base 的 `characterinfo` / `knowledgeinfo` / `faction` / `factionnode` 重載失敗時，`TryMergeDlcTables`
    （`:755-758`）會把 DLC 列 **再 merge 一次** 到舊的已 merge handle 裡，DLC 列因此重複
    （例如 `FactionMemberCount` 會重複計算）。
  - `_missionNameToKey`（`:1887`，`??=`）從來沒有重設。Sealed Artifact 流程（`LookupMissionKeyByInternalName`）
    在換安裝之後會繼續用舊的 name → key 對照。
  - `SetGameInstallRoot` 的註解寫著「failure leaves the provider in whatever state the previous bootstrap left it」
    （`MainWindowViewModel.cs:532-534`），把失敗當成可接受。但上面這些問題 **成功時也會發生**。
- **重現**：開機時自動偵測到安裝 A（例如 `D:\SteamLibrary\...\Crimson Desert`）→ Tools → Set Game Install Folder
  選安裝 B（例如另一份版本快照 `X:\Crimson Desert`）→ 載入存檔，resolved-name 欄看到的是 B 的 bridge 配 A 的 PALOC。
  兩邊版本不同時，名稱錯位或空白。次要語言也一樣，`SecondaryLanguage` 若 B 沒有該語言，仍然留著（`:520-541`
  只在 setter 裡驗證）。
- **建議修法**：抽出 `private void ResetAll()`，把 `Dispose()`（`:2490-2568`）裡的釋放邏輯搬過去，另外加上
  `_namesByLang`、`_missionNameToKey`、`_languageSources`、`_factionMembers` 的清除，並由 `TryBootstrapFromGameRoot`
  開頭呼叫。`_secondaryLanguage` 在 `DiscoverLanguages` 之後重新驗證，不合法就清成 null。比照
  `Bootstrap_WithoutOrWithBrokenDlc_LoadsTheBaseGame` 加一個「同一個 provider bootstrap 兩次」的測試。

### B3-02 — `EnsurePortraitList` race：同時開始的頭像載入，大多數拿到 null 並被永久快取
- **嚴重度**：bug（功能失效；直到重開 app 都不會恢復）
- **位置**：`Services/PortraitProvider.cs:258-283`（`EnsurePortraitList`）、`:162-190`（`GetPortrait`）、`:197-201`、`:250`；
  觸發端 `ViewModels/CharacterPickerViewModel.cs:173-191, 276-295`、`ViewModels/CharacterRefsBrowserViewModel.cs:168-182, 235-245`
- **證據**：
  - `EnsurePortraitList` 在 lock 內設 `_portraitListLoadAttempted = true`，然後 **在 lock 外** 呼叫
    `_paz.ListNpcPortraits`（`:274`）。其他執行緒在載入完成前進來時，`_portraitListLoadAttempted` 已經是 true，
    會直接 `return _portraitListBuffer`，而這時它還是 **null**（`:262-265`）。
  - `ResolveAndDecode` 收到 null 就回傳 null（`:210-214`），`GetPortrait` 再把 null 寫進 `_cache[characterKey]`
    （`:187`）。後面同一個 provider 的查詢都直接命中這個 null（`:170-173`）。
  - `CharacterPickerViewModel.Refresh` 開窗時會對最多 500 列逐一 `StartPortraitLoad`，每列各開一個 `Task.Run`
    （`:185`、`:286`）。`CharacterRefsBrowserViewModel.ApplyFilter` 沒有上限，最多 5,000 列（`MaxRows`，`:38`），
    每列都 `Task.Run`（`:178`、`:239`）。冷快取時，`ListNpcPortraits` 執行期間排進 thread pool 的工作幾乎都會拿到 null。
  - 次要 race：同一個 `CharacterKey` 在 Refs browser 會重複出現很多次。多個執行緒同時 miss，會各自走一次完整的
    抽取 + 解碼，並同時 `File.WriteAllBytes` 同一個檔案（`:250`）。另一個執行緒若在檔案寫到一半時 `File.Exists`
    成立（`:198`），會讀到不完整的 WebP，解碼失敗回傳 null，而且可能先搶到 `_cache` 的「first winner」，
    把正確的 Bitmap 丟掉（`:182-186`）。
- **重現**：刪除 `%LOCALAPPDATA%\CrimsonAtomtic\PortraitCache\` → 啟動 → Tools → Browse Characters：只有少數列出現頭像。
  關掉再開，結果一樣（同一個 provider 的 null 快取）。重開 app 後，會多出一些上次碰巧成功、寫進磁碟快取的頭像。
- **建議修法**：
  - 頭像清單改用 `Lazy<byte[]?>(…, LazyThreadSafetyMode.ExecutionAndPublication)`，或在 lock 內完成載入。
    載入失敗時不要把個別 key 記成 null。
  - 每個 key 一個 in-flight `Task<Bitmap?>`（`ConcurrentDictionary<uint, Lazy<Task<Bitmap?>>>`），避免同一個 key 重複抽取。
  - 列的頭像載入改走有上限的佇列（`SemaphoreSlim` 限 2–4 個並行），或只在列真的顯示時才載入。

### B3-03 — 圖示 / 頭像快取寫入不是原子操作；寫壞的檔案會被當成「已快取」，永遠不重抽
- **嚴重度**：bug（機率低，但出現後不會自己修復）
- **位置**：`Services/IconExtractionService.cs:161-166`（以 `File.Exists` 判斷 skip）、`:229`
  （`File.WriteAllBytesAsync(targetPath, webp, cancellationToken)`）、`:232-241`；`Services/PortraitProvider.cs:197-201, 250`；
  `Services/IconProvider.cs:156-190`
- **證據**：
  - 圖示直接寫到最終路徑，還把 cancellation token 傳給寫入呼叫。寫到一半被取消（Cancel 鈕或關窗，
    `IconExtractionProgressDialog.axaml.cs:163-183`）、`IOException`（磁碟滿），或 process 被終止，
    都會留下截斷的 `<ItemKey>.webp`。程式碼沒有刪除它的路徑。
  - 下次執行時 `overwriteExisting: false`（UI 寫死，`MainWindow.axaml.cs:1782`），`File.Exists` 成立就算
    `SkippedAlreadyCached`（`:162-166`），**壞檔保留**。取消後顯示的說明 `IconExtractCancelledBody` 寫的是
    「Run the action again to resume」，但重跑時恰好會跳過這個壞檔。
  - `IconProvider.GetItemIcon` 解碼失敗只會累加 `DecodeFailures` 並快取 null（`:185-189, 201`），不會刪檔或要求重抽。
  - 頭像相同：`File.WriteAllBytes(cachedFile, webp)`（`PortraitProvider.cs:250`）不是原子寫入；磁碟快取命中時
    `TryDecodeBitmap` 失敗只回傳 null（`:198-201`），**不會退回重新抽取**。
- **建議修法**：寫到同目錄的暫存檔（`<key>.webp.tmp`），完成後 `File.Move(tmp, final, overwrite: true)`；
  失敗或取消時刪掉暫存檔。讀取端解碼失敗時刪掉該檔（或改名成 `.bad`），讓下次重新抽取。頭像的磁碟快取解碼
  失敗時，改走 cold path 重抽。

### B3-04 — 換掉 icon / portrait provider 後，開著的非 modal picker 仍綁著已 Dispose 的 Bitmap
- **嚴重度**：risk（可能顯示錯誤、例外或洩漏；Avalonia 畫已 Dispose 的 Bitmap 時的行為未實機驗證）
- **位置**：`ViewModels/MainWindowViewModel.cs:541-559`（`SetGameInstallRoot`）、`:563-590`（`RefreshIconCache`）、
  `:504-511`（註解自己承認 picker 不會刷新）；`ViewModels/CharacterPickerViewModel.cs:62`、
  `ViewModels/CharacterRefsBrowserViewModel.cs:98`；`Services/PortraitProvider.cs:319-329`、`Services/IconProvider.cs:220-230`
- **證據**：
  - 兩個流程都把舊 provider 的 `Dispose` 以 `DispatcherPriority.Background` post 出去。理由是「Background runs after
    render, so no open character/mercenary dialog is bound to a disposed Bitmap」（`:551-553`）。但 `RebuildFromTop()`
    只重建主視窗的 grid。
  - Browse Characters、Browse Character References、Browse Items 都是 `Show(this)` 的非 modal 視窗
    （`MainWindow.axaml.cs:1577, 1619, 1730`）。Extract Icons 與 Set Game Install Folder 都從主視窗選單觸發，
    picker 可以同時開著。
  - picker 在建構時就抓住 provider：`_portraits = localization.Portraits`。已載入列的 `Portrait` 屬性仍指向舊
    Bitmap，Dispose 之後還在畫面上。
  - 尚未載入的列繼續呼叫舊 provider 的 `GetPortrait`。`Dispose` 沒有 `_disposed` 旗標，所以快取會被 **重新填滿**，
    而這些新 Bitmap 再也不會被 Dispose（洩漏）。
  - Item Picker 的圖示經由 `ItemKeyToIconConverter`。已實體化的 `Image` 不會重新查詢 converter，繼續顯示舊
    provider 已 Dispose 的 Bitmap。
- **建議修法**：provider 加上 `_disposed` 旗標，Dispose 之後 `GetPortrait` / `GetItemIcon` 回傳 null。被取代的
  provider 不立即 Dispose，改成發出 `ProvidersReplaced` 事件讓開著的 picker 重建（或直接關閉 picker）。最保守的
  作法是被取代的 provider 只清除參照，交給 GC / finalizer，不主動 Dispose。

### B3-05 — `LocalizationProvider` 被背景執行緒讀取，同時又在 UI 執行緒上被修改
- **嚴重度**：risk（`Dictionary` 並行讀寫是未定義行為；可能出現 `ObjectDisposedException` / `ArgumentNullException`，
  經由 async command 讓 process 崩潰，見 B2-04）
- **位置**：寫入端 `Services/LocalizationProvider.cs:1199-1205`（`TryLoadCatalog`）、`:1098`、`:777-779`、`:653`、
  `:1089-1090`、`:557-562`；讀取端 `:2252-2275`（`ResolvePortraitForCharacter`）、`:2297-2322`、`:2467-2478`、
  `:1440-1476`、`:1520-1530`
- **證據**：
  - 背景讀取者（全部都是非 modal 視窗開著時的 `Task.Run`）：
    - 頭像載入 → `PortraitProvider.GetPortrait` → `ResolvePortraitForCharacter`（讀 `_characterInfo`、`_palocParts`）。
    - `KnownDyesViewModel.cs:63` → `KnownDyeService.Scan` → `ResolveByFieldTypeName` / `ResolveDyeThemeName`
      （讀 `_palocParts`、`_secondaryLanguage`、`_dlcKeyResolvers`）。
    - `DyeEditorViewModel.cs:114` → `LookupDyeSlotCount` → `ItemDyeJoin()`。
    - 另有 `KnowledgeEditorViewModel.cs:97`、`AbyssGatesViewModel.cs:233` 等。
  - UI 執行緒寫入：Tools → Secondary Language → `SetSecondaryLanguage` → `TryLoadCatalog` 寫入 `_catalogs` /
    `_palocParts` / `_namesByLang`。Set Game Install Folder → `Clear()` 多個字典並 Dispose 全部 bridge。
    `Dictionary<,>` 只保證「多讀、無寫」時安全。
  - `_secondaryLanguage` 在同一個運算式裡讀兩次：`_secondaryLanguage is null ? null : ResolveKeyTableOne(..., _secondaryLanguage)`
    （`:2300-2302`）、`:2474-2476`。中間若被改成 null，`_palocParts.TryGetValue(null)` / `_namesByLang.TryGetValue(null)`
    會丟出 `ArgumentNullException`。
  - `ItemDyeJoin()` 在 lock 內回傳參照，`LookupDyeSlotNumbers` 卻在 **lock 外** 使用它（`:1524`）。重新 bootstrap 在
    lock 內 Dispose `_itemDyeJoin`（`:557-562`）。SafeHandle 會擋下 use-after-free（`NativeDyeCatalogs.cs:1011` 的
    `ObjectDisposedException.ThrowIf`），但這個例外不在 `catch (CrimsonSaveException)` 裡（`:1526`），會往外拋。
  - 附帶問題：join 第一次建置約 2 s，期間持有 `_itemDyeJoinLock`（`:1442-1475`）。這時 UI 執行緒若呼叫
    `LookupDyeSlotNumbers`（`MainWindow.axaml.cs:1076`、`DyeSlotEditorViewModel.cs:143`），會 **卡住 UI** 等這把鎖。
- **建議修法**：把可變狀態收進一個不可變的 `LocalizationSnapshot`（bridges + catalogs + maps）。bootstrap 或切換語言時
  在背景建好新的 snapshot，再用 `Volatile.Write` 一次替換，讀取端每次呼叫先取一次 snapshot 的區域參照。
  `_secondaryLanguage` 讀進區域變數只用一次。被換掉的 snapshot 延後 Dispose（等背景工作結束，或交給 finalizer）。

### B3-06 — DDS 解碼器的尺寸運算會整數溢位，並且在驗證長度之前就配置記憶體
- **嚴重度**：risk（遊戲 / DLC 檔案是半信任輸入；會丟出呼叫端沒有捕捉的例外，或一次配置到 GB 等級）
- **位置**：`Services/IconImageEncoder.cs:126-132`、`:175`、`:184-190`、`:222-228`、`:87-91`
- **證據**：
  - 寬高只檢查 `> 0`（`:128`），沒有上限。`width * height * 4`（`:175`）與 `blocksX * blocksY * blockSize`（`:184`）
    都是 unchecked `int` 運算。
  - **溢位情境**：`w = h = 65536`、DXT5 → `rgba = new byte[0]`（2³⁴ 溢位成 0），`expected = 0`（2³² 溢位成 0），長度檢查
    通過，`WriteBlockToImage` 寫 `image[0]` 時丟出 `IndexOutOfRangeException`。
  - **先配置再驗證**：`rgba` 在長度檢查（`:185`）**之前** 配置。`w = h = 16384` 的 DXT1 檔（只有 128 byte header）
    會先配置 1 GiB，才丟出 `InvalidDataException`。`w = h = 40000` 時，`w*h*4` 溢位成 2,105,032,704，接近 2 GB。
  - L8 路徑 `pxCount = width * height`（`:222`）溢位成負數時，長度檢查通過，`new byte[pxCount * 4]` 丟出 `OverflowException`。
  - 呼叫端只捕捉 `InvalidDataException` / `InvalidOperationException`（`IconExtractionService.cs:213-225`、
    `PortraitProvider.cs:245-246`）。在 `IconExtractionService` 裡，`IndexOutOfRange` / `Overflow` / `OutOfMemory`
    會從 `ProcessOneAsync` 往外拋，**整個抽取中止**，與類別註解「A single bad item never aborts the run」（`:28-29`）不符。
  - 測試（`IconImageEncoderTests`）只涵蓋非 DDS、截斷、FourCC 不支援三種情況，沒有超大尺寸的測試。
- **建議修法**：解析 header 時限制尺寸（例如 ≤ 16384），所有大小改用 `long` / `checked` 計算；先驗證 pixel 資料長度
  再配置。在 `EncodeDdsToWebp` / `DecodeDdsToRgba` 的邊界把非預期例外轉成 `InvalidDataException`。補上溢位尺寸的單元測試。

### B3-07 — 快取沒有版本或失效機制：遊戲更新後，圖示與頭像永遠是舊的
- **嚴重度**：risk（顯示錯誤的資產，且使用者無法從 UI 修正）
- **位置**：`Views/MainWindow.axaml.cs:1782`（`overwriteExisting: false` 寫死）、`Services/IconExtractionService.cs:64-67, 161-166`；
  `Services/PortraitProvider.cs:197-201`；`Services/LocalizationProvider.cs:446-451`
- **證據**：
  - `IconCache\<ItemKey>.webp` 與 `PortraitCache\<CharacterKey>.webp` 都只以 key 命名，沒有記錄來源的遊戲版本
    （`GameDataVersion` 已經讀到了，`LocalizationProvider.cs:570`）。
  - `overwriteExisting` 的參數說明寫「Useful for incremental top-ups after a game patch」，但它只會補 **新** 項目；
    UI 沒有任何途徑傳 `true`，所以 patch 換掉的圖示永遠不會更新。
  - 頭像來自 fuzzy match（`MinAcceptableScore = 50`，`PortraitProvider.cs:58`）。錯誤的配對一旦寫入磁碟就永久生效。
    `ConfigurePortraitProvider` 的註解明說「Disk-cached portraits from a previous game-root stay valid」（`:448-451`），
    但換安裝 / 換版本時，PALOC 名稱與 `ResolvePortrait` 的評分都可能不同。
- **建議修法**：在快取根目錄寫一個 `cache.json`，記錄 `{paver 版本, 格式版本}`；版本不符時整個資料夾作廢（或改用帶版本的
  子目錄）。Extract Icons 對話框加一個「Re-extract all」選項。

### B3-08 — Character Picker 的 pick mode 寫入 lo24 key，丟掉原值的 cat byte
- **嚴重度**：risk（可能寫入語意不同的存檔值；遊戲端的實際影響未經實機確認）
- **位置**：`Views/MainWindow.axaml.cs:1643-1652`（`OnPickCharacterClick`）；`ViewModels/CharacterPickerViewModel.cs:196-204`；
  對照 `src/CrimsonAtomtic.RustInterop/NativeKeyInfoCatalogs.cs:1207-1214`、`Services/LocalizationProvider.cs:1348-1350`
- **證據**：
  - picker 列出的是 `characterinfo` 的 row key，「already in lo24 form — no cat-byte」（`CharacterPickerViewModel.cs:198-199`）。
  - cat byte 是「hi-byte, 0x02..=0xFE region / variant / faction marker」，FieldNPC spawn row 特別常見
    （`NativeKeyInfoCatalogs.cs:1210-1213`）。`GetCharacter` 的註解也說存檔端的值「still need the strip before they'll match」。
  - `OnPickCharacterClick` 直接 `selectedField.RawText = key.ToString(...)`，沒有保留原值的高位元組，也沒有提示使用者。
    如果原值是 `0x05_0F4240`，選同一個角色會寫成 `0x00_0F4240`。
- **建議修法**：pick 時以 `(original & 0xFF000000) | key` 保留原值的 cat byte（或預設保留，再給一個「清除 marker」選項）。
  至少在原值帶 cat byte 時顯示警告。以實機存檔驗證 FieldNPC 列被改寫後的行為。

### B3-09 — 三個複製按鈕的 handler 是沒有 try/catch 的 `async void`，剪貼簿例外會讓 process 崩潰
- **嚴重度**：risk
- **位置**：`Views/ItemPickerWindow.axaml.cs:29-44`、`Views/CharacterPickerWindow.axaml.cs:41-56`、`Views/LocalizationSearchWindow.axaml.cs:27-42`
- **證據**：
  - `await clipboard.SetTextAsync(text)` 沒有 try/catch。Windows 剪貼簿被其他程式鎖住時，平台層可能丟出例外。
  - `async void` 的例外會回拋到 dispatcher；專案沒有全域 handler（Block 2 B2-04 的 grep 結果），所以 process 會崩潰。
  - `LocalizationSearchWindow` 的註解宣稱「treated as a no-op rather than surfacing as an unhandled exception」（`:24-25`），
    但只處理了 null clipboard 的情況。
  - 三份程式碼幾乎相同（cleanup）。
- **建議修法**：抽成一個共用的 `ClipboardHelper.TryCopyAsync(Control, string)`，內部捕捉例外並回報到狀態列；
  三個視窗改呼叫它。

### B3-10 — 每個載入過的語言都常駐記憶體，切換次要語言會累積
- **嚴重度**：risk（記憶體）
- **位置**：`Services/LocalizationProvider.cs:513-519`（註解）、`:1151-1206`、`:1139-1141`
- **證據**：
  - `SecondaryLanguage` 的註解寫「disposes the previously-active secondary catalog … but currently we keep all loaded
    catalogs in memory」。`TryLoadCatalog` 只會新增，`Dispose()` 之前沒有任何移除。
  - 依 `:1139-1141` 的估計（14 個語言約 350 MB），每個語言約 25 MB native 記憶體，另加 `BuildNameMap` 的 managed 字典。
    把 Tools → Secondary Language 逐一切換一輪，就保留了全部 13 個語言。
- **建議修法**：切換次要語言時 Dispose 前一個非 `eng`、非新選擇的 catalog（三個字典都要移除）。或只保留 LRU 2 個。

### B3-11 — `GameDataLayout.DlcNames` 的檔案系統例外沒有被捕捉，可能讓 app 啟動失敗
- **嚴重度**：risk
- **位置**：`src/CrimsonAtomtic.RustInterop/GameDataLayout.cs:110`（`Directory.GetDirectories`）；呼叫端
  `Services/LocalizationProvider.cs:697, 747, 780, 1104, 1486`；`App.axaml.cs:46`
- **證據**：
  - `DlcNames` 只先檢查 `Directory.Exists`，`GetDirectories` 可能丟出的 `UnauthorizedAccessException` / `IOException`
    （權限、網路磁碟斷線、AV 鎖定）沒有處理。
  - 五個呼叫點的 `foreach (var dlc in GameDataLayout.DlcNames(...))` 都在 try **外面**。
  - `App.OnFrameworkInitializationCompleted` 呼叫 `TryBootstrapFromGameRoot` 時也沒有 try（`App.axaml.cs:46`），
    例外會直接中止啟動。
  - `TryBootstrapFromGameRoot` 的契約是「Degrades cleanly when no install is found」，實際上這條路徑違反它。
- **建議修法**：`DlcNames` 內部捕捉 `IOException` / `UnauthorizedAccessException` 並回傳空清單（Block 1）。
  `App` 的 bootstrap 包一層保底 catch，失敗時以「未載入」狀態繼續。

### B3-12 — `LevelGimmickSceneObjectInfoKey` 先查 gimmickinfo 表，兩個 namespace 的數值範圍重疊（Rule 12）
- **嚴重度**：risk（可能顯示另一個 namespace 的名稱；與 FactionKey/0x30 是同一類問題）
- **位置**：`Services/LocalizationProvider.cs:2354-2356`、`:250-251`、`:218-224`、`:792-794`、`:2215-2221`
- **證據**：
  - `LevelGimmickSceneObjectInfoKey` 被路由到 `_gimmickInfo`（GimmickInfo 表）的 `LookupDisplayName` / `LookupStringKey`，
    而且 table-driven 路徑 **優先於** PALOC byte 0x00 的 fallback（`:2215-2221`）。
  - 程式碼裡的註解本身就顯示兩者是 **不同的 key 空間，但數值範圍重疊**：GimmickInfo `1007815 → "Skybridge Gate"`（`:172`），
    SceneObject `1000043 → "Skybridge Gate"`（`:222`），兩者都在 1,000,000 以上。任何 scene-object key 只要數值剛好等於某個
    gimmickinfo row，就會得到那個 gimmick 的名稱。
  - `:204-210` 記錄的 FactionKey 事件，正是「共用數值範圍的兩個 namespace 被同一條路徑解析」造成的。
  - 這是在消費端靠 namespace 猜測來補 parser 的缺口，屬於 Rule 12 要避免的模式。
- **建議修法**：確認遊戲是否有獨立的 level-gimmick scene-object 表。有的話，請 crimson-rs 加 bridge（依 CLAUDE.md
  rule 8 先改 source repo）。沒有的話，`LevelGimmickSceneObjectInfoKey` 只走 PALOC 0x00，並把它從 `TableDrivenKeyTypes`
  與 DLC 註冊（`:794`）中移除。補一個測試鎖定「scene-object key 不會解析成 gimmickinfo 名稱」。

### B3-13 — 沒有任何測試斷言「最新存檔的每個 key TypeName 都能解析」
- **嚴重度**：risk（測試缺口；status.md gotcha「A green suite says nothing about keys it never asks for」）
- **位置**：`src/CrimsonAtomtic.Tests/LocalizationTypeByteDiscoveryTests.cs:207-320`；`src/CrimsonAtomtic.Tests/LocalizationProviderTests.cs:170-246`；
  `Services/LocalizationProvider.cs:2157-2161`
- **證據**：
  - `Scan_DiscoverTypeBytesFromLiveSave` 會蒐集存檔中所有 `*Key` TypeName（`:240`、`:347-352`），但只 `output.WriteLine`，
    **沒有任何 Assert**。
  - `NewestSave_FactionKeysResolveToFactionRows` 只檢查 FactionKey。
  - 其餘 31 個可解析的 TypeName（`TableDrivenKeyTypes` 30 個 + `ItemKey` + `InventoryKey`，`CanResolveTypeName` `:2157-2161`）
    沒有「最新存檔中解析不到的 distinct key 數」的回歸檢查。存檔中出現、但根本不在清單裡的 `*Key` TypeName 也沒有測試把關。
- **建議修法**：新增 `NewestSave_EveryKeyTypeName_ResolvesOrIsAllowListed`：走訪最新存檔，以 `ResolveByFieldTypeName` 統計每個
  TypeName 的 unresolved distinct key 數，與一份提交進 repo 的 baseline（TypeName → 允許的最大未解析數）比對；不在
  `CanResolveTypeName` 裡的 `*Key` TypeName 列入明確的 allow-list。沒有存檔或安裝時 skip，比照現有 live 測試。

### B3-14 — Rule 4：`UiLanguageService` 直接 P/Invoke `kernel32!GetUserDefaultUILanguage`
- **嚴重度**：rule violation（Rule 4；同時影響跨平台）
- **位置**：`Services/UiLanguageService.cs:255-303`；`App.axaml.cs:71-73`；`ViewModels/MainWindowViewModel.cs:656`
- **證據**：
  - `NativeMethods.GetUserDefaultUILanguage` 以 `[DllImport("kernel32.dll")]` 宣告在 `Ui/Services` 裡，不在 `Core` 的介面後面。
  - Linux / macOS 上，`DllNotFoundException` 被裸 `catch`（`:262`）吞掉，退回 `DetectFromCulture(CultureInfo.CurrentUICulture)`。
    但 csproj 設了 `<InvariantGlobalization>true</InvariantGlobalization>`（`CrimsonAtomtic.Ui.csproj:20`），那裡的 `Name` 永遠是 `""`，
    所以 **非 Windows 平台的自動偵測永遠回傳英文**。CLAUDE.md 寫明 Linux / macOS 是目標平台。
- **建議修法**：在 `Core` 新增 `IOsUiLanguageProvider { string? GetUiLanguageTag(); }`，由 `WindowsPlatformPaths` 同層的
  Windows 實作包住 P/Invoke；Linux / macOS 實作讀 `LANG` / `LC_ALL` / `LC_MESSAGES`，macOS 也可以讀 `AppleLanguages`。
  `UiLanguageService` 只依賴介面。

### B3-15 — Rule 3：同步 I/O 與長時間工作跑在 UI 執行緒
- **嚴重度**：rule violation（Rule 3）
- **位置**：
  - `App.axaml.cs:46, 54`：啟動時同步 bootstrap，數十次 PAZ 抽取、約 210 次 PAMT `ListDir` 語言探測
    （`LocalizationProvider.cs:1117-1149`）、英文 PALOC 載入 + `BuildNameMap`（註解說每個語言 1–2 s，`:1222-1224`）。
  - `ViewModels/MainWindowViewModel.cs:535`：Set Game Install Folder 時同步重新 bootstrap（數秒凍結）。
  - `ViewModels/MainWindowViewModel.cs:594, 608`：切換次要語言時同步 `TryLoadCatalog`（抽取 + 解析 + `BuildNameMap`），
    接著同步 `loader.LoadBlockDetails`。
  - `ViewModels/MainWindowViewModel.cs:598-600, 635-636, 652, 663`：`AppSettingsStore.Load` / `TrySave` 同步讀寫檔案。
    其中 `IsUiLanguageAuto` 是 **property getter，每次讀取都讀一次磁碟**。
  - Picker 建構子同步掃描：`LocalizationSearchViewModel.cs:39-67`（約 18 萬筆 × 兩個語言，每筆一次 FFI `GetEntry`）、
    `CharacterPickerViewModel.cs:64-88`（每個角色 × 每個 PALOC part 一次 FFI `LookupDisplayName`；2.01 以後一個語言有很多 part）。
  - `Services/ItemKeyToIconConverter.cs:39-60` → `IconProvider.GetItemIcon`：在 layout 期間同步讀檔 + WebP 解碼（`IconProvider.cs:156-166`）。
- **建議修法**：bootstrap 與語言載入移到 `Task.Run`，完成後在 UI 執行緒交換 snapshot（與 B3-05 合併處理）。picker 建構改成
  `static Task<…> CreateAsync`，先開窗顯示 loading。`IsUiLanguageAuto` 改成快取欄位，設定變更時更新。圖示載入改成非同步
  （converter 先回傳 null，載入完成後由列的 ViewModel 觸發 PropertyChanged，比照頭像的作法）。

### B3-16 — Rule 7：magic string 散落各處，沒有集中檔案
- **嚴重度**：rule violation（Rule 7；pre-scan #4 在 Block 3 確認）
- **位置**：
  - PAZ 群組字面值：`Services/LocalizationProvider.cs:644, 667, 906, 935, 1007, 1033` 寫死 `"0008"`，同一個檔案的 `:577, 605, 1453`
    卻用 `GameDataLayout.GameDataGroup`；`"0009"`（`:1510`）；`"0012"` 在 `PortraitProvider.cs:65` 與 `IconExtractionService.cs:37`
    各定義一次；`"0.pamt"` 出現十幾次。
  - TypeName 字串在四處各寫一次：`TypeNameToTypeByte`（`:213-225`）、`TableDrivenKeyTypes`（`:242-293`）、DLC 註冊（`:784-822`）、
    `ResolveBaseKeyTable` 的 switch（`:2326-2397`），另加 `"InventoryKey"`（`:2161, 2203, 1765`）。任一處拼錯都會 **靜默** 讓那一欄空白，
    沒有編譯期檢查。
  - 錯誤碼：`IconExtractionService.cs:271-273` 自己定義 `NotFoundErrorCode = -16`（`NativeMethods.NOT_FOUND` 是 `internal`，
    `NativeSaveLoader.cs:1722, 1743`）；Rust 端是 `mod.rs:128 NOT_FOUND = -16`。
  - `UiLanguageService.cs:99` 的 `"__UiLangCode__"` 必須與三個 `Strings/*.axaml` 的 marker 一致，但沒有共用常數。
- **建議修法**：
  - `Ui` 專案新增 `UiConstants.cs`（或 `GameDataNames.cs`），集中 PAZ 群組、`"0.pamt"`、快取子目錄名、TypeName 常數，每個都加註解。
  - TypeName 解析改成單一表驅動：`Dictionary<string, KeyResolverSpec>` 一列描述 base bridge、DLC 表 stem、有無 PALOC chain，
    讓三處註冊從同一份資料產生。
  - RustInterop 公開 `CrimsonErrorCode` 常數（或在 `CrimsonSaveException` 上加 `IsNotFound`），消費端不再寫 `-16`。

### B3-17 — UI 字串寫死在 C#，沒有進三語系 `Strings/*.axaml`
- **嚴重度**：rule violation（Cross-cutting「UI strings in all three Resources/Strings/*.axaml」）
- **位置**：
  - `Services/LocalizationProvider.cs:310-326`（`InventoryContainerLabels`：「Backpack」、「Kuku Pot」…）、`:1760-1786`
    （`FormatItemSourceLabel`：「Equipped」、「Quick-Use Reserve」、「(Main, …)」、「InventoryKey {n}」）。
  - `ViewModels/ItemPickerViewModel.cs:119`（「+ Bag」）、`:129-130`（「Name (secondary)」）、`:233-244`（`ResultsCountText` 全部）。
  - `ViewModels/CharacterPickerViewModel.cs:137-138, 152-162`。
  - `ViewModels/LocalizationSearchViewModel.cs:84-85, 99-109`。
  - `ViewModels/CharacterRefsBrowserViewModel.cs:51-52, 82-85, 130-131`（`"<unknown>"`）、`:137`（`"<unresolved>"`）、`:160-162`。
    同一個檔案的 `:116` 卻已經用了 `UiText.Format`，同檔混用。
  - `Services/IconExtractionService.cs:94-102`：例外訊息由 `IconExtractionProgressDialog.axaml.cs:127` 原樣顯示給使用者。
- **補充**：三個 `Strings/*.axaml` 本身是同步的（見 §3），問題在於上述字串根本沒進字典。日文 / 繁中 UI 下，這些位置仍顯示英文。
- **建議修法**：全部改用 `UiText.Get` / `UiText.Format` 並在三個字典新增 key。`InventoryContainerLabels` 改成存 resource key，
  例如 `InventoryKeyLabel_2`。

### B3-18 — `FormatPair` 對只有 internal name 的 key 顯示「X / X」
- **嚴重度**：cleanup（顯示雜訊；與註解描述的意圖不符）
- **位置**：`Services/LocalizationProvider.cs:2480-2488`（`FormatPair`）、`:2297-2304`、`:2340-2346, 2365-2378`
- **證據**：Skill / QuestGauge / Store / DyeColorGroup / StringInfo 和 16 個 niche bridge 都沒有 PALOC chain，次要語言查到的是
  同一個 internal name（註解「secondary-language column intentionally echoes English」）。但 `ResolveViaKeyTable` 唯一的出口是
  `ResolveByFieldTypeName` → `FormatPair`，輸出成 **單一欄位** 的 `"Faction_Graymane / Faction_Graymane"`。註解說的「另一欄空白看起來像缺資料」
  只適用於兩個獨立欄位。`ResolveFactionLabel`（`:2106`）就有 `top.Secondary == top.Name` 的去重，`FormatPair` 沒有。
- **建議修法**：`FormatPair` 在 `English == Secondary`（ordinal）時只回傳一個；同步修正 `:2340-2346` 等處的註解。

### B3-19 — 重複的 helper 與死碼
- **嚴重度**：cleanup
- **位置 / 證據**：
  - `LookupUiResourceString` 在 `MainWindowViewModel.cs:1943-1951`、`ItemPickerViewModel.cs:182-185`、`FindItemsViewModel.cs:389`
    各有一份（共 28 個呼叫點）。`UiText.cs:13-15` 寫明它就是用來 **取代** 這些副本，但遷移沒有完成。
  - `UiLanguageService.MatchesUri`（`:391-401`）只有測試在用。`LastApplyOutcome`（`:132`）在正式程式碼中沒有任何讀取者。
    `ApplyOutcome.AlreadyActive`（`:20`）從來沒有被設定。`ResolveActiveFromOs`（`:175-188`）與 `ResolveActive`（`:147-165`）
    的前半段重複。
  - `ConfigureIconProvider` / `ConfigurePortraitProvider` 的 `ReferenceEquals(previous, Icons) ? null : previous`（`:440, 462`）
    永遠是 false，因為上一行才剛 `new`。文件寫的「or null when nothing changed」永遠不會發生。
  - `TryLoadDyeBridge`（`:981-996`）與 `TryLoadNicheBridge`（`:1079-1094`）逐行相同。
  - 三份複製按鈕 handler（見 B3-09）。
- **建議修法**：三個 VM 改用 `UiText.Get`。刪除 `MatchesUri` 與它的測試（或註明保留原因），以及未使用的 enum 值 / 屬性。
  `Configure*Provider` 改成無條件回傳 previous，並修正文件。合併兩個 loader。

### B3-20 — XML doc 錯置、孤立，以及過時的註解
- **嚴重度**：cleanup
- **位置 / 證據**：
  - 連續兩個 `<summary>`，第一個是孤兒：`LocalizationProvider.cs:731-737`（屬於 `TryBootstrapDlcKeyTables`，卻接在
    `TryMergeDlcTables` 前面）、`:2022-2031`（`ResolveFactionNodeName` 的文件放在 `ResolveDyeThemeName` 前面）、
    `UiLanguageService.cs:80-98`（`SnapshotDictionaries` 的文件放在常數前面）、`MainWindowViewModel.cs:498-521`
    （三個 `<summary>` 疊在 `SetGameInstallRoot` 上；`SetSecondaryLanguage`（`:592`）與 `RefreshIconCache`（`:563`）因此沒有文件）。
  - 過時：`UiLanguageService.cs:66-68, 90-91, 343-353` 以「Avalonia 11.3.12」為依據，但專案用的是 Avalonia 12.2.0。
    `LocalizationProvider.cs:91-99, 118-123` 寫「As of 1.06 … sourced from the 1.06 install」。
    `:1023-1024` 寫「13 niche bridges」，實際載入 16 個。
- **建議修法**：把文件移到正確的成員上。以 Avalonia 12 重新驗證 `Clear()+Add()` 的必要性並更新註解。更新版本敘述。

### B3-21 — `LocalizationProvider.cs`（2,569 行）的結構：可以按職責拆分，DLC 表也重複載入
- **嚴重度**：cleanup（結構；審查期間不重構）
- **證據**：
  - 一個類別混了七種職責：bootstrap、PALOC 語言管理、32 條 key resolver、DLC 合併、dye join、faction 彙總、portrait 撮合，
    以及 item 列舉與 `IsPlayerEditableItem` 這類業務規則。
  - DLC 的 `knowledgeinfo` / `factionnode` / `faction` / `characterinfo` 已經 **merge 進 base handle**（`:755-758`），卻又各自
    **另外載入一份** 註冊成 DLC resolver（`:790-791, 795-796, 803-806`）。這是重複抽取與重複的 native 記憶體，
    而且 merge 之後 DLC resolver 理論上永遠不會被問到。
  - `NativeSkillInfoCatalog.LoadFromBytes(pabgh, pabgb)` 是唯一 header 在前的 API（`NativeKeyInfoCatalogs.cs:825`），
    所以 `:799` 要寫 `(b, h) => …LoadFromBytes(h, b)`、`:916` 也要特別處理。目前是對的，但很容易踩錯（Block 1）。
- **建議修法**：拆成 `GameDataBootstrapper`（載入 + 生命週期）、`PalocLanguageSet`（語言、catalog、name map）、`KeyResolverRegistry`
  （表驅動 resolver，見 B3-16）、`DyeJoinService`、`FactionNameService`。移除已 merge 表的 DLC resolver 註冊。Skill 的 API
  參數順序與其他表統一（Block 1）。

### B3-22 — 所有載入失敗都靜默吞掉，沒有任何診斷資訊；`UiLanguageService` 用了裸 `catch`
- **嚴重度**：cleanup（與 Block 2 的 B2-21「Rule 6 未實作」相關）
- **位置**：`Services/LocalizationProvider.cs` 約 30 個 `catch (CrimsonSaveException) { }` / `catch (IOException) { }`
  （例如 `:656-662, 718-727, 768-769, 856-857, 1092-1093, 1468-1469`）；`Services/UiLanguageService.cs:262`
- **證據**：
  - 這些 catch 都不是寫入路徑，符合「degrade gracefully」的設計。但使用者只能從 `LocalizationStatus` 的筆數猜哪張表沒載到；
    `CrimsonSaveException.ErrorCode` 與表名全部丟失。2.01 / 2.06 這類 layout 變動發生時，只會看到一欄靜默變空。
  - `UiLanguageService.cs:262` 的裸 `catch` 連 `OutOfMemoryException` 也會吞掉。
- **建議修法**：bootstrap 時把每張表的 `(stem, base/DLC, ErrorCode)` 失敗收進 `LocalizationProvider.LoadFailures`，在狀態列
  tooltip 顯示，並在 Rule 6 logger 上線後寫入 log。裸 `catch` 改成
  `catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)`。

---

## 3. Cross-cutting checklist 結果

| 項目 | 結果 | 對應 |
|---|---|---|
| Rule 3 — async I/O / 不在 UI 執行緒阻塞 | ❌ 違反 | B3-15（另 B3-05 的 lock 等待） |
| Rule 4 — OS 呼叫在 `Core` 介面後 | ❌ 違反 | B3-14 |
| Rule 5 — 不使用 reflection API | ✅ 通過。Block 3 檔案沒有 `typeof(...).Get*` / `Activator` / reflection-based JSON。`DllImport` 只有一個 blittable `ushort` 回傳，AOT 安全 | — |
| Rule 7 — magic string 集中 | ❌ 違反 | B3-16 |
| Rule 12 — 不在消費端繞過 parser 問題 | ⚠️ 一處風險 | B3-12。`InventoryContainerLabels` 與 `IsPlayerEditableItem` 的 prefix 判斷是已記錄的刻意設計（遊戲資料沒有對應來源；status.md 也有記載），不列為違規 |
| 例外：不能用空 catch 掩蓋寫入失敗 | ⚠️ | Block 3 沒有存檔寫入路徑。快取寫入的問題是「失敗不清理」，見 B3-03。純載入的空 catch 見 B3-22 |
| UI 字串進三語系 `Strings/*.axaml` | ❌ 有遺漏 | B3-17 |
| 三個字典 key-for-key 一致 | ✅ 通過（見下） | — |

**三語系字典比對結果**（Audit focus 最後一項）：
- `en.axaml` / `ja.axaml` / `zh-TW.axaml` 各有 **852 個 `x:Key`**，三份之間每個 key 恰好出現 3 次，任何一份都沒有重複 key。
  也就是說，key 集合完全相同。
- 格式化 placeholder 的 index 直方圖三份完全相同（`{0}`×171、`{1}`×106、`{2}`×58、`{3}`×22、`{4}`×9、`{5}`×4、`{6}`×3、
  `{7}`–`{10}` 各 2）；含 placeholder 的 key 集合也相同。沒有發現會在 `string.Format` 引發 `FormatException` 的語系差異。
- 計畫書提到的「differ by a few lines」只是 **實體行數**（997 / 1,000 / 999），來自註解與排版，不是 key 的差異。

---

## 4. 交接清單（其他 block 的位置，本報告只當證據）

| 位置 | 問題 | 交給 |
|---|---|---|
| `src/CrimsonAtomtic.RustInterop/GameDataLayout.cs:110` | `DlcNames` 未捕捉 FS 例外（B3-11 的根因） | Block 1 |
| `src/CrimsonAtomtic.RustInterop/NativeKeyInfoCatalogs.cs:825` | Skill `LoadFromBytes` 參數順序與其他表相反（B3-21） | Block 1 |
| `NativeMethods.NOT_FOUND` 是 `internal` | 消費端只能寫 `-16`：`IconExtractionService.cs:273`、`DyeSlotEditorViewModel.cs:209`、`MainWindow.axaml.cs:1116`（B3-16） | Block 1 / 4 |
| `MainWindowViewModel.cs:~470-488`（`LocalizationStatus`） | 「Localization: … items」等英文字串寫死 | Block 2 |
| `Views/MainWindow.axaml.cs:1820` | folder picker 標題「Select Crimson Desert install folder」寫死 | Block 2 |
| `Views/MainWindow.axaml.cs:1835` → `SetGameInstallRoot` | 換安裝後 `GameDataVersion` 改變，但沒有重新執行 `GameVersionMismatchDialog`（`App.axaml.cs:129-135` 只在啟動時檢查） | Block 2 |
| `Views/MainWindow.axaml.cs:1121, 1221, 1838, 1873, 1938` 等 | `(string?)this.FindResource(key) ?? fallback`：若 Avalonia 在找不到 key 時回傳 `AvaloniaProperty.UnsetValue` 而不是 null，硬轉型會丟出 `InvalidCastException`。**未驗證**（本 session 無法讀取 Avalonia XML 文件）。目前三個字典 key 完整，不會觸發；建議統一改用 `UiText.Get` | Block 2 |
| `DyeEditorViewModel.cs:114`、`KnownDyesViewModel.cs:63` | 背景執行 `LocalizationProvider` 讀取（B3-05 的讀取端） | Block 4 |

---

## 5. Pre-scan observations 驗證結果

| # | 計畫書敘述 | 驗證結果 |
|---|---|---|
| 3 | Rule 4：`UiLanguageService` 直接 P/Invoke `kernel32!GetUserDefaultUILanguage`，不在 `Core` 介面後 | **確認屬實**。`Services/UiLanguageService.cs:284-303`（`[DllImport("kernel32.dll")]`），由 `:260` 呼叫。另發現：非 Windows 平台加上 `InvariantGlobalization=true`，自動偵測永遠回傳英文。見 B3-14 |
| 4 | Rule 7：沒有集中的 magic-string 類別 | **在 Block 3 確認屬實**。PAZ 群組（`"0008"` 寫死與 `GameDataLayout.GameDataGroup` 混用）、`"0012"` 重複定義、TypeName 字串四處重複、`-16` 錯誤碼字面值。見 B3-16 |
| 5 | 檔案大小熱點：`LocalizationProvider.cs` 2,569 行 | **確認屬實**（`wc -l` = 2,569）。拆分建議見 B3-21。`MainWindowViewModel.cs` 在 Block 3 範圍內的兩段（522–710、1943–2006）都已審查：前者的問題列在 B3-01 / B3-04 / B3-15 / B3-20，後者的 `LookupUiResourceString` 重複見 B3-19 |

**Audit focus 逐項結論**：
- *Resolver coverage*：無法靜態量測（§0-2）。測試缺口見 B3-13。
- *Base vs DLC fallback 順序*：**正確**。iteminfo（`ItemInfoFor` `:865-879`）、stringinfo（`:882-896`）、key table（`ResolveKeyTableOne`
  `:2306-2322`）、PALOC source（`:1103-1114`、`:1166-1185`）都是 base 優先，DLC 依名稱排序（`GameDataLayout.cs:113`）。
  **沒有安裝時**：`TryBootstrapFromGameRoot(null)` 回傳 false，`Icons` / `Portraits` 是非 null 的 stub，各 picker 以 `ItemCount` /
  `CharacterCount` / `IsLoaded` 擋住（`MainWindow.axaml.cs:962, 1568, 1691`），行為正確。例外情況見 B3-11。
- *Lazy cache 的執行緒安全*：dye join 本身有加鎖，但參照在鎖外使用，而且會阻塞 UI；其他字典完全沒有保護。見 B3-05、B3-02。
  *per-language 字典記憶體*：見 B3-10。
- *`%LOCALAPPDATA%` 快取*：patch 後不會失效（B3-07）、寫壞的檔案不會修復（B3-03）、並行抽取與讀取有 race（B3-02）。
- *DDS bounds check*：header 與 pixel 長度有基本檢查，但有整數溢位與「先配置再驗證」的問題。見 B3-06。
- *三語系字典一致*：**確認一致**（§3）。
