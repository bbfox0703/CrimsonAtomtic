using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// A faction's relation to another, as the save expresses it. The numeric
/// values are <c>FactionRelationType</c>'s; <see cref="GroupDefault"/> is the
/// absence of a relation row.
/// </summary>
public enum FactionRelation
{
    /// <summary>No row: the two factions' relation groups decide.</summary>
    GroupDefault = -1,

    /// <summary>A row whose <c>_relationTypeRaw</c> is absent (the game's own shape) or 0.</summary>
    Neutral = 0,

    /// <summary>Confirmed in-game 2026-11-27: the other faction's NPCs attack.</summary>
    War = 1,

    Agreement = 2,

    BloodAlly = 3,

    /// <summary>In the game's name table; no save has used it.</summary>
    ByGroup = 4,
}

/// <summary>One faction and its relation with the player's faction, both directions.</summary>
public sealed record FactionRelationEntry(
    uint FactionKey,
    uint LeaderCharacterKey,
    uint RelationGroupKey,
    FactionRelation PlayerToFaction,
    FactionRelation FactionToPlayer)
{
    /// <summary>The two rows disagree (the game expects them to match).</summary>
    public bool IsMismatched => PlayerToFaction != FactionToPlayer;
}

/// <summary>A requested relation between the player and <see cref="FactionKey"/>.</summary>
public sealed record FactionRelationChange(uint FactionKey, FactionRelation Relation);

/// <summary>Outcome of <see cref="FactionRelationService.Apply"/>.</summary>
public sealed record FactionRelationApplyResult(int Applied, string? Error);

/// <summary>
/// Reads and writes faction relations with the player's faction.
/// </summary>
/// <remarks>
/// <para>
/// <c>FactionSaveData._factionElementSaveDataList[i]</c> is one faction;
/// its <c>_factionRelationDataList[j]</c> rows are
/// <c>{_targetFactionKey, _relationTypeRaw u8, _dueDate u32}</c>. Every pair
/// the game wrote is symmetric (A→B and B→A agree, which its own data check
/// demands), so a change is written both ways. <c>_dueDate</c> is
/// <c>0xFFFFFFFF</c> on every row seen, and a new row gets the same.
/// </para>
/// <para>
/// Most factions have no row at all, and hostility then comes from their
/// relation group (<c>HostileCombat</c>, <c>Monster_Common</c>, …). A row
/// overrides it. A row with <c>_relationTypeRaw</c> absent is how the game
/// writes Neutral, so Neutral keeps that shape. Setting
/// <see cref="FactionRelation.GroupDefault"/> removes the rows; removing a
/// faction's last row makes the list absent again, the game's shape for a
/// faction with none.
/// </para>
/// <para>
/// The player's faction is the element whose leader is <c>CharacterKey 1</c>
/// (Kliff); <c>Faction_Graymane</c> (1000000) on every save seen.
/// </para>
/// </remarks>
public sealed class FactionRelationService(ISaveLoader loader)
{
    private const string FactionClass = "FactionSaveData";
    private const string ElementList = "_factionElementSaveDataList";
    private const string RelationList = "_factionRelationDataList";
    private const string OwnerField = "_ownerFactionKey";
    private const string LeaderField = "_leaderCharacterKey";
    private const string GroupField = "_relationGroupKey";
    private const string TargetField = "_targetFactionKey";
    private const string TypeField = "_relationTypeRaw";
    private const string DueDateField = "_dueDate";

    /// <summary><c>CharacterKey</c> of Kliff, the player character.</summary>
    private const uint PlayerCharacterKey = 1;

    /// <summary>Fallback when no element names Kliff as leader.</summary>
    public const uint PlayerFactionFallback = 1000000;

    private const uint PermanentDueDate = uint.MaxValue;

