using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Every <c>*Key</c> field type in the newest save either resolves to a
/// name or is a known gap. A green suite says nothing about keys it never
/// asks for, so this walks the whole save instead of sampling.
/// </summary>
/// <remarks>
/// <c>key_resolution_baseline.json</c> holds, per resolvable type, the most
/// distinct keys allowed to come back unresolved, and the key types that have
/// no resolver at all. A new gap fails the test with the keys listed;
/// investigate it, then accept it with <c>$env:UPDATE_KEY_BASELINE = '1'</c>
/// and a re-run. Skipped without a live install and save.
/// </remarks>
public sealed class KeyResolutionCoverageTests
{
    private const string BaselineFile = "key_resolution_baseline.json";

    [Fact]
    public void NewestSave_EveryKeyTypeName_ResolvesOrIsAKnownGap()
    {
        if (!File.Exists("crimson_rs.dll")) return;
        var pamt = LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup);
        var savePath = LiveSaves.Newest();
        if (pamt is null || savePath is null) return;

        var gameRoot = Path.GetDirectoryName(Path.GetDirectoryName(pamt))!;
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(gameRoot));
        using var loader = new NativeSaveLoader();
        var summary = loader.Load(savePath);

        var keysByType = new SortedDictionary<string, HashSet<uint>>(StringComparer.Ordinal);
        for (var i = 0; i < summary.Blocks.Count; i++)
        {
            Collect(loader.LoadBlockDetails(savePath, i), keysByType);
        }
        Assert.NotEmpty(keysByType);

        var unresolvedByType = new SortedDictionary<string, List<uint>>(StringComparer.Ordinal);
        var noResolver = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (type, keys) in keysByType)
        {
            if (!LocalizationProvider.CanResolveTypeName(type))
            {
                noResolver.Add(type);
                continue;
            }
            unresolvedByType[type] = keys
                .Where(k => string.IsNullOrEmpty(loc.ResolveByFieldTypeNamePair(type, k).English))
                .Order()
                .ToList();
        }

        var baselinePath = Path.Combine(RepoRoot(), "src", "CrimsonAtomtic.Tests", BaselineFile);
        if (Environment.GetEnvironmentVariable("UPDATE_KEY_BASELINE") == "1")
        {
            WriteBaseline(baselinePath, unresolvedByType, noResolver);
            return;
        }
        var (allowed, knownNoResolver) = ReadBaseline(baselinePath);

        var problems = new List<string>();
        foreach (var (type, unresolved) in unresolvedByType)
        {
            var limit = allowed.GetValueOrDefault(type);
            if (unresolved.Count > limit)
            {
                problems.Add($"{type}: {unresolved.Count} of {keysByType[type].Count} distinct keys unresolved "
                    + $"(baseline {limit}): {string.Join(", ", unresolved.Take(20))}");
            }
        }
        foreach (var type in noResolver.Where(t => !knownNoResolver.Contains(t)))
        {
            problems.Add($"{type}: no resolver and not a known gap ({keysByType[type].Count} distinct keys)");
        }
        Assert.True(problems.Count == 0,
            $"{LiveSaves.Describe(savePath)}:\n" + string.Join("\n", problems));
    }

    private static void Collect(BlockDetails block, SortedDictionary<string, HashSet<uint>> into)
    {
        foreach (var f in block.Fields)
        {
            if (f.Present && f.TypeName.EndsWith("Key", StringComparison.Ordinal) && TryU32(f) is { } v
                && v != 0 && v != uint.MaxValue)
            {
                if (!into.TryGetValue(f.TypeName, out var set))
                {
                    into[f.TypeName] = set = [];
                }
                set.Add(v);
            }
            if (f.Child is { } child) Collect(child, into);
            foreach (var e in f.Elements ?? []) Collect(e, into);
        }
    }

    private static uint? TryU32(DecodedFieldRow f) =>
        ScalarFieldEditing.TryParse(f.Value, out var raw, out var tag)
        && tag == "u32"
        && uint.TryParse(raw, System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    private static (Dictionary<string, int> Allowed, HashSet<string> NoResolver) ReadBaseline(string path)
    {
        var allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        var noResolver = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return (allowed, noResolver);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var (type, n) in root["maxUnresolved"]!.AsObject())
        {
            allowed[type] = n!.GetValue<int>();
        }
        foreach (var n in root["noResolver"]!.AsArray())
        {
            noResolver.Add(n!.GetValue<string>());
        }
        return (allowed, noResolver);
    }

    private static void WriteBaseline(string path, SortedDictionary<string, List<uint>> unresolved, SortedSet<string> noResolver)
    {
        var max = new JsonObject();
        foreach (var (type, keys) in unresolved.Where(kv => kv.Value.Count > 0))
        {
            max[type] = keys.Count;
        }
        var root = new JsonObject
        {
            ["maxUnresolved"] = max,
            ["noResolver"] = new JsonArray(noResolver.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
        };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CrimsonAtomtic.slnx"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("CrimsonAtomtic.slnx not found above the test binary.");
    }
}
