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

    /// <summary>
    /// Per-field decode of <see cref="SelectedBlock"/>, loaded lazily on
    /// selection change. Null while idle / loading.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaskBytesText))]
    [NotifyPropertyChangedFor(nameof(TrailingPadText))]
    [NotifyPropertyChangedFor(nameof(UndecodedRangesText))]
    private BlockDetails? _selectedBlockDetails;

    [ObservableProperty]
    private string? _detailsError;

    /// <summary>
    /// Live text filter for <see cref="VisibleFields"/>. Empty / null
    /// shows everything.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsFilterCountText))]
    private string? _fieldsFilter;

    public ObservableCollection<BlockSummary> Blocks { get; } = [];

    /// <summary>
    /// Source-of-truth field list for the currently selected block.
    /// Populated by <see cref="OnSelectedBlockChanged"/>; the View binds
    /// to <see cref="VisibleFields"/> instead so the filter applies.
    /// </summary>
    private readonly List<DecodedFieldRow> _allFields = [];

    /// <summary>Filtered view of <see cref="_allFields"/>.</summary>
    public ObservableCollection<DecodedFieldRow> VisibleFields { get; } = [];

    public bool HasSave => Summary is not null;
    public bool HasSelectedBlock => SelectedBlock is not null;

    public string SchemaTypeCountText => Summary is null ? "" : Summary.SchemaTypeCount.ToString("N0");
    public string TocEntryCountText => Summary is null ? "" : Summary.TocEntryCount.ToString("N0");
    public string HmacStatusText => Summary is null ? "" : Summary.HmacOk ? "verified" : "FAILED";
    public string PayloadSizeText => Summary is null ? "" : $"{Summary.PayloadSize:N0} bytes";
    public string UncompressedSizeText => Summary is null ? "" : $"{Summary.UncompressedSize:N0} bytes";

    public string MaskBytesText => SelectedBlockDetails?.MaskBytesHex ?? "";
    public string TrailingPadText =>
        string.IsNullOrEmpty(SelectedBlockDetails?.TrailingPadHex) ? "(none)" : SelectedBlockDetails.TrailingPadHex;
    public string UndecodedRangesText
    {
        get
        {
            var ranges = SelectedBlockDetails?.UndecodedRanges;
            if (ranges is null || ranges.Count == 0)
            {
                return "(none)";
            }
            return string.Join(", ", ranges.Select(r => $"[{r[0]:N0}..{r[1]:N0})"));
        }
    }

    public string FieldsFilterCountText =>
        _allFields.Count == 0 ? string.Empty : $"{VisibleFields.Count:N0} of {_allFields.Count:N0}";

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
        ClearBlockDetails();
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
        ClearBlockDetails();
        if (value is null || _loadedPath is null)
        {
            return;
        }
        try
        {
            var details = loader.LoadBlockDetails(_loadedPath, value.Index);
            SelectedBlockDetails = details;
            _allFields.AddRange(details.Fields);
            ApplyFieldsFilter();
        }
        catch (CrimsonSaveException ex)
        {
            DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
        }
    }

    partial void OnFieldsFilterChanged(string? value) => ApplyFieldsFilter();

    private void ApplyFieldsFilter()
    {
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

    private void ClearBlockDetails()
    {
        SelectedBlockDetails = null;
        _allFields.Clear();
        VisibleFields.Clear();
        FieldsFilter = null;
        DetailsError = null;
        OnPropertyChanged(nameof(FieldsFilterCountText));
    }
}
