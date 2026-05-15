using System.Collections.ObjectModel;
using System.Text;
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
/// "Pet rename" in the predecessor save editor is mercenary rename in
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
        string savePath,
        int topBlockIdx,
        int listFieldIdx,
        int nameFieldIdx)
    {
        _loader = loader;
        _localization = localization;
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
            loader, localization, savePath, top.Index, listField.FieldIndex, nameFieldIdx);

        for (var i = 0; i < elements.Count; i++)
        {
            var row = BuildRow(vm, i, elements[i], localization);
            vm.Mercenaries.Add(row);
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
        var row = new MercenaryRow(vm, index, mercNo, characterKey, equipCount, resolvedName);
        return row;
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
        string resolvedCharacterName)
    {
        _parent = parent;
        Index = index;
        MercNo = mercNo;
        CharacterKey = characterKey;
        EquipCount = equipCount;
        ResolvedCharacterName = resolvedCharacterName;
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

    [ObservableProperty]
    private string? _newName;

    [ObservableProperty]
    private string? _appliedName;

    [ObservableProperty]
    private string? _lastError;

    /// <summary>Apply button only enables once the user has typed something.</summary>
    public bool CanApply => !string.IsNullOrEmpty(NewName);

    partial void OnNewNameChanged(string? value) =>
        OnPropertyChanged(nameof(CanApply));

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => _parent.ApplyRename(this);
}