    /// <summary>Every faction except the player's, with its relation to the player.</summary>
    public IReadOnlyList<FactionRelationEntry> Scan(string savePath, IReadOnlyList<BlockSummary> blocks)
    {
        if (Load(savePath, blocks) is not { } ctx)
        {
            return [];
        }
        var result = new List<FactionRelationEntry>();
        foreach (var e in ctx.Elements)
        {
            if (Key(e, OwnerField) is not { } key || key == ctx.PlayerFaction)
            {
                continue;
            }
            result.Add(new FactionRelationEntry(
                key,
                Key(e, LeaderField) ?? 0,
                Key(e, GroupField) ?? 0,
                ReadSide(ctx, ctx.PlayerFaction, key),
                ReadSide(ctx, key, ctx.PlayerFaction)));
        }
        return result;
    }

    /// <summary>The player's faction key in this save, or null without a faction block.</summary>
    public uint? PlayerFaction(string savePath, IReadOnlyList<BlockSummary> blocks) =>
        Load(savePath, blocks)?.PlayerFaction;

    /// <summary>
    /// Write each change in both directions. Stops at the first error; the
    /// count says how many changes fully landed before it.
    /// </summary>
    public FactionRelationApplyResult Apply(
        string savePath, IReadOnlyList<BlockSummary> blocks, IReadOnlyList<FactionRelationChange> changes)
    {
        if (Load(savePath, blocks) is not { } first)
        {
            return new FactionRelationApplyResult(0, UiText.Get("RelErrNoBlock", "This save has no FactionSaveData block."));
        }
        var player = first.PlayerFaction;
        var applied = 0;
        try
        {
            foreach (var c in changes)
            {
                if (c.FactionKey == player)
                {
                    continue;
                }
                if (ApplySide(savePath, blocks, player, c.FactionKey, c.Relation) is { } e1)
                {
                    return new FactionRelationApplyResult(applied, e1);
                }
                if (ApplySide(savePath, blocks, c.FactionKey, player, c.Relation) is { } e2)
                {
                    return new FactionRelationApplyResult(applied, e2);
                }
                applied++;
            }
        }
        catch (CrimsonSaveException ex)
        {
            return new FactionRelationApplyResult(applied, ex.Message);
        }
        return new FactionRelationApplyResult(applied, null);
    }

    /// <summary>Make <paramref name="owner"/>'s row toward <paramref name="target"/> express <paramref name="relation"/>.</summary>
    private string? ApplySide(string savePath, IReadOnlyList<BlockSummary> blocks, uint owner, uint target, FactionRelation relation)
    {
        var ctx = Load(savePath, blocks)!;
        if (ctx.IndexOf(owner) is not { } ei)
        {
            return UiText.Format("RelErrNoFaction", "Faction {0} is not in this save's faction list.", owner);
        }
        var elementPath = new[] { new PathStep((uint)ctx.ElementListField, (uint)ei) };
        var list = SaveFieldValues.Field(ctx.Elements[ei], RelationList)
                   ?? throw new InvalidOperationException($"{RelationList} missing from the faction element schema");
        var rows = list.Present ? list.Elements ?? [] : [];
        var ri = RowIndex(rows, target);

        if (relation == FactionRelation.GroupDefault)
        {
            if (ri is { } r)
            {
                if (rows.Count == 1)
                {
                    loader.SetObjectListPresent(ctx.Block, elementPath, list.FieldIndex, makePresent: false);
                }
                else
                {
                    loader.ListRemoveElement(ctx.Block, elementPath, list.FieldIndex, r);
                }
            }
            return null;
        }

        if (ri is not { } row)
        {
            if (!list.Present || rows.Count == 0)
            {
                // Materialises a one-element list; the element's class comes
                // from a sibling faction that has rows.
                loader.SetObjectListPresent(ctx.Block, elementPath, list.FieldIndex, makePresent: true);
                row = 0;
            }
            else
            {
                loader.ListCloneElement(ctx.Block, elementPath, list.FieldIndex, sourceIndex: 0, destinationIndex: rows.Count);
                row = rows.Count;
            }
            var fresh = RowAt(savePath, blocks, ei, row);
            SetScalar(ctx.Block, RowPath(ctx, ei, list.FieldIndex, row), fresh, TargetField, BitConverter.GetBytes(target));
            SetScalar(ctx.Block, RowPath(ctx, ei, list.FieldIndex, row), fresh, DueDateField, BitConverter.GetBytes(PermanentDueDate));
        }

        var current = RowAt(savePath, blocks, ei, row);
        var typeField = SaveFieldValues.Field(current, TypeField)
                        ?? throw new InvalidOperationException($"{TypeField} missing from the relation row schema");
        var path = RowPath(ctx, ei, list.FieldIndex, row);
        if (relation == FactionRelation.Neutral)
        {
            if (typeField.Present)
            {
                loader.SetScalarFieldPresent(ctx.Block, path, typeField.FieldIndex, makePresent: false, ReadOnlySpan<byte>.Empty);
            }
        }
        else
        {
            SetScalar(ctx.Block, path, current, TypeField, [(byte)relation]);
        }
        return null;
    }

