# Code audit — Block 6: Build, release & Python tooling (2026-11-28)

> **Status: REPORT ONLY.** 依 [code-audit-2026-11-28-plan.md](code-audit-2026-11-28-plan.md)
> 的 Block 6 範圍做靜態審查；本回合 **未修改任何程式碼**，修法只列建議，實作留給後續 fix PR。
>
> - Snapshot：`dev` @ `9ba7ae4`（PR #64）。所有 `file:line` 以此 commit 為準；路徑都相對於 repo 根目錄。
> - Rust 端對照（唯讀）：`D:\Github\crimson-rs\src\c_abi\mod.rs`（status code）、
>   `src\c_abi\mission_info.rs`（missioninfo loader）、`src\python.rs`（PyO3 `parse_iteminfo_from_bytes`）。
>   `vendor/crimson-rs/` 只讀了 `.gitignore` 與 `.cargo/config.toml`，沒有改動。
> - 範圍：計畫書 Block 6 列出的全部 Files（`build.ps1`、`build.cmd`、`build_number.txt`、`global.json`、
>   `Directory.Build.props`、`Directory.Packages.props`、`CrimsonAtomtic.slnx`、`scripts/*`、
>   `.github/workflows/release.yml`、`vendor/update_vendors.ps1`、`vendor/README.md`、`tools/` 全部 `.py`
>   與 README、`.gitignore`、`docs/architecture.md`、`README.md`、`scripts/README.md`、
>   `ClaudeMdSizeTests.cs`、`CrimsonAtomtic.Tests.csproj`）。Build 行為牽涉到的
>   `src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj` 也一併讀過。

## 0. 範圍說明與限制

1. **測試基線未執行**：Execution plan 第 4 點要求跑 `dotnet run --project src/CrimsonAtomtic.Tests`
   並記錄數量。本 session 的權限設定擋下了這個指令（連 `dotnet --version` 都需要人工核准），
   所以 **數量沒有記錄**。開始修 Block 6 之前請補跑一次。
2. **沒有實際 build / publish**：`<NoWarn>IL2104;IL3053</NoWarn>` 是否還需要（B6-31）、
   ILCompiler pin 與 CI SDK 的實際衝突（B6-08）只做了靜態判斷，沒有跑 AOT publish 驗證。
3. **`MainWindowViewModel.cs`**：計畫書沒有分配任何 `MainWindowViewModel.cs` 行號範圍給 Block 6，
   所以本報告沒有審查它（版本字串的顯示位於 Block 2 的 1033–1290）。
4. **Python 工具的執行行為**：判斷依據是程式碼和 Rust 端的 API 契約（docstring + 實作），
   沒有對 2.06 安裝實際執行 `tools/` 裡的腳本。
5. **計畫書前提已過時**：Audit focus 寫「`target` vs `target-py`」，但現行 `scripts/setup_python_env.ps1:100`
   用的是 `vendor/crimson-rs/target/py`（巢狀在 crate 的 `/target` ignore 規則下）。本報告以現行程式碼為準，
   過時的文件列在 B6-29。

## 1. 摘要

| 嚴重度 | 數量 | 編號 |
|---|---|---|
| bug | 5 | B6-01 – B6-05 |
| risk | 14 | B6-06 – B6-19 |
| rule violation | 6 | B6-20 – B6-22（Rule 9）、B6-23（Rule 10）、B6-24（Rule 7）、B6-25（tools 規則 6） |
| cleanup | 11 | B6-26 – B6-36 |

**最嚴重的三項：**

- **B6-01 / B6-02**：game 2.06 把 row name 搬進 `.staticinfoheader`，但 `tools/analyze/` 仍然只餵 body。
  `dump_challenges.py` 在 2.06 上 **0 筆輸出、exit 0**；`dump_catalogs.py` 輸出的每個 item `string_key` 都是空字串，
  `extract_keycases.py` 又把空字串當成「已解析」。兩者都是 **靜默的錯誤資料**。
- **B6-05**：`release.yml` 註解宣稱 `package_aot.ps1`「throws on regression」，實際上 single-file 形狀回歸
  只發 warning，而 `dumpbin` 那一關在 CI runner 上根本不會執行。DirectPInvoke 回歸會直接進 draft release。
- **B6-03**：`build debug` / `build.ps1 -Mode Debug` 會建 Rust debug DLL，但 C# 專案永遠複製
  `target\release\crimson_rs.dll`，所以 debug 建置跑的是舊的（或不存在的）release DLL。

**建議的修正 PR 順序：**

1. **Python 工具 2.06 對齊**（B6-01、B6-02、B6-13、B6-20、B6-22）：所有 static-info 讀取改走
   body + header，`load_iteminfo` 把 `""` 視為 null，`_read_pamt` 不再吞例外，`dump_challenges.py`
   移除 import-time 副作用。
2. **Release 管線硬化**（B6-04、B6-05、B6-06、B6-07、B6-08、B6-09、B6-10）：vendor checkout
   失敗即停、回歸檢查改為 throw、pin crimson-rs SHA 與 Rust toolchain、`setup-dotnet` 改讀 `global.json`、
   actions 以 SHA 釘選、加 PR CI（至少 `dotnet run --project src/CrimsonAtomtic.Tests` + `ClaudeMdSizeTests`）。
3. **本機 build 正確性**（B6-03、B6-11、B6-12、B6-14、B6-15、B6-16）。
4. **Rule 9 / 10 / 7 與 tools 規則**（B6-21、B6-23 – B6-25）。
5. **文件與 cleanup**（B6-26 – B6-36）。

---

## 2. Findings

### B6-01 — `dump_challenges.py` 只把 missioninfo body 餵給 C ABI；2.06 上 0 筆輸出且 exit 0
- **嚴重度**：bug（靜默的錯誤輸出）
- **位置**：`tools/analyze/dump_challenges.py:263-264`（bind `crimson_missioninfo_load_from_bytes`）、
  `:460-462`（只 extract `layout.body(MISSIONINFO_TABLE)`）、`:471`（load）、`:522-525`（entry count）
- **證據**：
  - Rust 端 `D:\Github\crimson-rs\src\c_abi\mission_info.rs:166-169` 的 doc comment：
    「Required from 2.06, where row names moved out of the body into the header:
    `crimson_missioninfo_load_from_bytes` on a 2.06 body finds no rows.」
    body-only loader 在 `:159` 呼叫 `CrimsonMissionInfoHandle::from_bytes(slice, None)`。
  - 腳本從頭到尾沒有讀 `.staticinfoheader`，也沒有 bind `crimson_missioninfo_load_from_bytes_with_header`
    （`:175`）。`tools/common/gamedata_layout.py:53-55` 已經提供 `BinLayout.header()`，但整個 `tools/` 沒有任何呼叫端。
  - 當 `total == 0` 時迴圈不會執行，腳本印出 `matched: 0 challenge rows` 後 `return 0`（`:596`, `:668`）。
- **重現**：對 2.06 安裝執行 `python tools/analyze/dump_challenges.py --game-dir <2.06> --out x.jsonl`
  → `x.jsonl` 是空檔，exit code 0。
- **建議修法**：extract `layout.header(MISSIONINFO_TABLE)`，改 bind
  `crimson_missioninfo_load_from_bytes_with_header(data, len, header, len, out)`（舊版安裝時 header 會被忽略，
  見 Rust doc `:169`）。另外，`entry_count == 0` 時以非零碼結束，讓靜默失敗變成明確失敗。

