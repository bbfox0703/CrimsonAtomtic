using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Every <c>CLAUDE.md</c> in the repo stays at or under 40 KB (root
/// CLAUDE.md, Rule 0): the agent loads it on every turn, so it is trimmed,
/// never grown past the cap.
/// </summary>
public sealed class ClaudeMdSizeTests
{
    private const long MaxBytes = 40 * 1024;

    // Pruned by name before descending, so build trees, caches and the
    // vendored crate's target/ are never walked.
    private static readonly string[] SkippedDirNames =
        ["vendor", "bin", "obj", ".git", ".vs", ".venv", "node_modules", "target", "__pycache__"];

    // Root-level folders only: a "data" or "out" folder deeper down is
    // ordinary source.
    private static readonly string[] SkippedRootDirs = ["out", "dist", "data"];

    [Fact]
    public void EveryClaudeMd_IsAtMost40KB()
    {
        var root = FindRepoRoot();
        var files = FindClaudeMds(root);

        Assert.Contains(files, f => Path.GetDirectoryName(f) == root);

        var over = files
            .Select(f => (Path: Path.GetRelativePath(root, f), Bytes: new FileInfo(f).Length))
            .Where(x => x.Bytes > MaxBytes)
            .Select(x => $"{x.Path}: {x.Bytes:N0} B")
            .ToList();

        Assert.True(over.Count == 0,
            $"CLAUDE.md over {MaxBytes:N0} B; trim it or move text to a doc: {string.Join("; ", over)}");
    }

    private static List<string> FindClaudeMds(string root)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            var claudeMd = Path.Combine(dir, "CLAUDE.md");
            if (File.Exists(claudeMd)) found.Add(claudeMd);
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", options))
            {
                var name = Path.GetFileName(sub);
                if (SkippedDirNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (dir == root && SkippedRootDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                pending.Push(sub);
            }
        }
        return found;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CrimsonAtomtic.slnx")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("CrimsonAtomtic.slnx not found above the test binary.");
    }
}
