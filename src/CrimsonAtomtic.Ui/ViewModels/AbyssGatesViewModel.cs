using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the per-gate "Edit Abyss Gates" dialog. Walks every
/// top-level <c>FieldSaveData</c> block in the loaded save, drills
/// into its <c>_fieldGimmickSaveDataList</c> object-list field,
/// filters the elements down to the abyss-gate / hyperspace-ruin
/// subset by cross-referencing each element's <c>_gimmickInfoKey</c>
/// against the allowlist built from <c>gimmickinfo.pabgb</c>, and
/// exposes a per-row Lock/Unlock toggle.
///
/// <para>
/// Touches the <b>gate state</b> layer of unlock: writes
/// <c>_initStateNameHash</c> via the path-addressed scalar setter
/// (path = <c>[(_fieldGimmickSaveDataList, elementIdx)]</c>). The
/// companion <b>discovery flag</b> layer (gates show up on the map)
/// is handled by Tools → Unlock All Abyss Gates which writes
/// <c>KnowledgeSaveData._list</c>.
/// </para>
///
/// <para>
/// Per the upstream survey at
/// <c>vendor/crimson-rs/docs/abyss-gate-map.md</c>: three
/// <c>_initStateNameHash</c> values were originally documented —
/// <see cref="DefaultUntouched"/> (player hasn't interacted),
/// <see cref="ActivatedCrossed"/> (player has crossed / activated),
/// and <see cref="IdleDecoration"/> (scenery that never transitions).
/// Slot probes against 1.05–1.07 saves surface additional rarer
/// values (~3 more across thousands of rows) — those are treated as
/// Unknown and stay read-only, identical to how Idle is handled.
/// The toggle only flips between Default and Activated.
/// </para>
///
/// <para>
/// <b>History note:</b> the v1 of this dialog (shipped 2026-05-15)
/// looked for top-level <c>FieldGimmickSaveData</c> blocks, of
/// which real saves have zero — every gate sits nested under
/// <c>FieldSaveData._fieldGimmickSaveDataList</c>. This v2 walks
/// the correct shape and surfaces gates across both
/// <c>FieldSaveData</c> roots (typically 2 per save) at once.
/// </para>
/// </summary>
public sealed partial class AbyssGatesViewModel : ObservableObject
{
    public const uint DefaultUntouched = 0x866c7489;
    public const uint ActivatedCrossed = 0xe300acfe;
    public const uint IdleDecoration   = 0x150b14d0;

    /// <summary>Class name of the per-field container block.</summary>
    private const string FieldSaveDataClass = "FieldSaveData";

    /// <summary>
    /// Object-list field on <c>FieldSaveData</c> that holds every
    /// per-gimmick save record (gates + scenery + miscellaneous).
    /// </summary>
    private const string FieldGimmickListFieldName = "_fieldGimmickSaveDataList";

    /// <summary>Field names read off each nested gimmick element.</summary>
    private const string GimmickInfoKeyField     = "_gimmickInfoKey";
    private const string InitStateNameHashField  = "_initStateNameHash";
    private const string OwnerLevelNameField     = "_ownerLevelName";
    private const string FieldGimmickSaveKeyField = "_fieldGimmickSaveDataKey";

    /// <summary>
    /// Gimmick-name substrings that mark a row as abyss-related.
    /// Substring (not prefix) — gimmick internal names are
    /// descriptive (e.g. <c>gimmick_abyssone_bridge_gate_01</c>) so
    /// substring catches the variants.
    /// </summary>
    private static readonly string[] AbyssGimmickNameSubstrings =
    [
        "abyss",
        "hyperspace",
    ];

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private readonly ChangeJournal _journal;
    private readonly string _savePath;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<AbyssGateRow> Rows { get; } = new();

    private AbyssGatesViewModel(
        ISaveLoader loader, LocalizationProvider localization,
        ChangeJournal journal, string savePath)
    {
        _loader = loader;
        _localization = localization;
        _journal = journal;
        _savePath = savePath;
    }

