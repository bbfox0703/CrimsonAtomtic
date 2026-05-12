using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// View-model for the main window. Holds the optional loaded save plus
/// the file-open command. AOT-safe: every observable comes from a
/// CommunityToolkit.Mvvm source generator, no reflection.
/// </summary>
public sealed partial class MainWindowViewModel(ISaveLoader loader) : ObservableObject
{
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

    public ObservableCollection<BlockSummary> Blocks { get; } = [];
    public ObservableCollection<DecodedFieldRow> SelectedBlockFields { get; } = [];

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
            foreach (var f in details.Fields)
            {
                SelectedBlockFields.Add(f);
            }
        }
        catch (CrimsonSaveException ex)
        {
            DetailsError = $"{ex.Message} (code {ex.ErrorCode})";
        }
    }

    private void ClearBlockDetails()
    {
        SelectedBlockDetails = null;
        SelectedBlockFields.Clear();
        DetailsError = null;
    }
}
