using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// View-model for the Tools → Rename Mercenary dialog. Enumerates every
/// <c>MercenarySaveData</c> entry under the loaded save's
/// <c>MercenaryClanSaveData._mercenaryDataList</c> and exposes a
/// per-row "new name" textbox + Apply.
/// </summary>
/// <remarks>
/// <para>
/// "Pet rename" in CRIMSON-DESERT-SAVE-EDITOR is mercenary rename in
/// the save model — pets / horses / companions are all stored as
/// mercenary entries; equip count == 0 typically infers an animal.
/// </para>
/// <para>
/// Row identification: the dialog shows the character/template name
/// resolved from <c>_characterKey</c> via
/// <see cref="LocalizationProvider.ResolveByFieldTypeName"/> — same
/// source as the main window's <c>mercenaryDataList</c> Name column.
/// That makes rows recognisable as e.g. "Damiane / 德米安" instead of
/// raw numbers. The user's <i>custom</i> rename (stored as
/// <c>InlineBytes</c> in <c>_mercenaryName</c>) is still not shown —
/// the FFI exposes an
/// <see cref="ISaveLoader.SetInlineBytesField">inline_bytes setter</see>
/// but no symmetric getter. A read-side FFI is the natural next
/// iteration.
/// </para>
/// </remarks>
public sealed partial class RenameMercenaryViewModel : ObservableObject
{
    /// <summary>
    /// Class name of the top-level block holding the mercenary list.
    /// One per save.
    /// </summary>
    private const string MercenaryClanClass = "MercenaryClanSaveData";

    /// <summary>Name of the list field inside <see cref="MercenaryClanClass"/>.</summary>
    private const string MercenaryListField = "_mercenaryDataList";

    /// <summary>
    /// Element class name we expect under the list. Used as a sanity
    /// guard when the schema drifts between game patches.
    /// </summary>
    private const string MercenaryElementClass = "MercenarySaveData";

    /// <summary>Per-element fields we read / write.</summary>
    private const string MercenaryNumberField = "_mercenaryNo";
    private const string MercenaryCharacterKeyField = "_characterKey";
    private const string MercenaryNameField = "_mercenaryName";

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private readonly ChangeJournal _journal;
    /// <summary>
    /// Portrait resolver — same instance as <c>_localization.Portraits</c>.
    /// Captured separately so <see cref="MercenaryRow"/> can call it
    /// without re-traversing the provider hierarchy on every row.
    /// </summary>
    internal PortraitProvider Portraits => _localization.Portraits;
    private readonly string _savePath;
    private readonly int _topBlockIdx;
    private readonly int _listFieldIdx;
    private readonly int _nameFieldIdx;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<MercenaryRow> Mercenaries { get; } = new();

    private RenameMercenaryViewModel(
        ISaveLoader loader,
        LocalizationProvider localization,
        ChangeJournal journal,
        string savePath,
        int topBlockIdx,
        int listFieldIdx,
        int nameFieldIdx)
    {
        _loader = loader;
        _localization = localization;
        _journal = journal;
        _savePath = savePath;
        _topBlockIdx = topBlockIdx;
        _listFieldIdx = listFieldIdx;
        _nameFieldIdx = nameFieldIdx;
    }

    /// <summary>
    /// Build the view-model against a loaded save. Returns null when
    /// the save has no <c>MercenaryClanSaveData</c> block (e.g. the
    /// player hasn't recruited any mercenaries / pets yet) so the
    /// caller can surface "nothing to rename" rather than opening
    /// an empty window.
    /// </summary>
    public static RenameMercenaryViewModel? TryCreate(
        ISaveLoader loader,
        LocalizationProvider localization,
        ChangeJournal journal,
        string savePath,
        IReadOnlyList<BlockSummary> blocks)
    {
        BlockSummary? top = null;
        foreach (var b in blocks)
        {
            if (string.Equals(b.ClassName, MercenaryClanClass, StringComparison.Ordinal))
            {
                top = b;
                break;
            }
        }
        if (top is null)
        {
            return null;
        }

        BlockDetails details;
        try
        {
            details = loader.LoadBlockDetails(savePath, top.Index);
        }
        catch (CrimsonSaveException)
        {
            return null;
        }

        // Locate the _mercenaryDataList field index inside MercenaryClanSaveData.
        DecodedFieldRow? listField = null;
        foreach (var f in details.Fields)
        {
            if (string.Equals(f.Name, MercenaryListField, StringComparison.Ordinal))
            {
                listField = f;
                break;
            }
        }
        if (listField is null || listField.Elements is not { Count: > 0 } elements)
        {
            return null;
        }

        // First element drives the per-element schema; assume it's
        // representative of all entries (per-class field layout is
        // fixed in any given save).
        var firstElement = elements[0];
        if (!string.Equals(firstElement.ClassName, MercenaryElementClass, StringComparison.Ordinal))
        {
            return null;
        }
        int nameFieldIdx = -1;
        foreach (var f in firstElement.Fields)
        {
            if (string.Equals(f.Name, MercenaryNameField, StringComparison.Ordinal))
            {
                nameFieldIdx = f.FieldIndex;
                break;
            }
        }
        if (nameFieldIdx < 0)
        {
            return null;
        }

        var vm = new RenameMercenaryViewModel(
            loader, localization, journal, savePath, top.Index, listField.FieldIndex, nameFieldIdx);

        for (var i = 0; i < elements.Count; i++)
        {
            var row = BuildRow(vm, i, elements[i], localization);
            vm.Mercenaries.Add(row);
            // Kick off the portrait load eagerly so the thread pool
            // starts draining while the user reads the dialog header.
            // PortraitProvider.GetPortrait is safe under concurrent
            // access and the UI binding fills in as each one lands.
            row.StartPortraitLoad(vm.Portraits);
        }
        vm.StatusMessage = $"{elements.Count} mercenary entries loaded.";
        return vm;
    }

