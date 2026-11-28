using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// The comment rules in docs/code-comments.md, checked over the repo's own code. Line
/// references, audit / PR / commit ids, history phrases and stacked doc blocks must be absent.
/// </summary>
public sealed partial class CommentRulesTests
{
    private static readonly string[] SkippedDirs =
        ["bin", "obj", ".git", ".venv", "vendor", "out", "dist", "data", "node_modules", "__pycache__", "target"];

    // This file documents the patterns with examples of what they catch.
    private static readonly string[] SkippedFiles = ["src/CrimsonAtomtic.Tests/CommentRulesTests.cs"];

    private static readonly (string Dir, string[] Exts)[] Scopes =
    [
        ("src", [".cs", ".axaml", ".csproj"]),
        ("tools", [".py", ".ps1"]),
        ("scripts", [".ps1", ".py"]),
        ("vendor", [".ps1"]),
        (".github", [".yml", ".yaml"]),
        ("", [".ps1", ".props", ".cmd"]),
    ];

    [GeneratedRegex(@"[\w./\\-]+\.(?:cs|axaml|rs|py|ps1|ya?ml|md|csproj|props|toml)\s*:\s*\d+|#L\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex LineRef();

    [GeneratedRegex(@"\bB[1-6]-\d\d\b|\b(?:PR|pull request)\s*#\d+|\bcommit\s+[0-9a-f]{7,40}\b|\baudit\s+(?:#?\d|[A-Z]{1,3}\d)|\breview (?:round|finding|R\d)", RegexOptions.IgnoreCase)]
    private static partial Regex AuditId();

    private static readonly Regex[] History =
    [
        // "it used to Y" is past behaviour; "the extractor used to enumerate" is a purpose, so only
        // a pronoun, a plural actor or "used to be" counts.
        new(@"\b(?:it|this|that|they|we|which|callers?|tests?|code|editor|app|dialog|parser)\s+used to\b|\bused to be\b", RegexOptions.IgnoreCase),
        new(@"\b(?:pre-fix|before the fix|after the fix|until the fix|was fixed|were fixed|the old (?:code|version|path|behaviou?r))\b", RegexOptions.IgnoreCase),
        new(@"(?:^\s*|[.;:(]\s*)(?:Previously|Originally|Formerly)\b"),
        new(@"\bno longer\b", RegexOptions.IgnoreCase),
        new(@"\bturned out\b", RegexOptions.IgnoreCase),
        new(@"\b(?:[Tt]he (?:first|original|previous) version)\b"),
        new(@"\b(?:[Ff]ound|[Ss]een|[Oo]bserved|[Rr]eported|[Rr]eproduced|[Mm]easured|[Cc]onfirmed|[Vv]erified|[Ff]ixed|[Aa]dded|[Ss]hipped|[Ll]anded|[Rr]emoved)\s+(?:on\s+|in\s+)?20\d\d-\d\d-\d\d\b"),
        new(@"\b(?:before|until|since|as of)\s+20\d\d-\d\d-\d\d\b", RegexOptions.IgnoreCase),
        new(@"\bthis (?:PR|commit|fix)\b", RegexOptions.IgnoreCase),
        // A change's label ("PR B", "PR B addendum") names the history the dev log holds.
        new(@"\bPR [A-Z]\b|\baddendum\b"),
    ];

    [Fact]
    public void Comments_HaveNoLineReferences()
    {
        var hits = Scan().SelectMany(f => f.Lines.Where(l => LineRef().IsMatch(l.Text)).Select(l => Fmt(f, l))).ToList();
        Assert.True(hits.Count == 0, Report("line references into the repo (name the symbol instead)", hits));
    }

    [Fact]
    public void Comments_HaveNoAuditPrOrCommitIds()
    {
        var hits = Scan().SelectMany(f => f.Lines.Where(l => AuditId().IsMatch(l.Text)).Select(l => Fmt(f, l))).ToList();
        Assert.True(hits.Count == 0, Report("audit / review ids, PR numbers or commit hashes (history goes to docs/dev-log.md)", hits));
    }

    [Fact]
    public void CSharp_HasOneSummaryPerMember()
    {
        var hits = new List<string>();
        foreach (var path in Files().Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("/// <summary>", StringComparison.Ordinal)) continue;
                var j = i + 1;
                while (j < lines.Length && lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    if (lines[j].Contains("<summary>", StringComparison.Ordinal)) break;
                    j++;
                }
                while (j < lines.Length && (lines[j].Trim().Length == 0 || lines[j].TrimStart().StartsWith('[')))
                    j++;
                if (j < lines.Length && lines[j].TrimStart().StartsWith("/// <summary>", StringComparison.Ordinal))
                    hits.Add($"{Rel(path)}:{i + 1}: doc block followed by another doc block at line {j + 1}");
            }
        }
        Assert.True(hits.Count == 0, Report("stacked <summary> blocks (the first attaches to the wrong member)", hits));
    }

