using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// One option in the Add-Dye slot picker. Carries the dye slot number (the
/// value that lands in the save's <c>_dyeSlotNo</c> field) plus its default
/// material name from <c>partprefabdyeslotinfo</c> for orientation
/// ("slot 0 = cloth", "slot 4 = leather", etc.).
/// </summary>
public sealed record DyeSlotPickerOption(int SlotIndex, string DisplayLabel);

/// <summary>
/// VM for the small modal that opens after the user clicks "+ Add"
/// on an un-dyed equipped item. Lists the item's dye slot numbers (sparse,
/// from <see cref="LocalizationProvider.LookupDyeSlotNumbers"/>) with their
/// default material names so the user can pick a valid slot for the
/// newly-materialized dye element — each item only accepts its prefab's
/// own subset of numbers, so the dialog only offers those.
/// </summary>
public sealed partial class DyeSlotPickerViewModel : ObservableObject
{
    public DyeSlotPickerViewModel(
        uint itemKey,
        string itemDisplayLabel,
        IReadOnlyList<int> dyeSlotNumbers,
        LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(dyeSlotNumbers);
        ArgumentNullException.ThrowIfNull(localization);
        ItemDisplayLabel = itemDisplayLabel;
        ItemKey = itemKey;
        Options = BuildOptions(itemKey, dyeSlotNumbers, localization);
        SelectedOption = Options.Count > 0 ? Options[0] : null;
    }

    public uint ItemKey { get; }
    public string ItemDisplayLabel { get; }
    public ObservableCollection<DyeSlotPickerOption> Options { get; }

    [ObservableProperty]
    private DyeSlotPickerOption? _selectedOption;

    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// Build the slot-option list: one entry per dye slot number, labelled
    /// with its default material ("Slot 4 (leather)"), or "Slot N" when the
    /// material is unnamed.
    /// </summary>
    private static ObservableCollection<DyeSlotPickerOption> BuildOptions(
        uint itemKey, IReadOnlyList<int> dyeSlotNumbers, LocalizationProvider localization)
    {
        var options = new ObservableCollection<DyeSlotPickerOption>();
        foreach (var n in dyeSlotNumbers)
        {
            var material = localization.DescribeDyeSlot(itemKey, n);
            var label = string.IsNullOrEmpty(material)
                ? string.Format(CultureInfo.InvariantCulture, "Slot {0}", n)
                : string.Format(CultureInfo.InvariantCulture, "Slot {0} ({1})", n, material);
            options.Add(new DyeSlotPickerOption(n, label));
        }
        return options;
    }
}