    /// <summary>Read identifying fields out of one decoded mercenary element.</summary>
    private static MercenaryRow BuildRow(
        RenameMercenaryViewModel vm, int index, BlockDetails element,
        LocalizationProvider localization)
    {
        ulong mercNo = 0;
        uint characterKey = 0;
        int equipCount = 0;
        foreach (var f in element.Fields)
        {
            if (string.Equals(f.Name, MercenaryNumberField, StringComparison.Ordinal)
                && TryParseScalarUInt(f.Value, out var mn))
            {
                mercNo = mn;
            }
            else if (string.Equals(f.Name, MercenaryCharacterKeyField, StringComparison.Ordinal)
                     && TryParseScalarUInt(f.Value, out var ck)
                     && ck <= uint.MaxValue)
            {
                characterKey = (uint)ck;
            }
            else if (f.Kind == "object_list")
            {
                // Heuristic for "equip count": the only object_list field
                // we expect on MercenarySaveData is the equipped-items
                // list (varies by patch but stays single-list). If the
                // schema grows additional object_lists in a future patch
                // this rolls them up; refine if it ever causes a
                // misleading display.
                equipCount += f.Elements?.Count ?? 0;
            }
        }
        // Resolve the character/template name from CharacterKey via the
        // same path the main-window mercenaryDataList Name column uses
        // (PALOC-backed character namespace). Empty when localization
        // isn't loaded (no game install configured) or when this
        // CharacterKey has no PALOC entry — we render that as a blank
        // cell rather than guessing.
        var resolvedName = characterKey == 0
            ? string.Empty
            : localization.ResolveByFieldTypeName("CharacterKey", characterKey);
        // Pull the characterinfo internal name (string_key) so the
        // category bucket can be derived from its prefix (Riding_Horse_*
        // / Riding_Wagon_* / Riding_Balloon_* / Animal_* / NHM_* / NOM_*
        // / NHW_*). Only ~3 mercenary CharKeys in 1.07 actually have an
        // NPC portrait shipped; the rest get a generic class glyph
        // sourced from this bucket. Null is fine — it just yields the
        // Unknown bucket.
        var internalName = characterKey == 0
            ? null
            : localization.LookupCharacterInternalName(characterKey);
        var category = ClassifyByInternalName(internalName);
        var row = new MercenaryRow(vm, index, mercNo, characterKey, equipCount,
                                   resolvedName, category);
        return row;
    }