### B6-02 — `dump_catalogs.py` 在 2.06 上輸出空的 `string_key`；`extract_keycases.py` 把 `""` 當成已解析
- **嚴重度**：bug（靜默的錯誤資料，會污染 RE 工作清單）
- **位置**：`tools/analyze/dump_catalogs.py:193-197`；`tools/analyze/extract_keycases.py:142-153`（`load_iteminfo`）、
  `:291-302`（`build_resolves`）、`:362-367`（`_is_fully_unresolved`）
- **證據**：
  - PyO3 契約 `D:\Github\crimson-rs\src\python.rs:43-46`：「Since 2.06 a row's `string_key` lives in the
    table's `.staticinfoheader`; pass that header to get the names … **Without it `string_key` is ""**.」
    `parse_iteminfo_from_bytes(data, header=None)`（`:64-75`）。
  - `dump_catalogs.py:197` 呼叫 `crimson_rs.parse_iteminfo_from_bytes(raw)`，沒有傳 header，所以每一列的
    `string_key` 都是 `""`。
  - `extract_keycases.py:151` 的條件 `isinstance(s, str)` 會接受 `""`，於是 `items_by_key[k] = ""`。
    接著 `build_resolves` 回傳 `"iteminfo_id": ""`，而 `_is_fully_unresolved`（`:364`）判斷的是 `is not None`，
    所以任何與 item key 數值相同的 Key 都被當成「已解析」。`--unresolved-only` 的工作清單因此會漏掉這些值。
- **建議修法**：在資料源頭修正（Rule 12）：`dump_iteminfo` 同時 extract `layout.header(ITEMINFO_TABLE)`，
  呼叫 `parse_iteminfo_from_bytes(raw, header)`。消費端 `load_iteminfo` 再加一道防線，把空字串視為缺值
  （`if isinstance(s, str) and s`）。docstring `dump_catalogs.py:22-23` 裡的 stringinfo 路徑也一起更新。

### B6-03 — Debug 模式建出 Rust debug DLL，但 C# 專案永遠複製 `target\release`
- **嚴重度**：bug（測試或執行的是舊的 native binary）
- **位置**：`build.ps1:126-132`（`$rustProfile = "debug"` → `build_rust.ps1 -Profile debug`）、
  `scripts/build_rust.ps1:120`（輸出到 `target\$Profile`）；
  `src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj:123`、`src/CrimsonAtomtic.Tests/CrimsonAtomtic.Tests.csproj:82`
  （寫死 `vendor\crimson-rs\target\release\crimson_rs.dll`）
