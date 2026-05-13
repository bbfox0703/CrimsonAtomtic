using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Core;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// View-model for the main window. Holds the optional loaded save plus
/// the file-open / save / edit commands. AOT-safe: every observable
/// comes from a CommunityToolkit.Mvvm source generator, no reflection.
/// </summary>
public sealed partial class MainWindowViewModel(ISaveLoader loader, IPlatformPaths paths) : ObservableObject
{
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
    /// Live text filter for <see cref="VisibleFields"/>. Empty / null
    /// shows everything. Applies only when <see cref="IsShowingFields"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsFilterCountText))]
    private string? _fieldsFilter;

    /// <summary>
    /// Field selected in the field-detail DataGrid. The View binds the
    /// inline edit panel below the DataGrid to this row, so users edit by
    /// click-to-select + type, not via DataGrid cell-edit mode.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditPanelVisible))]
    [NotifyPropertyChangedFor(nameof(SelectedFieldTypeHint))]
    private FieldRowViewModel? _selectedField;

    public ObservableCollection<BlockSummary> Blocks { get; } = [];

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
    public ObservableCollection<FieldRowViewModel> VisibleFields { get; } = [];
    public ObservableCollection<BlockDetails> VisibleElements { get; } = [];
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

    public string ElementsCountText =>
        !IsShowingElements ? string.Empty
        : $"{VisibleElements.Count:N0} element{(VisibleElements.Count == 1 ? "" : "s")}";

    /// <summary>Edit panel is shown when the user has selected an editable scalar field.</summary>
    public bool IsEditPanelVisible => SelectedField is { IsEditable: true };

    /// <summary>Type-tag hint shown in the edit panel, e.g. "u32" or "f64".</summary>
    public string SelectedFieldTypeHint => SelectedField?.TypeTag ?? string.Empty;

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
        IsDirty = false;
        Blocks.Clear();
        SelectedBlock = null;
        ClearNavigation();
        if (Summary is not null)
        {
            foreach (var block in Summary.Blocks)
            {
                Blocks.Add(block);
            }
        }
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WindowTitle));
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
            PushFrame(new BlockFrame(details.ClassName, details));
        }
        catch (CrimsonSaveException ex)
        {
            DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
        }
    }

    partial void OnFieldsFilterChanged(string? value) => ApplyFieldsFilter();

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
        if (row is null)
        {
            return;
        }
        if (row.Child is { } child)
        {
            PushFrame(new BlockFrame($"{row.Name}: {child.ClassName}", child));
        }
        else if (row.Elements is { Count: > 0 } elements)
        {
            PushFrame(new ElementsFrame($"{row.Name}[{elements.Count}]", elements));
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
        var label = idx >= 0 ? $"[{idx}]: {element.ClassName}" : element.ClassName;
        PushFrame(new BlockFrame(label, element));
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
            loader.SetScalarField(block.Index, row.FieldIndex, bytes);
        }
        catch (CrimsonSaveException ex)
        {
            row.EditError = $"{ex.Message} (code {ex.ErrorCode})";
            return;
        }

        // Re-fetch the block: the mutation may ripple (e.g. masks). Reuse
        // the existing FieldRowViewModel instances so the DataGrid keeps
        // its scroll / selection state.
        var fresh = loader.LoadBlockDetails(_loadedPath, block.Index);
        for (var i = 0; i < _allFields.Count && i < fresh.Fields.Count; i++)
        {
            _allFields[i].ApplyCommittedValue(fresh.Fields[i]);
        }
        IsDirty = true;
        OnPropertyChanged(nameof(WindowTitle));
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
        // Re-anchor: load the freshly-written file so the cached handle
        // matches the new path, then clear nav state. Re-reading also
        // proves the file round-trips (HMAC + LZ4 + ChaCha20 all good).
        Summary = loader.Load(destinationPath);
        _loadedPath = destinationPath;
        IsDirty = false;
        Blocks.Clear();
        SelectedBlock = null;
        ClearNavigation();
        if (Summary is not null)
        {
            foreach (var block in Summary.Blocks)
            {
                Blocks.Add(block);
            }
        }
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
        VisibleFields.Clear();
        VisibleElements.Clear();
        SelectedField = null;
        FieldsFilter = null;

        if (_navStack.Count > 0)
        {
            switch (_navStack.Peek())
            {
                case BlockFrame bf:
                    // Editing is gated on depth == 1: only top-level blocks
                    // are addressable by the C ABI's TOC index. Children
                    // get read-only wrappers.
                    var topLevel = _navStack.Count == 1;
                    foreach (var field in bf.Block.Fields)
                    {
                        _allFields.Add(new FieldRowViewModel(field, topLevel));
                    }
                    ApplyFieldsFilter();
                    break;
                case ElementsFrame ef:
                    foreach (var el in ef.Elements)
                    {
                        VisibleElements.Add(el);
                    }
                    break;
            }
        }

        NotifyNavigationChanged();
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
            // Case-insensitive substring match across the three columns
            // a human is most likely to search by.
            foreach (var f in _allFields)
            {
                if (f.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || f.TypeName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || f.DisplayValue.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    VisibleFields.Add(f);
                }
            }
        }
        OnPropertyChanged(nameof(FieldsFilterCountText));
    }

    private void ClearNavigation()
    {
        _navStack.Clear();
        Breadcrumb.Clear();
        _allFields.Clear();
        VisibleFields.Clear();
        VisibleElements.Clear();
        SelectedField = null;
        FieldsFilter = null;
        DetailsError = null;
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

    private abstract record NavFrame(string Label);
    private sealed record BlockFrame(string Label, BlockDetails Block) : NavFrame(Label);
    private sealed record ElementsFrame(string Label, IReadOnlyList<BlockDetails> Elements) : NavFrame(Label);
}

/// <summary>
/// One segment of the navigation breadcrumb. <see cref="Depth"/> is its
/// 0-based position in the stack; clicking the breadcrumb pops back to
/// this depth.
/// </summary>
public sealed record BreadcrumbItem(int Depth, string Label);