    private void SetScalar(int block, PathStep[] path, BlockDetails element, string field, byte[] bytes)
    {
        var f = SaveFieldValues.Field(element, field)
                ?? throw new InvalidOperationException($"{field} missing from the relation row schema");
        if (f.Present)
        {
            loader.SetScalarField(block, path, f.FieldIndex, bytes);
        }
        else
        {
            loader.SetScalarFieldPresent(block, path, f.FieldIndex, makePresent: true, bytes);
        }
    }

    private static PathStep[] RowPath(Context ctx, int elementIndex, int listField, int row) =>
    [
        new PathStep((uint)ctx.ElementListField, (uint)elementIndex),
        new PathStep((uint)listField, (uint)row),
    ];

    private BlockDetails RowAt(string savePath, IReadOnlyList<BlockSummary> blocks, int elementIndex, int row)
    {
        var ctx = Load(savePath, blocks)!;
        return SaveFieldValues.Field(ctx.Elements[elementIndex], RelationList)!.Elements![row];
    }

    private static int? RowIndex(IReadOnlyList<BlockDetails> rows, uint target)
    {
        for (var j = 0; j < rows.Count; j++)
        {
            if (Key(rows[j], TargetField) == target)
            {
                return j;
            }
        }
        return null;
    }

    private static FactionRelation ReadSide(Context ctx, uint owner, uint target)
    {
        if (ctx.IndexOf(owner) is not { } ei
            || SaveFieldValues.Field(ctx.Elements[ei], RelationList) is not { Present: true, Elements: { } rows }
            || RowIndex(rows, target) is not { } r)
        {
            return FactionRelation.GroupDefault;
        }
        return SaveFieldValues.Field(rows[r], TypeField) is { } t && SaveFieldValues.TryU64(t) is { } v
            ? (FactionRelation)(int)v
            : FactionRelation.Neutral;
    }

    private static uint? Key(BlockDetails e, string field) =>
        SaveFieldValues.Field(e, field) is { } f && SaveFieldValues.TryU64(f) is { } v ? (uint)v : null;

    private sealed record Context(int Block, int ElementListField, IReadOnlyList<BlockDetails> Elements, uint PlayerFaction)
    {
        public int? IndexOf(uint faction)
        {
            for (var i = 0; i < Elements.Count; i++)
            {
                if (Key(Elements[i], OwnerField) == faction)
                {
                    return i;
                }
            }
            return null;
        }
    }

    /// <summary>Re-read the faction block (structural edits shift indices, so every step reloads).</summary>
    private Context? Load(string savePath, IReadOnlyList<BlockSummary> blocks)
    {
        var block = blocks.FirstOrDefault(b => b.ClassName == FactionClass);
        if (block is null)
        {
            return null;
        }
        var details = loader.LoadBlockDetails(savePath, block.Index);
        if (SaveFieldValues.Field(details, ElementList) is not { Elements: { } elements } list)
        {
            return null;
        }
        uint player = PlayerFactionFallback;
        foreach (var e in elements)
        {
            if (Key(e, LeaderField) == PlayerCharacterKey && Key(e, OwnerField) is { } k)
            {
                player = k;
                break;
            }
        }
        return new Context(block.Index, list.FieldIndex, elements, player);
    }
}