- **證據**：`build.cmd debug test` → `build.ps1 -Mode Debug -Target Test` → cargo 輸出到 `target\debug\`，
  但 `dotnet run --project …Tests -c Debug` 的 `<Content Include>` 只看 `target\release\`。
  如果 release DLL 不存在，`Condition="Exists(...)"` 讓 build 照樣成功，測試則在執行期丟出 `DllNotFoundException`；
  如果 release DLL 是舊的，測試跑的就是舊 ABI。剛改過 crimson-rs 的人會以為自己的修改已經驗證過了。
- **建議修法**：二選一：(a) Rust 一律用 release profile 建置（debug 只影響 C#），刪掉 `$rustProfile` 分支；
  (b) 在 csproj 加 `<CrimsonRsProfile>`（Debug 設定對應 `debug`），`Content`/`NativeLibrary` 路徑都改用它。
  另外，DLL 不存在時應該讓 build 失敗或至少發 warning，不要讓 `Exists` 條件靜默略過。

### B6-04 — CI 的 vendor 步驟：`git checkout $CRIMSON_RS_REF` 失敗會被下一行的成功蓋掉
- **嚴重度**：bug（潛在；pin 了錯的 ref 會靜默建出 `main`）
- **位置**：`.github/workflows/release.yml:83-90`
- **證據**：
  - GitHub Actions 的 `shell: pwsh` 會在 script 前面加上 `$ErrorActionPreference = 'stop'`，在最後加上
    `if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE }`。原生指令失敗不會丟例外
    （`$PSNativeCommandUseErrorActionPreference` 預設是 `$false`），所以 step 是否失敗只看 **最後一個**
    原生指令的 exit code。
  - 第 88 行 `git -C vendor/crimson-rs checkout "$env:CRIMSON_RS_REF"` 失敗後，第 89 行的 `git rev-parse`
    會成功（HEAD 還在 clone 下來的 `main`），把 `$LASTEXITCODE` 覆寫成 0，所以 step 顯示綠燈。
  - 第 51-52 行的註解建議「Bump to a pinned tag/SHA for a reproducible release」。真的照做時，打錯一個字元
    就會建出 `main` 而不是 pin 的版本，log 只多一行 `error: pathspec …`。
- **建議修法**：每個原生指令後面都檢查 `$LASTEXITCODE`（`if ($LASTEXITCODE) { throw … }`），
  或在 step 開頭設定 `$PSNativeCommandUseErrorActionPreference = $true`。另外，把 `$sha` 寫進
  `$GITHUB_ENV`，供 B6-06 的 release notes 使用。

### B6-05 — Single-file 回歸檢查只發 warning；`release.yml` 卻說會 throw，且 dumpbin 檢查在 CI 不會執行
- **嚴重度**：bug（文件化的安全閘門實際上不存在）
- **位置**：`scripts/package_aot.ps1:149-153`（stray DLL → `Write-Warning`）、`:158-171`（dumpbin 不在 PATH 就略過）、
  `:178-182`（找不到 exe 也只是 warning）；`.github/workflows/release.yml:118-121`（「It verifies the single-file
  shape and throws on regression.」）
- **證據**：腳本裡的三個回歸條件（dist 中出現 `crimson_rs.dll`、exe 仍 import `crimson_rs.dll`、找不到
  `CrimsonAtomtic.exe`）都沒有 `throw` 或 `exit 1`，腳本最後正常結束（exit 0）。GitHub 的 windows runner 預設
  PATH 上沒有 `dumpbin.exe`（要進 VS dev shell 才有），所以 `$dumpbinImportsOk` 在 CI 永遠是 `$null`，
  import 驗證從來沒有執行。
- **重現**：讓 `DirectPInvoke` 的名稱與 `[LibraryImport("crimson_rs")]` 不一致 → exe 改為動態 import
  `crimson_rs.dll`，而 dist 裡沒有這個 DLL → CI 綠燈，draft release 上的 zip 一啟動就失敗。
- **建議修法**：三個條件都改成 `throw`（或加 `-Strict` 開關，CI 一律開啟）。import 驗證不要依賴 dumpbin：
  可以用 `vswhere` 找到 `dumpbin`，或直接讀 PE import table（PowerShell 幾十行即可），也可以在 CI 執行
  `CrimsonAtomtic.exe --version` 之類的 smoke 啟動。同時修正 `release.yml:118-121` 的註解，讓它與實作一致。

### B6-06 — Release 不可重現：crimson-rs 跟 `main`、cargo 沒有 `--locked`、SHA 沒有寫進產出物
- **嚴重度**：risk
- **位置**：`.github/workflows/release.yml:50-54`（`CRIMSON_RS_REF: main`）、`:86-90`；`scripts/build_rust.ps1:51`
- **證據**：同一個 tag 在不同時間重跑，連結進去的是不同的 crimson-rs commit。本機 `vendor/` 是 `update_vendors.ps1`
  當時抓到的 `main`（計畫書記錄為 `0a551a6`），CI 則是 tag 推上去當下的 `main`，兩者不保證一致：本機測試過的組合
  未必就是 release 出去的組合。crimson-rs 的 SHA 只出現在 CI log（`:90`），沒有寫進 release notes、zip 或 exe
  metadata。`cargo build` 沒有 `--locked`，`Cargo.lock` 與 `Cargo.toml` 不一致時 cargo 會靜默改寫 lock 檔。
  `docs/release-process.md` 已經把這點列為已知 gotcha，但流程上沒有防護。
- **建議修法**：release 前把 `CRIMSON_RS_REF` 設成本機 `vendor/crimson-rs` 的 SHA（例如在 repo 加一個
  `vendor/crimson-rs.ref` 檔，由 `update_vendors.ps1` 寫入、CI 讀取）。`build_rust.ps1` 加上 `--locked`。
  SHA 寫進 release notes footer（或 `InformationalVersion` 的 `+crimsonrs.<sha>` 後綴）。

### B6-07 — CI 的 Rust toolchain 沒有釘版本（`dtolnay/rust-toolchain@stable`）
- **嚴重度**：risk（已實際發生過）
- **位置**：`.github/workflows/release.yml:77-81`；後果見 `src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj:144-156`
  （Rust 1.100 需要 `Synchronization.lib`）、`docs/status.md:528-534`
- **證據**：註解說「pin stable-msvc so the build is not at the mercy of the runner image」，但 `@stable` 本身
  就是會移動的目標，每次新的 stable 發布都會改變 CI 的編譯器。status.md 記錄 v2.05.01 第一次 cut 就因此在 CI
  link 失敗，而本機全部通過。
- **建議修法**：在 crimson-rs 加 `rust-toolchain.toml`（`channel = "1.xxx"`），或在 workflow 用
  `dtolnay/rust-toolchain@master` + `with: toolchain: 1.xxx`。升級 Rust 改為刻意的 PR，並在本機同步。

### B6-08 — CI 的 .NET SDK（`10.0.x`）不讀 `global.json`；ILCompiler pin 會隨 runner 更新觸發 NU1109
- **嚴重度**：risk
- **位置**：`.github/workflows/release.yml:72-75`；`global.json:3-5`（`10.0.203`、`latestFeature`）；
  `Directory.Packages.props:61-69`；`docs/status.md:517-527`
- **證據**：`setup-dotnet` 安裝最新的 10.0.x SDK，它自動注入的 `Microsoft.DotNet.ILCompiler` 版本跟著該 SDK
  的 runtime 走。只要 runtime 比中央 pin `10.0.15` 新，restore 就會出現 NU1109，build 失敗（status.md 記錄已經
  bump 過 6 次）。CI 和本機用的 SDK 也不保證相同。`global.json` 的 `10.0.203` 下限遠低於 pin 註解假設的 SDK 10.0.404。
- **建議修法**：`setup-dotnet` 改用 `global-json-file: global.json`，`global.json` 釘到實際驗證過的 SDK
  （例如 `10.0.404` + `rollForward: latestPatch`）。之後升級 SDK 時，pin、`global.json`、CI 三者在同一個 PR 一起動。

### B6-09 — 供應鏈：actions 用可變 tag/branch 引用，且擁有 `contents: write`；NuGet / pip 沒有 lock
- **嚴重度**：risk
- **位置**：`.github/workflows/release.yml:47-48`（workflow 層級 `contents: write`）、`:65`（`actions/checkout@v6`）、
  `:73`（`actions/setup-dotnet@v5`）、`:81`（`dtolnay/rust-toolchain@stable`，第三方 action，引用的是 branch）、
  `:137`（`actions/upload-artifact@v7`）；`scripts/setup_python_env.ps1:81, 114`（`maturin>=1.0,<2.0`、`pytest>=8.0`、`ruff>=0.5`）
- **證據**：tag 和 branch 都可以被改寫。第三方 action 在 job 內執行時，可以使用 `GITHUB_TOKEN`（具 `contents: write`，
  能建立 release 和推 tag）。`Directory.Packages.props` 雖然釘了精確版本，但沒有啟用 `RestorePackagesWithLockFile`，
  沒有 content hash 驗證。
- **建議修法**：所有 action 改用 commit SHA 釘選（後面註解版本號），可搭配 Dependabot 的 `github-actions` 生態系
  自動更新。`permissions` 預設 `contents: read`，只在建立 release 的 job/step 提升（必要時拆成兩個 job）。
  NuGet 啟用 lock file，CI 用 `--locked-mode`。pip 用 constraints 檔釘版本。

### B6-10 — 沒有 PR CI；release workflow 也不跑測試（Pre-scan #6 確認）
- **嚴重度**：risk
- **位置**：`.github/workflows/`（只有 `release.yml`）；`release.yml:116-122`（build 後直接打包）
- **證據**：`git ls-files .github` 只有 `release.yml`。release job 沒有任何測試步驟，所以 root CLAUDE.md 的
  40 KB 上限（`ClaudeMdSizeTests`）、`ClaudeMdSizeTests` 以外的純邏輯測試，在 merge 或 release 前都沒有自動化閘門，
  只能靠有人在本機記得執行。
- **建議修法**：新增 `ci.yml`（`pull_request` + `push: dev`）：build Rust c_abi → `dotnet build` →
  `dotnet run --project src/CrimsonAtomtic.Tests`（live install / save 相關測試本來就會 skip）。release.yml 在 publish
  前也跑同一組測試。可選：`ruff check tools/`。

### B6-11 — `update_vendors.ps1` 的 dirty 檢查只看 working tree；vendor clone 內的本地 commit 會被 `reset --hard` 靜默丟掉
- **嚴重度**：risk（資料遺失，只能從 reflog 救回）
- **位置**：`vendor/update_vendors.ps1:120-128`（只用 `status --porcelain` 判斷）、`:144-146`（`reset --hard origin/$branch`）；
  `:17-20`（`-Force` 的說明）
- **證據**：在 `vendor/crimson-rs` 裡 commit 一個修正（working tree 乾淨）→ 執行 `update_vendors.ps1`（沒有 `-Force`）
  → dirty 檢查通過 → `reset --hard` 把 HEAD 移到 `origin/main`，本地 commit 沒有任何警告就消失了。CLAUDE.md Rule 8
  說這個腳本「silently wipes any local edits」，guard 只擋住了其中一半。另一方面，`-Force` 的說明寫
  「Discard local uncommitted changes」，但 `reset --hard` 不會刪除 untracked 檔案（沒有 `git clean`），所以帶
  `-Force` 跑過一次後，下次不帶 `-Force` 仍然會被同一批 untracked 檔案擋住。
- **建議修法**：reset 前檢查 `git rev-list --count origin/$branch..HEAD`（fetch 之後），大於 0 就拒絕並列出 commit，
  除非帶 `-Force`。`-Force` 的說明改寫為實際行為，或補上 `git clean -fd`（不要加 `-x`，以保留 `target/`）。

### B6-12 — Vendor 更新後不重建也不提示：`.pyd` 與 `crimson_rs.dll`/`.lib` 會停在舊版
- **嚴重度**：risk（status.md 記錄已重複發生三次）
- **位置**：`vendor/update_vendors.ps1:149-163`；`docs/status.md:535-566`
- **證據**：status.md 記錄 1.17、2.02、2.03 三次 vendor 更新後 `.pyd` 都是舊的，2.03 那次連 c_abi 的 dll/lib 也是舊的；
  並寫到「it recurs because documenting it does not run the script」。腳本結束時只印出
  `All vendor deps updated.`，沒有任何後續步驟提示。C# 端的 `<Content>` 用 `PreserveNewest` 複製舊 DLL 時也不會有
  任何警告（B6-03）。
- **建議修法**：HEAD 有變動時印出醒目的「Next: `scripts/build_rust.ps1` + `scripts/setup_python_env.ps1`」提示，
  或提供 `-Rebuild` 開關直接呼叫這兩個腳本。進一步的做法：讓 `build_rust.ps1` 把 vendor HEAD SHA 寫進
  `target/release/.crimson-rs-sha`，C# build 比對 SHA 不一致時發 warning。

### B6-13 — `gamedata_layout._read_pamt` 吞掉所有例外，錯誤訊息誤導成「遊戲沒安裝？」
- **嚴重度**：risk（掩蓋舊 `.pyd` 的問題）
- **位置**：`tools/common/gamedata_layout.py:72-75`（`except Exception: return None`）、`:91-96`（`LookupError("… is the game installed at …?")`）、
  `:119-121`（`paloc_files` 回傳 `None`）
- **證據**：舊版 `.pyd` 無法解析新 PAMT 格式時（2.04 改過 PAMT trie，見 vendor CLAUDE.md），`parse_pamt_bytes`
  會丟出 `ValueError`。這個例外被吞掉後，使用者只看到「cannot read …0.pamt — is the game installed?」，而檔案明明存在。
  `paloc_files` 回傳 `None` 時，`dump_catalogs.py:235-240` 會報「no 'eng' paloc in group」，同樣是錯誤的診斷方向。
- **建議修法**：只把「檔案不存在」轉成 `None`；解析例外要帶原始訊息往上拋（`raise LookupError(...) from e`），
  訊息中提示「若剛更新 vendor，請先跑 `setup_python_env.ps1`」。

### B6-14 — Build 腳本宣稱支援 `linux-x64` / `osx-arm64`，實際流程只支援 Windows
- **嚴重度**：risk
- **位置**：`build.ps1:56-57`、`scripts/package_aot.ps1:38-39`（`ValidateSet` 接受三個 RID）；`package_aot.ps1:62-65`
  （寫死 `crimson_rs.lib` / `.dll`）、`:155`（找 `CrimsonAtomtic.exe`）；`scripts/build_rust.ps1:121-122`；
  `src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj:154-156`（`Userenv.lib`、`Synchronization.lib` 沒有平台條件）
- **證據**：`build.ps1 -Mode Publish -Runtime linux-x64` 在 Windows 上會過參數驗證，接著 `package_aot.ps1` 嘗試
  cross-publish（NativeAOT 不支援跨 OS）。在 Linux 主機上則 `build_rust.ps1` 會找 `crimson_rs.dll` / `.lib`
  （Linux 產出的是 `libcrimson_rs.so` / `.a`），回報 missing 後 `exit 1`。即使跳過這一步，csproj 也會把 Windows 的
  `.lib` 交給 linker。專案目標寫明要支援 Linux/macOS，但錯誤要到很後面才出現，而且訊息與真正原因無關。
- **建議修法**：在 cross-platform 實作完成前，`ValidateSet` 只保留 `win-x64`（或遇到非 win RID 時一開始就 throw，
  並說明原因）。實作時依 RID 決定 artifact 名稱，csproj 的 `NativeLibrary` 加上 `$(RuntimeIdentifier.StartsWith('win'))` 條件。

### B6-15 — `build_number.txt`：每次本機 build 都 +1 並污染 working tree；CI 的 tag pin 對實際 tag 格式永遠不會觸發
- **嚴重度**：risk
- **位置**：`build.ps1:84-96`；`.github/workflows/release.yml:99-114`；`docs/release-process.md:154-155`；
  `src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj:48-55`
- **證據**：
  - `build.ps1` 在任何 build 之前就先遞增並寫檔，包括 `-Target DLL`（不產生 C# 版本）、後來失敗的 build、
    `-Target Test`。每次執行都會讓 `build_number.txt` 出現在 `git status`。
  - CI 只在 tag 是 `^v(\d+)$` 時 pin build number，但專案實際的 tag 是 `v2.02.01` 這種格式
    （release-process.md:31, 74），所以這個 pin 從來沒有生效過。release exe 的第 4 碼因此等於「最後一次有人
    把本機計數器 commit 進去的值」，可能倒退、重複，也與 tag 沒有關係。
- **建議修法**：讓 CI 一律設定 build number（例如 `-p:VerBuild=$env:GITHUB_RUN_NUMBER`，或從 tag 的 patch 推導），
  不要依賴 commit 進去的檔案。本機的遞增只在 C# 產出成功後才寫回，`-Target DLL` 不遞增。或者乾脆讓本機 build 不碰這個
  檔案，把 `build_number.txt` 定位為 CI-only。

### B6-16 — `build_rust.ps1` 不控制 `CARGO_TARGET_DIR`；`#requires -Version 7` 允許 stderr 語意不同的 7.0/7.1
- **嚴重度**：risk（低）
- **位置**：`scripts/build_rust.ps1:49-52, 120`；`:1`（`#requires -Version 7`）、`:63`（`& cargo … 2>&1`）
- **證據**：
  - 使用者環境如果有全域 `CARGO_TARGET_DIR`（常見的 sccache / 共用 target 設定），cargo 會把產物放到別處，
    但 `:120` 與 csproj 都假設在 `vendor\crimson-rs\target\release`。結果是 `exit 1`（missing），或者 C# 拿到舊 DLL。
    `setup_python_env.ps1:102-110` 有處理這個環境變數，`build_rust.ps1` 沒有。
  - PowerShell 7.0/7.1 在 `$ErrorActionPreference = "Stop"` 下，會把重導的 native stderr 當成 terminating error
    （7.2 才改為不套用）。cargo 的 `Compiling …` 寫在 stderr，所以在 7.0/7.1 上第一行輸出就會中止腳本。