    [Fact]
    public void Comments_HaveNoHistory()
    {
        var hits = Scan().SelectMany(f => f.Lines.Where(l => History.Any(r => r.IsMatch(l.Text))).Select(l => Fmt(f, l))).ToList();
        Assert.True(hits.Count == 0, Report("history in comments (move it to docs/dev-log.md)", hits));
    }

    private static string Fmt(ScannedFile f, CommentLine l) => $"{f.Rel}:{l.No}: {l.Text.Trim()}";

    private static string Report(string what, List<string> hits) =>
        $"{hits.Count} comment line(s) with {what}; see docs/code-comments.md:\n" + string.Join("\n", hits.Take(400));

    private sealed record CommentLine(int No, string Text);

    private sealed record ScannedFile(string Rel, List<CommentLine> Lines);

    private static IEnumerable<ScannedFile> Scan()
    {
        foreach (var path in Files())
        {
            var text = File.ReadAllText(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var lines = ext switch
            {
                ".cs" => CLikeComments(text),
                ".axaml" or ".csproj" or ".props" => XmlComments(text),
                ".py" => PythonComments(text),
                ".ps1" => PowerShellComments(text),
                ".cmd" => CmdComments(text),
                _ => HashComments(text),
            };
            yield return new ScannedFile(Rel(path), lines);
        }
    }

    private static IEnumerable<string> Files()
    {
        var root = RepoRoot();
        foreach (var (dir, exts) in Scopes)
        {
            var start = Path.Combine(root, dir);
            if (!Directory.Exists(start)) continue;
            var option = dir.Length == 0 ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
            foreach (var path in Directory.EnumerateFiles(start, "*", option))
            {
                if (!exts.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                var rel = Rel(path);
                var parts = rel.Split('/');
                var skipDirs = dir == "vendor" ? parts.Skip(1).SkipLast(1) : parts.SkipLast(1);
                if (dir == "vendor" && parts.Length != 2) continue;
                if (skipDirs.Any(p => SkippedDirs.Contains(p, StringComparer.OrdinalIgnoreCase))) continue;
                if (SkippedFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;
                yield return path;
            }
        }
    }

    // A scanner, not a parser: it tracks "...", @"...", """...""" and char literals so a // or
    // /* inside a string is not read as a comment.
    private static List<CommentLine> CLikeComments(string text)
    {
        var outp = new List<CommentLine>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inBlock = false;
        var inRaw = false;
        var inVerbatim = false;
        for (var no = 0; no < lines.Length; no++)
        {
            var line = lines[no];
            var sb = new StringBuilder();
            var i = 0;
            while (i < line.Length)
            {
                if (inBlock)
                {
                    var end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0) { sb.Append(line, i, line.Length - i); i = line.Length; break; }
                    sb.Append(line, i, end - i);
                    inBlock = false;
                    i = end + 2;
                    continue;
                }
                if (inRaw)
                {
                    var end = line.IndexOf("\"\"\"", i, StringComparison.Ordinal);
                    if (end < 0) { i = line.Length; break; }
                    inRaw = false;
                    i = end + 3;
                    continue;
                }
                if (inVerbatim)
                {
                    var q = line.IndexOf('"', i);
                    if (q < 0) { i = line.Length; break; }
                    if (q + 1 < line.Length && line[q + 1] == '"') { i = q + 2; continue; }
                    inVerbatim = false;
                    i = q + 1;
                    continue;
                }
                var c = line[i];
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    sb.Append(line, i + 2, line.Length - i - 2);
                    break;
                }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    inBlock = true;
                    i += 2;
                    continue;
                }
                if (c == '"' && line.AsSpan(i).StartsWith("\"\"\""))
                {
                    var end = line.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                    if (end < 0) { inRaw = true; i = line.Length; break; }
                    i = end + 3;
                    continue;
                }
                if (c == '@' && i + 1 < line.Length && line[i + 1] == '"' || c == '$' && i + 2 < line.Length && line[i + 1] == '@' && line[i + 2] == '"')
                {
                    inVerbatim = true;
                    i = line.IndexOf('"', i) + 1;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    var quote = c;
                    i++;
                    while (i < line.Length && line[i] != quote)
                        i += line[i] == '\\' ? 2 : 1;
                    i++;
                    continue;
                }
                i++;
            }
            if (sb.Length > 0) outp.Add(new CommentLine(no + 1, sb.ToString()));
        }
        return outp;
    }

    private static List<CommentLine> XmlComments(string text)
    {
        var outp = new List<CommentLine>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inC = false;
        for (var no = 0; no < lines.Length; no++)
        {
            var line = lines[no];
            var sb = new StringBuilder();
            var i = 0;
            while (i < line.Length)
            {
                if (inC)
                {
                    var end = line.IndexOf("-->", i, StringComparison.Ordinal);
                    if (end < 0) { sb.Append(line, i, line.Length - i); break; }
                    sb.Append(line, i, end - i);
                    inC = false;
                    i = end + 3;
                    continue;
                }
                var start = line.IndexOf("<!--", i, StringComparison.Ordinal);
                if (start < 0) break;
                inC = true;
                i = start + 4;
            }
            if (sb.Length > 0) outp.Add(new CommentLine(no + 1, sb.ToString()));
        }
        return outp;
    }

