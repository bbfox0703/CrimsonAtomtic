using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for Tools → Browse Character References. Flat-lists every
/// schema-tagged <c>CharacterKey</c> occurrence in the loaded save —
/// one row per scalar field; one row per element of a
/// <c>DynamicArray&lt;CharacterKey&gt;</c>. Powered by
/// <see cref="ISaveLoader.ListCharacterRefs"/>.
///
/// <para>
/// <b>Linkage caveat</b> (surfaced verbatim in the dialog's warning
/// banner): the same <c>CharacterKey</c> value can map to different
/// concrete entities under different schema field roles (spawn
/// marker vs friendly mercenary vs quest target). This dialog
/// enumerates every reference and leaves cross-verification to the
/// user — the per-row Jump button parachutes them into the raw
/// block editor for that.
/// </para>
///
/// <para>
/// <b>UI sharing</b>: rows reuse the <see cref="CharacterPickerRow"/>
/// shape for the portrait + name + key triplet so the per-row
/// rendering matches the standalone Character Picker.
/// </para>
/// </summary>
public sealed partial class CharacterRefsBrowserViewModel : ObservableObject
{
    /// <summary>Hard cap on rows surfaced — defensive against schema drift.</summary>
    public const int MaxRows = 5000;

    private readonly List<CharacterRefRow> _allRows = new();
    private readonly PortraitProvider _portraits;

    /// <summary>
    /// Captured at construction so secondary-language switches mid-
    /// dialog don't desync the cached row labels — close and reopen.
    /// </summary>
    public string? SecondaryLanguage { get; }

    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLanguage);

    public string SecondaryNameHeader =>
        HasSecondary ? $"Name ({SecondaryLanguage})" : "Name (secondary)";

    /// <summary>Filtered view bound to the DataGrid.</summary>
    public ObservableCollection<CharacterRefRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Raised when a row's Jump button fires. Payload is the
    /// top-level <c>BlockIndex</c> the user wants to navigate to.
    /// The host MainWindow subscribes, closes the browser dialog,
    /// and calls
    /// <see cref="MainWindowViewModel.NavigateToTopLevelBlockAsync"/>.
    /// </summary>
    public event Action<int>? JumpToBlockRequested;

    internal void RequestJump(CharacterRefRow row) =>
        JumpToBlockRequested?.Invoke(row.BlockIndex);

    public int TotalRows => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (TotalRows == 0) return "No CharacterKey references found.";
            if (string.IsNullOrWhiteSpace(SearchText))
                return $"{TotalRows:N0} reference(s) total.";
            return $"{Rows.Count:N0} of {TotalRows:N0} reference(s) match.";
        }
    }

    public CharacterRefsBrowserViewModel(
        ISaveLoader loader,
        LocalizationProvider localization,
        IReadOnlyList<BlockSummary> blocks)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(blocks);
        SecondaryLanguage = localization.SecondaryLanguage;
        _portraits = localization.Portraits;

        // Build an index for class-name lookup so we don't do an O(N²)
        // sweep when the save has ~thousands of blocks. BlockSummary
        // index is dense from 0..N-1; a plain array is enough.
        var blockClassNames = new string[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            blockClassNames[blocks[i].Index] = blocks[i].ClassName;
        }

        IReadOnlyList<CharacterRefRecord> refs;
        try
        {
            refs = loader.ListCharacterRefs(out _);
        }
        catch (CrimsonSaveException ex)
        {
            StatusMessage = UiText.Format("CharRefsEnumFailed",
                "Failed to enumerate CharacterKey refs: {0} (code {1})", ex.Message, ex.ErrorCode);
            return;
        }

        var capped = false;
        foreach (var rec in refs)
        {
            if (_allRows.Count >= MaxRows)
            {
                capped = true;
                break;
            }
            var className = rec.BlockIndex < (uint)blockClassNames.Length
                ? blockClassNames[(int)rec.BlockIndex] ?? "<unknown>"
                : "<unknown>";

            var key = rec.CharacterKey;
            var nameEn = localization.LookupCharacterDisplayName(
                             key, LocalizationProvider.DefaultLanguage)
                         ?? localization.LookupCharacterInternalName(key)
                         ?? "<unresolved>";
            var nameSec = SecondaryLanguage is null
                ? null
                : localization.LookupCharacterDisplayName(key, SecondaryLanguage);

            var row = new CharacterRefRow(
                parent: this,
                blockIndex: (int)rec.BlockIndex,
                blockClassName: className,
                characterKey: key,
                nameEnglish: nameEn,
                nameSecondary: nameSec);
            _allRows.Add(row);
        }

        // Stable sort: by block index, then key — predictable order so
        // the same save always reads the same way.
        _allRows.Sort((a, b) =>
        {
            var c = a.BlockIndex.CompareTo(b.BlockIndex);
            return c != 0 ? c : a.CharacterKey.CompareTo(b.CharacterKey);
        });

        StatusMessage = capped
            ? $"Showing first {MaxRows:N0} references (cap hit — schema drift?)."
            : $"{_allRows.Count:N0} CharacterKey reference(s) total.";
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var needle = SearchText;
        var unfiltered = string.IsNullOrWhiteSpace(needle);
        foreach (var r in _allRows)
        {
            if (unfiltered || r.Matches(needle!))
            {
                Rows.Add(r);
                r.StartPortraitLoad(_portraits);
            }
        }
        OnPropertyChanged(nameof(ResultsCountText));
    }
}