    /// <summary>
    /// Build a VM by walking the save asynchronously. Reports
    /// progress through <paramref name="progress"/>; the dialog
    /// shows a "Scanning N/Total roots…" footer while this runs.
    /// </summary>
    public static async Task<AbyssGatesViewModel> CreateAsync(
        ISaveLoader loader,
        LocalizationProvider localization,
        ChangeJournal journal,
        string savePath,
        IReadOnlyList<BlockSummary> blocks,
        IProgress<(int Done, int Total)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(blocks);

        var vm = new AbyssGatesViewModel(loader, localization, journal, savePath);

        // Build the abyss-gimmick allowlist from gimmickinfo. Lazy
        // walk — typically a few thousand entries, ~5 ms.
        var allowlist = new HashSet<uint>(
            localization.EnumerateGimmicksByNameContains(AbyssGimmickNameSubstrings)
                        .Select(g => g.GimmickInfoKey));
        if (allowlist.Count == 0)
        {
            vm.StatusMessage = "No abyss / hyperspace entries in gimmickinfo.pabgb "
                + "(game install not configured?). The dialog is empty.";
            return vm;
        }

        // Collect candidate roots first so the progress total is accurate.
        var roots = new List<BlockSummary>();
        foreach (var b in blocks)
        {
            if (string.Equals(b.ClassName, FieldSaveDataClass, StringComparison.Ordinal))
            {
                roots.Add(b);
            }
        }

        var built = new List<AbyssGateRow>();
        var scannedElements = 0;
        await Task.Run(() =>
        {
            for (var i = 0; i < roots.Count; i++)
            {
                progress?.Report((i, roots.Count));
                BlockDetails details;
                try
                {
                    details = loader.LoadBlockDetails(savePath, roots[i].Index);
                }
                catch (CrimsonSaveException)
                {
                    continue;
                }

                // Find the gimmick list field on this FieldSaveData root.
                DecodedFieldRow? listField = null;
                foreach (var f in details.Fields)
                {
                    if (string.Equals(f.Name, FieldGimmickListFieldName, StringComparison.Ordinal)
                        && f.Present)
                    {
                        listField = f;
                        break;
                    }
                }
                if (listField is null || listField.Elements is not { } elements)
                {
                    continue;
                }

                var listFieldIdx = (uint)listField.FieldIndex;
                for (var elemIdx = 0; elemIdx < elements.Count; elemIdx++)
                {
                    scannedElements++;
                    if (TryBuildRow(
                            vm,
                            roots[i].Index,
                            listFieldIdx,
                            (uint)elemIdx,
                            elements[elemIdx],
                            allowlist,
                            localization,
                            out var row))
                    {
                        built.Add(row);
                    }
                }
            }
            progress?.Report((roots.Count, roots.Count));
        }).ConfigureAwait(true);

        // Sort: owner level (alphabetical), then by gimmick internal
        // name within the level — matches how the upstream's probe
        // groups its sample output.
        built.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.OwnerLevelName, b.OwnerLevelName);
            return c != 0 ? c : string.CompareOrdinal(a.GimmickName, b.GimmickName);
        });
        foreach (var r in built)
        {
            vm.Rows.Add(r);
        }
        vm.StatusMessage = built.Count == 0
            ? $"Scanned {scannedElements} nested gimmick element(s) across "
              + $"{roots.Count} {FieldSaveDataClass} root(s) — "
              + "no abyss-gate gimmicks matched the allowlist."
            : $"Loaded {built.Count} abyss-gate row(s) from {scannedElements} "
              + $"nested gimmick element(s) across {roots.Count} {FieldSaveDataClass} "
              + "root(s). Toggle the Lock state to flip _initStateNameHash; "
              + "reload the save to revert.";
        return vm;
    }

    private static bool TryBuildRow(
        AbyssGatesViewModel parent,
        int rootBlockIdx,
        uint listFieldIdx,
        uint elementIdx,
        BlockDetails element,
        HashSet<uint> abyssGimmickKeys,
        LocalizationProvider localization,
        out AbyssGateRow row)
    {
        row = null!;
        uint gimmickInfoKey = 0;
        bool haveGimmickKey = false;
        uint stateHash = 0;
        bool haveStateHash = false;
        int stateHashFieldIdx = -1;
        string ownerLevelName = string.Empty;
        uint fieldGimmickSaveKey = 0;

        foreach (var f in element.Fields)
        {
            if (string.Equals(f.Name, GimmickInfoKeyField, StringComparison.Ordinal)
                && TryParseScalarUInt(f.Value, out var gk) && gk <= uint.MaxValue)
            {
                gimmickInfoKey = (uint)gk;
                haveGimmickKey = true;
            }
            else if (string.Equals(f.Name, InitStateNameHashField, StringComparison.Ordinal)
                     && f.Present
                     && TryParseScalarUInt(f.Value, out var sh) && sh <= uint.MaxValue)
            {
                stateHash = (uint)sh;
                haveStateHash = true;
                stateHashFieldIdx = f.FieldIndex;
            }
            else if (string.Equals(f.Name, OwnerLevelNameField, StringComparison.Ordinal))
            {
                ownerLevelName = ExtractStringValue(f.Value);
            }
            else if (string.Equals(f.Name, FieldGimmickSaveKeyField, StringComparison.Ordinal)
                     && TryParseScalarUInt(f.Value, out var fk) && fk <= uint.MaxValue)
            {
                fieldGimmickSaveKey = (uint)fk;
            }
        }

        if (!haveGimmickKey || !abyssGimmickKeys.Contains(gimmickInfoKey))
        {
            return false;
        }
        // Skip rows with no _initStateNameHash entirely — they can't
        // be toggled meaningfully.
        if (!haveStateHash || stateHashFieldIdx < 0)
        {
            return false;
        }

        var gimmickName = localization.ResolveByFieldTypeName("GimmickInfoKey", gimmickInfoKey);
        if (string.IsNullOrEmpty(gimmickName))
        {
            gimmickName = $"GimmickInfoKey 0x{gimmickInfoKey:X8}";
        }

        var path = new[] { new PathStep(listFieldIdx, elementIdx) };
        row = new AbyssGateRow(
            parent,
            rootBlockIdx,
            path,
            stateHashFieldIdx,
            gimmickInfoKey,
            gimmickName,
            string.IsNullOrEmpty(ownerLevelName) ? "(no owner-level name)" : ownerLevelName,
            fieldGimmickSaveKey,
            stateHash);
        return true;
    }

    /// <summary>
    /// Apply a Lock/Unlock toggle on a row. Default ↔ Activated only;
    /// Idle / Unknown stay read-only.
    /// </summary>
    internal void Apply(AbyssGateRow row, uint targetHash)
    {
        if (targetHash != DefaultUntouched && targetHash != ActivatedCrossed)
        {
            return;
        }
        var bytes = BitConverter.GetBytes(targetHash);
        try
        {
            _loader.SetScalarField(row.BlockIndex, row.Path, row.StateHashFieldIndex, bytes);
        }
        catch (CrimsonSaveException ex)
        {
            row.LastError = $"{ex.Message} (code {ex.ErrorCode})";
            StatusMessage = $"Toggle failed on {row.OwnerLevelName} / {row.GimmickName}: {ex.Message}";
            return;
        }
        row.LastError = null;
        row.CurrentStateHash = targetHash;
        IsDirty = true;
        _journal.Log("Abyss gates",
            $"Set {row.OwnerLevelName} / {row.GimmickName} → "
            + AbyssGateRow.LabelForHash(targetHash));
        StatusMessage =
            $"Set {row.OwnerLevelName} / {row.GimmickName} → {AbyssGateRow.LabelForHash(targetHash)}.";
    }

    /// <summary>
    /// True iff at least one toggle has been applied. The host
    /// MainWindow reads this on dialog close to set its own dirty
    /// flag so the title bar's * shows + the next File → Save
    /// writes the new state.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Extract the textual portion from a BlockDetails-formatted
    /// value (the editor formats values as "&lt;text&gt; &lt;tag&gt;").
    /// Returns the input unchanged when no trailing tag is found,
    /// which is the safe fallback for unfamiliar field shapes.
    /// </summary>
    private static string ExtractStringValue(string formatted)
    {
        if (string.IsNullOrEmpty(formatted))
        {
            return string.Empty;
        }
        var lastSpace = formatted.LastIndexOf(' ');
        if (lastSpace < 0)
        {
            return formatted;
        }
        var tail = formatted.AsSpan(lastSpace + 1);
        // Tagged shape is "<...>" — strip; otherwise the value
        // probably contains spaces (an owner-level name with weird
        // chars) and we shouldn't truncate it.
        if (tail.Length >= 3 && tail[0] == '<' && tail[^1] == '>')
        {
            return formatted[..lastSpace];
        }
        return formatted;
    }

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
        return ulong.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out value);
    }
}