    /// <summary>
    /// Bucket an internal name into a coarse class so the dialog can
    /// show a meaningful glyph when no NPC portrait DDS exists for the
    /// row. Prefix-based matching — fast, allocation-free, and
    /// covers every internal-name pattern seen across slot102 / 103 /
    /// 104 / 105 probes.
    /// </summary>
    private static MercenaryCategory ClassifyByInternalName(string? internalName)
    {
        if (string.IsNullOrEmpty(internalName))
        {
            return MercenaryCategory.Unknown;
        }
        // Order matters — Riding_Balloon_ must beat Riding_ generic.
        if (internalName.StartsWith("Riding_Balloon_", StringComparison.OrdinalIgnoreCase))
            return MercenaryCategory.Balloon;
        if (internalName.StartsWith("Riding_Wagon_", StringComparison.OrdinalIgnoreCase))
            return MercenaryCategory.Wagon;
        if (internalName.StartsWith("Riding_Horse_", StringComparison.OrdinalIgnoreCase)
            || internalName.StartsWith("Horse_", StringComparison.OrdinalIgnoreCase))
            return MercenaryCategory.Mount;
        if (internalName.StartsWith("Riding_", StringComparison.OrdinalIgnoreCase))
            return MercenaryCategory.Mount;     // covers Riding_Camel_, Riding_Bear_, etc.
        if (internalName.StartsWith("Animal_", StringComparison.OrdinalIgnoreCase))
            return MercenaryCategory.Animal;
        if (internalName.StartsWith("Pet_", StringComparison.OrdinalIgnoreCase))
            return MercenaryCategory.Pet;
        if (internalName.StartsWith("NHM_", StringComparison.OrdinalIgnoreCase)
            || internalName.StartsWith("NOM_", StringComparison.OrdinalIgnoreCase)
            || internalName.StartsWith("NHW_", StringComparison.OrdinalIgnoreCase)
            || internalName.StartsWith("FieldNPC_", StringComparison.OrdinalIgnoreCase))
            return MercenaryCategory.Npc;
        return MercenaryCategory.Unknown;
    }

    /// <summary>
    /// Apply a row's pending new name: UTF-8 encode the textbox value,
    /// write via <see cref="ISaveLoader.SetInlineBytesField"/>. Sets
    /// <see cref="MercenaryRow.AppliedName"/> on success.
    /// </summary>
    internal void ApplyRename(MercenaryRow row)
    {
        var newName = row.NewName ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(newName);
        var path = new[] { new PathStep((uint)_listFieldIdx, (uint)row.Index) };
        try
        {
            _loader.SetInlineBytesField(_topBlockIdx, path, _nameFieldIdx, bytes);
        }
        catch (CrimsonSaveException ex)
        {
            StatusMessage = $"Rename failed for index {row.Index}: {ex.Message}";
            row.LastError = ex.Message;
            return;
        }
        row.AppliedName = newName;
        row.LastError = null;
        StatusMessage =
            $"Renamed idx {row.Index} (MercNo {row.MercNo}) → "
            + (string.IsNullOrEmpty(newName)
                ? "(empty)"
                : $"\"{newName}\" ({bytes.Length} bytes UTF-8)");
        IsDirty = true;
        var identity = string.IsNullOrEmpty(row.ResolvedCharacterName)
            ? $"MercNo {row.MercNo}"
            : row.ResolvedCharacterName;
        _journal.Log("Rename Mercenary",
            string.IsNullOrEmpty(newName)
                ? $"Cleared custom name on {identity}"
                : $"Renamed {identity} → \"{newName}\"");
    }

    /// <summary>
    /// Becomes true after the first successful Apply. The hosting
    /// MainWindowViewModel reads this on dialog close to flip its own
    /// dirty flag so the user gets a "*" in the title until Save.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Pre-formatted scalar value (<c>"123 &lt;u32&gt;"</c>) →
    /// <see cref="ulong"/>. Returns false on signed / float / bytes
    /// values so the caller can skip the field instead of writing a
    /// wrong number.
    /// </summary>
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
        return ulong.TryParse(rawText, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out value);
    }
}

/// <summary>
/// Coarse class buckets a <c>MercenaryRow</c> can land in. Drives the
/// fallback glyph shown when no NPC portrait DDS resolves for the row.
/// Buckets reflect the in-game taxonomy surfaced by characterinfo
/// internal-name prefixes — not the user-facing display name.
/// </summary>
public enum MercenaryCategory
{
    /// <summary>Default. No internal-name match — show a neutral glyph.</summary>
    Unknown = 0,
    /// <summary>Named or generic NPC mercenary (NHM_* / NOM_* / NHW_* / FieldNPC_*).</summary>
    Npc,
    /// <summary>Horse / camel / bear / wolf etc. ridable mount (Riding_Horse_*, Riding_Bear_*, …).</summary>
    Mount,
    /// <summary>Cart / cloudcart / wagon (Riding_Wagon_*).</summary>
    Wagon,
    /// <summary>Hot-air balloon (Riding_Balloon_*).</summary>
    Balloon,
    /// <summary>Wild animal — stag, ibex, lion, etc. (Animal_*).</summary>
    Animal,
    /// <summary>Domestic pet (Pet_*).</summary>
    Pet,
}

/// <summary>
/// One mercenary row in the rename dialog. Tracks the identifying info
/// plus the user's pending new name.
/// </summary>
public sealed partial class MercenaryRow : ObservableObject
{
    private readonly RenameMercenaryViewModel _parent;

