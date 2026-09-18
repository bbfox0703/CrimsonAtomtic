using System.Collections.Generic;
using System.Globalization;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// End-to-end mechanics test for the dragon mount-unlock at the loader level
/// (mirrors <c>MainWindowViewModel.InsertDragonElementAsync</c> +
/// <c>FillDragonHpAsync</c>, minus the VM glue): build
/// <see cref="MountCatalog.DragonElementTemplateHex"/> into a real save with
/// <see cref="ISaveLoader.ListInsertElementTemplate"/>, renumber it, clear
/// main, fill HP, then write + reload and confirm HMAC + the dragon decodes
/// completely under that save's schema with the captured values.
///
/// <para>Runs once per schema among the live saves: the element has to fit
/// whichever patch wrote the save (<c>MercenarySaveData</c> lost
/// <c>_occupationState</c> in 2.00 and gained <c>_shipStationSaveList</c> in
/// 2.01). Skips when no live save is present. Never writes the user's real
/// saves — loads read-only, writes only to a temp output.</para>
/// </summary>
public sealed class MountUnlockMechanicsTests(ITestOutputHelper output)
{
    private const string MercClass = "MercenaryClanSaveData";
    private const string MercListField = "_mercenaryDataList";

    /// <summary>The classes the dragon template builds.</summary>
    private static readonly string[] TemplateClasses =
        ["MercenarySaveData", "ExperienceLevelSaveData", "FriendlyDailyCountSaveData"];