/// <summary>
/// One abyss-gate row. Identification: owner level (e.g.
/// <c>AbyssBridge_0001_Phase00_00</c>) + gimmick internal name +
/// per-save unique <see cref="FieldGimmickSaveDataKey"/>.
/// </summary>
public sealed partial class AbyssGateRow : ObservableObject
{
    private readonly AbyssGatesViewModel _parent;
    private readonly PathStep[] _path;

    public AbyssGateRow(
        AbyssGatesViewModel parent,
        int blockIndex,
        PathStep[] path,
        int stateHashFieldIndex,
        uint gimmickInfoKey,
        string gimmickName,
        string ownerLevelName,
        uint fieldGimmickSaveDataKey,
        uint currentStateHash)
    {
        _parent = parent;
        BlockIndex = blockIndex;
        _path = path;
        StateHashFieldIndex = stateHashFieldIndex;
        GimmickInfoKey = gimmickInfoKey;
        GimmickName = gimmickName;
        OwnerLevelName = ownerLevelName;
        FieldGimmickSaveDataKey = fieldGimmickSaveDataKey;
        _currentStateHash = currentStateHash;
    }

    /// <summary>
    /// Top-level block index (the owning <c>FieldSaveData</c>).
    /// </summary>
    public int BlockIndex { get; }