/// <summary>
/// One row in the Character Refs browser. Mirrors
/// <see cref="CharacterPickerRow"/>'s portrait + name + key triple
/// so the per-row visual surface stays consistent between the two
/// dialogs, but adds the per-save fields (block index + block class
/// name) the browser needs.
/// </summary>
public sealed partial class CharacterRefRow : ObservableObject
{
    private readonly CharacterRefsBrowserViewModel _parent;
    private int _portraitLoadStarted;

    public CharacterRefRow(
        CharacterRefsBrowserViewModel parent,
        int blockIndex,
        string blockClassName,
        uint characterKey,
        string nameEnglish,
        string? nameSecondary)
    {
        _parent = parent;
        BlockIndex = blockIndex;
        BlockClassName = blockClassName;
        CharacterKey = characterKey;
        CharacterKeyText = characterKey.ToString(CultureInfo.InvariantCulture);
        NameEnglish = nameEnglish;
        NameSecondary = nameSecondary;
        Name = string.IsNullOrEmpty(nameSecondary)
            ? nameEnglish
            : $"{nameEnglish} / {nameSecondary}";
    }

    public int BlockIndex { get; }
    public string BlockClassName { get; }
    public uint CharacterKey { get; }
    public string CharacterKeyText { get; }
    public string NameEnglish { get; }
    public string? NameSecondary { get; }

    /// <summary>Combined display name, "Eng" or "Eng / Sec" per VendorBuyback style.</summary>
    public string Name { get; }

    [ObservableProperty]
    private Bitmap? _portrait;

    /// <summary>
    /// Kick off the background portrait load. Idempotent — the
    /// filter loop calls this for every visible row but only the
    /// first call wins (Interlocked-flag gated).
    /// </summary>
    internal void StartPortraitLoad(PortraitProvider portraits)
    {
        if (CharacterKey == 0 || !portraits.IsAvailable) return;
        if (Interlocked.Exchange(ref _portraitLoadStarted, 1) != 0) return;
        Task.Run(() =>
        {
            var bmp = portraits.GetPortrait(CharacterKey);
            if (bmp is null) return;
            Dispatcher.UIThread.Post(() => Portrait = bmp);
        });
    }

    /// <summary>True when <paramref name="needle"/> matches any of the row's identifying fields.</summary>
    internal bool Matches(string needle)
    {
        if (BlockClassName.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (NameEnglish.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (NameSecondary is not null
            && NameSecondary.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (CharacterKeyText.Contains(needle, StringComparison.Ordinal)) return true;
        return false;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Jump() => _parent.RequestJump(this);
}