- **建議修法**：`build_rust.ps1` 開頭暫存並清除 `CARGO_TARGET_DIR`，或明確設成 `vendor\crimson-rs\target`，
  `finally` 區塊再還原。所有 `#requires` 改成 `-Version 7.2`。

### B6-17 — Build / test 會帶入遙測套件，CI 沒有設定 opt-out（需確認）
- **嚴重度**：risk（低；隱私 / 供應鏈）
- **位置**：`Directory.Packages.props:10`（`Avalonia.BuildServices`）、`:51`（`Microsoft.Testing.Extensions.Telemetry`）；
  `src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj:86-89`、`src/CrimsonAtomtic.Tests/CrimsonAtomtic.Tests.csproj:19-22, 49`
- **證據**：`Avalonia.BuildServices` 是 Avalonia 的 build-time 匿名遙測套件（opt-out：`AVALONIA_TELEMETRY_OPTOUT=1`）。
  MTP 的 telemetry extension 預設啟用（opt-out：`TESTINGPLATFORM_TELEMETRY_OPTOUT=1`）。release.yml 沒有設定任何
  `*_OPTOUT` 環境變數。測試專案還把 `Microsoft.ApplicationInsights`、`Azure.Monitor.OpenTelemetry.Exporter`
  提升成直接引用（見 B6-30）。**本項依套件公開說明判斷，沒有實測網路流量**。
- **建議修法**：在 release.yml / 未來的 ci.yml 的 `env:` 設定兩個 opt-out 變數，並在 `docs/dev-workflow.md`
  註明本機如何關閉。確認 `Avalonia.BuildServices` 不是必要相依後，考慮移除它的 `PackageReference`。

### B6-18 — 從遊戲擷取的資產 `sample_bc3_32x32.dds` 被 commit 進 repo
- **嚴重度**：risk（低；政策一致性）
- **位置**：`src/CrimsonAtomtic.Tests/fixtures/sample_bc3_32x32.dds`；`src/CrimsonAtomtic.Tests/CrimsonAtomtic.Tests.csproj:86-92`
  （註解：「a real BC3-encoded DDS extracted from group 0012」）