    /// <summary>
    /// Path from <see cref="BlockIndex"/> down to the nested gimmick
    /// element this row represents. One step today
    /// (<c>(_fieldGimmickSaveDataList, elementIdx)</c>); kept as
    /// <see cref="PathStep"/>[] so the toggle goes through the same
    /// path-addressed scalar setter every other nested editor uses.
    /// </summary>
    public PathStep[] Path => _path;

    public int StateHashFieldIndex { get; }
    public uint GimmickInfoKey { get; }
    public string GimmickName { get; }
    public string OwnerLevelName { get; }
    public uint FieldGimmickSaveDataKey { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(CanLock))]
    [NotifyPropertyChangedFor(nameof(CanUnlock))]
    [NotifyPropertyChangedFor(nameof(StateHashText))]
    private uint _currentStateHash;

    [ObservableProperty]
    private string? _lastError;

    public string StateLabel => LabelForHash(CurrentStateHash);
    public string StateHashText => $"0x{CurrentStateHash:X8}";

    /// <summary>
    /// True iff this row is currently in the Activated state and the
    /// "Lock" button should be enabled. Activated → Default.
    /// </summary>
    public bool CanLock => CurrentStateHash == AbyssGatesViewModel.ActivatedCrossed;

    /// <summary>
    /// True iff this row is currently in the Default (untouched)
    /// state and the "Unlock" button should be enabled. Default →
    /// Activated. Idle / Unknown states stay read-only — flipping
    /// scenery hashes might destabilise the gimmick.
    /// </summary>
    public bool CanUnlock => CurrentStateHash == AbyssGatesViewModel.DefaultUntouched;

    public static string LabelForHash(uint hash) => hash switch
    {
        AbyssGatesViewModel.DefaultUntouched => "Locked (untouched)",
        AbyssGatesViewModel.ActivatedCrossed => "Unlocked (activated)",
        AbyssGatesViewModel.IdleDecoration   => "Idle (scenery)",
        _                                    => $"Unknown (0x{hash:X8})",
    };

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private void Unlock() => _parent.Apply(this, AbyssGatesViewModel.ActivatedCrossed);

    [RelayCommand(CanExecute = nameof(CanLock))]
    private void Lock() => _parent.Apply(this, AbyssGatesViewModel.DefaultUntouched);
}
