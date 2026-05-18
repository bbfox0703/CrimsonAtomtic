using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// One option in the Add-Dye slot picker. Carries the slot index (the
/// value that lands in the save's <c>_dyeSlotNo</c> field) plus the
/// default material name from <c>partprefabdyeslotinfo</c> for
/// orientation ("slot 0 = cloth", "slot 1 = leather", etc.).
/// </summary>
public sealed record DyeSlotPickerOption(int SlotIndex, string DisplayLabel);

/// <summary>
/// VM for the small modal that opens after the user clicks "+ Add"
/// on an un-dyed equipped item. Lists slots <c>0..N-1</c> with their
/// default material names so the user can pick a valid slot for the
/// newly-materialized dye element. Closes the soundness gap that
/// pulled the original Add UX (commit 5b107d4) — each item only
/// accepts a per-prefab-specific subset of slot numbers, so the
/// dialog only offers slots known to be valid.
/// </summary>
public sealed partial class DyeSlotPickerViewModel : ObservableObject
{
    public DyeSlotPickerViewModel(
        uint itemKey,
        string itemDisplayLabel,
        int slotCount,
        LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ItemDisplayLabel = itemDisplayLabel;
        ItemKey = itemKey;
        Options = BuildOptions(itemKey, slotCount, localization);
        SelectedOption = Options.Count > 0 ? Options[0] : null;
    }

    public uint ItemKey { get; }
    public string ItemDisplayLabel { get; }
    public ObservableCollection<DyeSlotPickerOption> Options { get; }

    [ObservableProperty]
    private DyeSlotPickerOption? _selectedOption;

    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// Build the slot-option list. For each slot 0..N-1, try to label
    /// it with the prefab's first default-material name; fall back to
    /// "Slot N" when the default-material lookup misses (e.g.
    /// partprefab resolved but the per-slot bridge returned null).
    /// </summary>
    private static ObservableCollection<DyeSlotPickerOption> BuildOptions(
        uint itemKey, int slotCount, LocalizationProvider localization)
    {
        var options = new ObservableCollection<DyeSlotPickerOption>();
        if (slotCount <= 0) return options;

        // Resolve the item's first partprefab key so we can pull each
        // slot's default-material name for label hints. Best-effort —
        // any failure here just falls through to numeric labels.
        var slotInfo = localization.DyeSlotInfo;
        uint? prefabKey = null;
        try
        {
            prefabKey = localization.ItemPartPrefab?.LookupFirstPrefabKey(itemKey);
        }
        catch (CrimsonSaveException) { }

        for (var i = 0; i < slotCount; i++)
        {
            string label;
            string? material = null;
            if (prefabKey is { } pk && slotInfo is not null)
            {
                try
                {
                    material = slotInfo.LookupSlotDefaultMaterial(pk, i, 0);
                }
                catch (CrimsonSaveException) { }
            }
            if (!string.IsNullOrEmpty(material))
            {
                label = string.Format(
                    CultureInfo.InvariantCulture,
                    "Slot {0} ({1})", i, material);
            }
            else
            {
                label = string.Format(
                    CultureInfo.InvariantCulture, "Slot {0}", i);
            }
            options.Add(new DyeSlotPickerOption(i, label));
        }
        return options;
    }
}