- **證據**：crimson-rs `.gitignore` 把從安裝擷取出的內容列為「copyrighted Pearl Abyss content; keep them local」，
  真實存檔也已移到 private repo。本 repo 的 `docs/data-policy.md` 沒有針對「game-extracted test fixture」的規範，
  兩個 repo 的做法不一致。這個檔案只有 32×32，風險低，但目前缺少明確的政策依據。
- **建議修法**：二選一，並寫進 data-policy.md：(a) 改用合成的 BC3 fixture（encoder 已存在，可在測試中產生）；
  (b) 明確允許「≤ N KB、解碼器回歸必需」的擷取 fixture。

### B6-19 — `.gitignore` 的 `!tests/fixtures/**/save.save` 指向不存在的目錄，且與「存檔不進公開 repo」相衝
- **嚴重度**：risk（低）
- **位置**：`.gitignore:464-469`
- **證據**：repo 沒有 `tests/fixtures/` 目錄（fixture 放在 `src/CrimsonAtomtic.Tests/fixtures/`）。這條 negation
  的實際效果，只是讓未來任何人在 `tests/fixtures/` 放進真實玩家存檔時可以直接 `git add`。crimson-rs 在 2026-11-27
  已把所有存檔移到 private repo，理由是「Real save files are player data」。
- **建議修法**：刪掉兩條 negation，改成全域 `*.save`（與 crimson-rs 一致）。

### B6-20 — Rule 9：`dump_challenges.py` 在 import 時重設 stdout、以寫死的絕對路徑載入 DLL
- **嚴重度**：rule violation（CLAUDE.md Rule 9：no-args 印 usage 並 exit 2；import 不可有副作用）
- **位置**：`tools/analyze/dump_challenges.py:226-228`（`sys.stdout.reconfigure`）、`:239`
  （`DLL_PATH = r"D:\Github\CrimsonAtomtic\vendor\…\crimson_rs.dll"`）、`:253`（`_dll = ctypes.CDLL(DLL_PATH)`）、`:263-277`（module 層級 bind）
- **證據**：這些都在 `main()` / `require_args()` 之前執行。repo clone 在其他路徑、DLL 還沒 build、或 DLL 被
  PyO3 build 覆蓋（缺 c_abi symbol）時，無參數執行會丟出 `OSError` / `AttributeError` traceback（exit 1），
  不會印 usage 並 exit 2。被其他模組 import 時也會改掉呼叫端的 stdout。
- **建議修法**：把 reconfigure、DLL 載入、bind 都移進 `main()`（或 lazy 的 `_load_dll()`）。DLL 路徑改用
  `--dll` 參數，預設由 `Path(__file__).resolve().parents[2] / "vendor/crimson-rs/target/release/crimson_rs.dll"` 推導。
  順便修 B6-01。

### B6-21 — Rule 9：`tools/inspect/sa-investigations/` 的 8 支腳本都沒有 usage，無參數就直接執行
- **嚴重度**：rule violation（Rule 9）
- **位置**：`all_sa_far.py:73-157`、`feast_diff.py:137-200`、`feast_vs_hooves.py:74-108`、`missioninfo_ctypes.py:21-112`、
  `missioninfo_key_to_name.py:30-76`、`missioninfo_strings.py:20-52`、`sa_item_mission_map.py:22-92`、`search_x2.py:19-55`
  （每支都 `if __name__ == "__main__": raise SystemExit(main())`，沒有 argparse / `require_args`）
- **證據**：無參數執行時會直接讀 live install（寫死 `D:\SteamLibrary\…`），或讀相對於 cwd 的 `out/sa-feast-diff/*.bin`，
  有些還會寫檔（`all_sa_far.py:154-155`、`feast_diff.py:138, 191-199`）。每支都在 module 層級 `import crimson_rs`。
  folder README（`sa-investigations/README.md:90-96`）承認這些腳本「frozen」，而且在 2.01+ 上會失敗。
  tools/CLAUDE.md「Removing a tool」要求不用的腳本直接刪除。Root CLAUDE.md Rule 9 沒有「frozen probe」的例外。
- **建議修法**：二選一：(a) 刪除（研究結論已經寫在 README 與 status.md，腳本可以從 git history 取回）；
  (b) 每支加最小 argparse（`--game-dir` / `--body` 必填）+ `require_args`，`import crimson_rs` 移進 `main()`。
  建議選 (a)，因為這些腳本無法對現行遊戲版本執行。

### B6-22 — Rule 9（跨平台）：`extract_save.py` 建 parser 時就呼叫 `save_root()`，非 Windows 上 `--help` 也會 crash
- **嚴重度**：rule violation（Rule 9；跨平台目標）
- **位置**：`tools/extract/extract_save.py:58-62`（help 字串中的 `f"{save_root()}"`）；`tools/common/paths.py:21-26`
  （`LOCALAPPDATA` 不存在時 raise）
- **證據**：`build_parser()` 在 `require_args()` 之前執行。Linux/macOS 沒有 `LOCALAPPDATA`，所以無參數或 `--help`
  都會得到 `RuntimeError` traceback，不是 usage + exit 2。
- **建議修法**：help 字串改成靜態的 `%LOCALAPPDATA%\Pearl Abyss\CD\save`，只在真正需要 autodiscover 時才呼叫 `save_root()`。

### B6-23 — Rule 10：同一張圖的衍生格式與複本被 commit
- **嚴重度**：rule violation（Rule 10；需確認哪個是原始檔）
- **位置**：`img/Logo.png` / `img/Logo.jpg` / `img/Logo.webp`、`img/MainIcon.png` / `img/MainIcon.ico`；
  `src/CrimsonAtomtic.Ui/Assets/Logo.jpg`、`src/CrimsonAtomtic.Ui/Assets/MainIcon.ico`
- **證據**：`git ls-files -s` 顯示 `img/Logo.jpg` 與 `src/…/Assets/Logo.jpg` 是同一個 blob（`8e721ec2…`），
  `img/MainIcon.ico` 與 `src/…/Assets/MainIcon.ico` 也是同一個 blob（`e044555a…`）。`.jpg` / `.webp` / `.ico`
  看起來是從 `.png` 轉出來的，沒有任何文件記錄生成方式。這正是 data-policy §3 描述的「A 與 B 都 commit、之後各自漂移」。
- **建議修法**：確認原始檔（應該是 `.png`）。README 用的圖與 app 資源只保留一份：Ui csproj 可以用
  `<AvaloniaResource Include="..\..\img\Logo.jpg" Link="Assets\Logo.jpg" />` 引用 `img/`，不要另存複本。
  衍生格式如果確實需要，寫成生成腳本並記錄在 `img/README.md`，或在 data-policy 中明確豁免「人工產出的品牌素材」。

### B6-24 — Rule 7：路徑與常數散落各處，沒有集中定義
- **嚴重度**：rule violation（Rule 7；Pre-scan #4 在 Block 6 範圍內確認）
- **位置**：
  - `vendor\crimson-rs\target\release\crimson_rs.{dll,lib}`：`Ui.csproj:123, 154`、`Tests.csproj:82`、
    `package_aot.ps1:62-63`、`build_rust.ps1:120-122`、`dump_challenges.py:239`、`missioninfo_ctypes.py:17`。
  - 機器專屬的磁碟路徑：`tools/common/paths.py:14-18`（`D:\…`、`F:\…`、`X:\…`）、5 支 sa 腳本的 `GAME_ROOT`。
  - C ABI status code：`dump_challenges.py:245-248`（數值與 `crimson-rs/src/c_abi/mod.rs:98-128` 相符，但屬於複製的常數）。
