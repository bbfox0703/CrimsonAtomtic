using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Core;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Platform;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// View-model for the main window. Holds the optional loaded save plus
/// the file-open / save / edit commands. AOT-safe: every observable
/// comes from a CommunityToolkit.Mvvm source generator, no reflection.
/// </summary>
public sealed partial class MainWindowViewModel(
    ISaveLoader loader,
    IPlatformPaths paths,
    LocalizationProvider localization,
    SaveBackupService backupService,
    UiLanguageService uiLanguage) : ObservableObject
{
    /// <summary>
    /// Backup service exposed for the Restore dialog (read-only — it
    /// enumerates the on-disk backup tree). The VM owns the write side
    /// via <see cref="BackupBeforeWriteSilent"/>.
    /// </summary>
    public SaveBackupService BackupService => backupService;

    /// <summary>
    /// Localization service exposed for child view-models (e.g. the
    /// browse-localization dialog opened from the Tools menu).
    /// </summary>
    public LocalizationProvider Localization => localization;

    /// <summary>
    /// Append-only journal of "what the user changed" since the last
    /// Save / Load. Surfaced via Tools → Review changes + the
    /// close-on-dirty confirm dialog so the user knows what they're
    /// about to commit / discard.
    /// </summary>
    public ChangeJournal Journal { get; } = new ChangeJournal();

    /// <summary>
    /// Expose the active save loader for child dialogs that mutate the
    /// same handle (e.g. Rename Mercenary). The handle lives on this VM
    /// for the lifetime of the app, so handing out the reference is
    /// safe — children don't take ownership.
    /// </summary>
    internal ISaveLoader GetSaveLoader() => loader;

    /// <summary>
    /// Expose the platform-paths singleton for child dialogs that need
    /// to read / write <c>AppSettings</c> (e.g. the custom-gem-sets
    /// editor). Same instance the VM uses internally.
    /// </summary>
    internal IPlatformPaths GetPlatformPaths() => paths;

    /// <summary>
    /// Load the user's persisted custom gem sets from settings.json.
    /// Returns an empty list when the field is absent or the settings
    /// file doesn't exist yet.
    /// </summary>
    internal IReadOnlyList<CustomGemSet> LoadCustomGemSets()
    {
        var settings = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        return settings.CustomGemSets ?? Array.Empty<CustomGemSet>();
    }

    /// <summary>
    /// Flip <see cref="IsDirty"/> + refresh window-title chrome when a
    /// child dialog (e.g. Rename Mercenary) has mutated the save body.
    /// Mirrors the post-edit notifications the main edit panel emits
    /// directly when the user applies a field change.
    /// </summary>
    /// <remarks>
    /// Child dialogs that own their own logging (Sockets, Rename
    /// Mercenary, etc.) call <see cref="Journal"/>.Log directly with
    /// a per-operation description, then call this method to flip
    /// the title-bar * + Save command. Callers that don't have a
    /// per-operation description can pass <paramref name="logCategory"/>
    /// + <paramref name="logSummary"/> to log a generic line at the
    /// same time.
    /// </remarks>
    internal void MarkDirtyFromExternalEdit(string? logCategory = null, string? logSummary = null)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(WindowTitle));
        if (!string.IsNullOrEmpty(logCategory) && !string.IsNullOrEmpty(logSummary))
        {
            Journal.Log(logCategory, logSummary);
        }
    }

    /// <summary>
    /// Drives the Find Items dialog's per-row "Go" button: navigates
    /// the main window into the exact item slot identified by
    /// <paramref name="rec"/>, rebuilding the nav stack down through
    /// <c>InventorySaveData → _inventorylist[N] → _itemList[M] → item</c>
    /// so the user lands on the item-detail view with a clean Back
    /// trail (item → _itemList picker → container → _inventorylist
    /// picker → InventorySaveData → no-back).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Race control: the default <see cref="OnSelectedBlockChanged"/>
    /// fire-and-forget worker would push its own root frame and could
    /// land after our deeper push, wiping the target. The
    /// <see cref="_suppressBlockSelectionLoad"/> flag tells that
    /// handler to skip the work this one time; we then load the top
    /// block ourselves (awaited) and build the full stack
    /// synchronously.
    /// </para>
    /// <para>
    /// Best-effort silent on failure (block not found, schema drift
    /// breaks the field-name match, FFI throws): the navigator
    /// reports through <see cref="DetailsError"/> and bails — the
    /// dialog stays open so the user can pick another row.
    /// </para>
    /// </remarks>
    public async Task NavigateToInventoryItemAsync(InventoryItemRecord rec)
    {
        if (_loadedPath is null)
        {
            return;
        }
        // Locate the BlockSummary so we can highlight the row in the
        // BlocksDataGrid. Skip if the index drifted out from under us
        // (mutation between snapshot and click).
        BlockSummary? blockSummary = null;
        foreach (var b in _allBlocks)
        {
            if (b.Index == (int)rec.BlockIndex)
            {
                blockSummary = b;
                break;
            }
        }
        if (blockSummary is null)
        {
            DetailsError = $"Block #{rec.BlockIndex} not found in current save.";
            return;
        }

        BlockDetails topDetails;
        try
        {
            topDetails = await Task.Run(() =>
                loader.LoadBlockDetails(_loadedPath, (int)rec.BlockIndex)).ConfigureAwait(true);
        }
        catch (CrimsonSaveException ex)
        {
            DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
            return;
        }

        // Highlight the row without re-firing the default loader (we
        // already have the details and are about to push a deeper
        // stack than the default handler would).
        _suppressBlockSelectionLoad = true;
        try { SelectedBlock = blockSummary; }
        finally { _suppressBlockSelectionLoad = false; }

        // Build the stack from scratch: BlockFrame(top) → ElementsFrame(_inventorylist)
        // → BlockFrame(container) → ElementsFrame(_itemList) → BlockFrame(item).
        ClearNavigation();
        PushFrame(new BlockFrame(topDetails.ClassName, topDetails, Array.Empty<PathStep>()));

        var invListField = FindFieldByName(topDetails, "_inventorylist");
        if (invListField?.Elements is not { Count: > 0 } containers
            || rec.InventoryElementIndex >= (uint)containers.Count)
        {
            return; // Top frame only — user can drill manually from here.
        }
        PushFrame(new ElementsFrame(
            $"{invListField.Name}[{containers.Count}]",
            containers,
            Array.Empty<PathStep>(),
            (uint)invListField.FieldIndex));

        var container = containers[(int)rec.InventoryElementIndex];
        var path1 = new[] { new PathStep((uint)invListField.FieldIndex, rec.InventoryElementIndex) };
        PushFrame(new BlockFrame(
            $"{container.ClassName}[{rec.InventoryElementIndex}]", container, path1));

        var itemListField = FindFieldByName(container, "_itemList");
        if (itemListField?.Elements is not { Count: > 0 } items
            || rec.ItemElementIndex >= (uint)items.Count)
        {
            return; // Container frame is the deepest — leave the user there.
        }
        PushFrame(new ElementsFrame(
            $"{itemListField.Name}[{items.Count}]",
            items,
            path1,
            (uint)itemListField.FieldIndex));

        var item = items[(int)rec.ItemElementIndex];
        var path2 = new[]
        {
            new PathStep((uint)invListField.FieldIndex, rec.InventoryElementIndex),
            new PathStep((uint)itemListField.FieldIndex, rec.ItemElementIndex),
        };
        PushFrame(new BlockFrame(
            $"{item.ClassName}[{rec.ItemElementIndex}]", item, path2));
    }

    /// <summary>
    /// Drives the Vendor Buyback dialog's per-row "Jump" button:
    /// builds the nav stack down through
    /// <c>StoreSaveData → _storeDataList[storeIdx] → _storeSoldItemDataList[itemIdx]</c>
    /// so the user lands on the buyback item's <c>ItemSaveData</c>
    /// detail view. The generic block editor there handles stack /
    /// endurance / sockets / dye edits — same schema as inventory
    /// items, so no dialog-local editor is needed.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="NavigateToInventoryItemAsync"/>'s race
    /// control (<see cref="_suppressBlockSelectionLoad"/>). On any
    /// drift between the Buyback dialog's cached indices and the
    /// current save state (e.g. an in-flight remove shifted the
    /// element list), the navigator silently stops at the deepest
    /// frame it can build — caller can drill manually from there.
    /// </remarks>
    public async Task NavigateToVendorBuybackItemAsync(
        int blockIndex, uint storeElementIdx, uint buybackElementIdx)
    {
        if (_loadedPath is null)
        {
            return;
        }
        BlockSummary? blockSummary = null;
        foreach (var b in _allBlocks)
        {
            if (b.Index == blockIndex)
            {
                blockSummary = b;
                break;
            }
        }
        if (blockSummary is null)
        {
            DetailsError = $"Block #{blockIndex} not found in current save.";
            return;
        }

        BlockDetails topDetails;
        try
        {
            topDetails = await Task.Run(() =>
                loader.LoadBlockDetails(_loadedPath, blockIndex)).ConfigureAwait(true);
        }
        catch (CrimsonSaveException ex)
        {
            DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
            return;
        }

        _suppressBlockSelectionLoad = true;
        try { SelectedBlock = blockSummary; }
        finally { _suppressBlockSelectionLoad = false; }

        ClearNavigation();
        PushFrame(new BlockFrame(topDetails.ClassName, topDetails, Array.Empty<PathStep>()));

        // _storeDataList: object_list of StoreDataSaveData under StoreSaveData.
        var storeListField = FindFieldByName(topDetails, "_storeDataList");
        if (storeListField?.Elements is not { Count: > 0 } stores
            || storeElementIdx >= (uint)stores.Count)
        {
            return;
        }
        PushFrame(new ElementsFrame(
            $"{storeListField.Name}[{stores.Count}]",
            stores,
            Array.Empty<PathStep>(),
            (uint)storeListField.FieldIndex));

        var storeBlock = stores[(int)storeElementIdx];
        var path1 = new[] { new PathStep((uint)storeListField.FieldIndex, storeElementIdx) };
        PushFrame(new BlockFrame(
            $"{storeBlock.ClassName}[{storeElementIdx}]", storeBlock, path1));

        // _storeSoldItemDataList: object_list of ItemSaveData under StoreDataSaveData.
        var buybackListField = FindFieldByName(storeBlock, "_storeSoldItemDataList");
        if (buybackListField?.Elements is not { Count: > 0 } items
            || buybackElementIdx >= (uint)items.Count)
        {
            return;
        }
        PushFrame(new ElementsFrame(
            $"{buybackListField.Name}[{items.Count}]",
            items,
            path1,
            (uint)buybackListField.FieldIndex));

        var item = items[(int)buybackElementIdx];
        var path2 = new[]
        {
            new PathStep((uint)storeListField.FieldIndex, storeElementIdx),
            new PathStep((uint)buybackListField.FieldIndex, buybackElementIdx),
        };
        PushFrame(new BlockFrame(
            $"{item.ClassName}[{buybackElementIdx}]", item, path2));
    }

    /// <summary>
    /// Navigate the main window's block tree to a single top-level
    /// block — no descent into specific elements or fields. Used by
    /// the Character Refs Browser's per-row Jump button: the user
    /// drills manually from the landed-on top frame to the specific
    /// CharacterKey field, since the flat-list ABI doesn't carry
    /// field-level descent paths.
    /// </summary>
    /// <remarks>
    /// Mirrors the BEGINNING of <see cref="NavigateToInventoryItemAsync"/>
    /// (race control + nav-stack clear + one BlockFrame push). Best-
    /// effort silent on failure — block index not found / FFI throws
    /// surfaces through <see cref="DetailsError"/> and bails.
    /// </remarks>
    public async Task NavigateToTopLevelBlockAsync(int blockIndex)
    {
        if (_loadedPath is null)
        {
            return;
        }
        BlockSummary? blockSummary = null;
        foreach (var b in _allBlocks)
        {
            if (b.Index == blockIndex)
            {
                blockSummary = b;
                break;
            }
        }
        if (blockSummary is null)
        {
            DetailsError = $"Block #{blockIndex} not found in current save.";
            return;
        }

        BlockDetails topDetails;
        try
        {
            topDetails = await Task.Run(() =>
                loader.LoadBlockDetails(_loadedPath, blockIndex)).ConfigureAwait(true);
        }
        catch (CrimsonSaveException ex)
        {
            DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
            return;
        }

        _suppressBlockSelectionLoad = true;
        try { SelectedBlock = blockSummary; }
        finally { _suppressBlockSelectionLoad = false; }

        ClearNavigation();
        PushFrame(new BlockFrame(topDetails.ClassName, topDetails, Array.Empty<PathStep>()));
    }

    private static DecodedFieldRow? FindFieldByName(BlockDetails block, string name)
    {
        foreach (var f in block.Fields)
        {
            if (string.Equals(f.Name, name, StringComparison.Ordinal))
            {
                return f;
            }
        }
        return null;
    }

    /// <summary>
    /// When set true, the next <see cref="OnSelectedBlockChanged"/>
    /// invocation skips its fire-and-forget loader. Used by
    /// <see cref="NavigateToInventoryItemAsync"/> to drive the nav
    /// stack itself without racing against the default handler.
    /// </summary>
    private bool _suppressBlockSelectionLoad;

    /// <summary>
    /// Status string for the icon-cache slot of the footer.
    /// Shape:
    /// - "Icons: not set" → no path configured.
    /// - "Icons: <basename> (6,011 files)" → path resolved with N webp files.
    /// - "Icons: <basename> (0 files)" → path exists but empty / wrong subfolder.
    /// - "Icons: decode failed — <last error>" → at least one file failed
    ///   to decode; surfaces the codec / IO error so the user can act.
    /// </summary>
    public string IconStatus
    {
        get
        {
            var icons = localization.Icons;
            if (!icons.IsAvailable)
            {
                return "Icons: cache directory inaccessible";
            }
            if (icons.FileCount == 0)
            {
                return "Icons: empty (Tools → Extract Icons from Game Data…)";
            }
            if (icons.DecodeFailures > 0 && !string.IsNullOrEmpty(icons.LastError))
            {
                return $"Icons: {icons.FileCount:N0} files — {icons.DecodeFailures} decode fail(s); {icons.LastError}";
            }
            return $"Icons: {icons.FileCount:N0} files";
        }
    }

    /// <summary>
    /// Status string for the footer: "Localization: 102,300 entries / 6,400 items"
    /// when both layers loaded, dropping pieces when bits are missing.
    /// </summary>
    public string LocalizationStatus
    {
        get
        {
            if (!localization.IsLoaded && localization.ItemCount == 0)
            {
                return "Localization: not loaded";
            }
            var parts = new List<string>(3);
            parts.Add(localization.IsLoaded
                ? $"{localization.EntryCount:N0} entries"
                : "PALOC missing");
            if (localization.ItemCount > 0)
            {
                parts.Add($"{localization.ItemCount:N0} items");
            }
            if (!string.IsNullOrEmpty(localization.SecondaryLanguage))
            {
                parts.Add($"+ {localization.SecondaryLanguage}");
            }
            return $"Localization: {string.Join(" / ", parts)}";
        }
    }

    /// <summary>Available secondary-language codes (per-language picker).</summary>
    public IReadOnlyList<string> AvailableLanguages =>
        localization.AvailableLanguages.OrderBy(c => c, StringComparer.Ordinal).ToList();

    /// <summary>Currently-active secondary language (null = English only).</summary>
    public string? SecondaryLanguage => localization.SecondaryLanguage;

    /// <summary>
    /// Pick a secondary language by code, or pass null to revert to
    /// English-only. Persists the choice via <see cref="AppSettingsStore"/>
    /// so the next launch reloads it. After the swap, refreshes the
    /// currently-displayed fields so their resolved names update in place.
    /// </summary>
    /// <summary>
    /// Re-seed the icon provider against the fixed
    /// <c>%LOCALAPPDATA%\CrimsonAtomtic\IconCache\</c> directory and
    /// invalidate the Bitmap cache so newly-extracted .webp files
    /// surface in already-rendered DataGrids without a restart.
    /// Open Item Picker windows aren't refreshed here — they hold
    /// their own VMs and need close + reopen for the new icons.
    /// </summary>
    /// <summary>
    /// Persist a user-picked Crimson Desert install folder + re-bootstrap
    /// the localization provider against it. Validates the witness file
    /// (<c>0020\0.pamt</c>) before writing settings so a misclick can't
    /// poison <c>game_install_root</c>; returns <c>false</c> on validation
    /// failure so the caller can surface an error dialog. The override
    /// takes precedence over auto-probe (Steam libraryfolders.vdf / Epic
    /// manifest) on next launch and on every subsequent
    /// <see cref="IPlatformPaths.GameInstallRoot"/> access.
    /// </summary>
    public bool SetGameInstallRoot(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)
            || !SteamLibraryProbe.LooksLikeCrimsonDesertInstall(installRoot))
        {
            return false;
        }
        var existing = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory,
            existing with { GameInstallRoot = installRoot });
        // Re-bootstrap localization against the new root. Best-effort:
        // failure leaves the provider in whatever state the previous
        // bootstrap left it (degraded or otherwise).
        localization.TryBootstrapFromGameRoot(installRoot);
        // Portrait provider keyed on the install's 0012/0.pamt — must
        // be re-seeded so subsequent CharacterKey lookups extract from
        // the new install's PAZ. Disk-cached portraits from the
        // previous install stay valid (keyed on CharacterKey, not
        // install path).
        localization.ConfigurePortraitProvider(
            PortraitProvider.ResolveRoot(paths.LocalAppDataDirectory));
        OnPropertyChanged(nameof(LocalizationStatus));
        OnPropertyChanged(nameof(IconStatus));
        // Repaint any open save view so resolved-name columns pick up
        // the fresh catalog data.
        if (_navStack.Count > 0)
        {
            RebuildFromTop();
        }
        return true;
    }

    public void RefreshIconCache()
    {
        localization.ConfigureIconProvider(
            IconProvider.ResolveRoot(paths.LocalAppDataDirectory));
        ItemKeyToIconConverter.Provider = localization.Icons;

        // Force every currently-rendered icon binding to re-query the
        // converter by rebuilding the visible row collections. The
        // converter doesn't have a "cache invalidated, re-query"
        // signal otherwise.
        if (_navStack.Count > 0)
        {
            RebuildFromTop();
        }
        OnPropertyChanged(nameof(IconStatus));
    }

    public void SetSecondaryLanguage(string? langCode)
    {
        localization.SecondaryLanguage = langCode;
        // Preserve every other field — re-creating AppSettings from
        // scratch would otherwise drop the user's icon path, font
        // size, summary state, etc.
        var existing = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory,
            existing with { SecondaryLanguage = localization.SecondaryLanguage });
        // Rebuild the currently-visible field wrappers so their
        // ResolvedName picks up the new secondary text. Same surgical
        // refresh path the post-commit code uses.
        if (SelectedBlock is { } sel && _loadedPath is not null && _navStack.Count > 0)
        {
            try
            {
                var fresh = loader.LoadBlockDetails(_loadedPath, sel.Index);
                RefreshNavStack(fresh);
                RebuildFromTop();
            }
            catch (CrimsonSaveException)
            {
                // Block re-fetch failed — leave the stale view in place.
            }
        }
        OnPropertyChanged(nameof(SecondaryLanguage));
        OnPropertyChanged(nameof(LocalizationStatus));
    }

    /// <summary>
    /// Currently-applied UI language code (e.g. <c>"en"</c>, <c>"ja"</c>,
    /// <c>"zh-TW"</c>). Reflects what's actually on screen now —
    /// post-startup auto-detect output OR the user's latest pick. Used by
    /// the Tools → UI Language menu to highlight the active radio item.
    /// </summary>
    public string CurrentUiLanguage => uiLanguage.Current;

    /// <summary>
    /// <c>true</c> when the user has NOT set an explicit UI language
    /// override (settings.ui_language is null). The Tools → UI Language
    /// menu uses this to put a check mark next to "Auto" rather than
    /// next to the auto-detected concrete code.
    /// </summary>
    public bool IsUiLanguageAuto =>
        string.IsNullOrEmpty(AppSettingsStore.Load(paths.LocalAppDataDirectory).UiLanguage);

    /// <summary>
    /// Swap the running UI language. <paramref name="code"/> = <c>null</c>
    /// clears any persisted override and falls back to OS auto-detect;
    /// otherwise it must be one of <see cref="UiLanguageService.SupportedCodes"/>.
    /// The change applies live — every AXAML binding that uses
    /// <c>DynamicResource</c> re-resolves against the new dictionary on
    /// the next layout pass.
    /// </summary>
    public void SetUiLanguage(string? code)
    {
        // Compute the effective code to apply. Null means "auto" — we
        // persist null but still need to figure out what to actually
        // render now, which is the same auto-detect path App.axaml.cs
        // uses at startup.
        var existing = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        // ResolveActiveFromOs uses the Win32 detector — required because
        // the build sets <InvariantGlobalization>true</InvariantGlobalization>
        // (see App.axaml.cs for the rationale).
        var effective = UiLanguageService.ResolveActiveFromOs(code);
        uiLanguage.Apply(effective);

        // Persist the user's PICK (which may be null = auto), not the
        // resolved effective code — otherwise "Auto" would freeze to
        // whatever the OS reported on the last machine the settings
        // travelled to.
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory,
            existing with { UiLanguage = code });

        OnPropertyChanged(nameof(CurrentUiLanguage));
        OnPropertyChanged(nameof(IsUiLanguageAuto));
    }

    /// <summary>
    /// Best initial folder for the Open Save dialog. Picks one save root
    /// across all detected platforms (Steam / Epic / Game Pass) using
    /// the rule:
    /// <list type="number">
    ///   <item><b>Preferred platform from settings</b> — if the user has
    ///         previously opened a save successfully, the platform of
    ///         that save is persisted to <c>preferred_platform</c> and
    ///         honoured here when it still exists on disk.</item>
    ///   <item><b>Most-recently-modified save</b> — when no preference is
    ///         stored, or the preferred platform's root has since
    ///         disappeared, the platform whose latest <c>save.save</c>
    ///         was written most recently wins (proxy for "actively
    ///         being played").</item>
    ///   <item><b>Static fallback</b> — when no platform is detected at
    ///         all, returns the Steam path so the picker has a defined
    ///         starting point and the user can browse manually.</item>
    /// </list>
    /// Within the chosen platform, drills into the single user-id
    /// subfolder when exactly one exists; otherwise stops at the root.
    /// </summary>
    public string DefaultOpenSaveStartingPath
    {
        get
        {
            var root = ResolveDefaultOpenSaveRoot();
            if (!Directory.Exists(root))
            {
                return root;
            }
            // Take(2) is enough to tell "exactly one" from "two or more".
            var users = Directory.EnumerateDirectories(root).Take(2).ToArray();
            return users.Length == 1 ? users[0] : root;
        }
    }

    /// <summary>
    /// Implementation detail of <see cref="DefaultOpenSaveStartingPath"/> —
    /// returns the platform-scoped save root to anchor the picker at.
    /// </summary>
    private string ResolveDefaultOpenSaveRoot()
    {
        var discovered = paths.DiscoverSaveRoots();
        if (discovered.Count == 0)
        {
            // No platform installed (or none with an existing save folder).
            // Synthesize the canonical Steam path so the picker has somewhere
            // to anchor; the user can navigate from there if their saves
            // live somewhere unusual.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pearl Abyss", "CD", "save");
        }

        var preferred = AppSettingsStore.Load(paths.LocalAppDataDirectory).PreferredPlatform;
        if (!string.IsNullOrEmpty(preferred)
            && Enum.TryParse<SavePlatform>(preferred, ignoreCase: true, out var preferredEnum))
        {
            var match = discovered.FirstOrDefault(r => r.Platform == preferredEnum);
            if (match is not null)
            {
                return match.RootPath;
            }
        }
        // discovered is already ordered most-recent-first.
        return discovered[0].RootPath;
    }

    /// <summary>
    /// Find the on-disk save root that matches a backup entry's
    /// platform. Returns <c>null</c> when the launcher that owned the
    /// backed-up save is no longer installed on this machine (rare but
    /// possible — user reinstalled, switched platforms, etc.). Legacy
    /// backups (<see cref="SavePlatform.Unknown"/>) restore into the
    /// first available save root as a best-effort fallback so the
    /// pre-multi-platform history isn't stranded.
    /// </summary>
    private string? ResolveSaveRootForBackup(BackupEntry entry)
    {
        var discovered = paths.DiscoverSaveRoots();
        if (discovered.Count == 0)
        {
            return null;
        }
        if (entry.Platform == SavePlatform.Unknown)
        {
            return discovered[0].RootPath;
        }
        var match = discovered.FirstOrDefault(r => r.Platform == entry.Platform);
        return match?.RootPath;
    }

    /// <summary>
    /// Persist the loaded save's platform to <c>preferred_platform</c>
    /// so future Open dialogs default to the same launcher. No-op when
    /// the path doesn't sit under any known platform root (e.g. user
    /// Browse-opened a save from an unusual location — we don't want
    /// to anchor a sticky preference on a one-off).
    /// </summary>
    private void PersistLoadedSavePlatform(string savePath)
    {
        var platform = paths.ClassifySavePath(savePath);
        if (platform == SavePlatform.Unknown)
        {
            return;
        }
        var existing = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        var serialized = platform.ToString();
        if (string.Equals(existing.PreferredPlatform, serialized, StringComparison.Ordinal))
        {
            return; // already at this value; skip the write
        }
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory,
            existing with { PreferredPlatform = serialized });
    }

    private string? _loadedPath;

    /// <summary>
    /// Last-write timestamp of the file we loaded from. Captured once
    /// at load time and re-applied to every subsequent <c>WriteToFile</c>
    /// destination, so the on-disk mtime never advances past the
    /// original save. Steam Cloud uses mtime to decide which side of a
    /// sync is newer — silently bumping it would have Cloud pick the
    /// edited save over whatever the user actually wants to keep. Same
    /// reasoning for the in-game save picker, which sorts by recency.
    /// </summary>
    private DateTime? _loadedFileLastWriteTime;

    /// <summary>Currently loaded save's on-disk path, or null when no save is loaded.</summary>
    public string? LoadedPath => _loadedPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSave))]
    [NotifyPropertyChangedFor(nameof(SchemaTypeCountText))]
    [NotifyPropertyChangedFor(nameof(TocEntryCountText))]
    [NotifyPropertyChangedFor(nameof(HmacStatusText))]
    [NotifyPropertyChangedFor(nameof(PayloadSizeText))]
    [NotifyPropertyChangedFor(nameof(UncompressedSizeText))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(BackgroundOpacity))]
    private SaveSummary? _summary;

    /// <summary>Currently selected row in the blocks DataGrid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedBlock))]
    private BlockSummary? _selectedBlock;

    [ObservableProperty]
    private string? _detailsError;

    /// <summary>
    /// True when there are uncommitted in-memory edits — set whenever a
    /// successful <see cref="CommitFieldEditCommand"/> mutates a scalar
    /// field, cleared by Save / Save As.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isDirty;

    /// <summary>
    /// Live text filter for <see cref="VisibleBlocks"/>. Empty / null
    /// shows everything. Matches against the block's class name and
    /// its TOC index — covers both "I know the type of block I'm
    /// looking for" (typing "Inventory" jumps to InventorySaveData)
    /// and "I know the row number" (typing "1106" jumps directly).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlocksFilterCountText))]
    private string? _blocksFilter;

    /// <summary>
    /// Live text filter for <see cref="VisibleFields"/>. Empty / null
    /// shows everything. Applies only when <see cref="IsShowingFields"/>.
    /// Matches against field name, type name, raw display value, and
    /// the resolved item name (so typing "gold" in a block with item
    /// references highlights the row showing "Gold").
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsFilterCountText))]
    private string? _fieldsFilter;

    /// <summary>
    /// Live text filter for <see cref="VisibleElements"/>. Empty /
    /// null shows everything. Applies only when
    /// <see cref="IsShowingElements"/>. Matches against class name,
    /// raw ItemKey, and resolved item name — covers both "I know
    /// the key" and "I know the name" workflows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElementsCountText))]
    private string? _elementsFilter;

    /// <summary>
    /// Field selected in the field-detail DataGrid. The View binds the
    /// inline edit panel below the DataGrid to this row, so users edit by
    /// click-to-select + type, not via DataGrid cell-edit mode.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditPanelVisible))]
    [NotifyPropertyChangedFor(nameof(SelectedFieldTypeHint))]
    [NotifyPropertyChangedFor(nameof(CanFillSelectedFieldToMaxStack))]
    [NotifyPropertyChangedFor(nameof(SelectedFieldMaxStackHintText))]
    [NotifyPropertyChangedFor(nameof(IsSelectedFieldCharacterKey))]
    [NotifyCanExecuteChangedFor(nameof(FillSelectedFieldToMaxStackCommand))]
    private FieldRowViewModel? _selectedField;

    /// <summary>
    /// True when the currently-selected field is a scalar typed as
    /// <c>CharacterKey</c>. The edit panel binds the "Pick
    /// character…" button's visibility here — it surfaces only for
    /// fields the picker dialog can meaningfully fill.
    /// </summary>
    public bool IsSelectedFieldCharacterKey =>
        SelectedField is { IsEditable: true, TypeName: "CharacterKey" };

    /// <summary>
    /// Element selected in the element-picker DataGrid. Two-way binding
    /// lets the View highlight the row the user clicks AND lets the VM
    /// restore the previously-drilled element when popping back through
    /// the breadcrumb.
    /// </summary>
    [ObservableProperty]
    private ElementRowViewModel? _selectedElement;

    /// <summary>
    /// True when the current nav top is an ItemSaveData element list, so
    /// the Add-Item picker can clone-and-patch a new item into it. Drives
    /// the picker's top action bar (enabled state) + the per-row
    /// "Add Item…" button. Recomputed on every nav change and whenever
    /// <see cref="SelectedElement"/> changes (the clone template).
    /// </summary>
    [ObservableProperty]
    private bool _canAddItemToCurrentList;

    /// <summary>
    /// Live clone-source display name for the picker's top bar (the
    /// selected donor row's resolved name, e.g. <c>"Bullet / 子彈"</c>), or
    /// <c>null</c> when nothing is selected (first-row fallback) or adding
    /// isn't possible. The picker composes the full localized phrase from
    /// this name so each language controls word order.
    /// </summary>
    [ObservableProperty]
    private string? _addItemSourceName;

    /// <summary>
    /// Recompute <see cref="CanAddItemToCurrentList"/> +
    /// <see cref="AddItemSourceName"/> from the current nav top and
    /// <see cref="SelectedElement"/>. Mirrors the eligibility + clone-
    /// template logic in <see cref="AddItemToCurrentListAsync"/> so the
    /// picker's live bar matches what an Add will actually do.
    /// </summary>
    private void RecomputeAddItemTarget()
    {
        if (_navStack.Count > 0
            && _navStack.Peek() is ElementsFrame parent
            && parent.Elements.Count > 0
            && string.Equals(parent.Elements[0].ClassName, "ItemSaveData", StringComparison.Ordinal))
        {
            CanAddItemToCurrentList = true;
            // Prefer the selected row (the clone donor); null = first-row
            // fallback, matching AddItemToCurrentListAsync.
            if (SelectedElement is { Block: not null } selRow)
            {
                AddItemSourceName = string.IsNullOrEmpty(selRow.ResolvedName)
                    ? selRow.KeyText
                    : selRow.ResolvedName;
            }
            else
            {
                AddItemSourceName = null;
            }
        }
        else
        {
            CanAddItemToCurrentList = false;
            AddItemSourceName = null;
        }
    }

    partial void OnSelectedElementChanged(ElementRowViewModel? value) => RecomputeAddItemTarget();

    /// <summary>
    /// Raised after a pop-back when the VM has restored selection but
    /// the View still needs to scroll the row into the viewport.
    /// Subscribed once by <c>MainWindow</c>'s code-behind, which calls
    /// <c>DataGrid.ScrollIntoView</c> on the appropriate grid. Stays
    /// outside the observable-property machinery because Avalonia
    /// DataGrid doesn't expose ScrollIntoView as a bindable property.
    /// </summary>
    public event Action<FieldRowViewModel>? FieldScrollRequested;

    /// <summary>Counterpart of <see cref="FieldScrollRequested"/> for the elements DataGrid.</summary>
    public event Action<ElementRowViewModel>? ElementScrollRequested;

    /// <summary>
    /// Async confirmation callback (title, message) → user-said-yes.
    /// Provided by <c>MainWindow</c>'s code-behind via
    /// <see cref="ConfirmDialog.ShowAsync"/> so the VM doesn't depend
    /// on Avalonia Window types. Null when no view is attached
    /// (headless test scenarios), in which case any flow that
    /// requires confirmation aborts safely. Property (not field) so
    /// CA1051 stays happy.
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmRequested { get; set; }

    /// <summary>
    /// Async one-button info / refusal alert callback (title, message).
    /// Provided by <c>MainWindow</c>'s code-behind via
    /// <see cref="ConfirmDialog.ShowAlertAsync"/>. Null in headless
    /// test scenarios, in which case the refusal path silently falls
    /// back to <see cref="BulkOpStatus"/> only.
    /// </summary>
    public Func<string, string, Task>? AlertRequested { get; set; }

    /// <summary>
    /// Status footer text for the most recent bulk operation —
    /// "Filled 168 stacks." / "Bulk fill cancelled." etc. Lives in
    /// <see cref="DetailsError"/>'s sibling slot so it doesn't fight
    /// for screen real estate. Cleared on the next navigation.
    /// </summary>
    [ObservableProperty]
    private string? _bulkOpStatus;

    /// <summary>
    /// True when the left-side Save Summary panel is collapsed to a
    /// narrow rail. Toggled via <see cref="ToggleSummaryCollapsed"/>;
    /// the View binds the summary <c>ColumnDefinition</c>'s
    /// <c>Width</c> through <see cref="SummaryColumnWidth"/> and the
    /// inner content's <c>IsVisible</c> to
    /// <see cref="IsSummaryExpanded"/>. State persists in
    /// <see cref="AppSettings.SummaryCollapsed"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryColumnWidth))]
    [NotifyPropertyChangedFor(nameof(IsSummaryExpanded))]
    [NotifyPropertyChangedFor(nameof(SummaryToggleGlyph))]
    private bool _isSummaryCollapsed
        = AppSettingsStore.Load(paths.LocalAppDataDirectory).SummaryCollapsed ?? false;

    /// <summary>Inverse of <see cref="IsSummaryCollapsed"/> — easier to bind for visibility.</summary>
    public bool IsSummaryExpanded => !IsSummaryCollapsed;

    /// <summary>
    /// Grid column width for the summary panel: a fixed 36px rail
    /// when collapsed, 320px when expanded. The narrow-rail width is
    /// just enough for the toggle button plus a comfortable margin —
    /// users get back ~280px of horizontal room for the main grids.
    /// </summary>
    public Avalonia.Controls.GridLength SummaryColumnWidth =>
        IsSummaryCollapsed
            ? new Avalonia.Controls.GridLength(36)
            : new Avalonia.Controls.GridLength(320);

    /// <summary>Chevron glyph shown on the toggle button — direction matches the action.</summary>
    public string SummaryToggleGlyph => IsSummaryCollapsed ? "»" : "«";

    /// <summary>
    /// Toggle the summary panel between expanded and collapsed.
    /// Persists the new state to <c>settings.json</c> so the next
    /// launch picks up the user's preference.
    /// </summary>
    [RelayCommand]
    public void ToggleSummaryCollapsed()
    {
        IsSummaryCollapsed = !IsSummaryCollapsed;
        var existing = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory,
            existing with { SummaryCollapsed = IsSummaryCollapsed });
    }

    /// <summary>
    /// Base font size for the main window, in points. Avalonia
    /// cascades the Window's <c>FontSize</c> through the visual
    /// tree, so this affects every label / DataGrid cell / menu item
    /// in one shot. Driven through <see cref="SetFontSize"/> which
    /// clamps to <see cref="AppSettings.MinFontSize"/>..
    /// <see cref="AppSettings.MaxFontSize"/> and persists.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarFontSize))]
    private double _fontSize = Math.Clamp(
        AppSettingsStore.Load(paths.LocalAppDataDirectory).FontSize ?? AppSettings.DefaultFontSize,
        AppSettings.MinFontSize, AppSettings.MaxFontSize);

    /// <summary>
    /// Derived font size for the bottom status footer. Kept ~15%
    /// smaller than the body so the footer stays visually secondary
    /// (it carries lower-priority info — localization stats, icon
    /// cache state, transient bulk-op messages) but still scales
    /// when the user picks a larger base size.
    /// </summary>
    public double StatusBarFontSize => Math.Round(FontSize * 0.85, 1);

    /// <summary>Discrete font-size presets exposed by the Tools menu.</summary>
    public static IReadOnlyList<double> FontSizePresets { get; } =
        new[] { 10.0, 12.0, 13.0, 14.0, 16.0, 18.0, 20.0 };

    /// <summary>
    /// Apply a new font size and persist it. Values outside the
    /// supported range are clamped silently — protects against a
    /// hand-edited settings.json that would otherwise leave the UI
    /// unusable.
    /// </summary>
    public void SetFontSize(double size)
    {
        var clamped = Math.Clamp(size, AppSettings.MinFontSize, AppSettings.MaxFontSize);
        if (Math.Abs(FontSize - clamped) < 0.01)
        {
            return;
        }
        FontSize = clamped;
        var existing = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory,
            existing with { FontSize = clamped });
    }

    /// <summary>
    /// Backing store for every TOC block loaded from the save. The
    /// blocks DataGrid binds to <see cref="VisibleBlocks"/>, which is
    /// the filtered projection. Mutating <see cref="BlocksFilter"/>
    /// re-derives the visible set without touching this list.
    /// </summary>
    private readonly List<BlockSummary> _allBlocks = [];

    public ObservableCollection<BlockSummary> VisibleBlocks { get; } = [];

    public bool HasSave => Summary is not null;
    public bool HasSelectedBlock => SelectedBlock is not null;

    public string SchemaTypeCountText => Summary is null ? "" : Summary.SchemaTypeCount.ToString("N0");
    public string TocEntryCountText => Summary is null ? "" : Summary.TocEntryCount.ToString("N0");
    public string HmacStatusText => Summary is null ? "" : Summary.HmacOk ? "verified" : "FAILED";
    public string PayloadSizeText => Summary is null ? "" : $"{Summary.PayloadSize:N0} bytes";
    public string UncompressedSizeText => Summary is null ? "" : $"{Summary.UncompressedSize:N0} bytes";

    /// <summary>
    /// Opacity for the Logo.jpg watermark behind the window. Prominent
    /// on the empty splash state (~50 %) so it reads as the app's
    /// landing image, drops to a faint watermark (~7 %) once a save is
    /// loaded so the DataGrid content stays readable.
    /// </summary>
    public double BackgroundOpacity => HasSave ? 0.07 : 0.50;

    /// <summary>Window title — appends a "*" marker when the save has unsaved edits.</summary>
    public string WindowTitle
    {
        get
        {
            const string app = "CrimsonAtomtic";
            if (_loadedPath is null)
            {
                return $"{app} {AppVersion}";
            }
            var name = Path.GetFileName(_loadedPath);
            var prefix = IsDirty ? "*" : "";
            return $"{prefix}{name} — {app} {AppVersion}";
        }
    }

    /// <summary>
    /// Application version string from assembly metadata, e.g. "v1.0.0.42".
    /// The 4th digit is the build number baked in from build_number.txt at
    /// compile time (see CrimsonAtomtic.Ui.csproj). Uses GetEntryAssembly
    /// rather than GetExecutingAssembly because Native AOT single-file
    /// publish trims the latter to a versionless name.
    /// </summary>
    public static string AppVersion { get; } = GetAppVersion();

    private static string GetAppVersion()
    {
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return ver is not null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}" : "";
    }

    // ── Navigation ──────────────────────────────────────────────────────────
    //
    // Field-level inspection supports drilling into nested data:
    //   - object_locator fields with an inline child  → push a BlockFrame
    //   - object_list fields                          → push an ElementsFrame
    //                                                   (a chooser that lists
    //                                                    each element)
    //   - clicking an element in an ElementsFrame     → push a BlockFrame
    //
    // The Breadcrumb collection mirrors the stack, root → leaf. Clicking a
    // breadcrumb entry pops back to that depth.
    //
    // Scalar editing is permitted only at depth == 1 (the root frame of a
    // top-level block): the C ABI's SetScalarField addresses blocks by TOC
    // index, and nested children inlined under locators / lists aren't part
    // of the TOC. FieldRowViewModel.IsEditable is forced false on deeper
    // frames.

    private readonly Stack<NavFrame> _navStack = new();

    private readonly List<FieldRowViewModel> _allFields = [];
    private readonly List<ElementRowViewModel> _allElements = [];
    public ObservableCollection<FieldRowViewModel> VisibleFields { get; } = [];
    public ObservableCollection<ElementRowViewModel> VisibleElements { get; } = [];
    public ObservableCollection<BreadcrumbItem> Breadcrumb { get; } = [];

    public bool IsShowingFields => _navStack.Count > 0 && _navStack.Peek() is BlockFrame;
    public bool IsShowingElements => _navStack.Count > 0 && _navStack.Peek() is ElementsFrame;

    public BlockDetails? CurrentBlock => (_navStack.Count > 0 && _navStack.Peek() is BlockFrame b) ? b.Block : null;
    public bool CanGoBack => _navStack.Count > 1;

    public string MaskBytesText => CurrentBlock?.MaskBytesHex ?? "";
    public string TrailingPadText =>
        string.IsNullOrEmpty(CurrentBlock?.TrailingPadHex) ? "(none)" : CurrentBlock.TrailingPadHex;
    public string UndecodedRangesText
    {
        get
        {
            var ranges = CurrentBlock?.UndecodedRanges;
            if (ranges is null || ranges.Count == 0)
            {
                return "(none)";
            }
            return string.Join(", ", ranges.Select(r => $"[{r[0]:N0}..{r[1]:N0})"));
        }
    }

    public string FieldsFilterCountText =>
        !IsShowingFields || _allFields.Count == 0 ? string.Empty
        : $"{VisibleFields.Count:N0} of {_allFields.Count:N0}";

    public string ElementsCountText
    {
        get
        {
            if (!IsShowingElements)
            {
                return string.Empty;
            }
            var total = _allElements.Count;
            var visible = VisibleElements.Count;
            var word = total == 1 ? "element" : "elements";
            return string.IsNullOrEmpty(ElementsFilter)
                ? $"{total:N0} {word}"
                : $"{visible:N0} of {total:N0} {word}";
        }
    }

    /// <summary>Edit panel is shown when the user has selected an editable scalar field.</summary>
    public bool IsEditPanelVisible => SelectedField is { IsEditable: true };

    /// <summary>
    /// Type-tag hint shown in the edit panel, e.g. "u32" or "f64".
    /// For absent fields, suffix " (absent — Apply makes present)" so
    /// the user understands the click flips the presence bit, not just
    /// writes a value. The TextBox's placeholder text uses the same
    /// string, so an empty textbox spells out what's about to happen.
    /// </summary>
    public string SelectedFieldTypeHint =>
        SelectedField is not { } f ? string.Empty :
        f.Present       ? f.TypeTag :
        $"{f.TypeTag} (absent — Apply makes present)";

    /// <summary>
    /// True when the currently-selected scalar field can be filled
    /// with its item's <c>max_stack_count</c>. Requires:
    /// <list type="number">
    ///   <item>An editable scalar selected.</item>
    ///   <item>An integer-shaped tag (u8/u16/u32/u64) so the result
    ///         fits — max_stack values can hit 6+ digits.</item>
    ///   <item>A peer <c>ItemKey</c> field on the same block.</item>
    ///   <item>The iteminfo bridge has a <c>max_stack_count</c> entry
    ///         for that item key.</item>
    /// </list>
    /// </summary>
    public bool CanFillSelectedFieldToMaxStack => TryGetSelectedFieldMaxStack(out _);

    /// <summary>
    /// Right-aligned hint shown next to the Set-to-max button, e.g.
    /// <c>"Backpack stack: 999"</c>. Empty when no max-stack is
    /// available for the current selection.
    /// </summary>
    public string SelectedFieldMaxStackHintText =>
        TryGetSelectedFieldMaxStack(out var max)
            ? $"max stack: {max:N0}"
            : string.Empty;

    /// <summary>
    /// Find the max_stack_count value for the currently-selected
    /// field by locating its peer ItemKey on the same BlockFrame.
    /// Returns false when any link in the chain is missing.
    /// </summary>
    private bool TryGetSelectedFieldMaxStack(out ulong maxStack)
    {
        maxStack = 0;
        if (SelectedField is not { IsEditable: true } sel)
        {
            return false;
        }
        // Only sensible for integer-shaped scalars. f32/f64/bool/bytes
        // don't have a "fill to stack count" interpretation.
        if (sel.TypeTag is not ("u8" or "u16" or "u32" or "u64"
                                or "i8" or "i16" or "i32" or "i64"))
        {
            return false;
        }
        if (_navStack.Count == 0 || _navStack.Peek() is not BlockFrame top)
        {
            return false;
        }
        // Find a peer ItemKey on the same block. The conventional
        // shape is ItemSaveData with _itemKey + _stackCount as
        // sibling scalars.
        uint? itemKey = null;
        foreach (var f in top.Block.Fields)
        {
            if (f.TypeName != "ItemKey"
                || (f.Kind != "fixed_prefix" && f.Kind != "fixed_suffix"))
            {
                continue;
            }
            if (!ScalarFieldEditing.TryParse(f.Value, out var raw, out var tag)
                || tag != "u32"
                || !uint.TryParse(raw, System.Globalization.NumberStyles.Integer,
                                  System.Globalization.CultureInfo.InvariantCulture, out var k))
            {
                continue;
            }
            itemKey = k;
            break;
        }
        if (itemKey is not { } id)
        {
            return false;
        }
        var resolved = localization.GetItemMaxStackCount(id);
        if (resolved is not { } v || v == 0)
        {
            return false;
        }
        maxStack = v;
        return true;
    }

    /// <summary>
    /// Pre-fills the selected field's edit textbox with the peer
    /// ItemKey's <c>max_stack_count</c>. Deliberately doesn't auto-
    /// commit — the user reviews the value (and the resulting Apply
    /// is the single explicit "yes, write this" gesture).
    /// <para>
    /// The RawText assignment is deferred one dispatcher tick because
    /// Avalonia 12 has a "first click is a no-op" pattern when a
    /// focused TextBox is bound to the property being mutated: the
    /// VM-side property *does* change on the first click, but the
    /// focused TextBox doesn't repaint until something else (a second
    /// click, a focus loss) prods it. Posting to the dispatcher at
    /// Background priority lets the focus / binding events from the
    /// button click settle before the value lands, so the redraw
    /// fires on the next layout pass without needing a second click.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFillSelectedFieldToMaxStack))]
    private void FillSelectedFieldToMaxStack()
    {
        if (SelectedField is not { } sel || !TryGetSelectedFieldMaxStack(out var max))
        {
            return;
        }
        var newText = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => sel.RawText = newText,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Called from the View when the user picks a file via the
    /// platform's file dialog. Kept on the VM (not the View) so the
    /// load behavior is testable.
    /// </summary>
    [RelayCommand]
    private void LoadSave(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        Summary = loader.Load(path);
        _loadedPath = path;
        _loadedFileLastWriteTime = TryReadLastWriteTime(path);
        IsDirty = false;
        Journal.Clear();
        ReplaceBlocks(Summary?.Blocks);
        SelectedBlock = null;
        ClearNavigation();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WindowTitle));
        PersistLoadedSavePlatform(path);
    }

    /// <summary>
    /// Read the file's last-write timestamp, swallowing IO errors so a
    /// missing / locked file doesn't take down Load. Returns null when
    /// the read fails — callers should treat that as "don't try to
    /// preserve a timestamp we never captured".
    /// </summary>
    private static DateTime? TryReadLastWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTime(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Re-apply the captured load-time timestamp onto a freshly-written
    /// file. Best-effort: if the FS rejects the write (read-only, perm
    /// issue, file disappeared between WriteToFile and here), leave the
    /// natural "now" timestamp rather than failing the Save operation.
    /// </summary>
    private void PreserveOriginalTimestamp(string path)
    {
        if (_loadedFileLastWriteTime is not { } t)
        {
            return;
        }
        try
        {
            File.SetLastWriteTime(path, t);
        }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    /// <summary>
    /// Replace the loaded block set: stashes the full list in
    /// <see cref="_allBlocks"/>, resets the filter, and re-derives
    /// <see cref="VisibleBlocks"/>. Both Load and Save As path through
    /// here so the two flows can't drift.
    /// </summary>
    private void ReplaceBlocks(IReadOnlyList<BlockSummary>? blocks)
    {
        _allBlocks.Clear();
        if (blocks is not null)
        {
            _allBlocks.AddRange(blocks);
        }
        BlocksFilter = null;
        ApplyBlocksFilter();
    }

    partial void OnSelectedBlockChanged(BlockSummary? value)
    {
        if (_suppressBlockSelectionLoad)
        {
            // External navigator (e.g. Find Items goto) is driving
            // the load + stack push itself; skip the default
            // fire-and-forget worker so we don't race against it and
            // overwrite its deeper stack with a shallow root frame.
            // The navigator handles ClearNavigation + PushFrame
            // itself.
            return;
        }
        ClearNavigation();
        if (value is null || _loadedPath is null)
        {
            return;
        }
        // Fire-and-forget: LoadBlockDetails for large blocks (e.g.
        // QuestSaveData with 4341 missions) costs ~1-2 s on first hit
        // because Rust serializes the whole block tree to JSON and C#
        // deserializes it back. Running it on the UI thread freezes
        // the window; a Task.Run worker keeps clicks responsive. Cache
        // hits return in microseconds so the Task.Run overhead is
        // negligible there too. `_ =` discards the Task — the
        // continuation handles its own completion + error path.
        _ = LoadSelectedBlockAsync(value, _loadedPath);
    }

    /// <summary>
    /// Worker for <see cref="OnSelectedBlockChanged"/>: fetches block
    /// details on a thread-pool thread, then pushes the root frame on
    /// the UI thread via Avalonia's synchronization context.
    /// </summary>
    /// <remarks>
    /// Race guard: the user can click a different row while this is
    /// awaiting. When the awaited fetch completes we re-check that
    /// <see cref="MainWindowViewModel.SelectedBlock"/> is still the
    /// block we started loading; if not, the result is dropped silently
    /// and the more-recent click's worker is left to handle the new
    /// selection.
    /// </remarks>
    private async Task LoadSelectedBlockAsync(BlockSummary requested, string loadedPath)
    {
        BlockDetails details;
        try
        {
            details = await Task.Run(() => loader.LoadBlockDetails(loadedPath, requested.Index))
                .ConfigureAwait(true);
        }
        catch (CrimsonSaveException ex)
        {
            if (ReferenceEquals(SelectedBlock, requested))
            {
                DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
            }
            return;
        }

        if (!ReferenceEquals(SelectedBlock, requested))
        {
            return;
        }
        // Root frame: empty path — this block is at the TOC level.
        PushFrame(new BlockFrame(details.ClassName, details, Array.Empty<PathStep>()));
    }

    partial void OnFieldsFilterChanged(string? value) => ApplyFieldsFilter();

    partial void OnElementsFilterChanged(string? value) => ApplyElementsFilter();

    partial void OnBlocksFilterChanged(string? value) => ApplyBlocksFilter();

    partial void OnIsDirtyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Drill into a field's nested data. No-op when the field is a scalar
    /// (no child / empty elements list). Called from the View's button click.
    /// </summary>
    [RelayCommand]
    private void DrillIntoField(DecodedFieldRow? row)
    {
        if (row is null || _navStack.Count == 0 || _navStack.Peek() is not BlockFrame parent)
        {
            return;
        }
        // Record which row in _allFields the user drilled from, so a
        // later pop-back can restore that selection + scroll. _allFields
        // is in original field order, so IndexOf is the cheapest match.
        parent.LastDrilledIndex = FindAllFieldsIndex(row);
        if (row.Child is { } child)
        {
            // Locator descent: append (fieldIdx, 0). The C ABI ignores
            // ElementIndex on locator steps.
            var newPath = ExtendPath(parent.Path, new PathStep((uint)row.FieldIndex, 0));
            PushFrame(new BlockFrame($"{row.Name}: {child.ClassName}", child, newPath));
        }
        else if (row.Elements is { Count: > 0 } elements)
        {
            // List descent is two-stage: enter the element picker first;
            // the path step gets built when the user picks an element.
            PushFrame(new ElementsFrame(
                $"{row.Name}[{elements.Count}]",
                elements,
                parent.Path,
                (uint)row.FieldIndex));
        }
    }

    /// <summary>Drill into a specific element of an ObjectList frame.</summary>
    [RelayCommand]
    private void DrillIntoElement(BlockDetails? element)
    {
        if (element is null || _navStack.Count == 0 || _navStack.Peek() is not ElementsFrame parent)
        {
            return;
        }
        var idx = -1;
        for (var i = 0; i < parent.Elements.Count; i++)
        {
            if (ReferenceEquals(parent.Elements[i], element))
            {
                idx = i;
                break;
            }
        }
        // Stash the source index on the parent ElementsFrame so a pop-back
        // can re-highlight (and scroll to) the element row the user picked.
        parent.LastDrilledIndex = idx >= 0 ? idx : null;
        var label = idx >= 0 ? $"[{idx}]: {element.ClassName}" : element.ClassName;
        // List descent: append (listFieldIdx, elementIdx).
        var newPath = ExtendPath(parent.PathToList, new PathStep(parent.ListFieldIndex, (uint)Math.Max(idx, 0)));
        PushFrame(new BlockFrame(label, element, newPath));
    }

    private int? FindAllFieldsIndex(DecodedFieldRow row)
    {
        for (var i = 0; i < _allFields.Count; i++)
        {
            if (ReferenceEquals(_allFields[i].Row, row))
            {
                return i;
            }
        }
        return null;
    }

    private static PathStep[] ExtendPath(IReadOnlyList<PathStep> parent, PathStep step)
    {
        var arr = new PathStep[parent.Count + 1];
        for (var i = 0; i < parent.Count; i++)
        {
            arr[i] = parent[i];
        }
        arr[^1] = step;
        return arr;
    }

    /// <summary>
    /// Pop the navigation stack back to <paramref name="depth"/> (0-based,
    /// root inclusive). Called from breadcrumb clicks; depth past the
    /// current top is a no-op.
    /// </summary>
    [RelayCommand]
    private void NavigateToDepth(int depth)
    {
        if (depth < 0 || depth >= _navStack.Count - 1)
        {
            return;
        }
        var target = depth + 1;
        while (_navStack.Count > target)
        {
            _navStack.Pop();
        }
        RebuildFromTop();
        RestoreDrillSelection();
    }

    [RelayCommand]
    private void NavigateBack()
    {
        if (_navStack.Count <= 1)
        {
            return;
        }
        _navStack.Pop();
        RebuildFromTop();
        RestoreDrillSelection();
    }

    /// <summary>
    /// After popping the stack, re-select the row the user drilled from
    /// (stashed on each frame as <see cref="NavFrame.LastDrilledIndex"/>)
    /// and ask the View to scroll it into the viewport. Bounds-checks
    /// the index — a deeper edit that shrank the list silently falls
    /// through to no selection rather than crashing.
    /// </summary>
    private void RestoreDrillSelection()
    {
        if (_navStack.Count == 0)
        {
            return;
        }
        var top = _navStack.Peek();
        if (top.LastDrilledIndex is not { } idx)
        {
            return;
        }
        switch (top)
        {
            case BlockFrame when idx >= 0 && idx < _allFields.Count:
                var fieldVm = _allFields[idx];
                SelectedField = fieldVm;
                FieldScrollRequested?.Invoke(fieldVm);
                break;
            case ElementsFrame when idx >= 0 && idx < _allElements.Count:
                var elementVm = _allElements[idx];
                SelectedElement = elementVm;
                ElementScrollRequested?.Invoke(elementVm);
                break;
        }
    }

    /// <summary>
    /// Apply the edit currently sitting in <paramref name="row"/>'s
    /// <see cref="FieldRowViewModel.RawText"/>. Encodes per type tag,
    /// pushes the bytes through <see cref="ISaveLoader.SetScalarField"/>,
    /// and on success re-reads the block to refresh every field's display
    /// value (a single mutation can ripple into peer fields via the schema).
    /// On failure, leaves the raw text intact and stamps
    /// <see cref="FieldRowViewModel.EditError"/>.
    /// </summary>
    /// <remarks>
    /// No special-case guard for <c>MissionStateData._state ← 5</c> /
    /// <c>_completedTime</c> promotions any more — the dedicated
    /// "Mark Challenge Complete" command (with the engine-faithful
    /// alertHistory recipe) carries its own warning, and bare-row
    /// edits to those fields are now treated as expert-mode raw edits
    /// without an extra prompt.
    /// </remarks>
    [RelayCommand]
    private async Task CommitFieldEditAsync(FieldRowViewModel? row)
    {
        var block = SelectedBlock;
        if (row is null || !row.IsEditable || _loadedPath is null || block is null)
        {
            return;
        }
        if (!ScalarFieldEditing.TryEncode(row.TypeTag, row.RawText, out var bytes, out var err))
        {
            row.EditError = err;
            return;
        }
        await Task.CompletedTask; // keep async signature; no awaits remain
        try
        {
            // Path-addressed FFI: empty path collapses to a top-level mutation,
            // non-empty walks into locator children / list elements.
            //
            // Two routes depending on the field's current presence:
            //  - Present scalar → SetScalarField (in-place byte patch over
            //    the existing field bytes; mask unchanged).
            //  - Absent scalar → SetScalarFieldPresent with makePresent=true
            //    (flips the mask bit AND fills the freshly-allocated bytes
            //    with `bytes`; encoder rewrites the block's body and all
            //    cascading TOC offsets in one pass).
            var pathArr = row.EnclosingPath is PathStep[] a ? a : row.EnclosingPath.ToArray();
            if (row.Present)
            {
                loader.SetScalarField(block.Index, pathArr, row.FieldIndex, bytes);
            }
            else
            {
                loader.SetScalarFieldPresent(
                    block.Index, pathArr, row.FieldIndex,
                    makePresent: true, bytes);
            }
        }
        catch (CrimsonSaveException ex)
        {
            row.EditError = $"{ex.Message} (code {ex.ErrorCode})";
            return;
        }

        // Capture the pre-mutation display value for the journal —
        // FieldRowViewModel.DisplayValue carries the formatted "<val> <tag>"
        // string that's about to be replaced. We log here (after the FFI
        // call succeeded but before refreshing the row) so the journal
        // captures the "before" exactly as the user saw it.
        var preEditValue = row.DisplayValue;

        // Re-fetch the top-level block; refresh every nav frame so popping
        // back via breadcrumb shows fresh values (the mutation may ripple
        // across peer fields via the schema). Each existing FieldRowViewModel
        // gets its DisplayValue updated in place so the DataGrid keeps its
        // scroll position and the user's selection.
        var freshTop = loader.LoadBlockDetails(_loadedPath, block.Index);
        RefreshNavStack(freshTop);
        IsDirty = true;
        Journal.Log("Field edit",
            row.Present
                ? $"{block.ClassName}.{row.Name}: {preEditValue} → {row.RawText} <{row.TypeTag}>"
                : $"{block.ClassName}.{row.Name}: absent → {row.RawText} <{row.TypeTag}>");
        OnPropertyChanged(nameof(WindowTitle));
        // The "(absent — Apply makes present)" suffix on SelectedFieldTypeHint
        // depends on SelectedField.Present, which may have just flipped from
        // false → true. The accessor doesn't observe nested property changes,
        // so prod it manually here.
        OnPropertyChanged(nameof(SelectedFieldTypeHint));
    }

    /// <summary>
    /// Flip a currently-present scalar field to absent. Mirrors the
    /// "absent → present" path that <see cref="CommitFieldEdit"/> handles
    /// via <c>SetScalarFieldPresent(makePresent: true, …)</c>, but for
    /// the reverse direction with an empty initial-bytes span. Only
    /// reachable when the selected field is editable AND currently
    /// present — the inline edit panel's button is hidden otherwise.
    /// </summary>
    /// <remarks>
    /// The previously-stored byte content is dropped (the mask bit flips
    /// and the field's payload region is removed from the block). To
    /// restore it, the user types a fresh value and clicks Apply —
    /// <see cref="CommitFieldEdit"/> takes the absent → present path
    /// once <see cref="FieldRowViewModel.Present"/> is false.
    /// </remarks>
    [RelayCommand]
    private void MakeFieldAbsent(FieldRowViewModel? row)
    {
        var block = SelectedBlock;
        if (row is null || !row.IsEditable || !row.Present
            || _loadedPath is null || block is null)
        {
            return;
        }
        try
        {
            var pathArr = row.EnclosingPath is PathStep[] a ? a : row.EnclosingPath.ToArray();
            loader.SetScalarFieldPresent(
                block.Index, pathArr, row.FieldIndex,
                makePresent: false, ReadOnlySpan<byte>.Empty);
        }
        catch (CrimsonSaveException ex)
        {
            row.EditError = $"{ex.Message} (code {ex.ErrorCode})";
            return;
        }
        var freshTop = loader.LoadBlockDetails(_loadedPath, block.Index);
        RefreshNavStack(freshTop);
        IsDirty = true;
        Journal.Log("Field edit",
            $"{block.ClassName}.{row.Name}: made absent");
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SelectedFieldTypeHint));
    }

    /// <summary>
    /// Magic StringInfoKey hash that marks a Sealed Abyss Artifact
    /// challenge as <b>visible</b> in the in-game UI. Verified universal
    /// across all 12 solved-or-visible SA challenges in slot102 (Shield
    /// II / III / VI; Spear I; Sword III / IV; Bow I / III; Battle V;
    /// Operation VI; Living III; Hunting I; Crime I / VIII;
    /// ChallengeAndChange III). Always appears at <c>_usedTagList[1]</c>
    /// when the engine has discovered the challenge.
    /// </summary>
    private const uint VisibleTagHash = 3938836851u;

    // Note: there's also a "completed" magic tag (4104166156u) that the
    // engine writes to the catalog row's _usedTagList only AFTER the
    // user claims the reward in-game. Pattern B v1 doesn't write the
    // catalog at all, so we don't need that constant here — the engine
    // adds it when it processes the reward pickup.

    /// <summary>
    /// Append <paramref name="addTags"/> to <paramref name="existing"/>,
    /// preserving order and deduping (so repeated Mark-Complete clicks
    /// don't grow the list unbounded). Returns a fresh array suitable
    /// for <see cref="ISaveLoader.DynamicArraySetU32Elements"/>.
    /// </summary>
    private static uint[] MergeTags(IReadOnlyList<uint> existing, params uint[] addTags)
    {
        var seen = new HashSet<uint>(existing.Count + addTags.Length);
        var result = new List<uint>(existing.Count + addTags.Length);
        foreach (var t in existing)
        {
            if (seen.Add(t)) result.Add(t);
        }
        foreach (var t in addTags)
        {
            if (seen.Add(t)) result.Add(t);
        }
        return result.ToArray();
    }

    /// <summary>
    /// True when the current top-of-stack frame is a
    /// <c>MissionStateData</c> catalog row (positive <c>_key</c>,
    /// <c>_state != 5</c>) with an engine-shaped completion infrastructure
    /// in place — adjacent visibility twin at <c>catalog_idx + 1</c> in
    /// "visible" state AND a matching FAR tracker further down the list —
    /// AND the player currently holds at least one Sealed Abyss Artifact
    /// item in inventory. Drives the visibility / CanExecute of the
    /// "Mark Challenge Complete" block-action button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The held-artifact gate is the recipe's safety net. Pure placeholder
    /// challenges (the user has never encountered them in-game, no
    /// artifact was ever picked up) become visible / unlock only when the
    /// engine processes the artifact pickup sequence. Flipping their
    /// catalog state without that pickup leaves the engine in an
    /// inconsistent state and the card stays hidden in the UI. By
    /// requiring at least one SA artifact in the user's inventory we
    /// short-circuit the button on saves where the engine has done none
    /// of the discovery bookkeeping yet. <b>We do not match this specific
    /// challenge → its specific artifact</b> — the user is responsible
    /// for selecting the right catalog row.
    /// </para>
    /// <para>
    /// The original category restriction (only <c>Challenge_*</c> /
    /// <c>Mission_MiniGame_*</c> string-keys) has been removed. Non-SA
    /// challenges that happen to share the FAR-tracker shape now show
    /// the button too — the user takes responsibility for the FAR-shape
    /// match and the warning dialog reinforces the artifact-in-bag
    /// requirement.
    /// </para>
    /// <para>
    /// Re-evaluated lazily on every property read. Cached cheaply via
    /// <see cref="OnPropertyChanged"/> from <see cref="NotifyNavigationChanged"/>
    /// and from the post-edit refresh — keeps the UI in sync after the
    /// _state field changes.
    /// </para>
    /// </remarks>
    public bool IsCurrentChallengeMarkable => TryReadCurrentChallengeContext(out _, out _);

    /// <summary>
    /// True when navigation is on a <c>MissionStateData</c> catalog row
    /// (class + path of length 1). Drives the "Mark Challenge Complete"
    /// button's <b>visibility</b> — independent of eligibility, so the
    /// button stays present on every catalog row and grays out (with
    /// tooltip explanation) when the row itself is ineligible.
    /// </summary>
    public bool IsCurrentNavOnMissionStateRow
    {
        get
        {
            if (_navStack.Count == 0
                || _navStack.Peek() is not BlockFrame frame
                || !string.Equals(frame.Block.ClassName, "MissionStateData", StringComparison.Ordinal)
                || SelectedBlock is null
                || frame.Path is not { Count: 1 })
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Tooltip text for the "Mark Challenge Complete" button. When the
    /// challenge is eligible, returns the action-description string
    /// (<c>MarkChallengeCompleteTip</c> resource). When the row is on
    /// a <c>MissionStateData</c> catalog but ineligible, returns
    /// "<c>{MarkChallengeSkipReasonPrefix}</c> {skip reason}" so the
    /// user can see <i>why</i> the button is disabled (replaces the
    /// pre-2026-05-17 silent grey-out). Empty string when the button
    /// isn't visible at all.
    /// </summary>
    public string CurrentChallengeMarkTooltip
    {
        get
        {
            if (!IsCurrentNavOnMissionStateRow)
            {
                return string.Empty;
            }
            var markable = TryReadCurrentChallengeContext(out _, out var skipReason);
            if (markable)
            {
                return LookupUiResourceString("MarkChallengeCompleteTip") ?? string.Empty;
            }
            var prefix = LookupUiResourceString("MarkChallengeSkipReasonPrefix") ?? "Disabled:";
            return $"{prefix} {skipReason ?? "(unknown reason)"}";
        }
    }

    private static string? LookupUiResourceString(string key)
    {
        if (Avalonia.Application.Current?.TryGetResource(key, null, out var v) == true
            && v is string s)
        {
            return s;
        }
        return null;
    }

    /// <summary>
    /// Mark the currently-displayed catalog <c>MissionStateData</c>
    /// challenge as completed using the engine-faithful <b>Pattern B v1</b>
    /// recipe — exactly the shape the engine writes for an in-game
    /// completion that hasn't been claimed yet (verified against the
    /// slot102 → live slot103 Hooves II transition):
    /// <list type="number">
    ///   <item>FAR tracker (negative-keyed entry at <c>_key =
    ///         adjacent_twin._key - 1</c>): <c>_state ← 5</c>,
    ///         <c>_completedTime</c> stamped, <c>_usedTagList</c> grown
    ///         to <c>[base, visible]</c>.</item>
    ///   <item>NEW <c>MissionStateData</c> entry appended to
    ///         <c>_missionStateList</c> with <c>_key = X_2 sub-mission
    ///         catalog key</c> ("Use the sealed Abyss artifact"
    ///         follow-up). Implemented as <c>ListCloneElement</c> of
    ///         the FAR tracker (the clone inherits state=2, branched=
    ///         present, tags=[base] — the right shape) followed by a
    ///         <c>_key</c> + <c>_branchedTime</c> patch.</item>
    ///   <item><b>Catalog row + adjacent twin: NOT TOUCHED.</b> The
    ///         engine writes those (and the alertHistory entry) only
    ///         when the user actually claims the reward in-game (uses
    ///         the artifact item). After this edit + reload, the
    ///         engine sees the pre-completed state and offers the
    ///         user the reward pickup, which then triggers the rest
    ///         of the bookkeeping naturally.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Gate</b>: the button only enables when (a) the adjacent twin
    /// is in "visible" state (state=5 + _completedTime present +
    /// tags ≥ 2), (b) the FAR tracker exists and isn't already at
    /// state=5, and (c) the player currently holds at least one Sealed
    /// Abyss Artifact item in inventory. (a)+(b) require the user to
    /// have already picked up the reward artifact for this challenge
    /// in-game — which is what creates the FAR tracker. (c) is a
    /// global safety net: if the user has zero SA artifacts at all,
    /// the recipe's engine-side reconciliation has nothing to attach
    /// to. The category restriction has been removed — any
    /// MissionStateData row with the right shape is eligible.
    /// </para>
    /// <para>
    /// Pattern B v1 is the result of a long iterative debug. Earlier
    /// patterns (A v1 / v2 / v3) tried to write the post-claim state
    /// directly to the catalog row + adjacent twin + alertHistory; all
    /// three regressed previously-visible challenge cards into the
    /// "unknown / locked" UI state because some piece we couldn't
    /// fully reverse-engineer was missing. Pattern B v1 sidesteps that
    /// by writing only what the engine writes pre-claim and letting
    /// the engine fill in the rest on next load.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsCurrentChallengeMarkable))]
    private async Task MarkCurrentChallengeCompleteAsync()
    {
        BulkOpStatus = null;
        if (!TryReadCurrentChallengeContext(out var ctx, out _)
            || ConfirmRequested is not { } ask
            || _loadedPath is null)
        {
            return;
        }

        var farTagsAfter = MergeTags(ctx.FarUsedTagList, VisibleTagHash).Length;
        var msg =
            $"Mark this challenge as completed using the Pattern B v1 recipe?\n\n" +
            $"  Challenge key: {ctx.CatalogKey}\n" +
            $"  Internal name: {ctx.InternalName}\n" +
            $"  Adjacent visibility twin key: {ctx.TwinKey}\n" +
            $"  FAR tracker: idx {ctx.FarElementIdx}, key {ctx.TwinKey - 1u}\n" +
            $"  X_2 sub-mission to {(ctx.FollowUpAlreadyExists ? "update" : "create")}: " +
            $"key {ctx.FollowUpKey} ({ctx.FollowUpInternalName})\n\n" +
            "What this writes:\n" +
            "  • FAR tracker _state ← 5 + _completedTime stamped.\n" +
            $"  • FAR tracker _usedTagList: {ctx.FarUsedTagList.Count} → {farTagsAfter} entries " +
            "(adds visible magic tag).\n" +
            (ctx.FollowUpAlreadyExists
                ? "  • X_2 sub-mission entry already exists; skipped insert.\n"
                : "  • NEW MissionStateData entry appended (cloned from FAR tracker, " +
                  "_key + _branchedTime patched to the X_2 sub-mission shape).\n") +
            "  • Catalog row + adjacent twin: UNTOUCHED. Engine handles those at reward pickup.\n\n" +
            "[!! HOLD THE MATCHING ARTIFACT] Confirm that the Sealed Abyss Artifact for THIS " +
            "specific challenge is currently in your inventory. The button only enables when " +
            "you hold at least one SA artifact (any variant), but the in-game reward-claim " +
            "flow on next reload only completes if the matching artifact for this challenge " +
            "is in your bag. Pattern B v1 on its own is not enough — the artifact pickup is " +
            "the engine's gating signal.\n\n" +
            "[!! VERIFIED ON Shield II / Spear I / Hooves II / Slash III IN SLOT102] " +
            "Pattern B v1 is reverse-engineered from the engine's natural Hooves II " +
            "completion (slot102 → live slot103 transition). The recipe writes the " +
            "pre-claim state and lets the engine finish bookkeeping on next load. " +
            "Earlier patterns that tried to write the post-claim state directly all " +
            "corrupted the in-game UI. CONFIRM YOUR SAVE IS BACKED UP before proceeding " +
            "(auto-backup at %LOCALAPPDATA%\\CrimsonAtomtic\\Backups\\ — File → " +
            "Restore from Backup… can roll back).\n\n" +
            "[!! WILL AFFECT GAME PROGRESSION] Forcing the FAR tracker to state=5 tells the " +
            "engine the challenge is complete; downstream content / NPC dialogues that " +
            "depend on this challenge being done will trigger.\n\n" +
            "[!! ACHIEVEMENTS WILL NOT UNLOCK] Steam / platform achievements only fire on " +
            "a real in-game completion. Marking via file edit will not trigger the " +
            "achievement, and once the challenge is in this state the engine won't re-fire " +
            "it on subsequent legitimate completion. Do not use this for challenges you " +
            "intend to complete legitimately later.\n\n" +
            "Proceed?";
        var ok = await ask("Mark challenge complete (Pattern B v1)?", msg);
        if (!ok)
        {
            BulkOpStatus = "Mark cancelled.";
            return;
        }

        BulkOpStatus = $"Applying Pattern B v1 (FAR tracker + X_2 sub-mission)…";
        // Compute timestamp watermark — engine-natural completions
        // always sort after older ones.
        var maxCt = await Task.Run(() => ScanMaxMissionCompletedTime());
        var newCt = maxCt == 0UL ? 1UL : maxCt + 1UL;
        var newElementIdx = ctx.MissionStateListCount; // append index (= old count)

        var lookup = TryReadFarKeyFieldIdx(ctx);
        if (lookup.Error is { } lookupErr)
        {
            BulkOpStatus = $"Mark failed (could not re-read FAR tracker): {lookupErr.Message}";
            return;
        }
        if (lookup.FarKeyFieldIdx < 0)
        {
            BulkOpStatus = "Mark failed: FAR tracker lacks a _key field — recipe can't continue.";
            return;
        }

        var error = await Task.Run(() =>
            ApplyPatternBv1Writes(ctx, newCt, newElementIdx, lookup.FarKeyFieldIdx));

        RefreshSelectedBlockSilently();

        if (error is null)
        {
            IsDirty = true;
            Journal.Log("Mark Challenge",
                $"Marked challenge {ctx.CatalogKey} ({ctx.InternalName}) complete (Pattern B v1)");
            OnPropertyChanged(nameof(WindowTitle));
            BulkOpStatus = ctx.FollowUpAlreadyExists
                ? $"Marked {ctx.CatalogKey} ({ctx.InternalName}) complete via Pattern B v1 — "
                  + $"FAR tracker idx {ctx.FarElementIdx} flipped (X_2 sub-mission already existed)."
                : $"Marked {ctx.CatalogKey} ({ctx.InternalName}) complete via Pattern B v1 — "
                  + $"FAR tracker idx {ctx.FarElementIdx} flipped, "
                  + $"X_2 sub-mission entry created at idx {newElementIdx}.";
        }
        else
        {
            BulkOpStatus = $"Mark failed: {error.Message}. "
                           + "Save state may be partial — reload without writing to revert.";
        }
        NotifyMarkChallengeStateChanged();
    }

    /// <summary>
    /// Look up the FAR tracker's <c>_key</c> field index — needed to
    /// patch the freshly-cloned X_2 entry's key into the right shape.
    /// Returns <c>(-1, ex)</c> on FFI failure, <c>(-1, null)</c> on
    /// schema-shape failure, <c>(idx, null)</c> on success.
    /// </summary>
    private (int FarKeyFieldIdx, CrimsonSaveException? Error) TryReadFarKeyFieldIdx(
        CurrentChallengeContext ctx)
    {
        if (_loadedPath is null) return (-1, null);
        try
        {
            var top = loader.LoadBlockDetails(_loadedPath, ctx.MissionTopBlockIdx);
            var listField = top.Fields[ctx.MissionListFieldIdx];
            var far = listField.Elements![ctx.FarElementIdx];
            foreach (var f in far.Fields)
            {
                if (f.Name == "_key") return (f.FieldIndex, null);
            }
            return (-1, null);
        }
        catch (CrimsonSaveException ex)
        {
            return (-1, ex);
        }
    }

    /// <summary>
    /// Apply the Pattern B v1 mutation set for one challenge. Shared
    /// by the per-row "Mark Challenge Complete" button and the bulk
    /// "Complete All Held Sealed Abyss Artifact Challenges" sweep.
    /// Synchronous — wrap in <c>Task.Run</c> at the call site so the
    /// 5–6 length-changing FFI calls don't freeze the UI.
    /// </summary>
    /// <param name="ctx">Pre-built context (catalog row + FAR tracker addressing).</param>
    /// <param name="newCt">Timestamp watermark — must sort after every prior <c>_completedTime</c>.</param>
    /// <param name="appendElementIdx">
    /// Index where the cloned X_2 entry will land. Caller is
    /// responsible for tracking running list size across bulk applies
    /// (each successful apply with <c>!FollowUpAlreadyExists</c> grows
    /// the list by 1).
    /// </param>
    /// <param name="farKeyFieldIdx">
    /// FAR tracker's <c>_key</c> field index — must be looked up via
    /// <see cref="TryReadFarKeyFieldIdx"/> before calling.
    /// </param>
    /// <returns><c>null</c> on success; the failing exception otherwise.</returns>
    private CrimsonSaveException? ApplyPatternBv1Writes(
        CurrentChallengeContext ctx, ulong newCt, int appendElementIdx, int farKeyFieldIdx)
    {
        var ctBytes = BitConverter.GetBytes(newCt);
        var farPath = new[] { new PathStep((uint)ctx.MissionListFieldIdx, (uint)ctx.FarElementIdx) };
        var newPath = new[] { new PathStep((uint)ctx.MissionListFieldIdx, (uint)appendElementIdx) };
        try
        {
            // Phase 1: clone FAR tracker to end of list (only when the
            // X_2 entry doesn't already exist). The clone inherits
            // FAR's CURRENT shape (state=2, branched=present,
            // tags=[base]) — perfect template for the new X_2 sub-
            // mission entry.
            //
            // Each length-changing call (ListCloneElement,
            // SetScalarFieldPresent toggling, DynamicArraySetU32Elements)
            // forces a full body re-decode in crimson-rs — these are
            // the dominant cost (~25ms each on a 5MB body). Trailing
            // scalar setters get batched into one
            // SetScalarFieldsBatch call to cut FFI roundtrips, even
            // though the body re-decode count is unchanged.
            if (!ctx.FollowUpAlreadyExists)
            {
                loader.ListCloneElement(
                    ctx.MissionTopBlockIdx,
                    Array.Empty<PathStep>(),
                    ctx.MissionListFieldIdx,
                    ctx.FarElementIdx,
                    appendElementIdx);
            }

            // Phase 2a: length-changing parts of the FAR tracker
            // update — completedTime presence-promote (when absent)
            // + usedTagList grow.
            if (!ctx.FarCompletedTimeAlreadyPresent)
            {
                loader.SetScalarFieldPresent(
                    ctx.MissionTopBlockIdx, farPath,
                    ctx.FarCompletedTimeFieldIdx,
                    makePresent: true, ctBytes);
            }
            var farTarget = MergeTags(ctx.FarUsedTagList, VisibleTagHash);
            loader.DynamicArraySetU32Elements(
                ctx.MissionTopBlockIdx, farPath,
                ctx.FarUsedTagListFieldIdx, farTarget);

            // Phase 2b + 3: every remaining scalar mutation in one
            // batch. Saves up to 3 FFI roundtrips per challenge
            // versus the old per-field SetScalarField pattern. The
            // batch is all-or-nothing — if any op validates wrong,
            // none are applied. Validation only fails on schema
            // drift, which would have errored earlier in this method.
            var batch = new List<ScalarBatchOp>(4)
            {
                new ScalarBatchOp(
                    ctx.MissionTopBlockIdx, farPath,
                    ctx.FarStateFieldIdx, new byte[] { 5 }),
            };
            if (ctx.FarCompletedTimeAlreadyPresent)
            {
                batch.Add(new ScalarBatchOp(
                    ctx.MissionTopBlockIdx, farPath,
                    ctx.FarCompletedTimeFieldIdx, ctBytes));
            }
            if (!ctx.FollowUpAlreadyExists)
            {
                batch.Add(new ScalarBatchOp(
                    ctx.MissionTopBlockIdx, newPath,
                    farKeyFieldIdx, BitConverter.GetBytes(ctx.FollowUpKey)));
                batch.Add(new ScalarBatchOp(
                    ctx.MissionTopBlockIdx, newPath,
                    ctx.FarBranchedTimeFieldIdx, ctBytes));
            }
            loader.SetScalarFieldsBatch(batch);
            return null;
        }
        catch (CrimsonSaveException ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Tools menu: walk every Sealed Abyss Artifact item currently in
    /// the user's inventory, look up the catalog mission each one
    /// gates via <c>iteminfo.look_detail_mission_info</c>, and apply
    /// Pattern B v1 to every challenge that's eligible (FAR tracker
    /// present + not yet at state=5 + X_2 follow-up sub-mission key
    /// known).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bulk variant of the per-row "Mark Challenge Complete" button.
    /// Per-challenge writes are the exact same Pattern B v1 mutation
    /// set (5–6 length-changing FFI calls per challenge, sequential
    /// — no batch ABI for the clone op yet). Failures on individual
    /// challenges are reported but don't abort the sweep; partial
    /// progress is captured in the journal.
    /// </para>
    /// <para>
    /// <b>Why iterate by held artifact</b> (not by every catalog
    /// SA row): we can only safely complete challenges whose matching
    /// artifact the user actually holds (the engine's gating signal
    /// at reward claim time). Iterating from the artifact side keeps
    /// the "I hold X, complete the matching Y" invariant tight.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Pre-flight scan exposed for the Sealed Abyss preview dialog (and
    /// any future caller): enumerate every eligible SA-artifact challenge
    /// context in the loaded save. Pure read — safe to call off the UI
    /// thread (the dialog wraps it in <c>Task.Run</c>). Returns an empty
    /// preview when no save / iteminfo is loaded.
    /// </summary>
    /// <param name="includeNonSealedArtifact">
    /// false (default, strict): only true <c>Challenge_SealedArtifact_*</c>
    /// missions are candidates. true (broad): also include any other mission a
    /// Sealed_Abyss_Artifact item happens to point at (abyss gates, node /
    /// territory, knowledge / discovery, generic missions) — unsupported for
    /// Pattern B v1; the dialog warns before enabling it.
    /// </param>
    internal BulkSaPreview ScanSealedArtifactCandidates(bool includeNonSealedArtifact = false)
    {
        if (_loadedPath is null
            || Summary is not { Blocks: { } blocks }
            || localization.ItemCount == 0)
        {
            return new BulkSaPreview(
                0, Array.Empty<CurrentChallengeContext>(), 0, 0, 0, 0,
                Array.Empty<(uint, string?, string)>());
        }
        return ScanBulkSealedArtifactCandidates(blocks, includeNonSealedArtifact);
    }

    /// <summary>
    /// Apply Pattern B v1 to the given Sealed Abyss challenge contexts
    /// (the checked rows from the preview dialog). Behaviour-preserving
    /// extraction of the former bulk command's apply loop: deferred-
    /// redecode batch, per-challenge progress, partial-success journal +
    /// status. Returns (applied, firstError, firstErrorKey).
    /// </summary>
    internal async Task<(int Applied, CrimsonSaveException? Err, uint ErrKey)>
        ApplySealedArtifactChallengesAsync(IReadOnlyList<CurrentChallengeContext> contexts)
    {
        if (_loadedPath is null || contexts.Count == 0)
        {
            return (0, null, 0u);
        }

        // Apply loop: read FAR key field per ctx (cheap — mutation_version
        // cache handles repeat block reads), then run Pattern B v1
        // writes on the thread pool. Track running list count across
        // applies so successive X_2 inserts land at the right index.
        //
        // Live progress: each per-challenge write triggers ~3 body
        // re-decodes in crimson-rs (~25ms each on a 5MB body) — the
        // sweep takes ~8–10s for the full 141-artifact set. An
        // IProgress<T> callback marshals "N/total — challenge K"
        // ticks back to the UI thread so the status footer animates
        // instead of looking frozen.
        var totalCandidates = contexts.Count;
        BulkOpStatus = $"Applying Pattern B v1: 0 / {totalCandidates}…";
        var baseCt = await Task.Run(() => ScanMaxMissionCompletedTime());
        var newCt = baseCt == 0UL ? 1UL : baseCt + 1UL;
        // Per-block running append index — different QuestSaveData
        // blocks have independent _missionStateList counts.
        var perBlockCount = new Dictionary<int, int>();
        foreach (var c in contexts)
        {
            if (!perBlockCount.ContainsKey(c.MissionTopBlockIdx))
            {
                perBlockCount[c.MissionTopBlockIdx] = c.MissionStateListCount;
            }
        }

        // Progress<T> captures the UI SynchronizationContext at
        // construction (this method runs on the UI thread), so
        // ((IProgress<…>)progress).Report(...) from inside Task.Run
        // posts the lambda back to the UI thread automatically.
        var progress = new Progress<(int Done, int Total, uint CurrentKey)>(p =>
        {
            BulkOpStatus = p.CurrentKey == 0
                ? $"Applying Pattern B v1: {p.Done} / {p.Total}…"
                : $"Applying Pattern B v1: {p.Done} / {p.Total} — challenge 0x{p.CurrentKey:X8}";
        });
        var reporter = (IProgress<(int Done, int Total, uint CurrentKey)>)progress;

        var (applied, firstError, firstErrorKey) = await Task.Run<(int, CrimsonSaveException?, uint)>(() =>
        {
            var done = 0;
            CrimsonSaveException? loopError = null;
            uint loopErrorKey = 0;
            // Wrap the apply loop in a deferred-redecode batch (see
            // vendor/crimson-rs/docs/save-deferred-redecode.md). Each
            // per-challenge write today triggers ~3 length-changing
            // body re-decodes (~25ms each on a 5MB body); without the
            // batch, 141 challenges × 3 = ~423 re-decodes ≈ 10s. With
            // the batch, every write mutates the in-memory tree only,
            // and the trailing EndDeferredRedecode runs ONE encode +
            // parse + decode pass for the whole sweep — ~100ms total.
            //
            // Partial-success semantics preserved: per-op failure is
            // captured into loopError/loopErrorKey and we BREAK out
            // of the loop, letting the deferred batch commit the
            // already-applied work (matching the pre-batch behaviour
            // where partial progress was kept and the user could
            // still Save or reload-to-revert). Letting the exception
            // escape RunDeferred would Abort and roll the partial
            // work back, which is the wrong UX for this flow.
            try
            {
                loader.RunDeferred(() =>
                {
                    foreach (var c in contexts)
                    {
                        // Announce BEFORE applying so the user sees the
                        // current challenge key while the FFI is in
                        // flight. With the deferred batch the loop runs
                        // ~100x faster than before; the IProgress<T>
                        // posts to the UI thread coalesce naturally.
                        reporter.Report((done, totalCandidates, c.CatalogKey));
                        var lookup = TryReadFarKeyFieldIdx(c);
                        if (lookup.FarKeyFieldIdx < 0)
                        {
                            loopError = lookup.Error ?? new CrimsonSaveException(0,
                                $"Challenge {c.CatalogKey}: FAR tracker lacks _key field.");
                            loopErrorKey = c.CatalogKey;
                            return;
                        }
                        var appendIdx = perBlockCount[c.MissionTopBlockIdx];
                        var err = ApplyPatternBv1Writes(c, newCt, appendIdx, lookup.FarKeyFieldIdx);
                        if (err is not null)
                        {
                            loopError = err;
                            loopErrorKey = c.CatalogKey;
                            return;
                        }
                        if (!c.FollowUpAlreadyExists)
                        {
                            perBlockCount[c.MissionTopBlockIdx] = appendIdx + 1;
                        }
                        newCt++;
                        done++;
                    }
                });
            }
            catch (CrimsonSaveException commitEx)
            {
                // End_*'s own commit failed (MUTATION_INVALID): the
                // Rust side already rolled `blocks` back to the
                // pre-begin snapshot, so nothing landed. Surface as
                // applied=0 with the commit error.
                return (0, commitEx, 0u);
            }
            reporter.Report((done, totalCandidates, 0u));
            return (done, loopError, loopErrorKey);
        });

        RefreshSelectedBlockSilently();

        if (firstError is null)
        {
            IsDirty = true;
            Journal.Log("Mark Challenge",
                $"Bulk-completed {applied} Sealed Abyss Artifact challenge(s) (Pattern B v1)");
            OnPropertyChanged(nameof(WindowTitle));
            BulkOpStatus =
                $"Done: bulk-completed {applied} of {contexts.Count} eligible "
                + $"Sealed Abyss Artifact challenge(s) via Pattern B v1.";
        }
        else
        {
            BulkOpStatus =
                $"Bulk Mark failed at challenge {firstErrorKey} after {applied}/{contexts.Count} "
                + $"applied: {firstError.Message}. Save state is partial — reload without writing to revert.";
            // Even partial success counts as dirty so the user sees
            // the title-bar warning + can still Save what landed.
            if (applied > 0)
            {
                IsDirty = true;
                Journal.Log("Mark Challenge",
                    $"Bulk-completed {applied} Sealed Abyss Artifact challenge(s) (Pattern B v1, "
                    + $"partial — failed at {firstErrorKey})");
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
        NotifyMarkChallengeStateChanged();
        return (applied, firstError, firstErrorKey);
    }

    /// <summary>
    /// Pre-flight scan for the bulk SA challenge sweep: enumerate
    /// held SA artifacts, map each to its catalog mission key via
    /// <see cref="LocalizationProvider.LookupItemLookDetailMissionInfo"/>,
    /// then walk every <c>QuestSaveData._missionStateList</c> looking
    /// for catalog rows that match. For each match, try to build a
    /// Pattern B v1 context via
    /// <see cref="TryBuildChallengeContextFromCatalogRow"/>; counters
    /// track why each row was skipped so the confirm dialog can show
    /// the breakdown.
    /// </summary>
    private BulkSaPreview ScanBulkSealedArtifactCandidates(IReadOnlyList<BlockSummary> blocks, bool includeNonSealedArtifact = false)
    {
        if (_loadedPath is null)
        {
            return default;
        }
        // 1. Enumerate every Sealed Abyss Artifact item key from
        //    iteminfo (no "held in inventory" gate) and map each to its
        //    challenge mission key via the forward
        //    look_detail_mission_info lookup. The held-inventory check
        //    was the wrong layer — the per-challenge eligibility gate
        //    is "FAR tracker present in this save", which is created
        //    once on first artifact pickup and stays around even after
        //    the artifact item has been consumed / dropped. Filtering
        //    by held inventory dropped legitimate candidates where the
        //    user had picked up the artifact but no longer carried it.
        //    Now we let TryBuildChallengeContextFromCatalogRow gate
        //    purely on the save-side data shape.
        var allArtifacts = new HashSet<uint>(
            localization.EnumerateItemsByStringKeyPrefix("Sealed_Abyss_Artifact")
                        .Select(p => p.ItemKey));
        if (allArtifacts.Count == 0)
        {
            return new BulkSaPreview(0, new List<CurrentChallengeContext>(), 0, 0, 0, 0,
                Array.Empty<(uint, string?, string)>());
        }
        var missionKeyToArtifact = new Dictionary<uint, uint>();
        var skippedNoMission = 0;
        foreach (var ik in allArtifacts)
        {
            var mk = localization.GetItemLookDetailMissionInfo(ik);
            if (mk is null)
            {
                skippedNoMission++;
                continue;
            }
            if (!includeNonSealedArtifact)
            {
                // Strict (default): only true Challenge_SealedArtifact_*
                // missions. In 1.10 a Sealed_Abyss_Artifact item can also point
                // at abyss-gate / node / knowledge-discovery / generic missions,
                // which Pattern B v1 must not touch (e.g. Challenge_Discover_*
                // needs KnowledgeSaveData the recipe never writes). The broad
                // scan opts back into the old behaviour, behind a dialog warning.
                var name = localization.MissionInfoStringKey(mk.Value);
                if (name is null
                    || !name.StartsWith("Challenge_SealedArtifact_", StringComparison.Ordinal))
                {
                    skippedNoMission++;
                    continue;
                }
            }
            // 1:1 invariant (verified upstream) — collisions shouldn't
            // happen but defend just in case: keep first wins.
            missionKeyToArtifact.TryAdd(mk.Value, ik);
        }
        if (missionKeyToArtifact.Count == 0)
        {
            return new BulkSaPreview(
                allArtifacts.Count, new List<CurrentChallengeContext>(),
                skippedNoMission, 0, 0, 0,
                Array.Empty<(uint, string?, string)>());
        }

        // 2. Walk QuestSaveData blocks to find catalog rows whose
        //    _key is in the held-mission-key set. For each match,
        //    try to build a Pattern B v1 context.
        var candidates = new List<CurrentChallengeContext>();
        var skippedNoFar = 0;
        var skippedAlreadyDone = 0;
        var skippedOther = 0;
        // Per-key skip reasons so the dialog can pinpoint exactly which
        // catalog rows were filtered out. Captures (catalog _key,
        // internal name, reason string) for every row that
        // TryBuildChallengeContextFromCatalogRow rejected.
        var skipDetails = new List<(uint Key, string? Name, string Reason)>();
        foreach (var b in blocks)
        {
            if (!string.Equals(b.ClassName, "QuestSaveData", StringComparison.Ordinal))
            {
                continue;
            }
            BlockDetails top;
            try { top = loader.LoadBlockDetails(_loadedPath, b.Index); }
            catch (CrimsonSaveException) { continue; }
            for (var fi = 0; fi < top.Fields.Count; fi++)
            {
                var listField = top.Fields[fi];
                if (!string.Equals(listField.Name, "_missionStateList", StringComparison.Ordinal)
                    || listField.Elements is not { Count: > 0 } siblings)
                {
                    continue;
                }
                for (var ei = 0; ei < siblings.Count; ei++)
                {
                    var row = siblings[ei];
                    if (!string.Equals(row.ClassName, "MissionStateData", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // Quick filter: pull _key and check membership before
                    // running the full context build.
                    DecodedFieldRow? keyFld = null;
                    DecodedFieldRow? stateFld = null;
                    foreach (var f in row.Fields)
                    {
                        if (keyFld is null && f.Name == "_key") keyFld = f;
                        else if (stateFld is null && f.Name == "_state") stateFld = f;
                    }
                    if (keyFld is null
                        || !TryParseScalarUInt(keyFld.Value, out var kU64)
                        || kU64 == 0
                        || kU64 > uint.MaxValue
                        || !missionKeyToArtifact.ContainsKey((uint)kU64))
                    {
                        continue;
                    }
                    // Already done: catalog state=5 is a separate skip
                    // bucket from "context build failed".
                    if (stateFld is not null
                        && TryParseScalarUInt(stateFld.Value, out var stateU64)
                        && stateU64 == 5UL)
                    {
                        skippedAlreadyDone++;
                        continue;
                    }
                    if (!TryBuildChallengeContextFromCatalogRow(
                        b.Index, fi, ei, out var ctx, out var skipReason))
                    {
                        // Bucket the skip into NoFar / Other based on the
                        // shape of skipReason. Twin-already-state=5 means
                        // FAR tracker exists somewhere but downstream
                        // gate failed; otherwise the artifact hasn't been
                        // picked up yet.
                        var noFarShape =
                            skipReason is not null
                            && (skipReason.Contains("artifact never picked up")
                                || skipReason.Contains("artifact pickup")
                                || skipReason.Contains("FAR tracker")
                                    && skipReason.Contains("not found"));
                        if (noFarShape) skippedNoFar++;
                        else skippedOther++;
                        // Capture a per-key entry so the dialog can show
                        // exactly which challenges were skipped + why.
                        skipDetails.Add(((uint)kU64,
                            localization.MissionInfoStringKey((uint)kU64),
                            skipReason ?? "(unknown reason)"));
                        continue;
                    }
                    candidates.Add(ctx);
                }
            }
        }
        return new BulkSaPreview(
            allArtifacts.Count, candidates,
            skippedNoMission, skippedNoFar, skippedAlreadyDone, skippedOther,
            skipDetails);
    }

    // Note: the former `ScanHeldSealedArtifactItemKeys` helper was
    // removed 2026-05-17 along with the held-inventory eligibility
    // gate. If a future feature wants the "held SA artifacts" set
    // separately, the recipe is:
    //   localization.EnumerateItemsByStringKeyPrefix("Sealed_Abyss_Artifact")
    //     intersected with the _inventorylist[*]._itemList[*]._itemKey
    //     scan via loader.ListInventoryItems (one FFI call now).

    /// <summary>
    /// One faction-stronghold node addressable for a state edit. Captured
    /// by <see cref="ScanFactionNodes"/>; consumed by
    /// <see cref="SetFactionNodeStatesAsync"/>. <see cref="Path"/> descends
    /// from the <c>FactionSaveData</c> block to the element;
    /// <see cref="StateFieldIndex"/> is the <c>_factionState</c> scalar
    /// within it. <see cref="OwnerKey"/> is a <c>FactionNodeKey</c>
    /// (resolve via the factionnode bridge); <see cref="ConquerorKey"/> is
    /// a <c>FactionKey</c> (PALOC).
    /// </summary>
    internal readonly record struct FactionNodeTarget(
        int BlockIndex,
        PathStep[] Path,
        int StateFieldIndex,
        byte CurrentState,
        uint OwnerKey,
        uint ConquerorKey,
        bool IsCapital);

    /// <summary>
    /// The five <c>_factionState</c> (<c>FactionNodeStateType</c>, u8)
    /// values + labels. 2 = Active is the "discovered" state the in-game
    /// pickup writes. Mirrors the reference editor's STATE_LABELS.
    /// </summary>
    internal static class FactionNodeStates
    {
        public static readonly IReadOnlyList<(byte Value, string Label)> All =
        [
            (0, "Undiscovered"),
            (1, "Discovered"),
            (2, "Active"),
            (3, "Conquered"),
            (4, "Lost"),
        ];

        public static string Label(byte state) => state switch
        {
            0 => "Undiscovered",
            1 => "Discovered",
            2 => "Active",
            3 => "Conquered",
            4 => "Lost",
            _ => state.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Walk every <c>FactionSaveData</c> block and enumerate the
    /// <c>_factionNodeElementSaveDataList</c> elements as editable
    /// <see cref="FactionNodeTarget"/>s. Pure read — safe under
    /// <c>Task.Run</c> (the dialog calls it that way). Skips elements with
    /// no present <c>_factionState</c> (nothing to write).
    /// </summary>
    internal IReadOnlyList<FactionNodeTarget> ScanFactionNodes()
    {
        var result = new List<FactionNodeTarget>();
        if (_loadedPath is null || Summary is not { Blocks: { } blocks })
        {
            return result;
        }
        foreach (var b in blocks)
        {
            if (!string.Equals(b.ClassName, "FactionSaveData", StringComparison.Ordinal))
            {
                continue;
            }
            BlockDetails top;
            try { top = loader.LoadBlockDetails(_loadedPath, b.Index); }
            catch (CrimsonSaveException) { continue; }
            for (var fi = 0; fi < top.Fields.Count; fi++)
            {
                var listField = top.Fields[fi];
                if (!string.Equals(listField.Name, "_factionNodeElementSaveDataList", StringComparison.Ordinal)
                    || listField.Elements is not { Count: > 0 } nodes)
                {
                    continue;
                }
                for (var ei = 0; ei < nodes.Count; ei++)
                {
                    var stateFieldIdx = -1;
                    byte currentState = 0;
                    uint ownerKey = 0;
                    uint conquerorKey = 0;
                    var isCapital = false;
                    foreach (var f in nodes[ei].Fields)
                    {
                        if (!f.Present)
                        {
                            continue;
                        }
                        switch (f.Name)
                        {
                            case "_factionState":
                                stateFieldIdx = f.FieldIndex;
                                if (TryParseScalarUInt(f.Value, out var sv)) currentState = (byte)sv;
                                break;
                            case "_ownerFactionKey":
                                if (TryParseScalarUInt(f.Value, out var ov)) ownerKey = (uint)ov;
                                break;
                            case "_conquerorFactionKey":
                                if (TryParseScalarUInt(f.Value, out var cv)) conquerorKey = (uint)cv;
                                break;
                            case "_isCapital":
                                if (TryParseScalarUInt(f.Value, out var capv)) isCapital = capv != 0;
                                break;
                        }
                    }
                    if (stateFieldIdx < 0)
                    {
                        continue;
                    }
                    result.Add(new FactionNodeTarget(
                        b.Index,
                        [new PathStep((uint)fi, (uint)ei)],
                        stateFieldIdx,
                        currentState,
                        ownerKey,
                        conquerorKey,
                        isCapital));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Set <c>_factionState</c> on the given nodes via a single in-place
    /// <see cref="ISaveLoader.SetScalarFieldsBatch"/> (no list growth).
    /// Skips no-op changes (new == current). Returns (applied, error).
    /// </summary>
    internal async Task<(int Applied, CrimsonSaveException? Err)> SetFactionNodeStatesAsync(
        IReadOnlyList<(FactionNodeTarget Target, byte NewState)> changes)
    {
        if (_loadedPath is null || changes.Count == 0)
        {
            return (0, null);
        }
        var ops = new List<ScalarBatchOp>(changes.Count);
        foreach (var (t, ns) in changes)
        {
            if (ns != t.CurrentState)
            {
                ops.Add(new ScalarBatchOp(t.BlockIndex, t.Path, t.StateFieldIndex, new[] { ns }));
            }
        }
        if (ops.Count == 0)
        {
            return (0, null);
        }
        var (applied, err) = await Task.Run<(int, CrimsonSaveException?)>(() =>
        {
            try
            {
                loader.SetScalarFieldsBatch(ops);
                return (ops.Count, null);
            }
            catch (CrimsonSaveException ex)
            {
                return (0, ex);
            }
        });

        RefreshSelectedBlockSilently();
        if (applied > 0)
        {
            IsDirty = true;
            Journal.Log("Faction nodes", $"Set _factionState on {applied} node(s)");
            OnPropertyChanged(nameof(WindowTitle));
        }
        return (applied, err);
    }

    /// <summary>
    /// Pre-flight result for the bulk SA challenge sweep.
    /// <see cref="KnownArtifactCount"/> is the total number of
    /// <c>Sealed_Abyss_Artifact_*</c> rows iteminfo carries (typically
    /// 141 in 1.07) — NOT the inventory-held subset (that gate was
    /// removed 2026-05-17). Eligibility is the save-side data shape
    /// only.
    /// <see cref="SkipDetails"/> carries per-key (catalog _key,
    /// internal name, reason) tuples for every challenge that was
    /// considered but rejected — surfaced in the confirm dialog so
    /// users can see exactly which challenges were skipped and why.
    /// </summary>
    internal readonly record struct BulkSaPreview(
        int KnownArtifactCount,
        IReadOnlyList<CurrentChallengeContext> Candidates,
        int SkippedNoMission,
        int SkippedNoFar,
        int SkippedAlreadyDone,
        int SkippedOther,
        IReadOnlyList<(uint Key, string? Name, string Reason)> SkipDetails);

    /// <summary>
    /// Find the largest <c>_completedTime</c> present across every
    /// <c>MissionStateData</c> entry in the loaded save. Used by the
    /// Pattern B v1 recipe to stamp the FAR tracker with a value that
    /// sorts after every prior engine-natural completion.
    /// </summary>
    private ulong ScanMaxMissionCompletedTime()
    {
        if (_loadedPath is null || Summary is not { Blocks: { } blocks })
        {
            return 0UL;
        }
        ulong max = 0;
        foreach (var b in blocks)
        {
            if (!string.Equals(b.ClassName, "QuestSaveData", StringComparison.Ordinal))
            {
                continue;
            }
            BlockDetails top;
            try { top = loader.LoadBlockDetails(_loadedPath, b.Index); }
            catch (CrimsonSaveException) { continue; }
            foreach (var listField in top.Fields)
            {
                if (!string.Equals(listField.Name, "_missionStateList", StringComparison.Ordinal)
                    || listField.Elements is not { Count: > 0 } missions)
                {
                    continue;
                }
                foreach (var m in missions)
                {
                    foreach (var f in m.Fields)
                    {
                        if (f.Name != "_completedTime") continue;
                        if (f.Present
                            && TryParseScalarUInt(f.Value, out var v)
                            && v > max)
                        {
                            max = v;
                        }
                        break;
                    }
                }
            }
        }
        return max;
    }

    /// <summary>
    /// Inspect the current top-of-stack <see cref="BlockFrame"/>; when
    /// it's a catalog <c>MissionStateData</c> challenge currently at
    /// <c>_state != 5</c>, populate <paramref name="ctx"/> with the
    /// addressing info needed by
    /// <see cref="MarkCurrentChallengeCompleteAsync"/>.
    /// Returns false (with default <paramref name="ctx"/>) when any
    /// precondition fails.
    /// </summary>
    private bool TryReadCurrentChallengeContext(
        out CurrentChallengeContext ctx, out string? skipReason)
    {
        ctx = default;
        skipReason = null;
        if (_navStack.Count == 0
            || _navStack.Peek() is not BlockFrame frame
            || !string.Equals(frame.Block.ClassName, "MissionStateData", StringComparison.Ordinal)
            || SelectedBlock is not { } topBlock
            // Path must be exactly one step (the missionStateList element).
            || frame.Path is not { Count: 1 } path1)
        {
            skipReason = "navigation not on a MissionStateData catalog row";
            return false;
        }
        var pathStep = path1[0];
        return TryBuildChallengeContextFromCatalogRow(
            topBlock.Index, (int)pathStep.FieldIndex, (int)pathStep.ElementIndex,
            out ctx, out skipReason);
    }

    /// <summary>
    /// Bulk-friendly variant of <see cref="TryReadCurrentChallengeContext"/>
    /// addressed by (topBlockIdx, listFieldIdx, catalogElementIdx)
    /// instead of the nav-stack. Used both by the per-row button
    /// (wrapper above) and the Tools → Bulk Complete Held Sealed
    /// Artifact Challenges sweep.
    /// </summary>
    /// <param name="skipReason">
    /// When the method returns false, set to a human-readable reason
    /// explaining which gate failed (X_2 follow-up lookup failed, FAR
    /// tracker missing, etc.). Bulk sweep surfaces these in the
    /// confirm dialog so the user can see which challenges were
    /// skipped and why. Null when the method returns true.
    /// </param>
    private bool TryBuildChallengeContextFromCatalogRow(
        int topBlockIdx,
        int listFieldIdx,
        int catalogElementIdx,
        out CurrentChallengeContext ctx,
        out string? skipReason)
    {
        ctx = default;
        skipReason = null;
        if (_loadedPath is null) { skipReason = "no save loaded"; return false; }

        BlockDetails parentTop;
        try
        {
            parentTop = loader.LoadBlockDetails(_loadedPath, topBlockIdx);
        }
        catch (CrimsonSaveException ex)
        {
            skipReason = $"LoadBlockDetails failed: {ex.Message}";
            return false;
        }
        if (listFieldIdx < 0 || listFieldIdx >= parentTop.Fields.Count)
        { skipReason = "listFieldIdx out of range"; return false; }
        var listField = parentTop.Fields[listFieldIdx];
        var siblings = listField.Elements;
        if (siblings is null || catalogElementIdx < 0 || catalogElementIdx >= siblings.Count)
        { skipReason = "catalogElementIdx out of range"; return false; }
        var catalogRow = siblings[catalogElementIdx];
        if (!string.Equals(catalogRow.ClassName, "MissionStateData", StringComparison.Ordinal))
        { skipReason = $"unexpected class '{catalogRow.ClassName}'"; return false; }
        DecodedFieldRow? keyFld = null, stateFld = null;
        foreach (var f in catalogRow.Fields)
        {
            if (keyFld is null && f.Name == "_key") keyFld = f;
            else if (stateFld is null && f.Name == "_state") stateFld = f;
        }
        if (keyFld is null || stateFld is null)
        { skipReason = "catalog row missing _key or _state field"; return false; }
        if (!TryParseScalarUInt(keyFld.Value, out var keyU64)
            || keyU64 == 0
            || keyU64 > uint.MaxValue
            || !TryParseScalarUInt(stateFld.Value, out var stateU64))
        { skipReason = "catalog _key/_state could not be parsed"; return false; }
        // Catalog-only: negative-encoded engine-internal keys (0xFFFFxxxx) are
        // out of scope.
        if (keyU64 >= 0xFFFF0000UL)
        { skipReason = "catalog _key is negative-encoded (sub-step row, not a top-level catalog)"; return false; }
        if (stateU64 == 5UL)
        { skipReason = "already complete (catalog _state == 5)"; return false; }
        var name = localization.MissionInfoStringKey((uint)keyU64);
        // The recipe computes the X_2 follow-up sub-mission key from
        // `name + "_2"`, so we need a name.
        if (name is null)
        { skipReason = $"missioninfo bridge has no internal name for key {keyU64}"; return false; }

        // 1. Adjacent twin
        var twinElementIdx = catalogElementIdx + 1;
        if (twinElementIdx >= siblings.Count)
        { skipReason = "no adjacent row (catalog is the last element)"; return false; }
        var twin = siblings[twinElementIdx];
        if (!string.Equals(twin.ClassName, "MissionStateData", StringComparison.Ordinal))
        { skipReason = $"adjacent row class is '{twin.ClassName}', expected MissionStateData"; return false; }
        DecodedFieldRow? twinKeyFld = null, twinStateFld = null, twinCtFld = null, twinTagsFld = null;
        foreach (var f in twin.Fields)
        {
            if (twinKeyFld is null && f.Name == "_key") twinKeyFld = f;
            else if (twinStateFld is null && f.Name == "_state") twinStateFld = f;
            else if (twinCtFld is null && f.Name == "_completedTime") twinCtFld = f;
            else if (twinTagsFld is null && f.Name == "_usedTagList") twinTagsFld = f;
        }
        if (twinKeyFld is null || twinStateFld is null || twinCtFld is null || twinTagsFld is null)
        { skipReason = "adjacent twin missing one of (_key, _state, _completedTime, _usedTagList)"; return false; }
        if (!TryParseScalarUInt(twinKeyFld.Value, out var twinKeyU64)
            || twinKeyU64 < 0xFFFF0000UL
            || twinKeyU64 > uint.MaxValue)
        { skipReason = "adjacent twin _key is not negative-encoded (not an SA-shape twin)"; return false; }
        // Visibility gate: twin must be at state=5 + _completedTime present + 2+ tags.
        // That's the engine's "user has picked up the artifact" marker —
        // without it the recipe is unsafe (likely to leave the card hidden).
        if (!TryParseScalarUInt(twinStateFld.Value, out var twinStateU64) || twinStateU64 != 5UL)
        { skipReason = "adjacent twin _state != 5 (artifact never picked up)"; return false; }
        if (!twinCtFld.Present)
        { skipReason = "adjacent twin _completedTime absent (artifact pickup incomplete)"; return false; }
        var twinKey = (uint)twinKeyU64;
        uint[] twinTags;
        var twinPath = new[] { new PathStep((uint)listFieldIdx, (uint)twinElementIdx) };
        try
        {
            twinTags = loader.DynamicArrayGetU32Elements(
                topBlockIdx, twinPath, twinTagsFld.FieldIndex);
        }
        catch (CrimsonSaveException ex)
        { skipReason = $"twin _usedTagList read failed: {ex.Message}"; return false; }
        if (twinTags.Length < 2)
        { skipReason = $"twin _usedTagList only has {twinTags.Length} entries (need 2: base + visible)"; return false; }

        // 2. X_2 follow-up sub-mission key (catalog name + "_2"). The
        // anchor-scan missioninfo bridge drops negative-keyed rows
        // (0xFFFFxxxx) so multi-objective challenges whose `_2`
        // sub-step lives in that range (Living_*/Cooking series, etc.)
        // can't be located via name-lookup. Pattern B v1 is verified
        // only on linear single-step challenges (Shield II / Spear I /
        // Hooves II / Slash III), so this skip is correct — the
        // diagnostic just makes it visible.
        var followUpName = name + "_2";
        var followUpKeyOpt = localization.LookupMissionKeyByInternalName(followUpName);
        if (followUpKeyOpt is not { } followUpKey)
        {
            skipReason = $"X_2 follow-up '{followUpName}' not in missioninfo bridge "
                + "(likely a multi-objective challenge with negative-keyed sub-steps; "
                + "Pattern B v1 doesn't cover this shape)";
            return false;
        }

        // 3. FAR tracker — walk the list for entry with _key = twinKey - 1
        var farKey = twinKey - 1u;
        int? farElementIdx = null;
        bool followUpExists = false;
        for (var i = 0; i < siblings.Count; i++)
        {
            var sib = siblings[i];
            if (!string.Equals(sib.ClassName, "MissionStateData", StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var f in sib.Fields)
            {
                if (f.Name != "_key") continue;
                if (!f.Present
                    || !TryParseScalarUInt(f.Value, out var k)
                    || k > uint.MaxValue)
                {
                    break;
                }
                if (k == farKey)
                {
                    farElementIdx = i;
                }
                else if (k == followUpKey)
                {
                    followUpExists = true;
                }
                break;
            }
        }
        if (farElementIdx is not { } farIdx)
        { skipReason = $"FAR tracker (key 0x{(twinKey - 1u):X8}) not found in _missionStateList"; return false; }

        // Locate FAR tracker's field indices for the writes we'll do.
        var far = siblings[farIdx];
        DecodedFieldRow? farStateFld = null, farCtFld = null, farTagsFld = null, farBranchedFld = null;
        foreach (var f in far.Fields)
        {
            if (farStateFld is null && f.Name == "_state") farStateFld = f;
            else if (farCtFld is null && f.Name == "_completedTime") farCtFld = f;
            else if (farTagsFld is null && f.Name == "_usedTagList") farTagsFld = f;
            else if (farBranchedFld is null && f.Name == "_branchedTime") farBranchedFld = f;
        }
        if (farStateFld is null || farCtFld is null || farTagsFld is null || farBranchedFld is null)
        { skipReason = "FAR tracker missing one of (_state, _completedTime, _usedTagList, _branchedTime)"; return false; }
        // Skip if FAR is already at state=5 (engine has already marked completion;
        // user just needs to claim the reward in-game to push it to catalog).
        if (TryParseScalarUInt(farStateFld.Value, out var farStateU64) && farStateU64 == 5UL)
        { skipReason = "FAR tracker already at state=5 (challenge engine-completed; claim reward in-game)"; return false; }
        // FAR's _branchedTime must already be present (engine sets it on
        // artifact pickup); the cloned X_2 entry needs this field to
        // exist so we can patch its value to the new ct.
        if (!farBranchedFld.Present)
        { skipReason = "FAR tracker _branchedTime absent (artifact pickup not recorded)"; return false; }

        var farPath = new[] { new PathStep((uint)listFieldIdx, (uint)farIdx) };
        uint[] farTags;
        try
        {
            farTags = loader.DynamicArrayGetU32Elements(
                topBlockIdx, farPath, farTagsFld.FieldIndex);
        }
        catch (CrimsonSaveException ex)
        { skipReason = $"FAR _usedTagList read failed: {ex.Message}"; return false; }

        // Held-artifact gate removed 2026-05-17 (per user directive):
        // the engine's eligibility signal is the FAR tracker's presence
        // in the save (created once on first artifact pickup, stays
        // around even after the item is consumed). Checking current
        // inventory dropped legitimate candidates. The data-shape gates
        // above are the only correctness-relevant ones.

        ctx = new CurrentChallengeContext(
            CatalogKey: (uint)keyU64,
            InternalName: name,
            MissionTopBlockIdx: topBlockIdx,
            MissionListFieldIdx: listFieldIdx,
            CatalogElementIdx: catalogElementIdx,
            TwinKey: twinKey,
            FarElementIdx: farIdx,
            FarStateFieldIdx: farStateFld.FieldIndex,
            FarCompletedTimeFieldIdx: farCtFld.FieldIndex,
            FarCompletedTimeAlreadyPresent: farCtFld.Present,
            FarUsedTagListFieldIdx: farTagsFld.FieldIndex,
            FarUsedTagList: farTags,
            FarBranchedTimeFieldIdx: farBranchedFld.FieldIndex,
            FollowUpKey: followUpKey,
            FollowUpInternalName: followUpName,
            FollowUpAlreadyExists: followUpExists,
            MissionStateListCount: siblings.Count);
        return true;
    }

    // HoldsAnySealedAbyssArtifact() removed 2026-05-17 along with the
    // inner held-artifact gate in TryBuildChallengeContextFromCatalogRow.
    // The data-shape gates (twin state=5 + completedTime + 2 tags, FAR
    // tracker present + branchedTime + state≠5) are the only correctness-
    // relevant checks; inventory presence was just noise that dropped
    // legitimate "picked up + consumed" candidates.

    /// <summary>
    /// Addressing info for the in-focus catalog challenge plus its
    /// engine-side completion infrastructure (adjacent visibility twin
    /// at <c>catalog_idx + 1</c>, FAR tracker at
    /// <c>_key = adjacent_twin._key - 1</c>, and the X_2 follow-up
    /// sub-mission catalog key) needed by the Pattern B v1 recipe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Engine completion shape</b> (verified slot102 → live slot103
    /// engine-natural Hooves II completion + slot102 Shield III static
    /// reference):
    /// </para>
    /// <list type="bullet">
    ///   <item>Catalog row: NEVER touched by completion. Engine writes
    ///         it only when the user CLAIMS the reward (uses the
    ///         artifact item).</item>
    ///   <item>Adjacent twin (<c>catalog_idx + 1</c>, key
    ///         <c>0xFFFFxxxx</c>): set to
    ///         <c>state=5 + _completedTime + tags=[base, visible]</c>
    ///         when the user PICKS UP the artifact (visibility marker).
    ///         Stays at that state through challenge completion.</item>
    ///   <item>FAR tracker (key = <c>adjacent_twin.key - 1</c>, lives
    ///         far away in the list — typically idx 3600-3900 range):
    ///         exists only after artifact pickup. Starts as
    ///         <c>state=2, _branchedTime=present, _usedTagList=[base]</c>;
    ///         on completion the engine flips it to
    ///         <c>state=5 + _completedTime + _usedTagList=[base, visible]</c>.</item>
    ///   <item>NEW <c>MissionStateData</c> with <c>_key = X_2 sub-mission
    ///         catalog key</c> ("Use the sealed Abyss artifact"
    ///         follow-up): inserted at end of <c>_missionStateList</c>
    ///         on engine completion, shape mirrors a fresh FAR-tracker
    ///         template (state=2, branched=now, tags=[base]).</item>
    /// </list>
    /// <para>
    /// <b>Pattern B v1 gate</b>: only enable when the adjacent twin is
    /// in "visible" state (state=5 + _completedTime + tags ≥ 2 items).
    /// That requires the user to have already picked up the reward
    /// artifact in-game — engine-discovery has happened and we just
    /// need to mark progression complete. AND the FAR tracker must
    /// exist (catches edge cases where twin is at state=5 but FAR
    /// wasn't created).
    /// </para>
    /// </remarks>
    internal readonly record struct CurrentChallengeContext(
        uint CatalogKey,
        string InternalName,
        int MissionTopBlockIdx,
        // Path from QuestSaveData top-block to the catalog
        // MissionStateData (one step: list_field_idx + catalog_element_idx).
        int MissionListFieldIdx,
        int CatalogElementIdx,
        // Adjacent twin — only used for visibility validation in the gate.
        uint TwinKey,
        // FAR tracker (engine's progression target).
        int FarElementIdx,
        int FarStateFieldIdx,
        int FarCompletedTimeFieldIdx,
        bool FarCompletedTimeAlreadyPresent,
        int FarUsedTagListFieldIdx,
        IReadOnlyList<uint> FarUsedTagList,
        int FarBranchedTimeFieldIdx,
        // X_2 follow-up sub-mission catalog key + whether it already
        // exists in the list (skip the insert in that case).
        uint FollowUpKey,
        string FollowUpInternalName,
        bool FollowUpAlreadyExists,
        // Total element count so we can address the "insert at end"
        // position without re-fetching after the FAR clone.
        int MissionStateListCount);

    /// <summary>
    /// Walk a top-level <see cref="BlockDetails"/> down a descent path.
    /// Returns the deep block reached at the end, or <c>null</c> when the
    /// path is malformed (out-of-range index, scalar mid-path, etc.).
    /// </summary>
    private static BlockDetails? WalkPath(BlockDetails top, IReadOnlyList<PathStep> path)
    {
        var current = top;
        foreach (var step in path)
        {
            if ((int)step.FieldIndex >= current.Fields.Count)
            {
                return null;
            }
            var field = current.Fields[(int)step.FieldIndex];
            if (field.Child is { } child)
            {
                current = child;
            }
            else if (field.Elements is { } elements
                     && (int)step.ElementIndex < elements.Count)
            {
                current = elements[(int)step.ElementIndex];
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    /// <summary>
    /// Rebuild every frame in <see cref="_navStack"/> by re-walking the
    /// stored paths against the freshly-decoded top-level block. Then
    /// stamp the top frame's data into the existing FieldRowViewModels
    /// (preserves DataGrid selection / scroll).
    /// </summary>
    private void RefreshNavStack(BlockDetails freshTop)
    {
        var rebuilt = new List<NavFrame>(_navStack.Count);
        foreach (var frame in _navStack.Reverse())
        {
            switch (frame)
            {
                case BlockFrame bf:
                    var fresh = WalkPath(freshTop, bf.Path) ?? bf.Block;
                    rebuilt.Add(bf with { Block = fresh });
                    break;
                case ElementsFrame ef:
                    var listOwner = WalkPath(freshTop, ef.PathToList);
                    if (listOwner is not null
                        && (int)ef.ListFieldIndex < listOwner.Fields.Count
                        && listOwner.Fields[(int)ef.ListFieldIndex].Elements is { } els)
                    {
                        rebuilt.Add(ef with { Elements = els });
                    }
                    else
                    {
                        rebuilt.Add(ef);
                    }
                    break;
                default:
                    rebuilt.Add(frame);
                    break;
            }
        }
        _navStack.Clear();
        foreach (var f in rebuilt)
        {
            _navStack.Push(f);
        }

        // Stamp fresh field values onto the existing FieldRowViewModels so
        // the DataGrid doesn't lose scroll position / selection.
        if (_navStack.Count > 0 && _navStack.Peek() is BlockFrame top)
        {
            for (var i = 0; i < _allFields.Count && i < top.Block.Fields.Count; i++)
            {
                _allFields[i].ApplyCommittedValue(top.Block.Fields[i]);
            }
        }
        // Block-action visibility may have changed (e.g. _state on the
        // current MissionStateData was just promoted to 5).
        NotifyMarkChallengeStateChanged();
    }

    /// <summary>
    /// Fill <c>_stackCount</c> to <c>max_stack_count</c> for either a
    /// single ItemSaveData row or every item inside a container row.
    /// Confirmation is mandatory: even single-item fills mutate the
    /// save and the user explicitly asked for a Yes/No gate. Items
    /// already at max are skipped (no-op write).
    /// </summary>
    [RelayCommand]
    private async Task BulkFillItemListMaxStackAsync(ElementRowViewModel? row)
    {
        BulkOpStatus = null;
        if (row is null
            || !row.IsBulkFillCandidate
            || _loadedPath is null
            || SelectedBlock is not { } topBlock
            || _navStack.Count == 0
            || _navStack.Peek() is not ElementsFrame parent)
        {
            return;
        }

        // Find this row's element index inside the parent ElementsFrame.
        // We need it to build the path step that descends into THIS
        // row (vs whichever other one is also in the picker).
        var elementIdx = -1;
        for (var i = 0; i < parent.Elements.Count; i++)
        {
            if (ReferenceEquals(parent.Elements[i], row.Block))
            {
                elementIdx = i;
                break;
            }
        }
        if (elementIdx < 0)
        {
            return;
        }

        var rowPath = ExtendPath(parent.PathToList,
                                  new PathStep(parent.ListFieldIndex, (uint)elementIdx));

        // Single-item vs container case. The single case is just the
        // container case applied to a one-element synthetic "list"
        // containing the row itself, so we route through the same
        // candidate collector.
        List<StackFillCandidate> candidates;
        if (row.IsSingleFillCandidate)
        {
            // Single-item "Fill stack" is a deliberate one-row gesture —
            // fill to the true max, uncapped (capLarge: false).
            candidates = new List<StackFillCandidate>(1);
            if (TryBuildSingleCandidate(row.Block, rowPath, capLarge: false, out var c))
            {
                candidates.Add(c);
            }
        }
        else
        {
            // Per-container "Fill stacks" is a bulk path — cap huge-cap
            // items at BulkFillCap (capLarge: true).
            candidates = CollectStackFillCandidates(row.Block, rowPath, capLarge: true);
        }

        if (candidates.Count == 0)
        {
            BulkOpStatus = "Nothing to fill — already at target, or no max_stack data.";
            return;
        }

        // Confirm only for the batch (container) case. Single-item
        // fills go straight through — the user explicitly asked to
        // skip the modal for one-row clicks since it's the same gesture
        // as clicking Set-to-max in the edit panel.
        if (row.IsContainerFillCandidate)
        {
            if (ConfirmRequested is not { } ask)
            {
                return;
            }
            var msg = $"Set _stackCount for {candidates.Count} item(s) in this container?\n\n"
                      + "Items with max_stack_count > 100 fill to max (capped at 9,999,999 — "
                      + "huge-cap items like currency stop there; stacks already larger are left alone).\n"
                      + "Items with max_stack_count ≤ 100 round up to the next full stack "
                      + "(e.g. count 120, max 50 → 150). Items already at a stack-boundary are skipped.\n\n"
                      + "Tip: the single-item \"Fill stack\" button fills to the true max, uncapped.\n\n"
                      + "Reversible by reloading the save without writing.";
            var ok = await ask("Fill stacks?", msg);
            if (!ok)
            {
                BulkOpStatus = "Fill cancelled.";
                return;
            }
        }

        BulkOpStatus = $"Filling {candidates.Count} stack(s)…";
        var blockIdx = topBlock.Index;

        // One batch FFI call: the Rust side validates every op first
        // (all-or-nothing), patches them all in input order, then
        // re-decodes once at the end. Replaces what used to be N
        // single-op SetScalarField calls (one full re-decode per
        // mutation, ~5 s for the 168-op container path); the batch
        // amortises to O(N + block_count). Still on Task.Run to
        // keep the UI thread free during the FFI call.
        var ops = new List<ScalarBatchOp>(candidates.Count);
        foreach (var c in candidates)
        {
            ops.Add(new ScalarBatchOp(blockIdx, c.Path, c.FieldIndex, c.Bytes));
        }

        var (applied, firstError) = await Task.Run<(int, CrimsonSaveException?)>(() =>
        {
            try
            {
                loader.SetScalarFieldsBatch(ops);
                return (ops.Count, null);
            }
            catch (CrimsonSaveException ex)
            {
                // Atomicity contract: on failure no op was applied,
                // so the count reported back is 0 — surfacing the
                // batch's all-or-nothing semantics in the status
                // text the user sees.
                return (0, ex);
            }
        });

        // Refresh nav stack so the picker's KeyText / ResolvedName
        // and the field-detail view (if open) reflect the new state.
        try
        {
            var freshTop = loader.LoadBlockDetails(_loadedPath, blockIdx);
            RefreshNavStack(freshTop);
            RebuildFromTop();
        }
        catch (CrimsonSaveException)
        {
            // Stale view is better than a crash — the next nav will
            // re-fetch cleanly.
        }

        if (applied > 0)
        {
            IsDirty = true;
            Journal.Log("Bulk fill",
                row.IsSingleFillCandidate
                    ? $"Filled stack of {row.ResolvedName}"
                    : $"Filled {applied} stack(s) in {row.ResolvedName}");
            OnPropertyChanged(nameof(WindowTitle));
        }
        BulkOpStatus = firstError is null
            ? $"Filled {applied} stack(s)."
            : $"Failed after {applied}/{candidates.Count}: {firstError.Message}";
    }

    /// <summary>
    /// Remove a single element from the currently-displayed
    /// <see cref="ElementsFrame"/>. Wired to the "Remove" per-row
    /// button on the elements DataGrid; confirms via the same modal
    /// dialog hook as "Fill stacks", then drives
    /// <see cref="ISaveLoader.ListRemoveElement"/> on the parent list.
    /// </summary>
    /// <remarks>
    /// Locates the element's index inside the parent's
    /// <see cref="ElementsFrame.Elements"/> the same way
    /// <see cref="BulkFillItemListMaxStackAsync"/> does. The Rust
    /// re-emit shrinks the list; the nav-stack refresh after the FFI
    /// call pulls the new <c>Elements</c> back into the frame so the
    /// grid re-renders without the removed row.
    /// </remarks>
    [RelayCommand]
    private async Task RemoveElementAsync(ElementRowViewModel? row)
    {
        BulkOpStatus = null;
        if (row is null
            || _loadedPath is null
            || SelectedBlock is not { } topBlock
            || _navStack.Count == 0
            || _navStack.Peek() is not ElementsFrame parent)
        {
            return;
        }

        var elementIdx = -1;
        for (var i = 0; i < parent.Elements.Count; i++)
        {
            if (ReferenceEquals(parent.Elements[i], row.Block))
            {
                elementIdx = i;
                break;
            }
        }
        if (elementIdx < 0)
        {
            return;
        }

        if (ConfirmRequested is not { } ask)
        {
            return;
        }
        var displayName = string.IsNullOrEmpty(row.ResolvedName) ? row.ClassName : row.ResolvedName;
        var msg = $"Remove this element from the list?\n\n"
                  + $"Element: [{elementIdx}] {displayName}\n"
                  + $"List size: {parent.Elements.Count} → {parent.Elements.Count - 1}\n\n"
                  + "Reversible by reloading the save without writing.";
        var ok = await ask("Remove element?", msg);
        if (!ok)
        {
            BulkOpStatus = "Remove cancelled.";
            return;
        }

        BulkOpStatus = "Removing element…";
        var blockIdx = topBlock.Index;
        var pathArr = parent.PathToList is PathStep[] a ? a : parent.PathToList.ToArray();
        var listFieldIdxRemove = (int)parent.ListFieldIndex;
        CrimsonSaveException? error = null;
        await Task.Run(() =>
        {
            try
            {
                loader.ListRemoveElement(blockIdx, pathArr, listFieldIdxRemove, elementIdx);
            }
            catch (CrimsonSaveException ex)
            {
                error = ex;
            }
        });

        try
        {
            var freshTop = loader.LoadBlockDetails(_loadedPath, blockIdx);
            RefreshNavStack(freshTop);
            RebuildFromTop();
        }
        catch (CrimsonSaveException)
        {
            // Stale view is better than a crash — next nav re-fetches.
        }

        if (error is null)
        {
            IsDirty = true;
            Journal.Log("Remove element",
                $"Removed {displayName} from {topBlock.ClassName} (index [{elementIdx}])");
            OnPropertyChanged(nameof(WindowTitle));
            BulkOpStatus = $"Removed element [{elementIdx}].";
        }
        else
        {
            BulkOpStatus = $"Remove failed: {error.Message}";
        }
    }

    /// <summary>
    /// Add a new <c>ItemSaveData</c> element to the currently-displayed
    /// inventory list with <paramref name="itemKey"/> as its
    /// <c>_itemKey</c>. Implemented as clone-and-patch on the existing
    /// list's first element so the new entry inherits a known-valid
    /// presence mask + field shape from the engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires the user to be navigated into an <c>object_list</c>
    /// frame whose first element is class <c>ItemSaveData</c> (an
    /// <c>_itemList</c> inside an <c>InventoryElementSaveData</c>).
    /// Empty bags fall back to <c>MakeEmptyElementBytes</c> +
    /// <c>ListInsertElement</c> with the first element's class index
    /// inferred from the schema — but no live save has an empty
    /// inventory bag, so that path is untested in practice.
    /// </para>
    /// <para>
    /// The clone is byte-identical to the source; we patch
    /// <c>_itemKey</c> to the picker-selected value. <c>_stackCount</c>
    /// is left at the source's value (typically 1 for cloned-from-stack-
    /// of-1 sources); the user can adjust via the field-edit panel or
    /// the existing Set-to-max button. <c>_slotNo</c> is also kept
    /// from the clone source — the game may renumber slots on its
    /// next save tick.
    /// </para>
    /// </remarks>
    public async Task AddItemToCurrentListAsync(uint itemKey)
    {
        BulkOpStatus = null;
        if (_loadedPath is null
            || SelectedBlock is not { } topBlock
            || _navStack.Count == 0
            || _navStack.Peek() is not ElementsFrame parent
            || parent.Elements.Count == 0)
        {
            BulkOpStatus = LookupUiResourceString("AddItemStatusOpenBag")
                ?? "Open a bag first (drill into _inventorylist[N]._itemList).";
            return;
        }
        // Only handle ItemSaveData lists — refusing other element classes
        // keeps us from inserting an ItemSaveData-shaped row into a
        // non-item list and corrupting the save.
        var sourceClass = parent.Elements[0].ClassName;
        if (!string.Equals(sourceClass, "ItemSaveData", StringComparison.Ordinal))
        {
            BulkOpStatus = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LookupUiResourceString("AddItemStatusWrongList")
                    ?? "This list holds {0}, not ItemSaveData. Add unsupported here.",
                sourceClass);
            return;
        }
        // Note: we deliberately do NOT refuse "+ Bag" into engine-managed
        // containers (Quest Artifacts, Kuku Pot, …). Cloning entries
        // there produces an orphan whose in-game "claim reward" /
        // "consume" path will silently fail, but the user has accepted
        // that consequence. Edit at your own risk — the editor's job
        // is to expose the surface, not policy.
        // Pick the clone template: prefer the row the user has
        // currently SELECTED in the elements DataGrid (so they can
        // point at a same-shape donor, e.g. an existing food row when
        // adding beer). Falling back to element [0] is risky — Gold
        // Bar / currency rows have a different mask from consumables,
        // and the game's load-time validation crashes on mask shapes
        // that don't match the item's iteminfo profile.
        int sourceIndex = 0;
        BlockDetails sourceElement = parent.Elements[0];
        string cloneSourceLabel = LookupUiResourceString("AddItemSourceFirst") ?? "[0]: first element";
        if (SelectedElement is { Block: var selBlock and not null } selRow)
        {
            for (var i = 0; i < parent.Elements.Count; i++)
            {
                if (ReferenceEquals(parent.Elements[i], selBlock))
                {
                    sourceIndex = i;
                    sourceElement = selBlock;
                    var name = string.IsNullOrEmpty(selRow.ResolvedName) ? selRow.KeyText : selRow.ResolvedName;
                    cloneSourceLabel = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        LookupUiResourceString("AddItemSourceSelected") ?? "selected [{0}]: {1}",
                        i, name);
                    break;
                }
            }
        }
        // Resolve field indices on the chosen source element. Schema
        // indices are stable per class so the same indices apply to
        // every element in the list (including the clone).
        if (!TryFindItemSaveDataFieldIndices(
                sourceElement,
                out var idxItemKey,
                out var idxStackCount,
                out var idxSlotNo,
                out var idxItemNo))
        {
            BulkOpStatus = LookupUiResourceString("AddItemStatusMissingFields")
                ?? "Source element missing one of "
                   + "_itemKey / _stackCount / _slotNo / _itemNo — bag layout "
                   + "incompatible with the clone-and-patch strategy.";
            return;
        }
        // _transferredItemKey is an engine-internal handle whose high
        // 16 bits encode the item's identity (verified empirically:
        // beer's transferredItemKey = 0x55F7_xxxx where 0x55F7 = 22007).
        // Cloning Water and patching only _itemKey leaves the
        // transferred value pointing at Water — the game's cross-check
        // on load fails and the save crashes. Find the field so we can
        // apply a delta patch alongside the four standard ones.
        var idxTransferred = -1;
        ulong sourceTransferred = 0;
        ulong sourceItemKeyValue = 0;
        for (var i = 0; i < sourceElement.Fields.Count; i++)
        {
            var f = sourceElement.Fields[i];
            if (f.Name == "_transferredItemKey" && f.Present
                && TryParseScalarUInt(f.Value, out var v))
            {
                idxTransferred = i;
                sourceTransferred = v;
            }
            else if (f.Name == "_itemKey" && f.Present
                     && TryParseScalarUInt(f.Value, out var ik))
            {
                sourceItemKeyValue = ik;
            }
        }
        // _isNewMark: bool flag the engine sets on freshly-spawned
        // items. Some items have it absent (currency, equipment with
        // a "received from quest" provenance). For a brand-new item,
        // present + true is the safe shape — matches what slot101's
        // engine-created beer looks like.
        var idxIsNewMark = -1;
        for (var i = 0; i < sourceElement.Fields.Count; i++)
        {
            if (sourceElement.Fields[i].Name == "_isNewMark")
            {
                idxIsNewMark = i;
                break;
            }
        }

        // Compute fresh values that won't collide with anything else in
        // the list. Cloning blindly inherits the source's _slotNo /
        // _itemNo / _stackCount, which crashes the game on load:
        //   - duplicate _slotNo (two items at the same UI grid cell)
        //   - duplicate _itemNo (game uses this as a persistent ID)
        //   - _stackCount > the new item's max_stack
        var (existingMaxSlot, existingMaxItemNo) =
            ScanItemListMaxes(parent.Elements);
        var newSlotNo = (ushort)Math.Min(existingMaxSlot + 1, ushort.MaxValue);
        var newItemNo = existingMaxItemNo + 1;
        // _stackCount defaults to 1 — guaranteed-safe across every
        // iteminfo entry. The user can bump it via the edit panel's
        // Set-to-max button or by typing a new value; those paths
        // already validate against iteminfo's max_stack.
        const ulong newStackCount = 1UL;

        BulkOpStatus = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            LookupUiResourceString("AddItemStatusProgress") ?? "Adding item {0} (template: {1})…",
            itemKey, cloneSourceLabel);
        var blockIdx = topBlock.Index;
        var listPath = parent.PathToList is PathStep[] a ? a : parent.PathToList.ToArray();
        var listFieldIdx = (int)parent.ListFieldIndex;
        // Insert at index 0; the game's UI re-sorts on next save tick
        // by _slotNo anyway, so the array position doesn't matter for
        // correctness.
        const int dstIdx = 0;
        // When the source isn't [0], cloning to dst=0 shifts the source
        // down by one (its old index becomes sourceIndex+1). We still
        // patch at clonePath=ExtendPath(listPath, [listFieldIdx, dstIdx])
        // since that's where the clone now lives.
        var clonePath = ExtendPath(listPath, new PathStep((uint)listFieldIdx, (uint)dstIdx));

        // Pre-compute LE byte buffers for the batched patch. One batch
        // FFI call mutates every scalar atomically (all-or-nothing on
        // validation), with a single post-batch re-decode.
        var itemKeyBytes = BitConverter.GetBytes(itemKey);
        var stackCountBytes = BitConverter.GetBytes(newStackCount);
        var slotNoBytes = BitConverter.GetBytes(newSlotNo);
        var itemNoBytes = BitConverter.GetBytes(newItemNo);
        var batchOps = new List<ScalarBatchOp>(6)
        {
            new(blockIdx, clonePath, idxItemKey,    itemKeyBytes),
            new(blockIdx, clonePath, idxStackCount, stackCountBytes),
            new(blockIdx, clonePath, idxSlotNo,     slotNoBytes),
            new(blockIdx, clonePath, idxItemNo,     itemNoBytes),
        };
        // _transferredItemKey encoding (verified empirically against
        // every ItemSaveData in the user's restored slot100):
        //
        //   _transferredItemKey = ((itemKey & 0xFFFF) << 16) | 0x0101
        //
        //   Worked examples:
        //     itemKey 22007    (Beer)             → 0x55F7_0101 = 1442251009
        //     itemKey 1000677  (Investigative Rpt)→ 0x44E5_0101 = 1155858689
        //     itemKey 950017   (some equipment)   → 0x7F01_0101 = 2130772225
        //
        // The high 16 bits are `itemKey % 65536` (i.e. the low 16 bits
        // of itemKey, even when itemKey itself overflows u16). The low
        // 16 bits are a constant `0x0101` for every item observed —
        // not a "newly-spawned" flag as I'd initially guessed; the
        // distinction was a math error on my part decoding the bytes.
        //
        // Same formula for all itemKeys, no conditional branch.
        const ushort TransferredLowConstant = 0x0101;
        if (idxTransferred >= 0)
        {
            var newTransferred = (uint)(((itemKey & 0xFFFFu) << 16) | TransferredLowConstant);
            var transferredBytes = BitConverter.GetBytes(newTransferred);
            batchOps.Add(new ScalarBatchOp(blockIdx, clonePath, idxTransferred, transferredBytes));
        }
        // `sourceTransferred` and `sourceItemKeyValue` were captured
        // for an earlier delta-shift approach that's no longer used;
        // keeping the destructure for diagnostic value if a future
        // patch needs them again.
        _ = sourceTransferred;
        _ = sourceItemKeyValue;
        // Mark the new item as "fresh" so the in-game inventory UI shows
        // the (NEW) indicator. When the source had _isNewMark present,
        // we just overwrite the byte; when it was absent, the simpler
        // path is the standalone SetScalarFieldPresent call below.
        var markIsNewMarkPresent = idxIsNewMark >= 0 && !sourceElement.Fields[idxIsNewMark].Present;

        CrimsonSaveException? error = null;
        await Task.Run(() =>
        {
            try
            {
                loader.ListCloneElement(blockIdx, listPath, listFieldIdx, sourceIndex, dstIdx);
                loader.SetScalarFieldsBatch(batchOps);
                if (idxIsNewMark >= 0)
                {
                    var newMarkByte = new byte[] { 0x01 };
                    if (markIsNewMarkPresent)
                    {
                        // Source had it absent — set present + true.
                        loader.SetScalarFieldPresent(blockIdx, clonePath, idxIsNewMark,
                            makePresent: true, newMarkByte);
                    }
                    else
                    {
                        // Source had it present — just overwrite the byte.
                        loader.SetScalarField(blockIdx, clonePath, idxIsNewMark, newMarkByte);
                    }
                }
            }
            catch (CrimsonSaveException ex)
            {
                error = ex;
            }
        });

        try
        {
            var freshTop = loader.LoadBlockDetails(_loadedPath, blockIdx);
            RefreshNavStack(freshTop);
            RebuildFromTop();
        }
        catch (CrimsonSaveException)
        {
            // Stale view is better than a crash.
        }

        if (error is null)
        {
            IsDirty = true;
            var addedName = localization.LookupItemName(itemKey, LocalizationProvider.DefaultLanguage)
                            ?? localization.ItemInfoStringKey(itemKey)
                            ?? itemKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Journal.Log("Add item",
                $"Added {addedName} (ItemKey {itemKey}, qty {newStackCount}) to inventory");
            OnPropertyChanged(nameof(WindowTitle));
            BulkOpStatus = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LookupUiResourceString("AddItemStatusSuccess")
                    ?? "Added item {0} from {1} (qty {2}, slot {3}, itemNo {4}).",
                itemKey, cloneSourceLabel, newStackCount, newSlotNo, newItemNo);
        }
        else
        {
            BulkOpStatus = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LookupUiResourceString("AddItemStatusFailed") ?? "Add failed: {0}",
                error.Message);
        }
    }

    /// <summary>
    /// Grant one item into a specific inventory container addressed by its
    /// <c>_inventoryKey</c> (e.g. 5 = Quest Artifacts), independent of the
    /// current navigation. Clones an existing <c>ItemSaveData</c> template
    /// in that container's <c>_itemList</c> and patches it to a fresh,
    /// collision-free entry — the same recipe as
    /// <see cref="AddItemToCurrentListAsync"/> (incl. the
    /// <c>_transferredItemKey</c> engine cross-check) but container-targeted.
    /// Used by the Mount-Unlock dialog to drop a Sigil of Solidarity into
    /// Quest Artifacts. Returns <c>(ok, message)</c>; sets
    /// <see cref="IsDirty"/> + logs on success.
    /// </summary>
    internal async Task<(bool Ok, string Message)> GrantItemToContainerAsync(
        uint inventoryKey, uint itemKey)
    {
        if (_loadedPath is null || Summary is not { Blocks: { } blocks })
        {
            return (false, "No save loaded.");
        }
        var invBlock = FindFirstBlockByClassName(blocks, "InventorySaveData");
        if (invBlock is null)
        {
            return (false, "No InventorySaveData block in this save.");
        }
        BlockDetails invDetails;
        try
        {
            invDetails = loader.LoadBlockDetails(_loadedPath, invBlock.Index);
        }
        catch (CrimsonSaveException ex)
        {
            return (false, $"Could not read inventory: {ex.Message}");
        }

        var invListField = FindFieldByName(invDetails, "_inventorylist");
        if (invListField?.Elements is not { Count: > 0 } containers)
        {
            return (false, "Inventory has no _inventorylist containers.");
        }
        // Find the container element whose _inventoryKey matches.
        var containerIdx = -1;
        for (var i = 0; i < containers.Count && containerIdx < 0; i++)
        {
            foreach (var f in containers[i].Fields)
            {
                if (string.Equals(f.Name, "_inventoryKey", StringComparison.Ordinal)
                    && TryParseScalarUInt(f.Value, out var ik) && ik == inventoryKey)
                {
                    containerIdx = i;
                    break;
                }
            }
        }
        if (containerIdx < 0)
        {
            return (false,
                $"No inventory container with _inventoryKey={inventoryKey} in this save "
                + "(it may be virtual / not yet materialised).");
        }
        var container = containers[containerIdx];
        var itemListField = FindFieldByName(container, "_itemList");
        if (itemListField is null
            || !string.Equals(itemListField.Kind, "object_list", StringComparison.Ordinal)
            || itemListField.Elements is not { Count: > 0 } items)
        {
            return (false,
                $"Container _inventoryKey={inventoryKey} has no item to use as a clone template "
                + "(empty container). Pick up any item into it in-game first, then retry.");
        }

        // Resolve field indices off the template (item[0]) — schema is
        // uniform across the list, so the same indices apply to the clone.
        var template = items[0];
        if (!TryFindItemSaveDataFieldIndices(template, out var idxItemKey, out var idxStackCount,
                out var idxSlotNo, out var idxItemNo))
        {
            return (false, "Container's item template is missing "
                + "_itemKey / _stackCount / _slotNo / _itemNo.");
        }
        var idxTransferred = -1;
        var idxIsNewMark = -1;
        for (var i = 0; i < template.Fields.Count; i++)
        {
            var n = template.Fields[i].Name;
            if (n == "_transferredItemKey") idxTransferred = i;
            else if (n == "_isNewMark") idxIsNewMark = i;
        }
        var sourceIsNewMarkPresent = idxIsNewMark >= 0 && template.Fields[idxIsNewMark].Present;

        var (maxSlot, maxItemNo) = ScanItemListMaxes(items);
        var newSlotNo = (ushort)Math.Min(maxSlot + 1, ushort.MaxValue);
        var newItemNo = maxItemNo + 1;
        const ulong newStackCount = 1UL;

        var blockIdx = invBlock.Index;
        var itemListFieldIdx = itemListField.FieldIndex;
        // Path to the container element that owns _itemList.
        var listPath = new[]
        {
            new PathStep((uint)invListField.FieldIndex, (uint)containerIdx),
        };
        const int dstIdx = 0;
        // Where the clone lands once inserted at index 0.
        var clonePath = new[]
        {
            new PathStep((uint)invListField.FieldIndex, (uint)containerIdx),
            new PathStep((uint)itemListFieldIdx, (uint)dstIdx),
        };

        var batchOps = new List<ScalarBatchOp>(6)
        {
            new(blockIdx, clonePath, idxItemKey,    BitConverter.GetBytes(itemKey)),
            new(blockIdx, clonePath, idxStackCount, BitConverter.GetBytes(newStackCount)),
            new(blockIdx, clonePath, idxSlotNo,     BitConverter.GetBytes(newSlotNo)),
            new(blockIdx, clonePath, idxItemNo,     BitConverter.GetBytes(newItemNo)),
        };
        // _transferredItemKey = ((itemKey & 0xFFFF) << 16) | 0x0101 — the
        // engine cross-check field; omitting it corrupts the entry (see
        // AddItemToCurrentListAsync for the empirical derivation).
        if (idxTransferred >= 0)
        {
            var newTransferred = (uint)(((itemKey & 0xFFFFu) << 16) | 0x0101u);
            batchOps.Add(new ScalarBatchOp(
                blockIdx, clonePath, idxTransferred, BitConverter.GetBytes(newTransferred)));
        }

        CrimsonSaveException? error = null;
        await Task.Run(() =>
        {
            try
            {
                loader.ListCloneElement(blockIdx, listPath, itemListFieldIdx,
                    sourceIndex: 0, destinationIndex: dstIdx);
                loader.SetScalarFieldsBatch(batchOps);
                if (idxIsNewMark >= 0)
                {
                    var markByte = new byte[] { 0x01 };
                    if (!sourceIsNewMarkPresent)
                    {
                        loader.SetScalarFieldPresent(blockIdx, clonePath, idxIsNewMark,
                            makePresent: true, markByte);
                    }
                    else
                    {
                        loader.SetScalarField(blockIdx, clonePath, idxIsNewMark, markByte);
                    }
                }
            }
            catch (CrimsonSaveException ex)
            {
                error = ex;
            }
        });

        // Refresh the navigation view if it's currently showing this block.
        try
        {
            var freshTop = loader.LoadBlockDetails(_loadedPath, blockIdx);
            RefreshNavStack(freshTop);
            RebuildFromTop();
        }
        catch (CrimsonSaveException)
        {
            // Stale view is better than a crash.
        }

        if (error is not null)
        {
            return (false, $"Grant failed: {error.Message} (code {error.ErrorCode}).");
        }
        IsDirty = true;
        var name = localization.LookupItemName(itemKey, LocalizationProvider.DefaultLanguage)
                   ?? localization.ItemInfoStringKey(itemKey)
                   ?? itemKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Journal.Log("Mount unlock",
            $"Granted {name} (ItemKey {itemKey}) into container _inventoryKey={inventoryKey}");
        OnPropertyChanged(nameof(WindowTitle));
        return (true,
            $"Granted {name} (slot {newSlotNo}, itemNo {newItemNo}).");
    }

    /// <summary>
    /// Apply the unlock for one <see cref="MountEntry"/>, dispatching on its
    /// <see cref="MountEntry.Kind"/>: sigil mounts get the sigil item granted
    /// into Quest Artifacts (use in-game to finish); the dragon gets its real
    /// element transplanted + knowledge injected. Returns <c>(ok, message)</c>.
    /// </summary>
    internal async Task<(bool Ok, string Message)> UnlockMountAsync(MountEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        switch (entry.Kind)
        {
            case MountUnlockKind.SigilGrant:
            {
                var (ok, msg) = await GrantItemToContainerAsync(
                    MountCatalog.QuestArtifactsInventoryKey, entry.SigilItemKey);
                if (!ok)
                {
                    return (false, msg);
                }
                return (true, msg
                    + " Load the game and USE the sigil from Quest Artifacts to finish the unlock.");
            }
            case MountUnlockKind.DragonTransplant:
                return await UnlockDragonAsync(entry);
            default:
                return (false, $"Unsupported unlock kind: {entry.Kind}.");
        }
    }

    /// <summary>
    /// Unlock the dragon: transplant its real <c>_mercenaryDataList</c>
    /// element from the embedded donor save (a charKey swap on a generic
    /// clone CTDs — the element content must match the charKey), then inject
    /// its identity / summon knowledge. The donor is extracted to a temp file
    /// because the save loader is file-path only. Post-transplant the element
    /// is re-numbered (fresh <c>_mercenaryNo</c>) and de-flagged as main so it
    /// doesn't displace the player's active mount.
    /// </summary>
    private async Task<(bool Ok, string Message)> UnlockDragonAsync(MountEntry entry)
    {
        if (_loadedPath is null || Summary is not { Blocks: { } blocks })
        {
            return (false, "No save loaded.");
        }
        var tgt = LoadMercList(loader, _loadedPath, blocks);
        if (tgt is null)
        {
            return (false, "No MercenaryClanSaveData._mercenaryDataList in this save.");
        }
        // If the dragon element is already present (e.g. an earlier run added
        // the element but with an incomplete knowledge set), skip the
        // transplant and fall through to the knowledge inject below — this
        // makes the unlock idempotent and lets a half-done run be repaired.
        var dragonAlreadyPresent = false;
        foreach (var el in tgt.Value.Elements)
        {
            foreach (var f in el.Fields)
            {
                if (string.Equals(f.Name, "_characterKey", StringComparison.Ordinal)
                    && TryParseScalarUInt(f.Value, out var ck)
                    && ck == MountCatalog.DragonCharacterKey)
                {
                    dragonAlreadyPresent = true;
                    break;
                }
            }
            if (dragonAlreadyPresent)
            {
                break;
            }
        }

        if (!dragonAlreadyPresent)
        {
            var graftError = await InsertDragonElementAsync(tgt.Value);
            if (graftError is not null)
            {
                return (false, graftError);
            }
        }

        // Inject the dragon's identity / summon knowledge (the proven 187-key
        // "no-quests" set), filtered to what's not already present. A
        // knowledge failure here is non-fatal — the element already landed —
        // so we report it as a warning.
        var kApplied = 0;
        string knowledgeNote;
        var ctx = ResolveKnowledgeList(blocks, out var kErr);
        if (ctx is null)
        {
            knowledgeNote = $" Knowledge inject skipped: {kErr}";
        }
        else
        {
            var toAdd = MountCatalog.DragonKnowledgeKeys
                .Where(k => !ctx.ExistingKeys.Contains(k))
                .ToList();
            if (toAdd.Count == 0)
            {
                knowledgeNote = " Knowledge already present.";
            }
            else
            {
                var (applied, injectErr, _) = await ApplyKnowledgeInjectAsync(ctx, toAdd);
                kApplied = applied;
                knowledgeNote = injectErr is null
                    ? $" + {applied} knowledge key(s)."
                    : $" Knowledge inject failed: {injectErr.Message}.";
            }
        }

        // Fill the grafted/existing dragon's HP to full (the donor was
        // captured mid-fight at 1038/2500). Runs on both the fresh and the
        // already-present paths so a half-done earlier run gets healed too.
        var (hpChanged, hpNote) = await FillDragonHpAsync();

        RefreshSelectedBlockSilently();

        // Only dirty the save if something actually changed (a re-run with the
        // element + all 187 keys + full HP already in place is a no-op).
        var changed = !dragonAlreadyPresent || kApplied > 0 || hpChanged;
        if (changed)
        {
            IsDirty = true;
            Journal.Log("Mount unlock", dragonAlreadyPresent
                ? $"Dragon already present — injected {kApplied} knowledge key(s){hpNote}"
                : $"Transplanted dragon (charKey {MountCatalog.DragonCharacterKey}) "
                    + $"+ {kApplied} knowledge key(s){hpNote}");
            OnPropertyChanged(nameof(WindowTitle));
        }

        if (dragonAlreadyPresent && kApplied == 0 && !hpChanged)
        {
            return (true, "Dragon already fully unlocked (element + all knowledge + full HP). "
                + "If it still won't summon, the issue is outside the save.");
        }
        return (true,
            (dragonAlreadyPresent
                ? "Dragon element was already present — injected the missing knowledge."
                : "Dragon unlocked: real element transplanted.")
            + knowledgeNote + hpNote + " Load in-game to summon.");
    }

    /// <summary>
    /// Fill the (already-present) dragon's <c>_currentHp</c> to
    /// <see cref="MountCatalog.DragonFullHp"/>. The field is a packed TStat —
    /// <c>[01 00 01 01 01][u16 current][00]</c> for the donor's dragon — so we
    /// overwrite only the inner current-HP u16 and leave the rest of the 8
    /// bytes untouched, and ONLY when the bytes match that exact known shape
    /// (so an unexpected layout is never corrupted). Returns
    /// <c>(changed, note)</c>: <c>changed</c> is true only when HP was actually
    /// raised; <c>note</c> is a short status fragment for the result message.
    /// </summary>
    private async Task<(bool Changed, string Note)> FillDragonHpAsync()
    {
        if (_loadedPath is null || Summary is not { Blocks: { } blocks })
        {
            return (false, string.Empty);
        }
        var merc = LoadMercList(loader, _loadedPath, blocks);
        if (merc is null)
        {
            return (false, string.Empty);
        }

        // Find the dragon element + its _currentHp field index + raw value.
        var dragonIdx = -1;
        var hpFieldIdx = -1;
        ulong hpRaw = 0;
        for (var i = 0; i < merc.Value.Elements.Count && dragonIdx < 0; i++)
        {
            var el = merc.Value.Elements[i];
            var isDragon = false;
            var fi = -1;
            ulong raw = 0;
            foreach (var f in el.Fields)
            {
                if (string.Equals(f.Name, "_characterKey", StringComparison.Ordinal)
                    && TryParseScalarUInt(f.Value, out var ck)
                    && ck == MountCatalog.DragonCharacterKey)
                {
                    isDragon = true;
                }
                else if (string.Equals(f.Name, "_currentHp", StringComparison.Ordinal))
                {
                    fi = f.FieldIndex;
                    if (TryParseScalarUInt(f.Value, out var parsedHp))
                    {
                        raw = parsedHp;
                    }
                }
            }
            if (isDragon)
            {
                dragonIdx = i;
                hpFieldIdx = fi;
                hpRaw = raw;
            }
        }
        if (dragonIdx < 0 || hpFieldIdx < 0)
        {
            return (false, string.Empty);
        }

        var hp = BitConverter.GetBytes(hpRaw); // 8 LE bytes
        // Guard: only the known donor-dragon TStat shape. byte[5..7] = the
        // current-HP u16; bytes 0..5 are the marker and byte 7 is 0.
        if (hp.Length != 8 || hp[0] != 0x01 || hp[1] != 0x00 || hp[2] != 0x01
            || hp[3] != 0x01 || hp[4] != 0x01 || hp[7] != 0x00)
        {
            return (false, string.Empty);
        }
        var current = (ushort)(hp[5] | (hp[6] << 8));
        if (current >= MountCatalog.DragonFullHp)
        {
            return (false, " HP already full.");
        }

        var full = BitConverter.GetBytes(MountCatalog.DragonFullHp);
        hp[5] = full[0];
        hp[6] = full[1];

        var path = new[] { new PathStep((uint)merc.Value.ListFieldIndex, (uint)dragonIdx) };
        var blockIdx = merc.Value.BlockIndex;
        var bytes = hp;
        CrimsonSaveException? error = null;
        await Task.Run(() =>
        {
            try { loader.SetScalarField(blockIdx, path, hpFieldIdx, bytes); }
            catch (CrimsonSaveException ex) { error = ex; }
        });
        return error is not null
            ? (false, $" HP fill failed: {error.Message}.")
            : (true, $" HP filled to {MountCatalog.DragonFullHp}.");
    }

    /// <summary>
    /// Insert the dragon's real <c>_mercenaryDataList</c> element (captured as
    /// <see cref="MountCatalog.DragonElementHex"/>) into the loaded save (a
    /// charKey swap on a generic clone CTDs — the element content must match
    /// the charKey). The captured bytes carry the source save's schema
    /// type-indices, so we remap them to THIS save's indices by class name
    /// (read from the save's own merc elements) — the same remap
    /// <c>crimson_save_transplant_list_element</c> does, but for a byte blob,
    /// so no whole-save donor embed is needed. The inserted element is
    /// re-numbered (fresh u64 <c>_mercenaryNo</c>) and de-flagged as main so
    /// it doesn't displace the player's active mount. Returns <c>null</c> on
    /// success, else an error message.
    /// </summary>
    private async Task<string?> InsertDragonElementAsync(
        (int BlockIndex, int ListFieldIndex, IReadOnlyList<BlockDetails> Elements) tgt)
    {
        if (tgt.Elements.Count == 0)
        {
            return "No existing mercenary to read this save's schema type-indices from.";
        }

        // Collect this save's type-index for each class the dragon element
        // nests, plus the _mercenaryNo / _isMainMercenary field indices —
        // both by walking the save's own merc elements (same schema).
        var classIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var mercNoFieldIdx = -1;
        var isMainFieldIdx = -1;
        foreach (var el in tgt.Elements)
        {
            CollectClassIndices(el, classIndices);
            foreach (var f in el.Fields)
            {
                if (mercNoFieldIdx < 0
                    && string.Equals(f.Name, "_mercenaryNo", StringComparison.Ordinal))
                {
                    mercNoFieldIdx = f.FieldIndex;
                }
                else if (isMainFieldIdx < 0
                    && string.Equals(f.Name, "_isMainMercenary", StringComparison.Ordinal))
                {
                    isMainFieldIdx = f.FieldIndex;
                }
            }
            if (mercNoFieldIdx >= 0 && isMainFieldIdx >= 0
                && Array.TrueForAll(MountCatalog.DragonElementTypeIndexFixups,
                    fx => classIndices.ContainsKey(fx.ClassName)))
            {
                break;
            }
        }

        // Remap the captured element's type-indices to this save.
        var bytes = Convert.FromHexString(MountCatalog.DragonElementHex);
        foreach (var (offset, className) in MountCatalog.DragonElementTypeIndexFixups)
        {
            if (!classIndices.TryGetValue(className, out var targetIdx))
            {
                return $"This save has no '{className}' type to remap the dragon element onto.";
            }
            if (targetIdx > ushort.MaxValue || offset + 2 > bytes.Length)
            {
                return "Dragon element remap offset out of range (schema drift?).";
            }
            bytes[offset] = (byte)(targetIdx & 0xFF);
            bytes[offset + 1] = (byte)((targetIdx >> 8) & 0xFF);
        }

        // Append at the tail; pick a collision-free _mercenaryNo (u64).
        var insertAt = tgt.Elements.Count;
        ulong maxMercNo = 0;
        foreach (var el in tgt.Elements)
        {
            foreach (var f in el.Fields)
            {
                if (string.Equals(f.Name, "_mercenaryNo", StringComparison.Ordinal)
                    && f.Present && TryParseScalarUInt(f.Value, out var mn) && mn > maxMercNo)
                {
                    maxMercNo = mn;
                }
            }
        }
        var newMercNo = maxMercNo + 1;

        var blockIdx = tgt.BlockIndex;
        var listFieldIdx = tgt.ListFieldIndex;
        CrimsonSaveException? error = null;
        await Task.Run(() =>
        {
            try
            {
                loader.ListInsertElement(
                    blockIdx, ReadOnlySpan<PathStep>.Empty, listFieldIdx, insertAt, bytes);
                var dragonPath = new[] { new PathStep((uint)listFieldIdx, (uint)insertAt) };
                // Fresh u64 _mercenaryNo so it can't collide with an existing
                // merc/mount; the captured element's number would.
                if (mercNoFieldIdx >= 0)
                {
                    loader.SetScalarField(
                        blockIdx, dragonPath, mercNoFieldIdx, BitConverter.GetBytes(newMercNo));
                }
                // Clear _isMainMercenary so the dragon doesn't displace the
                // player's current active mount on load.
                if (isMainFieldIdx >= 0)
                {
                    loader.SetScalarField(
                        blockIdx, dragonPath, isMainFieldIdx, new byte[] { 0 });
                }
            }
            catch (CrimsonSaveException ex)
            {
                error = ex;
            }
        });

        return error is not null
            ? $"Dragon element insert failed: {error.Message} (code {error.ErrorCode})."
            : null;
    }

    /// <summary>
    /// Recursively collect <c>class name → schema type-index</c> from a
    /// decoded object tree (first occurrence wins). Used to learn THIS save's
    /// type-indices for the classes the dragon element nests, so the captured
    /// bytes can be remapped onto it.
    /// </summary>
    private static void CollectClassIndices(BlockDetails obj, Dictionary<string, int> into)
    {
        into.TryAdd(obj.ClassName, obj.ClassIndex);
        foreach (var f in obj.Fields)
        {
            if (f.Child is { } child)
            {
                CollectClassIndices(child, into);
            }
            if (f.Elements is { } elements)
            {
                foreach (var e in elements)
                {
                    CollectClassIndices(e, into);
                }
            }
        }
    }

    /// <summary>
    /// Locate <c>MercenaryClanSaveData._mercenaryDataList</c> on a loader and
    /// return its block index, list field index, and current elements. Used
    /// by the dragon transplant for both the target (this save) and source
    /// (donor) loaders. Returns <c>null</c> when the block / field is absent.
    /// </summary>
    private static (int BlockIndex, int ListFieldIndex, IReadOnlyList<BlockDetails> Elements)? LoadMercList(
        ISaveLoader ld, string savePath, IReadOnlyList<BlockSummary> blocks)
    {
        var b = FindFirstBlockByClassName(blocks, "MercenaryClanSaveData");
        if (b is null)
        {
            return null;
        }
        BlockDetails d;
        try { d = ld.LoadBlockDetails(savePath, b.Index); }
        catch (CrimsonSaveException) { return null; }
        var listField = FindFieldByName(d, "_mercenaryDataList");
        if (listField?.Elements is not { } elements)
        {
            return null;
        }
        return (b.Index, listField.FieldIndex, elements);
    }

    /// <summary>
    /// Locate field indices of <c>_itemKey</c>, <c>_stackCount</c>,
    /// <c>_slotNo</c>, and <c>_itemNo</c> on an <c>ItemSaveData</c>
    /// element. All four must be present in the source's field list
    /// (their presence bits don't have to be set — the field indices
    /// are schema-stable). Returns <c>false</c> if any is missing.
    /// </summary>
    private static bool TryFindItemSaveDataFieldIndices(
        BlockDetails source,
        out int itemKey,
        out int stackCount,
        out int slotNo,
        out int itemNo)
    {
        itemKey = -1;
        stackCount = -1;
        slotNo = -1;
        itemNo = -1;
        for (var i = 0; i < source.Fields.Count; i++)
        {
            var f = source.Fields[i];
            switch (f.Name)
            {
                case "_itemKey":    itemKey    = i; break;
                case "_stackCount": stackCount = i; break;
                case "_slotNo":     slotNo     = i; break;
                case "_itemNo":     itemNo     = i; break;
            }
        }
        return itemKey >= 0 && stackCount >= 0 && slotNo >= 0 && itemNo >= 0;
    }

    /// <summary>
    /// Walk every element in the parent list and return
    /// <c>(maxSlotNo, maxItemNo)</c>, parsing each element's
    /// pre-formatted scalar value strings. Used to pick collision-free
    /// values for a newly-cloned element. Missing / unparseable
    /// fields are treated as 0 — the worst case is a redundant +1 on
    /// the already-existing maximum.
    /// </summary>
    private static (ulong MaxSlotNo, ulong MaxItemNo) ScanItemListMaxes(
        IReadOnlyList<BlockDetails> elements)
    {
        ulong maxSlot = 0;
        ulong maxItemNo = 0;
        foreach (var el in elements)
        {
            foreach (var f in el.Fields)
            {
                if (!f.Present)
                {
                    continue;
                }
                if (f.Name == "_slotNo" && TryParseScalarUInt(f.Value, out var slot) && slot > maxSlot)
                {
                    maxSlot = slot;
                }
                else if (f.Name == "_itemNo" && TryParseScalarUInt(f.Value, out var itemNo) && itemNo > maxItemNo)
                {
                    maxItemNo = itemNo;
                }
            }
        }
        return (maxSlot, maxItemNo);
    }

    /// <summary>
    /// Parse a pre-formatted scalar value (<c>"123 &lt;u32&gt;"</c>,
    /// <c>"100 &lt;u64&gt;"</c>) as an unsigned integer. Lossy on
    /// signed / float / bytes values — returns false instead of a
    /// wrong number, so the caller can skip the field.
    /// </summary>
    private static bool TryParseScalarUInt(string formatted, out ulong value)
    {
        value = 0;
        if (!ScalarFieldEditing.TryParse(formatted, out var rawText, out var tag))
        {
            return false;
        }
        if (tag is not ("u8" or "u16" or "u32" or "u64"))
        {
            return false;
        }
        return ulong.TryParse(rawText, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out value);
    }

    /// <summary>
    /// One scalar mutation to apply: the descent path from the top
    /// block to the leaf block, the leaf field's index inside that
    /// block, and the encoded bytes to write.
    /// </summary>
    private readonly record struct StackFillCandidate(
        PathStep[] Path,
        int FieldIndex,
        byte[] Bytes);

    /// <summary>
    /// Walk every <c>ObjectList</c> field on <paramref name="container"/>
    /// (the row's InventoryElementSaveData-shaped block); for each
    /// sub-element with both an ItemKey peer and a <c>_stackCount</c>
    /// scalar, look up the iteminfo max_stack_count and produce a
    /// candidate edit. Items already at-or-above max are skipped (no
    /// point round-tripping a no-op through SetScalarField).
    /// </summary>
    private List<StackFillCandidate> CollectStackFillCandidates(
        BlockDetails container,
        IReadOnlyList<PathStep> parentPath,
        bool capLarge)
    {
        var list = new List<StackFillCandidate>();
        foreach (var listField in container.Fields)
        {
            if (listField.Elements is not { Count: > 0 } items)
            {
                continue;
            }
            for (var itemIdx = 0; itemIdx < items.Count; itemIdx++)
            {
                var itemPath = ExtendPath(parentPath,
                                          new PathStep((uint)listField.FieldIndex, (uint)itemIdx));
                if (TryBuildSingleCandidate(items[itemIdx], itemPath, capLarge, out var candidate))
                {
                    list.Add(candidate);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Build a single fill-to-max candidate from one ItemSaveData-
    /// shaped block reachable at <paramref name="itemPath"/>. Returns
    /// <c>false</c> when the block doesn't carry both <c>ItemKey</c>
    /// and <c>_stackCount</c>, when iteminfo has no max-stack entry
    /// for the key, when the current value already meets-or-exceeds
    /// max (no-op skip), or when byte encoding fails.
    /// </summary>
    private bool TryBuildSingleCandidate(
        BlockDetails item,
        IReadOnlyList<PathStep> itemPath,
        bool capLarge,
        out StackFillCandidate candidate)
    {
        candidate = default;

        // Locate _itemKey + _stackCount on this element.
        DecodedFieldRow? itemKeyField = null;
        DecodedFieldRow? stackField = null;
        foreach (var inner in item.Fields)
        {
            if (itemKeyField is null
                && inner.TypeName == "ItemKey"
                && (inner.Kind == "fixed_prefix" || inner.Kind == "fixed_suffix"))
            {
                itemKeyField = inner;
            }
            else if (stackField is null
                     && inner.Name == "_stackCount"
                     && (inner.Kind == "fixed_prefix" || inner.Kind == "fixed_suffix"))
            {
                stackField = inner;
            }
        }
        if (itemKeyField is null || stackField is null)
        {
            return false;
        }

        if (!ScalarFieldEditing.TryParse(itemKeyField.Value, out var ikRaw, out var ikTag)
            || ikTag != "u32"
            || !uint.TryParse(ikRaw, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out var itemKey))
        {
            return false;
        }
        var maxStack = localization.GetItemMaxStackCount(itemKey);
        if (maxStack is not { } maxVal || maxVal == 0)
        {
            return false;
        }

        // Parse the current count. We need it for the target calculation
        // (and to skip no-op writes).
        if (!ScalarFieldEditing.TryParse(stackField.Value, out var scRaw, out var scTag)
            || !ulong.TryParse(scRaw, System.Globalization.NumberStyles.Integer,
                               System.Globalization.CultureInfo.InvariantCulture,
                               out var current))
        {
            return false;
        }
        if (!TryComputeTargetStack(current, maxVal, capLarge, out var target))
        {
            return false;
        }

        // Encode target as bytes. ScalarFieldEditing.TryEncode is the
        // single source of truth for type-tag → bytes; reuse it here
        // so a future tweak (e.g. endianness or precision) doesn't
        // need to be mirrored in two places.
        if (!ScalarFieldEditing.TryEncode(
                scTag ?? string.Empty,
                target.ToString(System.Globalization.CultureInfo.InvariantCulture),
                out var bytes,
                out _))
        {
            return false;
        }

        candidate = new StackFillCandidate(
            itemPath is PathStep[] arr ? arr : itemPath.ToArray(),
            stackField.FieldIndex,
            bytes);
        return true;
    }

    /// <summary>
    /// Upper bound the *bulk* fill paths (Fill ALL stacks, per-container
    /// Fill stacks) clamp huge-cap items to. Large-cap items (currency,
    /// contributions, etc.) have <c>max_stack_count</c> in the tens of
    /// millions; blasting them to the true max produces absurd piles, so
    /// when <c>capLarge</c> is set we cap at this value (and leave stacks
    /// that are already bigger untouched). The deliberate single-item
    /// "Fill stack" button and the edit-panel "Set to Max" stay uncapped
    /// (they pass <c>capLarge: false</c> / don't route through here).
    /// </summary>
    private const ulong BulkFillCap = 9_999_999UL;

    /// <summary>
    /// Decide what value to write for a fill-to-max operation given
    /// the current count and the iteminfo max_stack_count. Returns
    /// false (skip the write) when the target would equal the current
    /// value or when the inputs are invalid.
    /// <para>
    /// Two regimes, based on whether max_stack_count is "small" (≤100,
    /// the threshold where partial-stack accumulation matters) or
    /// "large" (currency, contributions, etc. — fill to max and move on):
    /// </para>
    /// <list type="bullet">
    ///   <item><b>max &gt; 100</b>: target = max. Skip when current ≥ max.</item>
    ///   <item><b>max ≤ 100</b>:
    ///     <list type="bullet">
    ///       <item>current &lt; max → target = max (top up a partial single stack).</item>
    ///       <item>current is an integer multiple of max → skip (already a clean N-stack pile).</item>
    ///       <item>current &gt; max with remainder &gt; 0 → round up to the next multiple.
    ///         Example: max=50, current=120 → 120 mod 50 = 20, target = 150.</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// <para>
    /// When <paramref name="capLarge"/> is set (the bulk paths), the
    /// computed target is clamped to <see cref="BulkFillCap"/>: stacks
    /// already above the cap are left alone (return false) and any target
    /// that would exceed the cap is pulled down to it. The ≤100 round-up
    /// branch never reaches the cap, so the clamp only affects large-cap
    /// items.
    /// </para>
    /// </summary>
    internal static bool TryComputeTargetStack(ulong current, ulong max, bool capLarge, out ulong target)
    {
        target = 0;
        if (max == 0)
        {
            return false;
        }
        // Bulk paths leave anything already above the cap untouched — we
        // never want to *reduce* a stack the user (or a prior uncapped
        // fill) deliberately pushed higher.
        if (capLarge && current > BulkFillCap)
        {
            return false;
        }
        // Threshold sourced from the user's domain note: items with
        // max_stack_count ≤ 100 (most regular items: arrows, herbs,
        // ores) benefit from partial-stack round-up. Items with bigger
        // caps (Camp Funds 6+ digits, contributions at 100k, etc.) just
        // want "fill to max".
        const ulong SmallStackThreshold = 100UL;

        if (max > SmallStackThreshold)
        {
            if (current >= max)
            {
                return false;
            }
            target = max;
        }
        else if (current < max)
        {
            // max ≤ 100, partial single stack → top up to max.
            target = max;
        }
        else
        {
            var remainder = current % max;
            if (remainder == 0)
            {
                return false;
            }
            // Round current up to the next multiple of max. The add is
            // safe against overflow at this scale (max ≤ 100, current is
            // a u64 game count — sum stays well below u64::MAX).
            target = current + (max - remainder);
        }

        if (capLarge && target > BulkFillCap)
        {
            target = BulkFillCap;
        }
        // After clamping, a no-op (or backwards) write is a skip.
        if (target <= current)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Tools menu: walk every <c>InventorySaveData</c> block in the save
    /// and fill <c>_stackCount</c> to max for every <c>ItemSaveData</c>
    /// reachable through <c>_inventorylist[N]._itemList[M]</c>. One
    /// confirm gates the whole sweep — unlike the per-row "Fill stack(s)"
    /// button, the user doesn't have to drill into each container first.
    /// </summary>
    /// <remarks>
    /// Implemented as a single
    /// <see cref="ISaveLoader.SetScalarFieldsBatch"/> spanning every
    /// matched item. Per-item target rules are the same as the per-row
    /// path (see <see cref="TryComputeTargetStack"/>): items with
    /// <c>max_stack_count &gt; 100</c> fill to max; items with
    /// <c>max_stack_count ≤ 100</c> round up to the next full stack.
    /// Items already at-target are skipped (no-op), so re-running is
    /// idempotent.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSave))]
    private async Task FillAllStacksAcrossInventoriesAsync()
    {
        BulkOpStatus = null;
        if (_loadedPath is null || Summary is not { Blocks: { } blocks })
        {
            return;
        }
        if (ConfirmRequested is not { } ask)
        {
            return;
        }

        // Phase 1: walk every InventorySaveData block, collect every
        // (item, target stackCount) pair. Heavy reads → Task.Run keeps
        // the UI responsive even on 1112-block saves.
        BulkOpStatus = "Scanning inventories…";
        var savePath = _loadedPath;
        var (allOps, containerCount) = await Task.Run(() =>
        {
            var ops = new List<ScalarBatchOp>();
            var seenContainers = 0;
            foreach (var b in blocks)
            {
                if (!string.Equals(b.ClassName, "InventorySaveData", StringComparison.Ordinal))
                {
                    continue;
                }
                BlockDetails top;
                try
                {
                    top = loader.LoadBlockDetails(savePath, b.Index);
                }
                catch (CrimsonSaveException)
                {
                    continue;
                }
                for (var f = 0; f < top.Fields.Count; f++)
                {
                    var listField = top.Fields[f];
                    if (!string.Equals(listField.Name, "_inventorylist", StringComparison.Ordinal)
                        || listField.Elements is not { Count: > 0 } bags)
                    {
                        continue;
                    }
                    for (var bagIdx = 0; bagIdx < bags.Count; bagIdx++)
                    {
                        seenContainers++;
                        var bag = bags[bagIdx];
                        for (var g = 0; g < bag.Fields.Count; g++)
                        {
                            var itemListField = bag.Fields[g];
                            if (!string.Equals(itemListField.Name, "_itemList", StringComparison.Ordinal)
                                || itemListField.Elements is not { Count: > 0 } items)
                            {
                                continue;
                            }
                            for (var itemIdx = 0; itemIdx < items.Count; itemIdx++)
                            {
                                var itemPath = new[]
                                {
                                    new PathStep((uint)f, (uint)bagIdx),
                                    new PathStep((uint)g, (uint)itemIdx),
                                };
                                // Fill ALL is a bulk path — cap huge-cap
                                // items at BulkFillCap (capLarge: true).
                                if (TryBuildSingleCandidate(items[itemIdx], itemPath, capLarge: true, out var c))
                                {
                                    ops.Add(new ScalarBatchOp(b.Index, c.Path, c.FieldIndex, c.Bytes));
                                }
                            }
                        }
                    }
                }
            }
            return (ops, seenContainers);
        });

        if (allOps.Count == 0)
        {
            BulkOpStatus = $"Nothing to fill — every stack across {containerCount} container(s) "
                           + "is already at target.";
            return;
        }

        var msg = $"Fill _stackCount to max for {allOps.Count} item(s) "
                  + $"across {containerCount} container(s) (every InventorySaveData block)?\n\n"
                  + "Items with max_stack_count > 100 fill to max (capped at 9,999,999 — "
                  + "huge-cap items like currency stop there; stacks already larger are left alone).\n"
                  + "Items with max_stack_count ≤ 100 round up to the next full stack "
                  + "(e.g. count 120, max 50 → 150). Items already at a stack-boundary are skipped.\n\n"
                  + "Tip: the single-item \"Fill stack\" button fills to the true max, uncapped.\n\n"
                  + "Reversible by reloading the save without writing.";
        var ok = await ask("Fill ALL stacks across every inventory?", msg);
        if (!ok)
        {
            BulkOpStatus = "Fill cancelled.";
            return;
        }

        BulkOpStatus = $"Filling {allOps.Count} stack(s) across {containerCount} container(s)…";
        var (applied, firstError) = await Task.Run<(int, CrimsonSaveException?)>(() =>
        {
            try
            {
                loader.SetScalarFieldsBatch(allOps);
                return (allOps.Count, null);
            }
            catch (CrimsonSaveException ex)
            {
                return (0, ex);
            }
        });

        // If the user happens to have a block on top of the nav stack,
        // refresh it so any displayed _stackCount values update in place.
        RefreshSelectedBlockSilently();

        if (applied > 0)
        {
            IsDirty = true;
            Journal.Log("Bulk fill",
                $"Filled {applied} stack(s) across {containerCount} container(s) (all inventories)");
            OnPropertyChanged(nameof(WindowTitle));
        }
        BulkOpStatus = firstError is null
            ? $"Filled {applied} stack(s) across {containerCount} container(s)."
            : $"Failed after {applied}/{allOps.Count}: {firstError.Message}";
    }

    /// <summary>
    /// Name prefixes the Abyss-Gate bulk-unlock flow harvests from
    /// <c>knowledgeinfo.pabgb</c>. Covers:
    /// <list type="bullet">
    ///   <item><c>AbyssGate_*</c> — per-gate discovery flags.</item>
    ///   <item><c>Knowledge_AbyssRuins_HyperSpace_*</c> — hyperspace ruin
    ///     discovery flags.</item>
    ///   <item><c>Knowledge_LevelGimmickIcon_AbyssGate</c> /
    ///     <c>Knowledge_LevelGimmickIcon_HyperSpace</c> — map-icon header
    ///     entries that CRIMSON-DESERT-SAVE-EDITOR's 398-key pack treats as
    ///     mandatory prelude rows. Without them the map icons stay
    ///     hidden even after the per-gate keys are present.</item>
    /// </list>
    /// </summary>
    private static readonly string[] AbyssGateKnowledgePrefixes =
    [
        "AbyssGate_",
        "Knowledge_AbyssRuins_HyperSpace",
        "Knowledge_LevelGimmickIcon_AbyssGate",
        "Knowledge_LevelGimmickIcon_HyperSpace",
    ];

    /// <summary>
    /// Class name of the top-level block carrying the player's
    /// knowledge / discovery list. One per save.
    /// </summary>
    private const string KnowledgeSaveDataClass = "KnowledgeSaveData";

    /// <summary>
    /// Object-list field on <see cref="KnowledgeSaveDataClass"/> that
    /// holds the player's known-knowledge entries. Each element is a
    /// <c>KnowledgeElementSaveData</c> with scalar fields
    /// <c>_key</c> (u32), <c>_level</c> (u8),
    /// <c>_learnedFieldTime</c> (u64) and <c>_isNewMark</c> (bool).
    /// </summary>
    private const string KnowledgeListFieldName = "_list";

    /// <summary>Scalar field names on a <c>KnowledgeElementSaveData</c> element.</summary>
    private const string KnowledgeElemKeyField = "_key";
    private const string KnowledgeElemLearnedTimeField = "_learnedFieldTime";
    private const string KnowledgeElemIsNewMarkField = "_isNewMark";

    /// <summary>
    /// Tools menu: bulk-append every missing abyss-gate knowledge key
    /// into <c>KnowledgeSaveData._list</c>. Touches only the
    /// **discovery flag** layer (gates show up on the map) — the
    /// gate-state layer (whether the bridge is actually crossable in
    /// world) lives on each nested
    /// <c>FieldGimmickSaveData._initStateNameHash</c> and is handled
    /// by the per-gate Edit Abyss Gates dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors CRIMSON-DESERT-SAVE-EDITOR's "No Map Reveal Abyss Gate
    /// Unlock Only" pack flow (398 hand-curated keys) — but the
    /// keyset is harvested live from <c>knowledgeinfo.pabgb</c> via
    /// <see cref="LocalizationProvider.EnumerateKnowledgeByNamePrefix"/>
    /// so no JSON pack is vendored. The harvested set is intersected
    /// with the user's current <c>_list</c> element keys before
    /// applying; already-discovered gates are left alone.
    /// </para>
    /// <para>
    /// <b>Field shape note:</b> across slots probed from 1.05 / 1.06 /
    /// 1.07 saves, <c>KnowledgeSaveData._list</c> is always an
    /// <c>object_list&lt;KnowledgeElementSaveData&gt;</c> (~1,740
    /// elements per save), <b>not</b> a <c>dynamic_array&lt;u32&gt;</c>.
    /// Each new entry is therefore created by cloning element 0 and
    /// patching <c>_key</c>, <c>_learnedFieldTime</c> and
    /// <c>_isNewMark</c> on the clone. The shipped v1 used a u32-array
    /// setter that silently read 0 elements and would have corrupted
    /// the save on apply; this v2 replaces it entirely.
    /// </para>
    /// <para>
    /// Implementation cost: ~3 length-changing FFI calls per missing
    /// key (clone + 2 scalar patches; the bool defaults to 0). A
    /// 98-key sweep takes a few seconds end-to-end on a 1.07-era
    /// save — every length-changer triggers a body re-decode in
    /// crimson-rs.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSave))]
    private async Task UnlockAllAbyssGatesAsync()
    {
        BulkOpStatus = null;
        if (_loadedPath is null
            || Summary is not { Blocks: { } blocks }
            || ConfirmRequested is not { } ask)
        {
            return;
        }
        if (localization.KnowledgeCount == 0)
        {
            BulkOpStatus = "Nothing to do — knowledgeinfo.pabgb not loaded (no game install configured).";
            return;
        }

        var ctx = ResolveKnowledgeList(blocks, out var resolveError);
        if (ctx is null)
        {
            BulkOpStatus = resolveError;
            return;
        }

        // Pre-flight: harvest the matching catalog keys + diff against
        // what's already present. Background thread — the catalog scan
        // walks ~2k entries.
        var preview = await Task.Run(() =>
        {
            var harvested = localization
                .EnumerateKnowledgeByNamePrefix(AbyssGateKnowledgePrefixes)
                .Select(e => e.KnowledgeKey)
                .ToHashSet();
            var alreadyHave = 0;
            var toAdd = new List<uint>(harvested.Count);
            foreach (var k in harvested)
            {
                if (ctx.ExistingKeys.Contains(k)) alreadyHave++;
                else toAdd.Add(k);
            }
            // Sort for deterministic order — keeps the diff against an
            // unedited save minimal regardless of HashSet seed variation.
            toAdd.Sort();
            return (Harvested: harvested.Count, AlreadyHave: alreadyHave, ToAdd: toAdd);
        });

        if (preview.Harvested == 0)
        {
            BulkOpStatus = "Nothing to do — no abyss-gate knowledge entries found in knowledgeinfo.pabgb.";
            return;
        }
        if (preview.ToAdd.Count == 0)
        {
            BulkOpStatus =
                $"Nothing to do — all {preview.Harvested} abyss-gate knowledge entries already discovered.";
            return;
        }

        var msg =
            $"Inject {preview.ToAdd.Count} abyss-gate knowledge key(s) into "
            + $"{KnowledgeSaveDataClass}._list?\n\n"
            + $"Harvested {preview.Harvested} matching keys from knowledgeinfo.pabgb "
            + $"({AbyssGateKnowledgePrefixes.Length} name prefixes).\n"
            + $"{preview.AlreadyHave} already present in your save — left alone.\n"
            + $"{preview.ToAdd.Count} will be appended.\n\n"
            + "This is the **discovery flag** layer only — abyss gates "
            + "show up on the map after this. To actually unlock gates "
            + "for crossing, use Tools → Edit Abyss Gates… for per-gate "
            + "state changes.\n\n"
            + "Reversible by reloading the save without writing.";
        var ok = await ask("Unlock all abyss gates (map discovery)?", msg);
        if (!ok)
        {
            BulkOpStatus = "Cancelled.";
            return;
        }

        var keysToAdd = preview.ToAdd;
        var baselineCount = ctx.BaselineCount;
        BulkOpStatus = $"Injecting {keysToAdd.Count} knowledge key(s)…";
        var (applied, firstError, firstFailedKey) =
            await ApplyKnowledgeInjectAsync(ctx, keysToAdd);

        RefreshSelectedBlockSilently();

        if (firstError is null)
        {
            IsDirty = true;
            Journal.Log("Abyss gates",
                $"Bulk-added {applied} abyss-gate knowledge key(s) "
                + $"to {KnowledgeSaveDataClass}._list (map discovery)");
            OnPropertyChanged(nameof(WindowTitle));
            BulkOpStatus = $"Done: added {applied} abyss-gate knowledge key(s) "
                + $"({baselineCount + applied} total in {KnowledgeSaveDataClass}._list).";
        }
        else
        {
            if (applied > 0)
            {
                // Partial-success path: some entries did land before
                // the failure. The save is dirty either way — the
                // user needs to decide whether to keep the partial
                // progress or reload.
                IsDirty = true;
                Journal.Log("Abyss gates",
                    $"Bulk-added {applied} of {keysToAdd.Count} abyss-gate knowledge key(s) "
                    + $"before failure at key 0x{firstFailedKey:X8}");
                OnPropertyChanged(nameof(WindowTitle));
            }
            BulkOpStatus = $"Failed after {applied}/{keysToAdd.Count}: "
                + $"{firstError.Message} (code {firstError.ErrorCode}). "
                + (applied > 0
                    ? "Reload the save without writing to revert the partial progress."
                    : "No changes written.");
        }
    }

    /// <summary>
    /// Resolved handles for appending into <c>KnowledgeSaveData._list</c>:
    /// the owning block, the list field index, the current element count
    /// (= append index), the per-element scalar field indices, and the
    /// set of <c>_key</c> values already present. Shared by the abyss-gate
    /// bulk-unlock and the mount-unlock dragon flow so neither duplicates
    /// the schema-drift guards.
    /// </summary>
    private sealed record KnowledgeListContext(
        int BlockIndex,
        int ListFieldIndex,
        int BaselineCount,
        int KeyFieldIndex,
        int LearnedTimeFieldIndex,
        int IsNewMarkFieldIndex,
        HashSet<uint> ExistingKeys);

    /// <summary>
    /// Locate <c>KnowledgeSaveData._list</c> (an <c>object_list</c>), read
    /// its per-element field indices off element 0, and collect the
    /// <c>_key</c> values already present. Returns <c>null</c> and sets
    /// <paramref name="error"/> on any not-found / schema-drift / empty-list
    /// condition (the same guards the abyss flow shipped with). Requires a
    /// loaded save.
    /// </summary>
    private KnowledgeListContext? ResolveKnowledgeList(
        IReadOnlyList<BlockSummary> blocks, out string? error)
    {
        error = null;
        if (_loadedPath is null)
        {
            error = "No save loaded.";
            return null;
        }
        var blockSummary = FindFirstBlockByClassName(blocks, KnowledgeSaveDataClass);
        if (blockSummary is null)
        {
            error = $"Nothing to do — no {KnowledgeSaveDataClass} block in this save.";
            return null;
        }
        BlockDetails details;
        try
        {
            details = loader.LoadBlockDetails(_loadedPath, blockSummary.Index);
        }
        catch (CrimsonSaveException ex)
        {
            error = $"Could not read {KnowledgeSaveDataClass}: {ex.Message}";
            return null;
        }
        var listField = FindFieldByName(details, KnowledgeListFieldName);
        if (listField is null)
        {
            error = $"Schema drift: {KnowledgeSaveDataClass} has no {KnowledgeListFieldName} field. "
                + "Bridge needs a new schema baseline.";
            return null;
        }
        // Reject the legacy dynamic_array shape — would mean a fresh
        // schema we haven't designed for. The append path is built
        // for object_list elements only.
        if (!string.Equals(listField.Kind, "object_list", StringComparison.Ordinal)
            || listField.Elements is not { } existingElements)
        {
            error =
                $"Schema drift: {KnowledgeSaveDataClass}.{KnowledgeListFieldName} "
                + $"is '{listField.Kind}', expected object_list. "
                + "Bridge needs an update.";
            return null;
        }
        if (existingElements.Count == 0)
        {
            error = $"Refusing to bulk-append into an empty {KnowledgeSaveDataClass}._list "
                + "(need at least one template element to clone). "
                + "Discover any knowledge in-game first.";
            return null;
        }

        // Discover the per-element field indices from the first existing
        // element — schema is uniform within a list.
        int keyFieldIdx = -1, learnedTimeFieldIdx = -1, isNewMarkFieldIdx = -1;
        foreach (var f in existingElements[0].Fields)
        {
            if (string.Equals(f.Name, KnowledgeElemKeyField, StringComparison.Ordinal))
                keyFieldIdx = f.FieldIndex;
            else if (string.Equals(f.Name, KnowledgeElemLearnedTimeField, StringComparison.Ordinal))
                learnedTimeFieldIdx = f.FieldIndex;
            else if (string.Equals(f.Name, KnowledgeElemIsNewMarkField, StringComparison.Ordinal))
                isNewMarkFieldIdx = f.FieldIndex;
        }
        if (keyFieldIdx < 0)
        {
            error =
                $"Schema drift: KnowledgeElementSaveData has no '{KnowledgeElemKeyField}' field. "
                + "Bridge needs an update.";
            return null;
        }

        var existingSet = new HashSet<uint>(existingElements.Count);
        foreach (var elem in existingElements)
        {
            foreach (var f in elem.Fields)
            {
                if (!string.Equals(f.Name, KnowledgeElemKeyField, StringComparison.Ordinal))
                    continue;
                if (TryParseScalarUInt(f.Value, out var k) && k <= uint.MaxValue)
                {
                    existingSet.Add((uint)k);
                }
                break;
            }
        }

        return new KnowledgeListContext(
            blockSummary.Index, listField.FieldIndex, existingElements.Count,
            keyFieldIdx, learnedTimeFieldIdx, isNewMarkFieldIdx, existingSet);
    }

    /// <summary>
    /// Append <paramref name="keysToAdd"/> into <c>KnowledgeSaveData._list</c>
    /// via a deferred clone+patch batch: clone element 0 to the tail, then
    /// patch <c>_key</c> (+ zero <c>_learnedFieldTime</c> / clear
    /// <c>_isNewMark</c> when present). Returns the count applied and the
    /// first error (with its key) on partial failure. Caller is expected to
    /// pass keys NOT already present (use <see cref="KnowledgeListContext"/>
    /// <c>.ExistingKeys</c> to filter). Runs on a background thread.
    /// </summary>
    private async Task<(int Applied, CrimsonSaveException? FirstError, uint? FirstFailedKey)>
        ApplyKnowledgeInjectAsync(KnowledgeListContext ctx, List<uint> keysToAdd)
    {
        var blockIdx = ctx.BlockIndex;
        var listFieldIdx = ctx.ListFieldIndex;
        var baselineCount = ctx.BaselineCount;
        var keyFieldIdx = ctx.KeyFieldIndex;
        var learnedTimeFieldIdx = ctx.LearnedTimeFieldIndex;
        var isNewMarkFieldIdx = ctx.IsNewMarkFieldIndex;
        var applied = 0;
        CrimsonSaveException? firstError = null;
        uint? firstFailedKey = null;

        await Task.Run(() =>
        {
            // Wrap the per-key inject loop in a deferred-redecode batch
            // (see vendor/crimson-rs/docs/save-deferred-redecode.md).
            // Each key costs 1 ListCloneElement (length-changing) + up
            // to 3 SetScalarField (in-place); without the batch every
            // ListCloneElement triggers a full body re-decode (~25ms on
            // a 5MB body). For a ~30-key inject that's ~30 re-decodes ≈
            // 750ms; with the batch every clone stays in the in-memory
            // tree and the trailing commit runs ONE encode+parse+decode.
            //
            // Partial-success preserved: per-op exceptions are caught
            // inside the inner try/catch + `return` out of the lambda.
            // RunDeferred sees normal completion → commits whatever
            // already landed. A commit-time MUTATION_INVALID surfaces
            // through the outer try/catch as applied=0.
            try
            {
                loader.RunDeferred(() =>
                {
                    for (var i = 0; i < keysToAdd.Count; i++)
                    {
                        var newIdx = baselineCount + i;
                        var key = keysToAdd[i];
                        try
                        {
                            // Clone element 0 as a fresh template at the tail.
                            loader.ListCloneElement(
                                blockIdx,
                                ReadOnlySpan<PathStep>.Empty,
                                listFieldIdx,
                                sourceIndex: 0,
                                destinationIndex: newIdx);

                            var newPath = new[] { new PathStep((uint)listFieldIdx, (uint)newIdx) };

                            // Patch _key → the target knowledge key.
                            loader.SetScalarField(
                                blockIdx, newPath, keyFieldIdx, BitConverter.GetBytes(key));

                            // Patch _learnedFieldTime → 0 (inject sentinel —
                            // distinguishes editor-injected discoveries from
                            // organically learned ones). Only if in schema.
                            if (learnedTimeFieldIdx >= 0)
                            {
                                loader.SetScalarField(
                                    blockIdx, newPath, learnedTimeFieldIdx,
                                    BitConverter.GetBytes((ulong)0));
                            }
                            // Patch _isNewMark → false so the injected entries
                            // don't all flash as new in the player's UI on the
                            // next load.
                            if (isNewMarkFieldIdx >= 0)
                            {
                                loader.SetScalarField(
                                    blockIdx, newPath, isNewMarkFieldIdx, new byte[] { 0 });
                            }
                            applied++;
                        }
                        catch (CrimsonSaveException ex)
                        {
                            firstError ??= ex;
                            firstFailedKey ??= key;
                            // Abort the sweep on first failure — the list shape
                            // may now be inconsistent, and continuing risks
                            // compounding the error. Returning out of the lambda
                            // lets the deferred batch commit the partial progress.
                            return;
                        }
                    }
                });
            }
            catch (CrimsonSaveException commitEx)
            {
                // End_*'s commit failed (MUTATION_INVALID): the Rust side
                // already rolled `blocks` back to pre-begin, so nothing
                // landed. Reset applied so reporting matches reality.
                applied = 0;
                firstError ??= commitEx;
            }
        });

        return (applied, firstError, firstFailedKey);
    }

    /// <summary>
    /// The set of knowledge keys already learned in this save
    /// (<c>KnowledgeSaveData._list._key</c>), or <c>null</c> when there's no
    /// save / no knowledge block. Drives the Knowledge editor's
    /// learned/unlearned column.
    /// </summary>
    internal HashSet<uint>? GetLearnedKnowledgeKeys()
    {
        if (Summary is not { Blocks: { } blocks })
        {
            return null;
        }
        return ResolveKnowledgeList(blocks, out _)?.ExistingKeys;
    }

    /// <summary>
    /// Learn (inject) the given knowledge keys into
    /// <c>KnowledgeSaveData._list</c>, skipping any already present. Used by
    /// the Knowledge editor's "Learn selected" / "Learn all in category"
    /// actions. Returns <c>(ok, applied, message)</c>; sets
    /// <see cref="IsDirty"/> + logs on success.
    /// </summary>
    internal async Task<(bool Ok, int Applied, string Message)> LearnKnowledgeAsync(
        IReadOnlyCollection<uint> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (_loadedPath is null || Summary is not { Blocks: { } blocks })
        {
            return (false, 0, "No save loaded.");
        }
        var ctx = ResolveKnowledgeList(blocks, out var error);
        if (ctx is null)
        {
            return (false, 0, error ?? "Knowledge list unavailable.");
        }
        var toAdd = keys.Where(k => !ctx.ExistingKeys.Contains(k)).Distinct().ToList();
        if (toAdd.Count == 0)
        {
            return (true, 0, "All selected knowledge is already learned.");
        }
        toAdd.Sort();

        var (applied, injectError, failedKey) = await ApplyKnowledgeInjectAsync(ctx, toAdd);
        RefreshSelectedBlockSilently();

        if (injectError is not null)
        {
            if (applied > 0)
            {
                IsDirty = true;
                Journal.Log("Knowledge",
                    $"Learned {applied} of {toAdd.Count} knowledge key(s) before failure "
                    + $"at 0x{failedKey:X8}");
                OnPropertyChanged(nameof(WindowTitle));
            }
            return (false, applied,
                $"Failed after {applied}/{toAdd.Count}: {injectError.Message} "
                + $"(code {injectError.ErrorCode}).");
        }

        IsDirty = true;
        Journal.Log("Knowledge", $"Learned {applied} knowledge key(s)");
        OnPropertyChanged(nameof(WindowTitle));
        return (true, applied, $"Learned {applied} knowledge key(s).");
    }

    /// <summary>
    /// Linear scan for the first block whose <c>ClassName</c> matches
    /// <paramref name="className"/>. Used by bulk-op flows that
    /// target a known singleton block (KnowledgeSaveData,
    /// ContentsMiscSaveData, etc.). Returns <c>null</c> when no
    /// matching block exists in the save.
    /// </summary>
    private static BlockSummary? FindFirstBlockByClassName(
        IReadOnlyList<BlockSummary> blocks, string className)
    {
        foreach (var b in blocks)
        {
            if (string.Equals(b.ClassName, className, StringComparison.Ordinal))
            {
                return b;
            }
        }
        return null;
    }

    /// <summary>
    /// Re-fetch the currently-selected top-level block and refresh the
    /// nav stack so any open view picks up post-mutation values.
    /// No-op when nothing is selected; swallows FFI errors so a stale
    /// view (re-fetched on next click) is the worst case.
    /// </summary>
    private void RefreshSelectedBlockSilently()
    {
        if (_loadedPath is null || SelectedBlock is not { } sb || _navStack.Count == 0)
        {
            return;
        }
        try
        {
            var freshTop = loader.LoadBlockDetails(_loadedPath, sb.Index);
            RefreshNavStack(freshTop);
            RebuildFromTop();
        }
        catch (CrimsonSaveException)
        {
            // Ignored — next nav re-fetches.
        }
    }

    /// <summary>Revert the in-progress edit on a row to its last committed value.</summary>
    [RelayCommand]
    private void RevertFieldEdit(FieldRowViewModel? row)
    {
        // Touch instance state so the RelayCommand source generator can
        // bind without CA1822 firing on a pure-delegate wrapper.
        DetailsError = null;
        row?.RevertEdit();
    }

    /// <summary>
    /// Save back to the originally-loaded path. CanExecute gates on
    /// <see cref="IsDirty"/> so the menu item disables when there's nothing
    /// to write.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_loadedPath is null)
        {
            return;
        }
        if (!await ConfirmStructuralEditOrAbortAsync())
        {
            return;
        }
        // PR B addendum: snapshot the on-disk state into the backup
        // tree before overwriting it. Backup failure (read-only folder,
        // disk full) reports via BulkOpStatus but doesn't abort the
        // Save itself — a missing backup is better than a missing Save.
        BackupBeforeWriteSilent(_loadedPath);
        loader.WriteToFile(_loadedPath);
        PreserveOriginalTimestamp(_loadedPath);
        IsDirty = false;
        Journal.Clear();
        OnPropertyChanged(nameof(WindowTitle));
    }

    private bool CanSave() => HasSave && IsDirty && _loadedPath is not null;

    // One-shot (per loaded document) acknowledgement of the structural-
    // edit warning. Reset whenever the loader reports no structural edit
    // pending — i.e. after a fresh Load (HasStructuralEdit goes back to
    // false), so each document re-warns once.
    private bool _structuralWarningAcknowledged;

    /// <summary>
    /// Gate a write when the pending edits include length-changing /
    /// structural mutations (completing a sealed-abyss-artifact challenge,
    /// adding/removing list items, growing arrays, toggling fields present).
    /// crimson-rs writes these correctly and they round-trip byte-perfectly,
    /// but Crimson Desert's own save loader has a latent fixed-buffer
    /// overflow that can make some heavily-progressed saves fail to load
    /// after a length change (the body shifts → the game's deserializer
    /// memcpy's a record into an undersized stack buffer → access
    /// violation). In-place scalar edits (item counts, states, hashes) are
    /// unaffected. Returns <c>true</c> to proceed with the write, or
    /// <c>false</c> if the user chooses not to save. Headless / no-UI hosts
    /// (no <see cref="ConfirmRequested"/>) never block.
    /// </summary>
    private async Task<bool> ConfirmStructuralEditOrAbortAsync()
    {
        if (!loader.HasStructuralEdit)
        {
            _structuralWarningAcknowledged = false;
            return true;
        }
        if (_structuralWarningAcknowledged)
        {
            return true;
        }
        if (ConfirmRequested is not { } ask)
        {
            return true;
        }
        var proceed = await ask(
            "Structural edit — may not load",
            "This save has length-changing (structural) edits, such as completing a "
            + "sealed abyss artifact challenge or adding/removing list items.\n\n"
            + "The data is written correctly, but Crimson Desert's own save loader has a "
            + "bug that can make some heavily-progressed saves crash on load after such an "
            + "edit. In-place edits (item counts, states, gate/flag toggles) are safe.\n\n"
            + "A backup of the original is kept. Save anyway?");
        if (proceed)
        {
            _structuralWarningAcknowledged = true;
        }
        return proceed;
    }

    /// <summary>
    /// Save to a user-chosen path. The View invokes this after running
    /// the SaveFilePicker. Re-anchors the working document to the new
    /// path (subsequent Saves go there), matching standard "Save As"
    /// semantics.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSave))]
    private async Task SaveAsAsync(string? destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || _loadedPath is null)
        {
            return;
        }
        if (!await ConfirmStructuralEditOrAbortAsync())
        {
            return;
        }
        // Same backup-before-overwrite policy as Save: snapshot the
        // destination's current state if it exists. Save As to a brand-
        // new path is a no-op (Skipped_NoSource).
        BackupBeforeWriteSilent(destinationPath);
        loader.WriteToFile(destinationPath);
        // Stamp the destination with the original save's mtime BEFORE
        // re-loading. SaveAs re-anchors `_loadedFileLastWriteTime` to
        // the destination's now-restored timestamp below, so the next
        // Save preserves the same value rather than drifting forward.
        PreserveOriginalTimestamp(destinationPath);
        // Re-anchor: load the freshly-written file so the cached handle
        // matches the new path, then clear nav state. Re-reading also
        // proves the file round-trips (HMAC + LZ4 + ChaCha20 all good).
        Summary = loader.Load(destinationPath);
        _loadedPath = destinationPath;
        _loadedFileLastWriteTime = TryReadLastWriteTime(destinationPath);
        IsDirty = false;
        Journal.Clear();
        ReplaceBlocks(Summary?.Blocks);
        SelectedBlock = null;
        ClearNavigation();
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>
    /// Snapshot <paramref name="targetPath"/>'s on-disk state into the
    /// backup tree and surface the result via <see cref="BulkOpStatus"/>.
    /// Never throws — backup failures are logged for the user but the
    /// caller's Save / Save As proceeds either way.
    /// </summary>
    private void BackupBeforeWriteSilent(string targetPath)
    {
        var outcome = backupService.BackupBeforeWrite(targetPath);
        if (outcome.IsSuccess && outcome.Entry is { } entry)
        {
            BulkOpStatus = outcome.VersionsPruned > 0
                ? $"Backup: {entry.SlotName} @ {SaveBackupService.FormatTimestamp(entry.Timestamp)} ({entry.TotalBytes:N0} B, pruned {outcome.VersionsPruned})."
                : $"Backup: {entry.SlotName} @ {SaveBackupService.FormatTimestamp(entry.Timestamp)} ({entry.TotalBytes:N0} B).";
        }
        else if (outcome.Kind == BackupOutcomeKind.Failed)
        {
            // Show the failure prominently so the user knows the safety
            // net's down; their Save still works.
            BulkOpStatus = $"⚠ Backup failed (save still wrote): {outcome.Message}";
        }
        // Skipped (no source / bad path) is silent — common for first
        // Save As to a fresh path, or for paths outside the canonical
        // <userId>/<slotName>/save.save layout.
    }

    /// <summary>
    /// Restore a backup snapshot back to the user's live save folder.
    /// Snapshots the current state first (so undo is itself undoable),
    /// copies the backup files, then re-loads the restored save so the
    /// UI reflects the new state.
    ///
    /// <para>
    /// The View opens a picker dialog that resolves the user's choice
    /// to a <see cref="BackupEntry"/> and invokes this. CanExecute
    /// gates on <see cref="ConfirmRequested"/> being wired (we always
    /// require explicit confirmation for a restore — it overwrites
    /// the on-disk save).
    /// </para>
    /// </summary>
    public async Task RestoreFromBackupAsync(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (ConfirmRequested is not { } ask)
        {
            BulkOpStatus = "Restore needs a confirm dialog hook — UI bug, file an issue.";
            return;
        }

        // Restore target = the discovered save root for the BACKUP'S
        // platform, NOT the currently-loaded save's platform. A user
        // might have a Steam save open and be restoring an Epic
        // backup; the restore has to land in Epic's tree.
        var gameSaveRoot = ResolveSaveRootForBackup(entry);
        if (gameSaveRoot is null)
        {
            BulkOpStatus =
                $"Restore failed: no {entry.Platform} save root is detected on this machine. "
                + "The launcher that wrote the original save may not be installed here.";
            return;
        }
        var targetSlotDir = Path.Combine(gameSaveRoot, entry.UserId, entry.SlotName);
        var targetSavePath = Path.Combine(targetSlotDir, "save.save");

        var msg = $"Restore {entry.SlotName} from backup taken at "
                  + $"{SaveBackupService.FormatTimestamp(entry.Timestamp)}?\n\n"
                  + $"Platform: {entry.Platform}\n"
                  + $"Files: {string.Join(", ", entry.FileNames)} ({entry.TotalBytes:N0} bytes)\n"
                  + $"Target: {targetSlotDir}\n\n"
                  + "The current contents of the slot folder will be backed up first.";
        var ok = await ask("Restore from backup?", msg);
        if (!ok)
        {
            BulkOpStatus = "Restore cancelled.";
            return;
        }

        // Snapshot the about-to-be-overwritten state. If the slot folder
        // doesn't exist (rare — restoring into an empty user dir), the
        // pre-restore backup is just a no-op.
        BackupBeforeWriteSilent(targetSavePath);

        try
        {
            await Task.Run(() => SaveBackupService.Restore(entry, gameSaveRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BulkOpStatus = $"Restore failed: {ex.Message}";
            return;
        }

        // Reload the freshly-restored save so the UI shows it. If the
        // user already had a save open from the same path, this re-anchors
        // the cached handle to the new bytes.
        try
        {
            LoadSave(targetSavePath);
            BulkOpStatus = $"Restored {entry.SlotName} from "
                           + $"{SaveBackupService.FormatTimestamp(entry.Timestamp)}.";
        }
        catch (CrimsonSaveException ex)
        {
            BulkOpStatus = $"Restored, but reload failed: {ex.Message}";
        }
    }

    private void PushFrame(NavFrame frame)
    {
        _navStack.Push(frame);
        RebuildFromTop();
    }

    private void RebuildFromTop()
    {
        // Breadcrumb: oldest → newest.
        Breadcrumb.Clear();
        var depth = 0;
        foreach (var f in _navStack.Reverse())
        {
            Breadcrumb.Add(new BreadcrumbItem(depth, f.Label));
            depth++;
        }

        _allFields.Clear();
        _allElements.Clear();
        VisibleFields.Clear();
        VisibleElements.Clear();
        SelectedField = null;
        SelectedElement = null;
        FieldsFilter = null;
        ElementsFilter = null;

        if (_navStack.Count > 0)
        {
            switch (_navStack.Peek())
            {
                case BlockFrame bf:
                    // Every scalar is editable at any depth — the path the
                    // VM tracks on the frame disambiguates which body
                    // region the FFI patches. Localization is passed
                    // through so u32 fields can pre-resolve item names.
                    foreach (var field in bf.Block.Fields)
                    {
                        _allFields.Add(new FieldRowViewModel(field, bf.Path, localization));
                    }
                    ApplyFieldsFilter();
                    break;
                case ElementsFrame ef:
                    foreach (var el in ef.Elements)
                    {
                        _allElements.Add(new ElementRowViewModel(el, localization));
                    }
                    ApplyElementsFilter();
                    break;
            }
        }

        NotifyNavigationChanged();
    }

    /// <summary>
    /// Recompute <see cref="VisibleBlocks"/> from <see cref="_allBlocks"/>
    /// using <see cref="BlocksFilter"/>. Matches case-insensitively
    /// against <c>ClassName</c> and the decimal <c>Index</c> string —
    /// the only two columns the user can reasonably search by (Offset
    /// and Size are byte coordinates, not human-recognisable).
    /// Preserves <see cref="SelectedBlock"/> when it survives the
    /// filter; clears it otherwise.
    /// </summary>
    private void ApplyBlocksFilter()
    {
        VisibleBlocks.Clear();
        var needle = BlocksFilter;
        if (string.IsNullOrWhiteSpace(needle))
        {
            foreach (var b in _allBlocks)
            {
                VisibleBlocks.Add(b);
            }
        }
        else
        {
            foreach (var b in _allBlocks)
            {
                if (b.ClassName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || b.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        .Contains(needle, StringComparison.Ordinal))
                {
                    VisibleBlocks.Add(b);
                }
            }
        }
        // Drop the selection if the previously-selected block was
        // filtered out — leaving it set would point at a row the user
        // can't see, with the field-detail pane stuck on a stale view.
        if (SelectedBlock is { } sel && !VisibleBlocks.Contains(sel))
        {
            SelectedBlock = null;
        }
        OnPropertyChanged(nameof(BlocksFilterCountText));
    }

    /// <summary>Footer-style count: "10 of 1,112" / "1,112" when unfiltered.</summary>
    public string BlocksFilterCountText
    {
        get
        {
            if (_allBlocks.Count == 0) return string.Empty;
            return string.IsNullOrEmpty(BlocksFilter)
                ? $"{_allBlocks.Count:N0}"
                : $"{VisibleBlocks.Count:N0} of {_allBlocks.Count:N0}";
        }
    }

    private void ApplyFieldsFilter()
    {
        if (!IsShowingFields)
        {
            return;
        }
        VisibleFields.Clear();
        var needle = FieldsFilter;
        if (string.IsNullOrWhiteSpace(needle))
        {
            foreach (var f in _allFields)
            {
                VisibleFields.Add(f);
            }
        }
        else
        {
            // Case-insensitive substring match across every column the
            // human is likely to search by: field name, type name, raw
            // display value, and the resolved item name (so typing
            // "gold" lights up the row even though the raw value is
            // a hash like "11 <u32>").
            foreach (var f in _allFields)
            {
                if (f.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || f.TypeName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || f.DisplayValue.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(f.ResolvedName)
                        && f.ResolvedName.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                {
                    VisibleFields.Add(f);
                }
            }
        }
        OnPropertyChanged(nameof(FieldsFilterCountText));
    }

    private void ApplyElementsFilter()
    {
        if (!IsShowingElements)
        {
            return;
        }
        VisibleElements.Clear();
        var needle = ElementsFilter;
        if (string.IsNullOrWhiteSpace(needle))
        {
            foreach (var e in _allElements)
            {
                VisibleElements.Add(e);
            }
        }
        else
        {
            // Match against class name (for non-item lists), the raw
            // key string, the directly-resolved name, AND the names of
            // nested ObjectList children (so "Gold" filters the
            // _inventorylist[18] picker down to the bag(s) holding gold
            // without the user drilling into each bag first). The
            // nested haystack is pre-lowered, so we lower the needle
            // once and search case-sensitively against it.
            var nestedNeedle = needle.ToLowerInvariant();
            foreach (var e in _allElements)
            {
                if (e.ClassName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(e.KeyText)
                        && e.KeyText.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(e.ResolvedName)
                        && e.ResolvedName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(e.NestedMatchHaystack)
                        && e.NestedMatchHaystack.Contains(nestedNeedle, StringComparison.Ordinal)))
                {
                    VisibleElements.Add(e);
                }
            }
        }
        OnPropertyChanged(nameof(ElementsCountText));
    }

    private void ClearNavigation()
    {
        _navStack.Clear();
        Breadcrumb.Clear();
        _allFields.Clear();
        _allElements.Clear();
        VisibleFields.Clear();
        VisibleElements.Clear();
        SelectedField = null;
        SelectedElement = null;
        FieldsFilter = null;
        ElementsFilter = null;
        DetailsError = null;
        BulkOpStatus = null;
        NotifyNavigationChanged();
    }

    private void NotifyNavigationChanged()
    {
        OnPropertyChanged(nameof(IsShowingFields));
        OnPropertyChanged(nameof(IsShowingElements));
        OnPropertyChanged(nameof(CurrentBlock));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(MaskBytesText));
        OnPropertyChanged(nameof(TrailingPadText));
        OnPropertyChanged(nameof(UndecodedRangesText));
        OnPropertyChanged(nameof(FieldsFilterCountText));
        OnPropertyChanged(nameof(ElementsCountText));
        // Block-action visibility — depends on current frame's class +
        // resolved field values, so re-check on every nav change.
        NotifyMarkChallengeStateChanged();
        // Add-Item picker target depends on the current frame + selection.
        RecomputeAddItemTarget();
    }

    /// <summary>
    /// Re-evaluate the "Mark Challenge Complete" button's visibility +
    /// enablement + tooltip after a nav change or a save mutation that
    /// might have flipped eligibility (e.g. <c>_state</c> promoted to 5
    /// on the current row).
    /// </summary>
    private void NotifyMarkChallengeStateChanged()
    {
        OnPropertyChanged(nameof(IsCurrentChallengeMarkable));
        OnPropertyChanged(nameof(IsCurrentNavOnMissionStateRow));
        OnPropertyChanged(nameof(CurrentChallengeMarkTooltip));
        MarkCurrentChallengeCompleteCommand.NotifyCanExecuteChanged();
    }

    // ── Navigation frame types ──────────────────────────────────────────────

    private abstract record NavFrame(string Label)
    {
        /// <summary>
        /// Row index this frame's user most recently drilled from. Set
        /// in <see cref="MainWindowViewModel.DrillIntoField"/> /
        /// <see cref="MainWindowViewModel.DrillIntoElement"/> right
        /// before pushing the child frame, restored on pop-back as
        /// the selected row (+ scrolled into view) so the user
        /// doesn't lose their place in a 200-row list. Settable on
        /// the abstract base so frame-agnostic code can read it
        /// without a downcast.
        /// </summary>
        public int? LastDrilledIndex { get; set; }
    }

    /// <summary>
    /// A view onto an <see cref="BlockDetails"/>. <see cref="Path"/> is the
    /// descent from the top-level TOC block to this block; root frames
    /// carry an empty path. Used by edits + by post-mutation refresh.
    /// </summary>
    private sealed record BlockFrame(
        string Label,
        BlockDetails Block,
        IReadOnlyList<PathStep> Path) : NavFrame(Label);

    /// <summary>
    /// A picker view onto the elements of an <c>ObjectList</c> field. Not
    /// itself addressable as a block; <see cref="PathToList"/> is the path
    /// to the *enclosing* block and <see cref="ListFieldIndex"/> is which
    /// field of that block is the list. Picking element N synthesises the
    /// next step <c>PathStep(ListFieldIndex, N)</c>.
    /// </summary>
    private sealed record ElementsFrame(
        string Label,
        IReadOnlyList<BlockDetails> Elements,
        IReadOnlyList<PathStep> PathToList,
        uint ListFieldIndex) : NavFrame(Label);
}

/// <summary>
/// One segment of the navigation breadcrumb. <see cref="Depth"/> is its
/// 0-based position in the stack; clicking the breadcrumb pops back to
/// this depth.
/// </summary>
public sealed record BreadcrumbItem(int Depth, string Label);
