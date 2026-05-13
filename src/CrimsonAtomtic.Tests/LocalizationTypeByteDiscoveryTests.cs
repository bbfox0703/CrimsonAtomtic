using System.Globalization;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// One-shot exploration test: scans the English PALOC and prints which
/// type-byte each known key namespace lives at (ItemKey 0x70 is already
/// known; FactionKey / CharacterKey / InventoryKey are the gaps).
///
/// Skipped silently when no game install is reachable; otherwise emits
/// a human-readable report through xunit's <see cref="ITestOutputHelper"/>
/// so the next session can read the type-byte constants out of the test
/// log instead of re-deriving them.
///
/// Kept in the test project (not deleted after one run) because the
/// type-byte layout could drift between game patches — re-running this
/// against 1.07+ is the cheapest sanity check.
/// </summary>
public sealed class LocalizationTypeByteDiscoveryTests(ITestOutputHelper output)
{
    private static string? FindEnglishPamt()
    {
        // Mirror PazExtractorTests.FindEnglishPamt — single source of truth
        // for the install probe order would be nice, but the extra
        // abstraction isn't worth it for two callers.
        string[] candidates =
        [
            @"D:\SteamLibrary\steamapps\common\Crimson Desert",
            @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
            @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
            @"E:\SteamLibrary\steamapps\common\Crimson Desert",
            @"F:\SteamLibrary\steamapps\common\Crimson Desert",
        ];
        foreach (var root in candidates)
        {
            var p = Path.Combine(root, "0020", "0.pamt");
            if (File.Exists(p))
            {
                return p;
            }
        }
        return null;
    }

    /// <summary>
    /// Probe targets — known save-side key values, one per namespace we
    /// want to resolve. The screenshots ("FactionSaveData._ownerFactionKey
    /// = 1000063", NPCScheduleCharacterSaveData._characterKey = 51306,
    /// FriendlySaveData[0]._characterKey = 704, inventoryList[1]._inventoryKey
    /// = 2") fed the values.
    /// </summary>
    private static readonly (string Namespace, uint Key)[] Probes =
    [
        ("ItemKey (sanity, expected 0x70)", 11u), // Gold == 11; sanity-check the scan
        ("FactionKey", 1_000_063u),
        ("CharacterKey", 51_306u),
        ("CharacterKey", 704u),
        ("InventoryKey", 2u),
        // FieldNPCSaveData._characterKey on a live FieldNPC instance:
        // 117_440_514 = 0x07000002 — high byte looks structured (sub-
        // namespace?), low bytes a sequence. Check whether it resolves
        // at 0x30 (regular character names) or elsewhere.
        ("FieldNPC _characterKey", 117_440_514u),
        // SkillLearnElementSaveData._knowledgeKey from KnowledgeSaveData
        // → skillLearnSaveDataList[74][0]: probably a skill name.
        ("SkillLearn _knowledgeKey", 40_114u),
    ];