- **證據**：上述位置的同一個常數各自寫死，改一處不會連動其他處。B6-03、B6-16 的問題都是因為這個 DLL 路徑有多份定義。
- **建議修法**：MSBuild 端在 `Directory.Build.props` 定義 `$(CrimsonRsTargetDir)`，兩個 csproj 共用。PowerShell
  端抽出 `scripts/_paths.ps1`（dot-source 使用）。Python 端在 `tools/common/` 新增 `crimson_abi.py`，集中 DLL 路徑解析與
  status code（有 docstring 說明來源 `c_abi/mod.rs`）。`DEFAULT_GAME_ROOTS` 改為讀 `CRIMSON_GAME_ROOT` 環境變數
  （與 crimson-rs 測試相同的變數名），寫死的清單只當 fallback。

### B6-25 — tools 規則 6：在 Python 裡做 byte 層級解析，並繞過 PyO3 直接用 ctypes 呼叫 C ABI
- **嚴重度**：rule violation（`tools/CLAUDE.md` 規則 6「crimson_rs is the only allowed binary-format dependency … do not re-implement byte-level parsing in Python」）
- **位置**：`tools/inspect/sa-investigations/missioninfo_key_to_name.py:38-61`、`search_x2.py:32-55`、
  `missioninfo_strings.py:26-27`（byte-scan / length-prefix heuristic）；`dump_challenges.py:251-312`、
  `missioninfo_ctypes.py:22-45`（ctypes 手寫 ABI 簽章）
- **證據**：規則要求這類程式碼使用 `_proto_` 前綴並在 crimson-rs 支援後刪除，sa 腳本卻被「升格」成固定名稱保留下來。
  `dump_challenges.py` 手寫的 ctypes 簽章在 2.06 新增 `_with_header` 變體時沒有跟著更新（B6-01），這正是規則想避免的漂移。
- **建議修法**：向 crimson-rs 提出 PyO3 binding（`parse_missioninfo_from_bytes(data, header=None)` + display-name lookup），
  `dump_challenges.py` 改用 PyO3。sa 腳本依 B6-21 處理。

### B6-26 — `docs/architecture.md` 與現況大幅脫節（Pre-scan #2 確認並補充）
- **嚴重度**：cleanup（文件；但會誤導新 session 的分層判斷）
- **位置與證據**：
  - `:53`：「`crate-type = ["cdylib"]`」→ 實際是 `["cdylib", "staticlib"]`（`build_rust.ps1:18-21`）。
  - `:56`：「C ABI (new, to add)」；`:67`：「`DllImport` wrappers」；`:69`：「`docs/c-abi.md` (to be created)」→
    C ABI 已上線多時，使用的是 `[LibraryImport]`，文件位於 crimson-rs `docs/c-abi-surface.md`。
  - `:16-18` 圖示：「DllImport (P/Invoke)」、「crimson_rs.dll (C ABI, cdylib)」→ release 是 staticlib 靜態連結，沒有 DLL。
  - `:74`、`:100-102`：SaveModel「Owns the in-memory edit model + undo/redo」→ `SaveModel` 只有 `SaveSummary.cs`、
    `ScalarFieldEditing.cs` 兩個檔案，沒有 edit model，也沒有 undo/redo。
  - `:79`：「ViewModels know nothing about FFI」→ **12** 個 ViewModel 有 `using CrimsonAtomtic.RustInterop`
    （Pre-scan 寫 13；實數 12：AbyssGates、CharacterRefsBrowser、DyeEditor、DyePalettePicker、DyeSlotEditor、FieldRow、
    FindItems、MainWindow、RenameMercenary、SocketEditor、VendorBuyback、WorldMap）。
  - `:97-99`：Core 含「settings persistence, save backup retention」→ 位於 `Ui/Services/AppSettings.cs`、`SaveBackupService.cs`；
    Core 只有 `IPlatformPaths.cs`、`ISingleInstanceGuard.cs`。
  - `:125`：「190/190 pass」→ status.md:62 記錄 452。
  - `:136-137`：「DataGrid 12.1.2 vs 12.1.1」→ core 已是 12.2.0（`Directory.Packages.props:8`）。
- **建議修法**：重寫「Layers」與「Current scaffolding」兩節，寫出實際分層（ViewModel 直接依賴 `ISaveLoader` 介面
  是現行的設計決策；如果仍希望 VM 與 FFI 分離，就列為 roadmap），並刪掉測試數這類會過期的數字，改成指向 status.md。

### B6-27 — `README.md` / `scripts/README.md` / root `CLAUDE.md` 的建置說明與實際不符
- **嚴重度**：cleanup（文件；其中第 2 點會讓人跳過 `.pyd` 重建，加重 B6-12）
- **位置與證據**：
  - `README.md:177`：「clones crimson-rs from D:\Github\crimson-rs」→ 實際從 GitHub URL clone（`update_vendors.ps1:47`）。
  - `README.md:180`：「produces crimson_rs.dll with both C ABI + PyO3」；`README.md:161`：「C ABI + PyO3 from one cdylib」→
    錯誤。兩者是不同 feature 的 build，而且刻意放在不同的 target dir（`setup_python_env.ps1:85-100`）。
  - `scripts/README.md:25-29`：「Build Rust core (also produces the .pyd …)」、「Set up Python env (depends on step 2)」→ 同樣錯誤。
  - `scripts/README.md:8-10`：`build_ui.ps1`「stub until src/ exists」、`package_aot.ps1`「stub」→ 已經是完整實作。
    root 的 `build.ps1` / `build.cmd` 沒有在任何 README 裡出現。
  - `README.md:194`、`tools/README.md:70`、`tools/CLAUDE.md:109`：示範執行 `tools\extract\extract_iteminfo.py`，但這支腳本不存在。
  - `CLAUDE.md:44`、`README.md:163`：「Tests: … pytest (Python)」→ repo 中沒有任何 pytest 測試（見 B6-35）。
- **建議修法**：以 `build.ps1` 作為唯一入口改寫 First-time setup。刪除「一次 build 兩用」的說法。示範指令改用實際存在的
  `extract_save.py`。`pytest` 改成「(planned)」，或補上最小的測試（B6-35）。

### B6-28 — `tools/` 各 README 的清單與實際檔案不一致
- **嚴重度**：cleanup
- **位置與證據**：
  - `tools/README.md:34`：`extract_save.py` 標示為 TODO → 已存在。`tools/extract/README.md:20`：「No scripts live here yet」→
    有 `extract_save.py`；`:10` 寫輸出是 `<out>/save.json` → 實際輸出 `<slot>.json` + `<slot>.bin`（`extract_save.py:111-113`）。
    `:16-17` 宣稱「Tag output with the source-file SHA」→ 只有 body SHA，沒有 source SHA。
  - `dump_challenges.py` 沒有出現在 `tools/README.md` 與 `tools/analyze/README.md`（違反 tools/CLAUDE.md「Adding a new tool」第 3 步）。
  - `tools/analyze/README.md:14-15`：「Output is always written as a report file — not just printed to stdout」→
    `dump_save_fields.py:83-84`、`dump_challenges.py:418-419` 預設輸出到 stdout。
  - `tools/inspect/README.md:10`：`inspect_save_body.py`「dump per-block summaries + per-field details」→ 實際只輸出 schema + TOC。
  - `tools/CLAUDE.md:81`：fixture 放在 `tests/fixtures/` → 目錄不存在。
- **建議修法**：依實際檔案更新清單，補上 `dump_challenges.py` 一列，修正輸出描述。

