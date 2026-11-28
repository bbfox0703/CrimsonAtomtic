using System.Text.RegularExpressions;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Every UI string key the code asks for exists in all three language
/// dictionaries, and the dictionaries hold the same keys. A missing key
/// silently shows the English fallback in every language.
/// </summary>
public sealed partial class UiStringKeysTests
{
    private static readonly string[] Languages = ["en", "ja", "zh-TW"];

    [GeneratedRegex("""x:Key="([^"]+)"[^>]*>""")]
    private static partial Regex DictionaryKey();

    // UiText.Get("Key", …), UiText.Format("Key", …), LookupUiResourceString("Key"),
    // FindResource("Key"), DynamicResource Key in AXAML.
    [GeneratedRegex("""(?:UiText\.(?:Get|Format)|LookupUiResourceString|FindResource)\(\s*"([A-Za-z0-9_]+)"\s*[,)]""")]
    private static partial Regex CodeKey();

    [GeneratedRegex("""\{DynamicResource\s+([A-Za-z0-9_]+)\s*\}""")]
    private static partial Regex XamlKey();

    [Fact]
    public void Dictionaries_HoldTheSameKeys()
    {
        var sets = Languages.ToDictionary(l => l, ReadKeys);
        foreach (var a in Languages)
        {
            foreach (var b in Languages.Where(b => b != a))
            {
                var missing = sets[a].Except(sets[b]).Order(StringComparer.Ordinal).ToList();
                Assert.True(missing.Count == 0, $"in {a} but not {b}: {string.Join(", ", missing)}");
            }
        }
    }

    [Fact]
    public void EveryKeyTheCodeUses_IsInEveryDictionary()
    {
        var root = RepoRoot();
        var ui = Path.Combine(root, "src", "CrimsonAtomtic.Ui");
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in CodeKey().Matches(File.ReadAllText(file)))
            {
                used.Add(m.Groups[1].Value);
            }
        }
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ui, "Views"), "*.axaml"))
        {
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
            {
                used.Add(m.Groups[1].Value);
            }
        }
        // Theme resources come from the Fluent theme, not the string dictionaries.
        used.RemoveWhere(k => k.Contains("Brush", StringComparison.Ordinal) || k.StartsWith("System", StringComparison.Ordinal));

        foreach (var lang in Languages)
        {
            var keys = ReadKeys(lang);
            var missing = used.Where(k => !keys.Contains(k)).ToList();
            Assert.True(missing.Count == 0, $"{missing.Count} key(s) missing from {lang}.axaml: {string.Join(", ", missing)}");
        }
    }

    private static HashSet<string> ReadKeys(string lang)
    {
        var path = Path.Combine(RepoRoot(), "src", "CrimsonAtomtic.Ui", "Resources", "Strings", $"{lang}.axaml");
        return DictionaryKey().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CrimsonAtomtic.slnx")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("CrimsonAtomtic.slnx not found above the test binary.");
    }
}