    // '#' outside a quoted string, plus docstrings: a triple-quoted string that opens a line.
    private static List<CommentLine> PythonComments(string text)
    {
        var outp = new List<CommentLine>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? doc = null;
        var inString = (string?)null;
        for (var no = 0; no < lines.Length; no++)
        {
            var line = lines[no];
            if (doc is not null)
            {
                var end = line.IndexOf(doc, StringComparison.Ordinal);
                outp.Add(new CommentLine(no + 1, end < 0 ? line : line[..end]));
                if (end >= 0) doc = null;
                continue;
            }
            if (inString is not null)
            {
                if (line.Contains(inString, StringComparison.Ordinal)) inString = null;
                continue;
            }
            var t = line.TrimStart();
            var lead = t.Length > 0 && "rRbBuUfF".Contains(t[0]) && t.Length > 1 && (t[1] == '"' || t[1] == '\'') ? 1 : 0;
            foreach (var q in new[] { "\"\"\"", "'''" })
            {
                if (t.AsSpan(lead).StartsWith(q))
                {
                    var rest = t[(lead + 3)..];
                    var end = rest.IndexOf(q, StringComparison.Ordinal);
                    outp.Add(new CommentLine(no + 1, end < 0 ? rest : rest[..end]));
                    if (end < 0) doc = q;
                    goto next;
                }
            }
            {
                var i = 0;
                while (i < line.Length)
                {
                    var c = line[i];
                    if (c == '#') { outp.Add(new CommentLine(no + 1, line[(i + 1)..])); break; }
                    if (c == '"' || c == '\'')
                    {
                        var triple = new string(c, 3);
                        if (line.AsSpan(i).StartsWith(triple))
                        {
                            var end = line.IndexOf(triple, i + 3, StringComparison.Ordinal);
                            if (end < 0) { inString = triple; break; }
                            i = end + 3;
                            continue;
                        }
                        i++;
                        while (i < line.Length && line[i] != c) i += line[i] == '\\' ? 2 : 1;
                        i++;
                        continue;
                    }
                    i++;
                }
            }
        next:;
        }
        return outp;
    }

    private static List<CommentLine> PowerShellComments(string text)
    {
        var outp = new List<CommentLine>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inBlock = false;
        for (var no = 0; no < lines.Length; no++)
        {
            var line = lines[no];
            if (inBlock)
            {
                var end = line.IndexOf("#>", StringComparison.Ordinal);
                outp.Add(new CommentLine(no + 1, end < 0 ? line : line[..end]));
                if (end >= 0) inBlock = false;
                continue;
            }
            var open = line.IndexOf("<#", StringComparison.Ordinal);
            if (open >= 0 && !InQuotes(line, open))
            {
                var end = line.IndexOf("#>", open + 2, StringComparison.Ordinal);
                outp.Add(new CommentLine(no + 1, end < 0 ? line[(open + 2)..] : line[(open + 2)..end]));
                if (end < 0) inBlock = true;
                continue;
            }
            var h = HashOutsideQuotes(line);
            if (h >= 0) outp.Add(new CommentLine(no + 1, line[(h + 1)..]));
        }
        return outp;
    }

    private static List<CommentLine> HashComments(string text)
    {
        var outp = new List<CommentLine>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var no = 0; no < lines.Length; no++)
        {
            var h = HashOutsideQuotes(lines[no]);
            if (h >= 0) outp.Add(new CommentLine(no + 1, lines[no][(h + 1)..]));
        }
        return outp;
    }

    private static List<CommentLine> CmdComments(string text)
    {
        var outp = new List<CommentLine>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var no = 0; no < lines.Length; no++)
        {
            var t = lines[no].TrimStart();
            if (t.StartsWith("::", StringComparison.Ordinal)) outp.Add(new CommentLine(no + 1, t[2..]));
            else if (t.StartsWith("rem ", StringComparison.OrdinalIgnoreCase)) outp.Add(new CommentLine(no + 1, t[4..]));
        }
        return outp;
    }

    private static int HashOutsideQuotes(string line)
    {
        char? q = null;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (q is not null) { if (c == q) q = null; continue; }
            if (c == '"' || c == '\'') { q = c; continue; }
            if (c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return i;
        }
        return -1;
    }

    private static bool InQuotes(string line, int at)
    {
        char? q = null;
        for (var i = 0; i < at; i++)
        {
            var c = line[i];
            if (q is not null) { if (c == q) q = null; continue; }
            if (c == '"' || c == '\'') q = c;
        }
        return q is not null;
    }

    private static string Rel(string path) => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

    private static string? _root;

    private static string RepoRoot()
    {
        if (_root is not null) return _root;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CrimsonAtomtic.slnx")))
                return _root = dir.FullName;
        }
        throw new DirectoryNotFoundException("CrimsonAtomtic.slnx not found above the test binary.");
    }
}