### B6-29 — 其他文件中的過時事實（`target-py`、DataGrid、game 1.10）
- **嚴重度**：cleanup
- **位置與證據**：
  - `docs/status.md:552`、`:574`：「`vendor/crimson-rs/target-py/` now exists」、「scoping `CARGO_TARGET_DIR` to
    `…/target-py`」→ 現行是 `target/py`（`setup_python_env.ps1:94-100` 說明了為什麼從 `target-py` 改掉）。
    本計畫書的 Audit focus 也沿用了舊名稱。
  - `docs/status.md:585-588`：「DataGrid is at 12.1.2 and now *leads* core (12.1.1)」→ core 已是 12.2.0，現在是 DataGrid 落後。
  - `docs/data-policy.md:57`：「The current game install is **1.10**」→ 2.06。
  - `tools/common/gamedata_layout.py:13-14`：「The file *contents* did not change, so every parser is untouched」→
    2.06 的 header 變更讓這個前提不再成立（B6-01 / B6-02 的根源）。
- **建議修法**：逐條更新。`gamedata_layout.py` 的 docstring 補一段 2.06（`.staticinfoheader` 現在是必需的），
  並說明 `BinLayout.header()` 的用途。

### B6-30 — `Directory.Packages.props` 註解錯位；測試專案把一長串 transitive 套件提升成直接引用
- **嚴重度**：cleanup
- **位置**：`Directory.Packages.props:23-27`（「MVVM source generators」註解在 `Azure.Core` 上方，「Testing」註解在
  `HarfBuzzSharp` 上方）；`src/CrimsonAtomtic.Tests/CrimsonAtomtic.Tests.csproj:18-59`
- **證據**：
  - 套件按字母排序後，原本的分組註解失去對應關係。
  - `CentralPackageTransitivePinningEnabled=true`（`Directory.Packages.props:4`）已經讓 `PackageVersion` 能夠 pin
    transitive 套件，不需要再加 `PackageReference`。測試專案卻直接引用 `Azure.Core`、`Azure.Monitor.OpenTelemetry.Exporter`、
    `Microsoft.ApplicationInsights`、`Microsoft.Identity.Client.Extensions.Msal`、`OpenTelemetry.PersistentStorage.*`，
    甚至 `runtime.win-x64.Microsoft.DotNet.ILCompiler`（`:55`，一個大型的 native toolchain 套件）。這讓 `NuGetAuditMode=direct`
    （`Directory.Build.props:23`）的稽核範圍變大，相依圖也難以閱讀。
  - xUnit v3 + MTP 直接執行的情況下，`xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk`（VSTest 相關）只有在 VS
    Test Explorer 才用得到。這兩個套件可以保留，但應該註明保留原因。
- **建議修法**：註解移到正確位置，或改成每個套件旁的行內註解。測試專案只保留真正直接使用的套件，transitive 的版本
  交給 `PackageVersion` pin（先確認 `git log` 中加入這些引用的原因，例如當時是否為了解決某個 advisory）。

### B6-31 — `<NoWarn>IL2104;IL3053</NoWarn>` 的理由已過時，是否仍需要沒有驗證
- **嚴重度**：cleanup
- **位置**：`src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj:57-65`
- **證據**：註解寫的是「Avalonia.Controls.DataGrid 12.0.0 … ilcompiler 10.0.7+」，現在是 DataGrid 12.1.2、ILCompiler 10.0.15。
  註解要求「Track removal when Avalonia ships a trim-safe DataGrid」，但沒有任何追蹤機制。因為整個專案都
  `TreatWarningsAsErrors`，這兩個代碼被抑制後，**任何** assembly 新產生的 IL2104/IL3053 都會被一起隱藏，不只 DataGrid。
- **建議修法**：暫時移除 NoWarn 跑一次 AOT publish，記錄實際觸發的 assembly。如果仍然需要，改用
  `<TrimmerSingleWarn>`（以 assembly 為單位設定），或用 `<WarningsNotAsErrors>` 保留警告、只取消 error 化，並更新註解。

### B6-32 — `ClaudeMdSizeTests` 先走完整棵樹才過濾，且沒有跳過 `.venv`
- **嚴重度**：cleanup（效率 / 穩定性）
- **位置**：`src/CrimsonAtomtic.Tests/ClaudeMdSizeTests.cs:17, 23-27`
- **證據**：`Directory.EnumerateFiles(root, "CLAUDE.md", SearchOption.AllDirectories)` 會遞迴進
  `vendor/crimson-rs/target`（數萬個 cargo 產物）、`.git`、`bin/obj`，之後才用 `SkippedDirs` 過濾，所以跳過清單省不了
  任何 I/O。`.venv`、`node_modules` 不在跳過清單中。遇到無權限的目錄時，`EnumerateFiles` 會丟
  `UnauthorizedAccessException`，讓測試失敗，而失敗原因與 CLAUDE.md 的大小無關。`"data"` 這種寬鬆的目錄名稱，
  會跳過任何層級裡名為 `data` 的資料夾。
- **建議修法**：改成手動遞迴，進入目錄前就依名稱剪枝；或使用 `new EnumerationOptions { RecurseSubdirectories = true,
  IgnoreInaccessible = true }` 加上 `FileSystemEnumerable` 的 `ShouldRecursePredicate`。跳過清單補上 `.venv`、
  `node_modules`、`target`。`data` 改成只跳過 repo 根目錄下的 `data/`。

### B6-33 — Build 腳本的小問題
- **嚴重度**：cleanup
- **位置與證據**：
  - `build.cmd:72-80`：找不到 pwsh 時 fallback 到 Windows PowerShell 5.1，但 `build.ps1:1` 的 `#requires -Version 7`
    一定會拒絕執行，這個 fallback 只會換來另一個錯誤。另外 `build.cmd` 沒有辦法傳 `-Runtime`。
  - `build.ps1:64`：`$VendorRs` 宣告了但沒有使用。
  - `build.ps1:117`：Clean 用 `-ErrorAction SilentlyContinue`。bin/obj 被鎖住時刪除會靜默失敗，舊的產物會留在原地，
    使用者卻以為已經是 clean build。
  - `build.ps1:79`：`Invoke-Step` 只看 `$LASTEXITCODE`。step 內部只有 PowerShell cmdlet 時，判斷依據會是上一個原生指令殘留的值。
- **建議修法**：`build.cmd` 找不到 pwsh 就直接報錯並附上安裝連結，另外加 `runtime=<rid>` 參數。刪除未使用的變數。
  Clean 失敗時改成 warning 並列出路徑。`Invoke-Step` 在執行 block 前先把 `$global:LASTEXITCODE` 設為 0。

### B6-34 — `.gitignore` 的矛盾與冗餘規則
- **嚴重度**：cleanup
- **位置**：`.gitignore:411-412` 與 `:477`（`!.vscode/settings.json` 之後又 `.vscode/settings.json`，以後者為準）；
  `:451` 與 `:457-462`（`data/*/` 已經涵蓋 `data/_mined/`、`data/_raw/`、`data/_saves/`）；`:443-444` 與 `:354-355`
  （`__pycache__`、`*.pyc` 重複）；`:288`（`Backup*/` 會 ignore 任何以 Backup 開頭的目錄，未來如果有 `Services/Backup/` 會被靜默排除）
- **建議修法**：刪除重複的規則。`.vscode` 的策略二選一。`Backup*/` 改成只作用於根目錄（`/Backup*/`），或直接刪掉這條（VS 轉檔備份已經不適用）。