    public MercenaryRow(
        RenameMercenaryViewModel parent,
        int index,
        ulong mercNo,
        uint characterKey,
        int equipCount,
        string resolvedCharacterName,
        MercenaryCategory category)
    {
        _parent = parent;
        Index = index;
        MercNo = mercNo;
        CharacterKey = characterKey;
        EquipCount = equipCount;
        ResolvedCharacterName = resolvedCharacterName;
        Category = category;
    }

    public int Index { get; }
    public ulong MercNo { get; }
    public uint CharacterKey { get; }
    public int EquipCount { get; }

    /// <summary>
    /// Localized character/template name resolved from
    /// <see cref="CharacterKey"/> via the PALOC-backed character
    /// namespace (e.g. <c>"Damiane / 德米安"</c>). Empty when no
    /// PALOC entry exists or localization isn't loaded. NOT the
    /// user's custom in-save rename — that lives in
    /// <c>_mercenaryName</c> and still needs a read-side FFI.
    /// </summary>
    public string ResolvedCharacterName { get; }

    /// <summary>Display tag derived from <see cref="EquipCount"/>.</summary>
    public string TypeTag => EquipCount == 0 ? "Animal" : "Mercenary";

    /// <summary>
    /// Class bucket derived from the row's characterinfo internal
    /// name. Drives <see cref="CategoryGlyph"/> when no portrait
    /// loads — the fallback shape every row gets to render *something*
    /// even when Pearl Abyss doesn't ship a per-character portrait.
    /// </summary>
    public MercenaryCategory Category { get; }

    /// <summary>
    /// Unicode glyph shown in the Portrait cell when no actual
    /// portrait DDS resolved. Mirrors <see cref="Category"/> — kept
    /// here as a presentation-layer convenience so the cell template
    /// doesn't need a value converter.
    /// </summary>
    public string CategoryGlyph => Category switch
    {
        MercenaryCategory.Npc     => "👤",
        MercenaryCategory.Mount   => "🐎",
        MercenaryCategory.Wagon   => "🛒",
        MercenaryCategory.Balloon => "🎈",
        MercenaryCategory.Animal  => "🦌",
        MercenaryCategory.Pet     => "🐾",
        _                         => "❔",
    };

    /// <summary>
    /// True iff <see cref="Portrait"/> is populated. The cell template
    /// uses this to swap between the Image (real DDS-derived portrait)
    /// and the TextBlock fallback (<see cref="CategoryGlyph"/>).
    /// Recomputed on every <c>Portrait</c> change via the source-gen
    /// <c>NotifyPropertyChangedFor</c> trigger below.
    /// </summary>
    public bool HasPortrait => Portrait is not null;

    /// <summary>
    /// NPC portrait for this row's <see cref="CharacterKey"/>, lazily
    /// populated by <see cref="StartPortraitLoad"/> on the thread
    /// pool. Starts null; remains null when the bridge has no match
    /// above <see cref="PortraitProvider.MinAcceptableScore"/> or when
    /// Pearl Abyss simply ships no portrait DDS for the key. The
    /// DataGrid binding re-evaluates automatically via the
    /// ObservableProperty notification.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPortrait))]
    private Bitmap? _portrait;

    /// <summary>
    /// Kick off the background portrait load for this row. Invoked
    /// once at row construction. The PortraitProvider's own cache
    /// (memory + disk) makes repeat opens of the dialog cheap; on
    /// first open the dialog renders rows immediately with empty
    /// portrait cells and they fill in as the thread pool drains.
    /// </summary>
    internal void StartPortraitLoad(PortraitProvider portraits)
    {
        if (CharacterKey == 0 || !portraits.IsAvailable)
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
            // Hop to the UI thread to publish — Bitmap can be
            // constructed off-thread but the binding update has to be
            // marshalled.
            Dispatcher.UIThread.Post(() => Portrait = bmp);
        });
    }

    [ObservableProperty]
    private string? _newName;

    [ObservableProperty]
    private string? _appliedName;

    [ObservableProperty]
    private string? _lastError;

    // Apply is always enabled. Empty NewName clears the custom rename
    // (see ApplyRename's "(empty)" / "Cleared custom name on …" branch),
    // which is a legitimate user action. The earlier CanApply gate
    // depended on a manually-implemented bool that wasn't wired to
    // ApplyCommand.NotifyCanExecuteChanged — the button effectively
    // stuck disabled until you tabbed out of the textbox, which
    // surfaced as "Apply 沒反應" against an obviously-typed value.
    // Dropping the gate fixes the bug AND enables the clear-name flow
    // without an extra "Clear" button.
    [RelayCommand]
    private void Apply() => _parent.ApplyRename(this);
}
