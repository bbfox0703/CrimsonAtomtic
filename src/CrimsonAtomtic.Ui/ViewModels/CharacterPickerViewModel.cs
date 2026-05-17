using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the standalone "Browse characters / NPCs" dialog. Mirror of
/// <see cref="ItemPickerViewModel"/> but driven by
/// <c>characterinfo.pabgb</c>: enumerates every
/// <c>(CharacterKey, internal_name)</c> pair, joins each with its
/// PALOC-resolved display name (English + optional secondary) and an
/// NPC portrait when one matches above
/// <see cref="PortraitProvider.MinAcceptableScore"/>.
///
/// <para>
/// Purpose: when the user is investigating a <c>_characterKey</c>
/// value in the save (FieldNPC spawn rows, mercenary characters,
/// quest NPCs), they need to know which character that numeric key
/// refers to. This dialog narrows the universe to the ~hundreds of
/// characters the game ships and lets them search by either name
/// surface. Read-only — no per-row action button.
/// </para>
/// </summary>
public sealed partial class CharacterPickerViewModel : ObservableObject
{
    /// <summary>Hard cap on rows surfaced in <see cref="Results"/>.</summary>
    public const int MaxResults = 500;

    private readonly List<CharacterPickerRow> _allRows;
    private readonly PortraitProvider _portraits;

    /// <summary>
    /// Raised when the user clicks a per-row "Pick" button in
    /// <see cref="IsPickMode"/> dialogs. The payload is the chosen
    /// <see cref="CharacterPickerRow.CharacterKey"/>. The hosting
    /// window subscribes once and closes itself with the key value
    /// — see <c>CharacterPickerWindow</c>'s DataContextChanged hook.
    /// </summary>
    public event Action<uint>? PickConfirmed;

    public CharacterPickerViewModel(LocalizationProvider localization)
        : this(localization, isPickMode: false)
    {
    }

    /// <summary>
    /// Construct the VM with explicit pick-mode opt-in. Pick mode
    /// surfaces a per-row "Pick" button and raises
    /// <see cref="PickConfirmed"/> when clicked; the hosting window
    /// closes with the chosen key. Browse-only mode (default) hides
    /// the column.
    /// </summary>
    public CharacterPickerViewModel(LocalizationProvider localization, bool isPickMode)
    {
        ArgumentNullException.ThrowIfNull(localization);
        IsPickMode = isPickMode;
        SecondaryLanguage = localization.SecondaryLanguage;
        _portraits = localization.Portraits;

        _allRows = new List<CharacterPickerRow>(localization.CharacterCount);
        for (var i = 0; i < localization.CharacterCount; i++)
        {
            var entry = localization.GetCharacter(i);
            if (entry is not { } e)
            {
                continue;
            }
            // English display name falls back to the internal name when
            // PALOC has no entry — mirrors the editor's other resolved-
            // name columns so users see consistent labels.
            var nameEn = localization.LookupCharacterDisplayName(
                             e.CharacterKey, LocalizationProvider.DefaultLanguage)
                         ?? e.InternalName;
            var nameSecondary = SecondaryLanguage is null
                ? null
                : localization.LookupCharacterDisplayName(e.CharacterKey, SecondaryLanguage);
            _allRows.Add(new CharacterPickerRow(
                parent: this,
                characterKey: e.CharacterKey,
                characterKeyText: e.CharacterKey.ToString(CultureInfo.InvariantCulture),
                internalName: e.InternalName,
                nameEnglish: nameEn,
                nameSecondary: nameSecondary));
        }
        // Sort by CharacterKey ascending — predictable order, lowest
        // keys (main-story NPCs) first. DataGrid still lets the user
        // re-sort by column.
        _allRows.Sort((a, b) => a.CharacterKey.CompareTo(b.CharacterKey));

        Refresh();
    }

    /// <summary>
    /// User's currently-active secondary language, captured at dialog
    /// open. Null when only English is loaded. Changing the secondary
    /// after this dialog is open won't refresh the cached rows — close
    /// and reopen.
    /// </summary>
    public string? SecondaryLanguage { get; }

    /// <summary>
    /// True when the dialog is hosting the per-field CharacterKey
    /// scalar picker (Pick button visible per row; Pick click raises
    /// <see cref="PickConfirmed"/> and the host window closes with
    /// the chosen key). False for the standalone "Browse Characters"
    /// dialog (read-only browse).
    /// </summary>
    public bool IsPickMode { get; }

    /// <summary>
    /// Last key the user picked via the per-row Pick button, or
    /// <c>null</c> when nothing has been picked yet. Mostly here for
    /// tests / inspection — production callers consume
    /// <see cref="PickConfirmed"/> directly.
    /// </summary>
    [ObservableProperty]
    private uint? _pickedKey;