### B6-35 — `tools/` 衛生問題：沒有測試、錯誤路徑 exit 0、handle 洩漏、`sys.path` 指錯位置
- **嚴重度**：cleanup
- **位置與證據**：
  - `tools/pyproject.toml:25-26`：`testpaths = ["tests"]`，但 `tools/tests/` 不存在，整個 `tools/` 沒有任何 pytest 測試。
    `split_paloc_string_key`、`_split_typed_value`、`parse_ign_challenges_text`、`gamedata_layout.paloc_files` 都是容易測試的純函式。
  - `tools/inspect/inspect_save_section.py:129-132`：`--toc-index` 超出範圍時印錯誤訊息後回傳 `[]`，`main()` 接著印
    「no blocks matched」然後 `return 0`。範圍訊息 `(0..{len})` 也包含了 len 本身。
  - `tools/analyze/dump_challenges.py:495-497, 507-509`：錯誤時提早 `return 1`，沒有釋放 `h_mi` / `h_pls`。`:523` 沒有檢查 `mi_count` 的回傳碼。
  - `all_sa_far.py:14`、`feast_diff.py:22`、`feast_vs_hooves.py:12` 等 sa 腳本的 `sys.path.insert(0, parents[1])`
    指向 `tools/inspect`，不是 `tools/`（目前沒有 import `common`，所以還不會出錯）。各檔 docstring 開頭仍寫著舊名稱 `_proto_*.py`。
  - `scripts/setup_python_env.ps1:47-56`：fallback 到 `python3.12` / `python` 之後沒有檢查版本，`python` 可能是 3.11，
    要到 maturin 的 abi3-py312 才會失敗，錯誤訊息很難理解。
- **建議修法**：新增 `tools/tests/`，為上述純函式寫測試，並在 B6-10 的 CI 執行 `pytest tools/tests` + `ruff check tools/`。
  錯誤路徑回傳非零碼。用 `try/finally` 釋放 handle。依 B6-21 處理 sa 腳本。`setup_python_env.ps1` 建立 venv 後執行
  `python -c "import sys; assert sys.version_info >= (3, 12)"`。

### B6-36 — `release.yml` 的過時註解與 `workflow_dispatch` 的邊界情況
- **嚴重度**：cleanup
- **位置**：`.github/workflows/release.yml:18-20`（「the default tracks `main` (which carries the bool round-trip fix)」，這個理由早已過時）、
  `:28-35`（範例 `git tag -a v9` / `vNNN`，與實際的 `v2.02.01` 格式不同）、`:45` 對照 `:100, :146`
- **證據**：`workflow_dispatch` 的註解寫「build + artifact only (no release)」，但如果在 GitHub UI 對一個 tag ref 手動觸發，
  `github.ref` 是 `refs/tags/…`，兩個 `startsWith(github.ref, 'refs/tags/')` 條件都會成立，結果仍然會建立 draft release
  並改寫 build number。
- **建議修法**：更新註解與範例（與 release-process.md 一致）。release 相關的兩個 step 的條件改成
  `github.event_name == 'push' && startsWith(github.ref, 'refs/tags/')`。

---

## 3. Cross-cutting checklist（Block 6 範圍）

| 項目 | 結果 |
|---|---|
| Rule 3 — async I/O，UI thread 不可 `.Result` / `.Wait()` | N/A。Block 6 唯一的 C# 是 `ClaudeMdSizeTests`（測試中使用同步 I/O，不在 UI thread，可接受）。 |
| Rule 4 — OS 呼叫只能經過 `Core` 介面 | N/A（沒有產品程式碼）。PowerShell / Python 本來就是 Windows-first 的工具，跨平台缺口見 B6-14、B6-22。 |
| Rule 5 — 不可使用 reflection（AOT） | ✓ `ClaudeMdSizeTests` 沒有使用 reflection。另見 B6-31：NoWarn 可能掩蓋 trim / AOT 警告。 |
| Rule 7 — magic strings 集中於一個檔案 | ✗ B6-24。 |
| Rule 12 — 不做消費端 workaround | B6-02 的修法要求在 **產生端**（`dump_catalogs.py` 傳 header）修正。`extract_keycases.py` 的 `""` 防線只是附帶的保險，不能取代產生端的修正。 |
| 例外：不可有吞掉寫入失敗的空 `catch` | `update_vendors.ps1:152-155` 的 catch 有記錄並計入失敗 ✓。需要注意的有：`gamedata_layout.py:74-75` 吞掉所有例外（B6-13），`build.ps1:117` 以 SilentlyContinue 刪除（B6-33）。 |
| UI 字串三語（en / ja / zh-TW） | N/A。Block 6 沒有任何 UI 字串（三語同步屬 Block 3）。 |

## 4. Pre-scan observations 驗證結果

| # | 觀察 | 結果 |
|---|---|---|
| 2 | Architecture doc drift | **確認，並修正一個數字**。`Core` 不含 settings / backup ✓；「190/190」對比 status.md 的 452 ✓；SaveModel 只有 2 個檔案、沒有 edit model / undo-redo ✓；「ViewModels know nothing about FFI」與事實不符 ✓，但 `using CrimsonAtomtic.RustInterop` 的 ViewModel 實際是 **12** 個，不是 13（清單見 B6-26）。另外還發現 crate-type、DllImport、`c-abi.md`、DataGrid 版本等多處過時內容（B6-26）。 |
| 4 | Rule 7：沒有集中的 magic-string class | **在 Block 6 範圍內確認**：scripts / csproj / tools 都沒有集中的路徑或常數定義（B6-24）。C# 專案部分由各 block 自行查證。 |
| 6 | CI 只有 release | **確認**：`.github/workflows/` 只有 `release.yml`（觸發條件：`v*` tag push + `workflow_dispatch`），而且 release job 本身也不跑測試（B6-10）。 |
| 1, 3, 5 | Rule 6 logs / `UiLanguageService` P/Invoke / 檔案大小熱點 | 不屬於 Block 6，未審查。 |

**Audit focus 逐項結論：**

- **Release 可重現性**：不可重現（B6-06、B6-07、B6-08、B6-15）。
- **`package_aot.ps1` linker 偵測**：`:112-115` 的 MSVC link.exe 判斷邏輯正確（Git 的 GNU `link.exe` 路徑不符合 regex，
  會交給 ILC 自行尋找）。真正的問題在後面的回歸驗證只發 warning（B6-05）。**`NoWarn IL2104;IL3053` 是否仍需要：未驗證**（B6-31）。
- **兩個 cargo target dir**：現行是 `target`（c_abi，由 `build_rust.ps1` 產生）與 `target/py`（PyO3，由 `setup_python_env.ps1:100`
  設定），彼此不會互相覆蓋 ✓，而且 `target/py` 落在 crate 的 `/target` ignore 規則下，不會觸發 `update_vendors.ps1` 的 dirty 檢查 ✓。
  vendor 更新後 `.pyd` 是否為最新：**沒有任何保證**（B6-12）。
- **`update_vendors.ps1` 的 `reset --hard` 防護**：防止 `git -C` 跑到上層 repo 的 guard 是正確的（`:109-118`）。
  未 commit 的修改有擋住 ✓，**本地 commit 沒有擋住** ✗（B6-11）。
- **Python Rule 9**：`extract_save.py`（Windows 上）、`inspect_save_body.py`、`inspect_save_section.py`、`dump_catalogs.py`、
  `dump_save_fields.py`、`extract_keycases.py` 都符合 ✓。不符合的有：`dump_challenges.py`（B6-20）、8 支 sa 腳本（B6-21）、
  非 Windows 上的 `extract_save.py`（B6-22）。`common/*.py` 是 library 模組，沒有 `main` 是合理的，建議在 Rule 9 的文字中明確
  排除 `common/`。所有 CLI 腳本在 module 層級做 `sys.path.insert` 是 tools/CLAUDE.md 記錄的 bootstrap 慣例，本報告不將其視為違規。
- **供應鏈**：actions 沒有用 SHA 釘選、第三方 action 引用 branch（B6-09）。NuGet 以 CPM 精確釘版（✓），但沒有 lock file（B6-09）。
