using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Core;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
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
    LocalizationProvider localization) : ObservableObject
{
    /// <summary>
    /// Localization service exposed for child view-models (e.g. the
    /// browse-localization dialog opened from the Tools menu).
    /// </summary>
    public LocalizationProvider Localization => localization;

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
                return "Icons: not set (Tools → Set Icon Folder…)";
            }
            var basename = Path.GetFileName(icons.Root!.TrimEnd(Path.DirectorySeparatorChar));
            if (icons.DecodeFailures > 0 && !string.IsNullOrEmpty(icons.LastError))
            {
                return $"Icons: {basename} — {icons.DecodeFailures} decode fail(s); {icons.LastError}";
            }
            return $"Icons: {basename} ({icons.FileCount:N0} files)";
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
    /// Update the active icon-cache directory and persist the choice
    /// to settings.json. Re-seeds the IconProvider AND the static
    /// converter singleton so already-rendered cells repaint with
    /// the new icons (or with blanks if the new path has no match).
    /// Empty path clears the configured value — falls back to the
    /// exe-dir probe.
    /// </summary>
    public void SetIconCacheDirectory(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? null : path;
        // Persist first so a crash mid-refresh doesn't drop the user's
        // choice. The previously-loaded language pref is preserved
        // through the record-with copy.
        var existing = AppSettingsStore.Load(paths.LocalAppDataDirectory);
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory,
            existing with { IconCacheDirectory = normalized });

        // Re-seed the provider. The exe directory is the same as on
        // first boot — passing it again keeps the
        // <exe-dir>/IconCache/ fallback consistent.
        var exeDir = Path.GetDirectoryName(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        localization.ConfigureIconProvider(normalized, exeDir);
        ItemKeyToIconConverter.Provider = localization.Icons;

        // Force every currently-rendered icon binding to re-query the
        // converter by rebuilding the visible row collections. The
        // converter doesn't have a "cache invalidated, re-query"
        // signal otherwise. Open Item Picker windows aren't refreshed
        // here — they hold their own VMs and need a close + reopen
        // for the new icons to surface.
        if (_navStack.Count > 0)
        {
            RebuildFromTop();
        }
        OnPropertyChanged(nameof(IconStatus));
    }

    public void SetSecondaryLanguage(string? langCode)
    {
        localization.SecondaryLanguage = langCode;
        AppSettingsStore.TrySave(paths.LocalAppDataDirectory, new AppSettings
        {
            SecondaryLanguage = localization.SecondaryLanguage,
        });
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
    /// Best initial folder for the Open Save dialog. Drills into the
    /// single user-id subfolder (e.g. <c>save\102190433\</c>) when
    /// exactly one exists, otherwise stops at the save root, falling
    /// back to the root path even when it doesn't exist so the dialog
    /// has a defined starting point.
    /// </summary>
    public string DefaultOpenSaveStartingPath
    {
        get
        {
            var root = paths.GameSaveRoot;
            if (!Directory.Exists(root))
            {
                return root;
            }
            // Take(2) is enough to tell "exactly one" from "two or more".
            var users = Directory.EnumerateDirectories(root).Take(2).ToArray();
            return users.Length == 1 ? users[0] : root;
        }
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
    [NotifyCanExecuteChangedFor(nameof(FillSelectedFieldToMaxStackCommand))]
    private FieldRowViewModel? _selectedField;

    /// <summary>
    /// Element selected in the element-picker DataGrid. Two-way binding
    /// lets the View highlight the row the user clicks AND lets the VM
    /// restore the previously-drilled element when popping back through
    /// the breadcrumb.
    /// </summary>
    [ObservableProperty]
    private ElementRowViewModel? _selectedElement;

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
    /// Status footer text for the most recent bulk operation —
    /// "Filled 168 stacks." / "Bulk fill cancelled." etc. Lives in
    /// <see cref="DetailsError"/>'s sibling slot so it doesn't fight
    /// for screen real estate. Cleared on the next navigation.
    /// </summary>
    [ObservableProperty]
    private string? _bulkOpStatus;

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
                return app;
            }
            var name = Path.GetFileName(_loadedPath);
            var prefix = IsDirty ? "*" : "";
            return $"{prefix}{name} — {app}";
        }
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

    /// <summary>Type-tag hint shown in the edit panel, e.g. "u32" or "f64".</summary>
    public string SelectedFieldTypeHint => SelectedField?.TypeTag ?? string.Empty;

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
        ReplaceBlocks(Summary?.Blocks);
        SelectedBlock = null;
        ClearNavigation();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WindowTitle));
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
        ClearNavigation();
        if (value is null || _loadedPath is null)
        {
            return;
        }
        try
        {
            var details = loader.LoadBlockDetails(_loadedPath, value.Index);
            // Root frame: empty path — this block is at the TOC level.
            PushFrame(new BlockFrame(details.ClassName, details, Array.Empty<PathStep>()));
        }
        catch (CrimsonSaveException ex)
        {
            DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
        }
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
    [RelayCommand]
    private void CommitFieldEdit(FieldRowViewModel? row)
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
        try
        {
            // Path-addressed FFI: empty path collapses to a top-level mutation,
            // non-empty walks into locator children / list elements.
            var pathArr = row.EnclosingPath is PathStep[] a ? a : row.EnclosingPath.ToArray();
            loader.SetScalarField(block.Index, pathArr, row.FieldIndex, bytes);
        }
        catch (CrimsonSaveException ex)
        {
            row.EditError = $"{ex.Message} (code {ex.ErrorCode})";
            return;
        }

        // Re-fetch the top-level block; refresh every nav frame so popping
        // back via breadcrumb shows fresh values (the mutation may ripple
        // across peer fields via the schema). Each existing FieldRowViewModel
        // gets its DisplayValue updated in place so the DataGrid keeps its
        // scroll position and the user's selection.
        var freshTop = loader.LoadBlockDetails(_loadedPath, block.Index);
        RefreshNavStack(freshTop);
        IsDirty = true;
        OnPropertyChanged(nameof(WindowTitle));
    }

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
            candidates = new List<StackFillCandidate>(1);
            if (TryBuildSingleCandidate(row.Block, rowPath, out var c))
            {
                candidates.Add(c);
            }
        }
        else
        {
            candidates = CollectStackFillCandidates(row.Block, rowPath);
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
                      + "Items with max_stack_count > 100 fill to max.\n"
                      + "Items with max_stack_count ≤ 100 round up to the next full stack "
                      + "(e.g. count 120, max 50 → 150). Items already at a stack-boundary are skipped.\n\n"
                      + "Reversible by reloading the save without writing.";
            var ok = await ask("Fill stacks?", msg);
            if (!ok)
            {
                BulkOpStatus = "Fill cancelled.";
                return;
            }
        }

        // Show a "working" message and yield to the UI before the
        // long-running loop. Without this and the Task.Run below, 168
        // SetScalarField calls + their per-call full-block re-decodes
        // hold the UI thread for several seconds — the user can't
        // even see the modal close cleanly.
        BulkOpStatus = $"Filling {candidates.Count} stack(s)…";
        var blockIdx = topBlock.Index;

        // SetScalarField is thread-safe via NativeSaveLoader's
        // internal _cacheLock; running the loop on a worker thread
        // keeps the UI responsive during the long re-decode storm.
        var (applied, firstError) = await Task.Run(() =>
        {
            var count = 0;
            CrimsonSaveException? err = null;
            foreach (var c in candidates)
            {
                try
                {
                    loader.SetScalarField(blockIdx, c.Path, c.FieldIndex, c.Bytes);
                    count++;
                }
                catch (CrimsonSaveException ex)
                {
                    err = ex;
                    break;
                }
            }
            return (count, err);
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
            OnPropertyChanged(nameof(WindowTitle));
        }
        BulkOpStatus = firstError is null
            ? $"Filled {applied} stack(s)."
            : $"Failed after {applied}/{candidates.Count}: {firstError.Message}";
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
        IReadOnlyList<PathStep> parentPath)
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
                if (TryBuildSingleCandidate(items[itemIdx], itemPath, out var candidate))
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
        if (!TryComputeTargetStack(current, maxVal, out var target))
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
    /// </summary>
    private static bool TryComputeTargetStack(ulong current, ulong max, out ulong target)
    {
        target = 0;
        if (max == 0)
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
            return true;
        }

        // max ≤ 100 branch.
        if (current < max)
        {
            target = max;
            return true;
        }
        var remainder = current % max;
        if (remainder == 0)
        {
            return false;
        }
        // Round current up to the next multiple of max. The add is
        // safe against overflow at this scale (max ≤ 100, current is
        // a u64 game count — sum stays well below u64::MAX).
        target = current + (max - remainder);
        return true;
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
    private void Save()
    {
        if (_loadedPath is null)
        {
            return;
        }
        loader.WriteToFile(_loadedPath);
        PreserveOriginalTimestamp(_loadedPath);
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private bool CanSave() => HasSave && IsDirty && _loadedPath is not null;

    /// <summary>
    /// Save to a user-chosen path. The View invokes this after running
    /// the SaveFilePicker. Re-anchors the working document to the new
    /// path (subsequent Saves go there), matching standard "Save As"
    /// semantics.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSave))]
    private void SaveAs(string? destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || _loadedPath is null)
        {
            return;
        }
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
        ReplaceBlocks(Summary?.Blocks);
        SelectedBlock = null;
        ClearNavigation();
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WindowTitle));
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
