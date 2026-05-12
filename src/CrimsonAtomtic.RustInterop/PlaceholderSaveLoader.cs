using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Stub <see cref="ISaveLoader"/> that returns canned data. Used while
/// the C ABI on <c>vendor/crimson-rs</c> is being built out; replaced by
/// a real P/Invoke loader once <c>crimson_rs.dll</c> exposes
/// <c>extern "C"</c> entry points.
/// </summary>
public sealed class PlaceholderSaveLoader : ISaveLoader
{
    public SaveSummary Load(string savePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        cancellationToken.ThrowIfCancellationRequested();

        // Return a deterministic placeholder so the UI shows *something*
        // when the user opens a save before the C ABI is wired up. The
        // numbers below come from the live 1.06 slot0 we've been testing
        // against — see docs/save-body-format.md for context.
        return new SaveSummary(
            Source: savePath,
            SlotName: Path.GetFileName(Path.GetDirectoryName(savePath)) ?? "",
            Version: 2,
            Flags: 0x0080,
            PayloadSize: 1_597_621,
            UncompressedSize: 5_226_599,
            HmacOk: true,
            SchemaTypeCount: 101,
            TocEntryCount: 1_112,
            TotalBlockBytes: 5_172_506,
            Blocks:
            [
                new BlockSummary( 0,  0, "CharacterStatusSaveData",         54_093,         40, 9, 9),
                new BlockSummary( 1,  3, "CustomizationSaveData",           54_133,        287, 3, 3),
                new BlockSummary( 2, 14, "SubLevelSaveData",                54_420,        890, 2, 2),
                new BlockSummary( 3, 20, "EquipmentSaveData",               55_310,      5_922, 4, 4),
                new BlockSummary( 4, 18, "GameEventSaveData",               61_232,          8, 0, 0),
                new BlockSummary( 5, 19, "NPCScheduleStageManagerSaveData", 61_240,    439_148, 1, 1),
                new BlockSummary( 6, 29, "FactionSpawnStageManagerSaveData",500_388,    83_163, 1, 1),
                new BlockSummary( 7, 32, "InventoryItemContentsSaveData",  583_551,        827, 1, 1),
                new BlockSummary( 8, 41, "InventorySaveData",              584_378,    132_290, 4, 4),
                new BlockSummary( 9, 38, "StoreSaveData",                  716_668,    481_351, 1, 1),
            ]);
    }
}