    [Fact]
    public void Scan_PrintTypeByteHistogram()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            output.WriteLine("SKIP: crimson_rs.dll not staged next to tests.");
            return;
        }
        var pamt = FindEnglishPamt();
        if (pamt is null)
        {
            output.WriteLine("SKIP: no game install found.");
            return;
        }

        // Extract the English PALOC and scan every entry.
        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt,
            "gamedata/stringtable/binary__",
            "localizationstring_eng.paloc");
        using var cat = NativePalocCatalog.LoadFromBytes(bytes);
        output.WriteLine($"PALOC entries: {cat.EntryCount:N0}");

        // typeByte -> first 5 sample (upperKey, value) pairs + total count.
        var samplesByType = new Dictionary<byte, List<(uint Upper, string Value)>>();
        var countsByType = new Dictionary<byte, int>();
        // For each probe target, accumulate every (typeByte, value) hit so
        // we can see whether a key shows up under multiple type bytes.
        var probeHits = new Dictionary<(string Ns, uint Key), List<(byte TypeByte, string Value)>>();
        foreach (var probe in Probes)
        {
            probeHits[probe] = [];
        }

        for (var i = 0; i < cat.EntryCount; i++)
        {
            var entry = cat.GetEntry(i);
            if (entry is null
                || !ulong.TryParse(entry.Value.Key, NumberStyles.Integer,
                                   CultureInfo.InvariantCulture, out var sid))
            {
                continue;
            }
            var typeByte = (byte)(sid & 0xFFul);
            var upper = (uint)(sid >> 32);

            countsByType[typeByte] = countsByType.GetValueOrDefault(typeByte) + 1;
            if (!samplesByType.TryGetValue(typeByte, out var samples))
            {
                samples = [];
                samplesByType[typeByte] = samples;
            }
            if (samples.Count < 5)
            {
                samples.Add((upper, entry.Value.Value));
            }

            foreach (var probe in Probes)
            {
                if (upper == probe.Key)
                {
                    probeHits[probe].Add((typeByte, entry.Value.Value));
                }
            }
        }

        output.WriteLine("");
        output.WriteLine("=== Type-byte histogram (sorted by count) ===");
        foreach (var kvp in countsByType.OrderByDescending(kv => kv.Value))
        {
            var samples = samplesByType[kvp.Key];
            var sampleText = string.Join(", ",
                samples.Select(s => $"upper={s.Upper}->'{Truncate(s.Value, 24)}'"));
            output.WriteLine($"  0x{kvp.Key:X2}  count={kvp.Value,7:N0}  samples: {sampleText}");
        }

        output.WriteLine("");
        output.WriteLine("=== Probe hits ===");
        foreach (var (probe, hits) in probeHits)
        {
            if (hits.Count == 0)
            {
                output.WriteLine($"  {probe.Ns} key={probe.Key}: (no PALOC entry)");
                continue;
            }
            foreach (var hit in hits)
            {
                output.WriteLine(
                    $"  {probe.Ns} key={probe.Key}: typeByte=0x{hit.TypeByte:X2} -> '{Truncate(hit.Value, 80)}'");
            }
        }
    }

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : string.Concat(s.AsSpan(0, n), "…");

    /// <summary>
    /// Type names worth probing for. Each is a schema-level
    /// <c>TypeName</c> the Rust decoder emits on scalar key fields;
    /// when we find one in the live save we harvest a few real values
    /// and look them up against every PALOC type byte to discover
    /// the namespace's home.
    /// </summary>
    private static readonly string[] HarvestTargetTypeNames =
    [
        "SkillKey",
        "GimmickInfoKey",
        "GimmickKey",
        "LevelGimmickSceneObjectInfoKey",
        "FieldGimmickSaveDataKey",
        "FieldInfoKey",
        "KnowledgeKey",
        "QuestKey",
        "RegionKey",
        "MercenaryClanKey",
        "StoreKey",
        "MissionKey",
        "BuffKey",
        "TribeInfoKey",
        "StatusKey",
        "EquipTypeKey",
    ];

    /// <summary>
    /// Save-driven discovery: walks every block in the live save,
    /// recurses through inline locator children and ObjectList elements
    /// to harvest u32 scalar values whose <c>TypeName</c> matches one of
    /// <see cref="HarvestTargetTypeNames"/>, then looks each harvested
    /// value up across every PALOC type byte and prints the matches.
    /// Skips cleanly when no save / no install is present.
    /// </summary>
    [Fact]
    public void Scan_DiscoverTypeBytesFromLiveSave()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            output.WriteLine("SKIP: crimson_rs.dll not staged next to tests.");
            return;
        }
        var pamt = FindEnglishPamt();
        if (pamt is null)
        {
            output.WriteLine("SKIP: no game install found.");
            return;
        }
        var savePath = FindLiveSave();
        if (savePath is null)
        {
            output.WriteLine("SKIP: no live save found under %LOCALAPPDATA%\\Pearl Abyss\\CD\\save.");
            return;
        }

        // Load the save and harvest samples per target TypeName.
        var loader = new NativeSaveLoader();
        var summary = loader.Load(savePath);
        var samples = new Dictionary<string, List<uint>>(StringComparer.Ordinal);
        foreach (var name in HarvestTargetTypeNames)
        {
            samples[name] = [];
        }
        // Track every distinct *Key TypeName the save schema emits, so
        // we can spot key namespaces the harvest list missed (the save
        // may name things differently from item_info — e.g. FactionKey
        // only exists save-side).
        var allKeyTypeNames = new Dictionary<string, int>(StringComparer.Ordinal);
        // Cap samples per type to keep output tight while still giving
        // enough range to disambiguate type bytes. Small keys (1..10)
        // collide with every namespace because every PALOC table uses
        // low integers; bumping to 12 typically pulls in some larger
        // keys for namespaces whose values aren't dense from 1.
        const int maxSamplesPerType = 12;
        foreach (var block in summary.Blocks)
        {
            var details = loader.LoadBlockDetails(savePath, block.Index);
            WalkBlock(details, samples, maxSamplesPerType, allKeyTypeNames: allKeyTypeNames);
        }

        output.WriteLine("=== All *Key TypeNames present in save (count) ===");
        foreach (var kv in allKeyTypeNames.OrderByDescending(kv => kv.Value))
        {
            output.WriteLine($"  {kv.Key}: {kv.Value}");
        }
        output.WriteLine("");

        output.WriteLine($"Save: {savePath}");
        output.WriteLine("");
        output.WriteLine("=== Harvested samples ===");
        foreach (var (name, ids) in samples)
        {
            output.WriteLine($"  {name}: [{string.Join(", ", ids)}]");
        }

        // Load English PALOC and build a (typeByte, upperKey) → value
        // lookup so probe queries are O(1).
        var extractor = new NativePazExtractor();
        var palocBytes = extractor.ExtractFile(
            pamt, "gamedata/stringtable/binary__", "localizationstring_eng.paloc");
        using var cat = NativePalocCatalog.LoadFromBytes(palocBytes);
        var byKey = new Dictionary<(byte TypeByte, uint Upper), string>(cat.EntryCount);
        for (var i = 0; i < cat.EntryCount; i++)
        {
            var entry = cat.GetEntry(i);
            if (entry is null
                || !ulong.TryParse(entry.Value.Key, NumberStyles.Integer,
                                   CultureInfo.InvariantCulture, out var sid))
            {
                continue;
            }
            var tb = (byte)(sid & 0xFFul);
            var upper = (uint)(sid >> 32);
            byKey[(tb, upper)] = entry.Value.Value;
        }

        output.WriteLine("");
        output.WriteLine("=== Probe hits across all type bytes ===");
        foreach (var (name, ids) in samples)
        {
            if (ids.Count == 0)
            {
                output.WriteLine($"  {name}: (no samples found in save)");
                continue;
            }
            output.WriteLine($"  {name}:");
            foreach (var id in ids)
            {
                var hits = byKey
                    .Where(kv => kv.Key.Upper == id)
                    .OrderBy(kv => kv.Key.TypeByte)
                    .ToList();
                if (hits.Count == 0)
                {
                    output.WriteLine($"    key={id}: (no PALOC entry at any type byte)");
                    continue;
                }
                foreach (var hit in hits)
                {
                    output.WriteLine(
                        $"    key={id}: typeByte=0x{hit.Key.TypeByte:X2} -> '{Truncate(hit.Value, 80)}'");
                }
            }
        }
    }

    /// <summary>
    /// Walks <paramref name="block"/> recursively (inline locator
    /// children + every list element) and stashes any scalar field
    /// whose <c>TypeName</c> matches a harvest target. Depth is
    /// unbounded in principle, but every Crimson Desert save we've
    /// inspected against 1.06 tops out at 3-4 levels — the
    /// <c>visited</c> guard short-circuits cycles if any appear.
    /// </summary>
    private static void WalkBlock(
        BlockDetails block,
        Dictionary<string, List<uint>> samples,
        int maxSamplesPerType,
        HashSet<BlockDetails>? visited = null,
        Dictionary<string, int>? allKeyTypeNames = null)
    {
        visited ??= new HashSet<BlockDetails>(ReferenceEqualityComparer.Instance);
        if (!visited.Add(block))
        {
            return;
        }
        foreach (var field in block.Fields)
        {
            // Track every *Key TypeName that ever appears in the save
            // schema (not just our harvest targets) so the report
            // surfaces gaps in our coverage list.
            if (allKeyTypeNames is not null
                && field.TypeName.EndsWith("Key", StringComparison.Ordinal))
            {
                allKeyTypeNames[field.TypeName] =
                    allKeyTypeNames.GetValueOrDefault(field.TypeName) + 1;
            }

            if (samples.TryGetValue(field.TypeName, out var ids)
                && ids.Count < maxSamplesPerType
                && (field.Kind == "fixed_prefix" || field.Kind == "fixed_suffix")
                && ScalarFieldEditing.TryParse(field.Value, out var raw, out var tag)
                && tag == "u32"
                && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
            if (field.Child is { } child)
            {
                WalkBlock(child, samples, maxSamplesPerType, visited, allKeyTypeNames);
            }
            if (field.Elements is { } elements)
            {
                foreach (var e in elements)
                {
                    WalkBlock(e, samples, maxSamplesPerType, visited, allKeyTypeNames);
                }
            }
        }
    }

    /// <summary>
    /// One-shot probe: for each <c>InventoryElementSaveData</c> entry
    /// in the player's save, print the InventoryKey value, the
    /// element's item count, and a short sample of the first three
    /// items' names. With these we can label the 18 InventoryKey
    /// values (1 = camp/contributions, 2 = backpack, …) so the UI
    /// can show meaningful container names instead of bare integers.
    /// </summary>
    [Fact]
    public void Probe_InventoryKeyContainers()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            output.WriteLine("SKIP: crimson_rs.dll not staged next to tests.");
            return;
        }
        var pamt = FindEnglishPamt();
        var savePath = FindLiveSave();
        if (pamt is null || savePath is null)
        {
            output.WriteLine("SKIP: game install or save missing.");
            return;
        }

        // Load PALOC + iteminfo so we can resolve item names just like
        // the UI does.
        var extractor = new NativePazExtractor();
        var palocBytes = extractor.ExtractFile(
            pamt, "gamedata/stringtable/binary__", "localizationstring_eng.paloc");
        using var cat = NativePalocCatalog.LoadFromBytes(palocBytes);
        var nameByItemKey = new Dictionary<uint, string>();
        for (var i = 0; i < cat.EntryCount; i++)
        {
            var e = cat.GetEntry(i);
            if (e is null) continue;
            if (!ulong.TryParse(e.Value.Key, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var sid)) continue;
            if ((byte)(sid & 0xFFul) != 0x70) continue;
            nameByItemKey[(uint)(sid >> 32)] = e.Value.Value;
        }

        var loader = new NativeSaveLoader();
        var summary = loader.Load(savePath);
        // Find the top-level InventorySaveData block (class name match).
        var inventoryBlock = summary.Blocks
            .FirstOrDefault(b => b.ClassName == "InventorySaveData");
        if (inventoryBlock is null)
        {
            output.WriteLine("SKIP: no InventorySaveData block in save.");
            return;
        }
        var details = loader.LoadBlockDetails(savePath, inventoryBlock.Index);
        // _inventorylist is the first ObjectList field.
        var listField = details.Fields.FirstOrDefault(
            f => f.Name == "_inventorylist" && f.Elements is { Count: > 0 });
        if (listField?.Elements is not { } inventoryElements)
        {
            output.WriteLine("SKIP: _inventorylist not present.");
            return;
        }
        output.WriteLine($"InventorySaveData._inventorylist has {inventoryElements.Count} entries:");
        output.WriteLine("");

        var listIndex = 0;
        foreach (var el in inventoryElements)
        {
            // Extract the InventoryKey scalar.
            var keyField = el.Fields.FirstOrDefault(f => f.TypeName == "InventoryKey");
            uint inventoryKey = uint.MaxValue;
            if (keyField is not null
                && ScalarFieldEditing.TryParse(keyField.Value, out var keyRaw, out _)
                && uint.TryParse(keyRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k))
            {
                inventoryKey = k;
            }

            // Find the _itemList ObjectList (every container has one).
            var itemList = el.Fields.FirstOrDefault(
                f => f.Name == "_itemList" && f.Elements is not null);
            var items = itemList?.Elements ?? [];

            // Sample first 3 item names by drilling into _itemKey on each.
            var samples = new List<string>();
            foreach (var item in items.Take(3))
            {
                var itemKeyField = item.Fields.FirstOrDefault(
                    f => f.TypeName == "ItemKey"
                         && (f.Kind == "fixed_prefix" || f.Kind == "fixed_suffix"));
                if (itemKeyField is null) continue;
                if (!ScalarFieldEditing.TryParse(itemKeyField.Value, out var ikRaw, out _)) continue;
                if (!uint.TryParse(ikRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ik)) continue;
                samples.Add(nameByItemKey.GetValueOrDefault(ik, $"<key={ik}>"));
            }

            output.WriteLine(
                $"  [{listIndex,2}] InventoryKey={inventoryKey,-6} items={items.Count,4}  " +
                $"samples=[{string.Join(", ", samples)}]");
            listIndex++;
        }
    }

    private static string? FindLiveSave()
    {
        // Mirror NativeSaveLoaderTests.FindLiveSave — same probe order
        // so a missing save here means a missing save there too.
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(local))
        {
            return null;
        }
        var root = Path.Combine(local, "Pearl Abyss", "CD", "save");
        if (!Directory.Exists(root))
        {
            return null;
        }
        foreach (var user in Directory.EnumerateDirectories(root))
        {
            foreach (var slot in new[] { "slot0", "slot1", "slot2" })
            {
                var p = Path.Combine(user, slot, "save.save");
                if (File.Exists(p))
                {
                    return p;
                }
            }
        }
        return null;
    }
}
