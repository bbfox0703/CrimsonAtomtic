using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the modal palette picker dialog. Renders the chosen color
/// group's full 109-position palette as a grid of clickable swatches.
/// The currently-applied <c>(R, G, B)</c> highlights to its cell when
/// the value is on-grid; off-grid CE-modified RGBs render no
/// highlight + show a banner.
/// </summary>
/// <remarks>
/// Source spec: <c>vendor/crimson-rs/docs/dye-editor-scope.md</c>
/// §"Recommended C# editor UX". Replaces the freeform R/G/B
/// NumericUpDown columns the editor previously used — those gave the
/// illusion of fine-grained control but the engine snaps every
/// in-game dye to one of the 109 palette positions, so off-grid
/// values are visually unreachable.
/// </remarks>
public sealed partial class DyePalettePickerViewModel : ObservableObject
{
    private readonly NativeDyeColorGroupInfoCatalog _catalog;
    private readonly uint _colorGroupKey;

    public string HeaderText { get; }
    public string SubHeaderText { get; }

    /// <summary>Grid cells (109 in 1.07) — bound to the UniformGrid.</summary>
    public ObservableCollection<DyePaletteCell> Cells { get; } = [];

    /// <summary>
    /// True when the row's current <c>(R, G, B)</c> was found in the
    /// palette via <see cref="NativeDyeColorGroupInfoCatalog.PositionForRgb"/>.
    /// False ⇒ current dye is off-grid (CE-modified or set via the raw
    /// scalar editor); the picker shows a banner explaining the next
    /// click will replace it with an on-grid color.
    /// </summary>
    public bool CurrentIsOnGrid { get; }

    /// <summary>
    /// Cell at the currently-applied position (highlighted in the grid),
    /// or null when off-grid.
    /// </summary>
    public DyePaletteCell? CurrentCell { get; private set; }

    /// <summary>
    /// True when the dialog should publish the user's choice on close.
    /// Set by <see cref="ConfirmCell"/>; window code-behind reads it.
    /// </summary>
    public bool HasResult { get; private set; }

    public byte ResultR { get; private set; }
    public byte ResultG { get; private set; }
    public byte ResultB { get; private set; }

    /// <summary>
    /// Window code-behind subscribes; raised when the user clicks a cell
    /// so the window can close + the parent slot row receives the RGB.
    /// </summary>
    public event Action? CloseRequested;

    public DyePalettePickerViewModel(
        NativeDyeColorGroupInfoCatalog catalog,
        uint colorGroupKey,
        string colorGroupName,
        byte currentR,
        byte currentG,
        byte currentB,
        string slotLabel)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _colorGroupKey = colorGroupKey;
        HeaderText = $"Pick a color — {colorGroupName}";
        SubHeaderText = $"{slotLabel} · current: #{currentR:X2}{currentG:X2}{currentB:X2}";

        // Build the grid: enumerate every palette position into a cell
        // VM. The grid layout (9 grayscale + 10×10 chromatic) emerges
        // naturally from a 10-column UniformGrid showing 109 cells.
        var size = catalog.PaletteSize(colorGroupKey) ?? 0;
        for (var i = 0; i < size; i++)
        {
            var rgba = catalog.PaletteAt(colorGroupKey, i);
            if (rgba is not { } v) continue;
            Cells.Add(new DyePaletteCell(this, i, v.R, v.G, v.B));
        }

        // Highlight the currently-applied cell when it's on-grid.
        var pos = catalog.PositionForRgb(colorGroupKey, currentR, currentG, currentB);
        if (pos is { } p && p < Cells.Count)
        {
            CurrentIsOnGrid = true;
            CurrentCell = Cells[p];
            CurrentCell.IsCurrent = true;
        }
    }

    /// <summary>
    /// Called by a <see cref="DyePaletteCell"/> when the user clicks it.
    /// Records the choice + raises <see cref="CloseRequested"/> so the
    /// window closes immediately (matching color-picker convention —
    /// no separate OK button).
    /// </summary>
    internal void ConfirmCell(DyePaletteCell cell)
    {
        ResultR = cell.R;
        ResultG = cell.G;
        ResultB = cell.B;
        HasResult = true;
        CloseRequested?.Invoke();
    }
}

/// <summary>
/// One clickable cell in the palette picker grid. Each cell wraps an
/// <c>(R, G, B)</c> from a palette position; clicking it confirms the
/// selection on the parent VM (no separate OK button).
/// </summary>
public sealed partial class DyePaletteCell : ObservableObject
{
    private readonly DyePalettePickerViewModel _parent;

    public int Position { get; }
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public IBrush Brush { get; }
    public string Tooltip { get; }

    [ObservableProperty]
    private bool _isCurrent;

    public DyePaletteCell(DyePalettePickerViewModel parent, int position, byte r, byte g, byte b)
    {
        _parent = parent;
        Position = position;
        R = r;
        G = g;
        B = b;
        Brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        Tooltip = $"Position {position} · #{r:X2}{g:X2}{b:X2}";
    }

    [RelayCommand]
    private void Pick() => _parent.ConfirmCell(this);
}
