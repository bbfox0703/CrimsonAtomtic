namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// The save-schema names the editor looks up by name (Rule 7: one place for
/// magic strings). Field indices move between game patches; these names are
/// what stays, so every lookup goes through a constant here and a rename in
/// the game shows up as one edit.
/// </summary>
public static class SaveSchemaNames
{
    // ── Top-level block / element classes ───────────────────────────────
    public const string InventorySaveData = "InventorySaveData";
    public const string ItemSaveData = "ItemSaveData";
    public const string QuestSaveData = "QuestSaveData";
    public const string MissionStateData = "MissionStateData";
    public const string MercenaryClanSaveData = "MercenaryClanSaveData";
    public const string FactionSaveData = "FactionSaveData";

    // ── Inventory and items ─────────────────────────────────────────────
    public const string InventoryList = "_inventorylist";
    public const string InventoryKey = "_inventoryKey";
    public const string ItemList = "_itemList";
    public const string ItemKey = "_itemKey";
    public const string StackCount = "_stackCount";
    public const string SlotNo = "_slotNo";
    public const string ItemNo = "_itemNo";
    public const string TransferredItemKey = "_transferredItemKey";
    public const string IsNewMark = "_isNewMark";
    public const string ValidSocketCount = "_validSocketCount";

    // ── Dye ─────────────────────────────────────────────────────────────
    public const string DyeSlotNo = "_dyeSlotNo";
    public const string DyeColorR = "_dyeColorR";
    public const string DyeColorG = "_dyeColorG";
    public const string DyeColorB = "_dyeColorB";
    public const string DyeColorA = "_dyeColorA";
    public const string GrimeOpacity = "_grimeOpacity";
    public const string DyeColorGroupInfoKey = "_dyeColorGroupInfoKey";
    public const string TexturePalleteKey = "_texturePalleteKey";

    // ── Stores ──────────────────────────────────────────────────────────
    public const string StoreDataList = "_storeDataList";
    public const string StoreSoldItemDataList = "_storeSoldItemDataList";

    // ── Quests and missions ─────────────────────────────────────────────
    public const string Key = "_key";
    public const string State = "_state";
    public const string CompletedTime = "_completedTime";
    public const string UsedTagList = "_usedTagList";
    public const string MissionStateList = "_missionStateList";

    // ── Mercenaries and characters ──────────────────────────────────────
    public const string MercenaryDataList = "_mercenaryDataList";
    public const string MercenaryNo = "_mercenaryNo";
    public const string CharacterKey = "_characterKey";

    // ── Factions ────────────────────────────────────────────────────────
    public const string OwnerFactionKey = "_ownerFactionKey";

    // ── Other names the editors look up ───────────────────────────────
    public const string CurrentEndurance = "_currentEndurance";
    public const string DueDate = "_dueDate";
    public const string FactionElementSaveDataList = "_factionElementSaveDataList";
    public const string FactionRelationDataList = "_factionRelationDataList";
    public const string FieldGimmickSaveDataKey = "_fieldGimmickSaveDataKey";
    public const string FieldGimmickSaveDataList = "_fieldGimmickSaveDataList";
    public const string FieldSaveData = "FieldSaveData";
    public const string ItemDyeDataList = "_itemDyeDataList";
    public const string ItemDyeSaveData = "ItemDyeSaveData";
    public const string KnowledgeSaveData = "KnowledgeSaveData";
    public const string LeaderCharacterKey = "_leaderCharacterKey";
    public const string LearnedFieldTime = "_learnedFieldTime";
    public const string List = "_list";
    public const string MercenaryName = "_mercenaryName";
    public const string MercenarySaveData = "MercenarySaveData";
    public const string RelationGroupKey = "_relationGroupKey";
    public const string RelationTypeRaw = "_relationTypeRaw";
    public const string SocketSaveDataList = "_socketSaveDataList";
    public const string StoreKey = "_storeKey";
    public const string StoreSaveData = "StoreSaveData";
    public const string TargetFactionKey = "_targetFactionKey";
    public const string TimeWhenPushItem = "_timeWhenPushItem";
}

/// <summary>
/// The game's archive groups the editor reads assets from (Rule 7). The
/// gamedata tables' own group is <c>GameDataLayout.GameDataGroup</c>.
/// </summary>
public static class GameDataGroups
{
    /// <summary>Item icons and NPC portraits.</summary>
    public const string Ui = "0012";

    /// <summary>Character and gear prefabs (the dye slot join reads it).</summary>
    public const string Prefabs = "0009";
}