    [Fact]
    public void InsertDragonTemplate_EachLiveSchema_FitsIt_AndSurvivesRoundTrip()
    {
        var saves = LiveSaves.All();
        if (saves.Count == 0)
        {
            return; // No live save — skip on CI / fresh machine.
        }
        // Newest first, and only the first save of each schema of the
        // template's classes goes through the unlock: saves one patch wrote
        // share it, and crimson-rs runs save calls one at a time behind a
        // single lock, so each repeat would add ~5 s and test nothing new.
        var schemas = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var path in saves)
        {
            problems.AddRange(DragonProblems(path, schemas, out var tested));
            output.WriteLine($"{LiveSaves.Describe(path)}: {(tested ? "unlock tested" : "not tested (schema already covered)")}");
        }
        Assert.True(problems.Count == 0,
            $"{problems.Count} problem(s) across {schemas.Count} schema(s) in {saves.Count} live save(s):\n"
            + string.Join("\n", problems));
    }

    private static List<string> DragonProblems(string path, HashSet<string> schemasTested, out bool tested)
    {
        tested = false;
        var name = LiveSaves.Describe(path);
        var outPath = Path.Combine(Path.GetTempPath(), $"cd_dragon_insert_{Guid.NewGuid():N}.save");
        try
        {
            using var loader = new NativeSaveLoader();
            var summary = loader.Load(path);
            var merc = FindMercList(loader, path, summary.Blocks);
            if (merc is null)
            {
                return [$"{name}: no {MercClass}.{MercListField}"];
            }
            var (blockIndex, listField, elements) = merc.Value;
            if (!schemasTested.Add(SchemaKey(elements)))
            {
                return []; // A newer save already covered this schema.
            }
            tested = true;

            // Same steps as the VM: append the template, fresh _mercenaryNo,
            // clear _isMainMercenary, fill _currentHp.
            var insertAt = elements.Count;
            var newMercNo = elements
                .Select(e => Field(e, "_mercenaryNo"))
                .Where(f => f is { Present: true })
                .Select(f => ParseLeadingUInt64(f!.Value))
                .DefaultIfEmpty(0UL)
                .Max() + 1;
            var dropped = loader.ListInsertElementTemplate(blockIndex, ReadOnlySpan<PathStep>.Empty,
                listField, insertAt, Convert.FromHexString(MountCatalog.DragonElementTemplateHex));
            var built = Elements(loader.LoadBlockDetails(path, blockIndex))[insertAt];
            var dragonPath = new[] { new PathStep((uint)listField, (uint)insertAt) };
            loader.SetScalarField(blockIndex, dragonPath, Field(built, "_mercenaryNo")!.FieldIndex,
                BitConverter.GetBytes(newMercNo));
            loader.SetScalarField(blockIndex, dragonPath, Field(built, "_isMainMercenary")!.FieldIndex,
                new byte[] { 0 });
            loader.SetScalarField(blockIndex, dragonPath, Field(built, "_currentHp")!.FieldIndex,
                BitConverter.GetBytes(MountCatalog.DragonFullHp));

            // Full encode → HMAC → reload.
            loader.WriteToFile(outPath);
            using var reloaded = new NativeSaveLoader();
            var reSummary = reloaded.Load(outPath);
            if (!reSummary.HmacOk)
            {
                return [$"{name}: dragon-inserted save failed HMAC"];
            }
            var reElements = FindMercList(reloaded, outPath, reSummary.Blocks)?.Elements;
            if (reElements?.Count != insertAt + 1)
            {
                return [$"{name}: merc list did not grow to {insertAt + 1}"];
            }

            var problems = new List<string>();
            var dragon = reElements[insertAt];
            // The template's _occupationState only survives where the
            // target class still has the field (saves from before 2.00).
            var expectDropped = Field(dragon, "_occupationState") is null ? 1 : 0;
            if (dropped != expectDropped)
            {
                problems.Add($"{name}: dropped {dropped} template field(s), expected {expectDropped}");
            }
            foreach (var (field, expected) in new (string, string)[]
            {
                ("_characterKey", $"{MountCatalog.DragonCharacterKey} <u32>"),
                ("_mercenaryNo", $"{newMercNo} <u64>"),
                ("_ownedCharacterKey", "1 <u32>"),
                ("_lastPaidTime", "3420870048 <u64>"),
                ("_spawnFieldInfoKey", "1 <u32>"),
                ("_isMainMercenary", "false <bool>"),
                ("_isInitialize", "true <bool>"),
                ("_currentHp", $"{MountCatalog.DragonFullHp} <u64>"),
                ("_currentMp", "0 <u64>"),
            })
            {
                var value = Field(dragon, field) is { Present: true } f ? f.Value : "(absent)";
                if (value != expected)
                {
                    problems.Add($"{name}: {field} = {value}, expected {expected}");
                }
            }
            if (Field(dragon, "_levelData")?.Child is not { ClassName: "ExperienceLevelSaveData" } level
                || level.Fields.Count(f => f.Child?.ClassName == "FriendlyDailyCountSaveData") < 3)
            {
                problems.Add($"{name}: _levelData is not an ExperienceLevelSaveData with its daily counts");
            }
            CheckFullyDecoded(dragon, $"{name}: dragon", problems);
            return problems;
        }
        catch (CrimsonSaveException ex)
        {
            return [$"{name}: {ex.Message}"];
        }
        finally
        {
            try { if (File.Exists(outPath)) File.Delete(outPath); }
            catch (IOException) { /* temp leak is harmless */ }
        }
    }

    /// <summary>
    /// Every byte of <paramref name="obj"/> is explained by a field, and an
    /// absent dynamic array / object list carries its one-byte marker — the
    /// shape the game itself writes.
    /// </summary>
    private static void CheckFullyDecoded(BlockDetails obj, string where, List<string> problems)
    {
        if (obj.UndecodedRanges.Count != 0 || obj.TrailingPadHex.Length != 0)
        {
            problems.Add($"{where} ({obj.ClassName}) is not fully decoded");
        }
        foreach (var f in obj.Fields)
        {
            if (!f.Present && f.MetaKind is 3 or 6 or 7 && f.End - f.Start != 1)
            {
                problems.Add($"{where}.{f.Name} is absent without its marker byte");
            }
            if (f.Child is { } child)
            {
                CheckFullyDecoded(child, $"{where}.{f.Name}", problems);
            }
            foreach (var e in f.Elements ?? [])
            {
                CheckFullyDecoded(e, $"{where}.{f.Name}[]", problems);
            }
        }
    }

    /// <summary>
    /// The field lists (name, kind, size) of <see cref="TemplateClasses"/>
    /// in this save's schema, read off its mercenary elements.
    /// </summary>
    private static string SchemaKey(IReadOnlyList<BlockDetails> elements)
    {
        var byClass = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Walk(BlockDetails obj)
        {
            if (Array.IndexOf(TemplateClasses, obj.ClassName) >= 0)
            {
                byClass.TryAdd(obj.ClassName,
                    string.Join(",", obj.Fields.Select(f => $"{f.Name}:{f.MetaKind}:{f.MetaSize}")));
            }
            foreach (var f in obj.Fields)
            {
                if (f.Child is { } child)
                {
                    Walk(child);
                }
                foreach (var e in f.Elements ?? [])
                {
                    Walk(e);
                }
            }
        }
        foreach (var e in elements)
        {
            Walk(e);
        }
        return string.Join(";", byClass.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static (int BlockIndex, int FieldIndex, IReadOnlyList<BlockDetails> Elements)? FindMercList(
        NativeSaveLoader loader, string path, IReadOnlyList<BlockSummary> blocks)
    {
        var block = blocks.FirstOrDefault(b => string.Equals(b.ClassName, MercClass, StringComparison.Ordinal));
        if (block is null)
        {
            return null;
        }
        var list = Field(loader.LoadBlockDetails(path, block.Index), MercListField);
        return list?.Elements is { } elements ? (block.Index, list.FieldIndex, elements) : null;
    }

    private static IReadOnlyList<BlockDetails> Elements(BlockDetails mercBlock) =>
        Field(mercBlock, MercListField)!.Elements!;

    private static DecodedFieldRow? Field(BlockDetails obj, string name) =>
        obj.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    private static ulong ParseLeadingUInt64(string formatted)
    {
        var token = formatted.Split(' ', 2)[0];
        return ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }
}
