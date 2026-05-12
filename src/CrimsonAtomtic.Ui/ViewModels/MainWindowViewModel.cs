using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Core;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// View-model for the main window. Holds the optional loaded save plus
/// the file-open command. AOT-safe: every observable comes from a
/// CommunityToolkit.Mvvm source generator, no reflection.
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSave))]
    [NotifyPropertyChangedFor(nameof(SchemaTypeCountText))]
    [NotifyPropertyChangedFor(nameof(TocEntryCountText))]
    [NotifyPropertyChangedFor(nameof(HmacStatusText))]
    [NotifyPropertyChangedFor(nameof(PayloadSizeText))]
    [NotifyPropertyChangedFor(nameof(UncompressedSizeText))]
    private SaveSummary? _summary;

    /// <summary>Currently selected row in the blocks DataGrid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedBlock))]
    private BlockSummary? _selectedBlock;

    [ObservableProperty]
    private string? _detailsError;

    /// <summary>
    /// Live text filter for <see cref="VisibleFields"/>. Empty / null
    /// shows everything. Applies only when <see cref="IsShowingFields"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsFilterCountText))]
    private string? _fieldsFilter;

    public ObservableCollection<BlockSummary> Blocks { get; } = [];

    public bool HasSave => Summary is not null;
    public bool HasSelectedBlock => SelectedBlock is not null;

    public string SchemaTypeCountText => Summary is null ? "" : Summary.SchemaTypeCount.ToString("N0");
    public string TocEntryCountText => Summary is null ? "" : Summary.TocEntryCount.ToString("N0");
    public string HmacStatusText => Summary is null ? "" : Summary.HmacOk ? "verified" : "FAILED";
    public string PayloadSizeText => Summary is null ? "" : $"{Summary.PayloadSize:N0} bytes";
    public string UncompressedSizeText => Summary is null ? "" : $"{Summary.UncompressedSize:N0} bytes";

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

    private readonly Stack<NavFrame> _navStack = new();

    private readonly List<DecodedFieldRow> _allFields = [];
    public ObservableCollection<DecodedFieldRow> VisibleFields { get; } = [];
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
        FieldsFilter = null;

        if (_navStack.Count > 0)
        {
            switch (_navStack.Peek())
            {
                case BlockFrame bf:
                    _allFields.AddRange(bf.Block.Fields);
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
                    || f.Value.Contains(needle, StringComparison.OrdinalIgnoreCase))
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