    /// <summary>
    /// Invoked by <see cref="CharacterPickerRow.Pick"/>. Updates
    /// <see cref="PickedKey"/> and raises <see cref="PickConfirmed"/>
    /// so the hosting window can close with the chosen value.
    /// </summary>
    internal void ConfirmPick(CharacterPickerRow row)
    {
        if (!IsPickMode) return;
        PickedKey = row.CharacterKey;
        PickConfirmed?.Invoke(row.CharacterKey);
    }

    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLanguage);

    public string SecondaryNameHeader =>
        HasSecondary ? $"Name ({SecondaryLanguage})" : "Name (secondary)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    public ObservableCollection<CharacterPickerRow> Results { get; } = [];

    public int TotalCharacters => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (TotalCharacters == 0)
            {
                return "Character info not loaded.";
            }
            if (string.IsNullOrEmpty(SearchText))
            {
                return $"Showing first {Results.Count:N0} of {TotalCharacters:N0} characters — type to filter.";
            }
            return Results.Count >= MaxResults
                ? $"{Results.Count:N0}+ matches (capped) of {TotalCharacters:N0}."
                : $"{Results.Count:N0} matches of {TotalCharacters:N0}.";
        }
    }

    partial void OnSearchTextChanged(string? value) => Refresh();

    private void Refresh()
    {
        Results.Clear();
        var needle = SearchText;
        var unfiltered = string.IsNullOrWhiteSpace(needle);
        foreach (var row in _allRows)
        {
            if (unfiltered
                // Numeric search ("4" jumps to Damiane, etc.) anchored by
                // Contains so the user can type partial prefixes too.
                || row.CharacterKeyText.Contains(needle!, StringComparison.Ordinal)
                || row.InternalName.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || row.NameEnglish.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || (row.NameSecondary is not null
                    && row.NameSecondary.Contains(needle!, StringComparison.OrdinalIgnoreCase)))
            {
                Results.Add(row);
                row.StartPortraitLoad(_portraits);
                if (Results.Count >= MaxResults)
                {
                    break;
                }
            }
        }
        OnPropertyChanged(nameof(ResultsCountText));
    }
}

/// <summary>
/// One row in the Character Picker DataGrid. <see cref="CharacterKey"/>
/// is the u32 row key from <c>characterinfo.pabgb</c> (already in lo24
/// form — no cat-byte). <see cref="InternalName"/> is the entry's
/// internal id (e.g. <c>"Yann_Friendly"</c>, <c>"FieldNPC_Bandit_Lvl3"</c>);
/// <see cref="NameEnglish"/> falls back to <see cref="InternalName"/>
/// when no PALOC entry exists at <c>lo32 = 0x30</c>, so the column
/// always has something showing. Portrait fills in asynchronously via
/// <see cref="StartPortraitLoad"/>.
/// </summary>
public sealed partial class CharacterPickerRow : ObservableObject
{
    private readonly CharacterPickerViewModel? _parent;

    /// <summary>
    /// Browse-only constructor retained for back-compat with callers
    /// that don't need a Pick command (none in the current tree, but
    /// new tests / dialogs can keep using this overload).
    /// </summary>
    public CharacterPickerRow(
        uint characterKey,
        string characterKeyText,
        string internalName,
        string nameEnglish,
        string? nameSecondary)
        : this(parent: null, characterKey, characterKeyText, internalName,
               nameEnglish, nameSecondary)
    {
    }

    /// <summary>
    /// Construct a row with a back-reference to the parent VM so the
    /// per-row <c>Pick</c> command can call back into
    /// <see cref="CharacterPickerViewModel.ConfirmPick"/>.
    /// </summary>
    internal CharacterPickerRow(
        CharacterPickerViewModel? parent,
        uint characterKey,
        string characterKeyText,
        string internalName,
        string nameEnglish,
        string? nameSecondary)
    {
        _parent = parent;
        CharacterKey = characterKey;
        CharacterKeyText = characterKeyText;
        InternalName = internalName;
        NameEnglish = nameEnglish;
        NameSecondary = nameSecondary;
    }

    public uint CharacterKey { get; }
    public string CharacterKeyText { get; }
    public string InternalName { get; }
    public string NameEnglish { get; }
    public string? NameSecondary { get; }

    /// <summary>True when the parent VM is in pick mode — controls per-row Pick button visibility.</summary>
    public bool IsPickMode => _parent?.IsPickMode ?? false;

    /// <summary>
    /// Per-row Pick command — visible only in pick mode. Confirms the
    /// row's <see cref="CharacterKey"/> back to the parent VM, which
    /// raises <see cref="CharacterPickerViewModel.PickConfirmed"/> so
    /// the hosting window can close with the chosen value.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Pick() => _parent?.ConfirmPick(this);

    [ObservableProperty]
    private Bitmap? _portrait;

    private int _portraitLoadStarted;

    /// <summary>
    /// Kick off the background portrait load for this row. Idempotent —
    /// the picker calls this whenever a row enters the result set, but
    /// the first call wins and subsequent ones are no-ops via the
    /// interlocked flag.
    /// </summary>
    internal void StartPortraitLoad(PortraitProvider portraits)
    {
        if (CharacterKey == 0 || !portraits.IsAvailable)
        {
            return;
        }
        if (Interlocked.Exchange(ref _portraitLoadStarted, 1) != 0)
        {
            return;
        }
        Task.Run(() =>
        {
            var bmp = portraits.GetPortrait(CharacterKey);
            if (bmp is null)
            {
                return;
            }
            Dispatcher.UIThread.Post(() => Portrait = bmp);
        });
    }
}
