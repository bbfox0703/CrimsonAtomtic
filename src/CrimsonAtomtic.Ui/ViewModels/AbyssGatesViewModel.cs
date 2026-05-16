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
/// top-level <c>FieldGimmickSaveData</c> block in the loaded save,
/// filters down to the abyss-gate / hyperspace-ruin subset by
/// cross-referencing each block's <c>_gimmickInfoKey</c> against the
/// allowlist built from <c>gimmickinfo.pabgb</c>, and exposes a
/// per-row Lock/Unlock toggle.
///
/// <para>
/// Touches the <b>gate state</b> layer of unlock: writes
/// <c>_initStateNameHash</c> via the existing scalar setter. The
/// companion <b>discovery flag</b> layer (gates show up on the map)
/// is handled by Tools → Unlock All Abyss Gates which writes
/// <c>KnowledgeSaveData._list</c>.
/// </para>
///
/// <para>
/// Per the upstream survey at
/// <c>vendor/crimson-rs/docs/abyss-gate-map.md</c>: only three
/// <c>_initStateNameHash</c> values appear across all observed abyss
/// gates — <see cref="DefaultUntouched"/> (player hasn't interacted),
/// <see cref="ActivatedCrossed"/> (player has crossed / activated),
/// and <see cref="IdleDecoration"/> (standstones / scenery that
/// never transitions). The toggle flips between Default and
/// Activated; Idle and Unknown rows are read-only.
/// </para>
///
/// <para>
/// <b>v1 limitation:</b> walks top-level <c>FieldGimmickSaveData</c>
/// blocks only. Nested <c>FieldGimmickSaveData</c> elements inside
/// container <c>ObjectList</c> fields are not currently surfaced;
/// the upstream probe found 4,264 total <c>FieldGimmickSaveData</c>
/// of which 356 are abyss-related, but the top-level vs nested
/// split isn't quantified. If a known abyss gate is missing from
/// the dialog, that's the gap.
/// </para>
/// </summary>
public sealed partial class AbyssGatesViewModel : ObservableObject
{
    public const uint DefaultUntouched = 0x866c7489;
    public const uint ActivatedCrossed = 0xe300acfe;
    public const uint IdleDecoration   = 0x150b14d0;

    /// <summary>Class name of the per-gimmick save block.</summary>
    private const string FieldGimmickClass = "FieldGimmickSaveData";

    /// <summary>Field names we read off each block.</summary>
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
    private readonly string _savePath;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<AbyssGateRow> Rows { get; } = new();

    private AbyssGatesViewModel(ISaveLoader loader, LocalizationProvider localization, string savePath)
    {
        _loader = loader;
        _localization = localization;
        _savePath = savePath;
    }

    /// <summary>
    /// Build a VM by walking the save asynchronously. Reports
    /// progress through <paramref name="progress"/>; the dialog
    /// shows a "Scanning N/Total blocks…" footer while this runs.
    /// </summary>
    public static async Task<AbyssGatesViewModel> CreateAsync(
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath,
        IReadOnlyList<BlockSummary> blocks,
        IProgress<(int Done, int Total)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(blocks);

        var vm = new AbyssGatesViewModel(loader, localization, savePath);

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

        // Collect candidate top-level blocks first so the progress
        // total is accurate.
        var candidates = new List<BlockSummary>();
        foreach (var b in blocks)
        {
            if (string.Equals(b.ClassName, FieldGimmickClass, StringComparison.Ordinal))
            {
                candidates.Add(b);
            }
        }

        var built = new List<AbyssGateRow>();
        await Task.Run(() =>
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                progress?.Report((i, candidates.Count));
                BlockDetails details;
                try
                {
                    details = loader.LoadBlockDetails(savePath, candidates[i].Index);
                }
                catch (CrimsonSaveException)
                {
                    continue;
                }
                if (TryBuildRow(vm, candidates[i].Index, details, allowlist, localization,
                                out var row))
                {
                    built.Add(row);
                }
            }
            progress?.Report((candidates.Count, candidates.Count));
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
            ? $"Scanned {candidates.Count} top-level {FieldGimmickClass} block(s) — "
              + "no abyss-gate gimmicks matched the allowlist."
            : $"Loaded {built.Count} abyss-gate row(s) from {candidates.Count} "
              + $"top-level {FieldGimmickClass} block(s). "
              + "Toggle the Lock state to flip _initStateNameHash; reload the save to revert.";
        return vm;
    }

    private static bool TryBuildRow(
        AbyssGatesViewModel parent,
        int blockIdx,
        BlockDetails details,
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

        foreach (var f in details.Fields)
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

        row = new AbyssGateRow(
            parent,
            blockIdx,
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
            _loader.SetScalarField(row.BlockIndex, row.StateHashFieldIndex, bytes);
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

    public AbyssGateRow(
        AbyssGatesViewModel parent,
        int blockIndex,
        int stateHashFieldIndex,
        uint gimmickInfoKey,
        string gimmickName,
        string ownerLevelName,
        uint fieldGimmickSaveDataKey,
        uint currentStateHash)
    {
        _parent = parent;
        BlockIndex = blockIndex;
        StateHashFieldIndex = stateHashFieldIndex;
        GimmickInfoKey = gimmickInfoKey;
        GimmickName = gimmickName;
        OwnerLevelName = ownerLevelName;
        FieldGimmickSaveDataKey = fieldGimmickSaveDataKey;
        _currentStateHash = currentStateHash;
    }

    public int BlockIndex { get; }
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
