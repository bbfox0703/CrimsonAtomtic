using CrimsonAtomtic.Core;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Pure C# tests for <see cref="SaveBackupService"/>. Uses a
/// temp-directory fake <see cref="IPlatformPaths"/> so the tests don't
/// need a live Crimson Desert install — and don't pollute the user's
/// real %LOCALAPPDATA%\CrimsonAtomtic\Backups\ tree.
/// </summary>
public sealed class SaveBackupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FakePaths _paths;
    private readonly SaveBackupService _service;

    public SaveBackupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"crimson-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _paths = new FakePaths(
            localAppData: Path.Combine(_root, "AppData"),
            steamSaveRoot: Path.Combine(_root, "GameSave"));
        Directory.CreateDirectory(_paths.LocalAppDataDirectory);
        Directory.CreateDirectory(_paths.GameSaveRoot);
        _service = new SaveBackupService(_paths);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException) { /* best-effort cleanup */ }
    }

    private string CreateSaveFile(string userId, string slotName, byte[]? content = null, bool withLobby = true)
    {
        var slotDir = Path.Combine(_paths.GameSaveRoot, userId, slotName);
        Directory.CreateDirectory(slotDir);
        var savePath = Path.Combine(slotDir, "save.save");
        File.WriteAllBytes(savePath, content ?? new byte[] { 0x01, 0x02, 0x03, 0x04 });
        if (withLobby)
        {
            File.WriteAllBytes(Path.Combine(slotDir, "lobby.save"), new byte[] { 0xAA, 0xBB });
        }
        return savePath;
    }

    [Fact]
    public void BackupRoot_LivesUnderLocalAppData()
    {
        Assert.Equal(
            Path.Combine(_paths.LocalAppDataDirectory, "Backups"),
            _service.BackupRoot);
    }

    [Fact]
    public void BackupBeforeWrite_HappyPath_CopiesSaveAndLobby()
    {
        var savePath = CreateSaveFile("102190433", "slot0");

        var outcome = _service.BackupBeforeWrite(savePath);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.Entry);
        var entry = outcome.Entry!;
        Assert.Equal("102190433", entry.UserId);
        Assert.Equal("slot0", entry.SlotName);
        Assert.Equal(SavePlatform.Steam, entry.Platform);
        Assert.Equal(2, entry.FileNames.Count);
        Assert.Contains("save.save", entry.FileNames);
        Assert.Contains("lobby.save", entry.FileNames);
        Assert.True(Directory.Exists(entry.BackupDirectory));
        Assert.True(File.Exists(Path.Combine(entry.BackupDirectory, "save.save")));
        Assert.True(File.Exists(Path.Combine(entry.BackupDirectory, "lobby.save")));
    }

    [Fact]
    public void BackupBeforeWrite_PlacesEntryUnderPlatformSubdirectory()
    {
        var savePath = CreateSaveFile("102190433", "slot0");

        var outcome = _service.BackupBeforeWrite(savePath);

        Assert.True(outcome.IsSuccess);
        // The on-disk path must include a platform layer between
        // BackupRoot and the userId — this is the multi-launcher
        // invariant the schema rests on.
        var rel = Path.GetRelativePath(_service.BackupRoot, outcome.Entry!.BackupDirectory);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.Equal("Steam", parts[0]);
        Assert.Equal("102190433", parts[1]);
        Assert.Equal("slot0", parts[2]);
        // parts[3] is the timestamp folder.
    }

    [Fact]
    public void BackupBeforeWrite_UnknownPlatformPath_TagsAsUnknown()
    {
        // Source is under _root but not under any FakePaths-known
        // save root → ClassifySavePath returns Unknown.
        var detachedSlot = Path.Combine(_root, "ad-hoc-copy", "u9", "slotX");
        Directory.CreateDirectory(detachedSlot);
        var savePath = Path.Combine(detachedSlot, "save.save");
        File.WriteAllBytes(savePath, new byte[] { 0x42 });

        var outcome = _service.BackupBeforeWrite(savePath);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(SavePlatform.Unknown, outcome.Entry!.Platform);
        var rel = Path.GetRelativePath(_service.BackupRoot, outcome.Entry.BackupDirectory);
        Assert.StartsWith("Unknown" + Path.DirectorySeparatorChar, rel);
    }

    [Fact]
    public void BackupBeforeWrite_EpicPath_TagsAsEpic()
    {
        // Set up a second platform root under the same fake and back up
        // a save inside it. Schema layer must isolate Epic from Steam.
        var epicRoot = Path.Combine(_root, "EpicSave");
        _paths.AddPlatform(SavePlatform.Epic, epicRoot);
        var slotDir = Path.Combine(epicRoot, "epic-user-1", "slot0");
        Directory.CreateDirectory(slotDir);
        var savePath = Path.Combine(slotDir, "save.save");
        File.WriteAllBytes(savePath, new byte[] { 0xEE });

        var outcome = _service.BackupBeforeWrite(savePath);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(SavePlatform.Epic, outcome.Entry!.Platform);
        var rel = Path.GetRelativePath(_service.BackupRoot, outcome.Entry.BackupDirectory);
        Assert.StartsWith("Epic" + Path.DirectorySeparatorChar, rel);
    }

    [Fact]
    public void BackupBeforeWrite_NoLobby_CopiesSaveOnly()
    {
        var savePath = CreateSaveFile("102190433", "slot1", withLobby: false);

        var outcome = _service.BackupBeforeWrite(savePath);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.Entry);
        var entry = outcome.Entry!;
        Assert.Single(entry.FileNames);
        Assert.Equal("save.save", entry.FileNames[0]);
    }

    [Fact]
    public void BackupBeforeWrite_MissingSource_SkipsCleanly()
    {
        var nonexistent = Path.Combine(_paths.GameSaveRoot, "102190433", "slot7", "save.save");

        var outcome = _service.BackupBeforeWrite(nonexistent);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(BackupOutcomeKind.SkippedNoSource, outcome.Kind);
        Assert.Null(outcome.Entry);
    }

    [Fact]
    public void BackupBeforeWrite_TwoComponentPath_SkipsBadPath()
    {
        // A path with insufficient parent directories. On Windows the
        // root drive's GetDirectoryName behaviour is platform-dependent,
        // so we use a non-existent two-component path.
        var bad = "save.save";

        var outcome = _service.BackupBeforeWrite(bad);

        // Either skipped because the file doesn't exist OR because the
        // path shape doesn't match — either is fine; both are no-ops.
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void BackupBeforeWrite_PreservesFileBytes()
    {
        var content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };
        var savePath = CreateSaveFile("102190433", "slot100", content);

        var outcome = _service.BackupBeforeWrite(savePath);

        Assert.True(outcome.IsSuccess);
        var copiedSave = Path.Combine(outcome.Entry!.BackupDirectory, "save.save");
        Assert.Equal(content, File.ReadAllBytes(copiedSave));
    }

    [Fact]
    public void BackupBeforeWrite_OverRetention_PrunesOldest()
    {
        // Drives one extra backup past MaxVersionsPerSlot so the prune
        // path fires deterministically regardless of the configured cap.
        // Sources its loop bound from the service constant so future
        // retention tweaks don't desync the test from the production
        // behavior it claims to pin.
        var savePath = CreateSaveFile("u1", "slot0");
        var rounds = SaveBackupService.MaxVersionsPerSlot + 1;
        var entries = new List<BackupEntry>(rounds);
        for (var i = 0; i < rounds; i++)
        {
            // Mutate the file slightly so each backup is distinct on
            // disk. Sleep so the timestamp folders differ — the
            // service uses second-resolution stamps.
            File.WriteAllBytes(savePath, new byte[] { (byte)i });
            Thread.Sleep(1100);
            var outcome = _service.BackupBeforeWrite(savePath);
            Assert.True(outcome.IsSuccess);
            entries.Add(outcome.Entry!);
        }

        // After the (cap+1)-th backup, exactly MaxVersionsPerSlot should
        // remain on disk; the very first entry is the one pruned.
        var listed = _service.ListBackups()
            .Where(b => b.UserId == "u1" && b.SlotName == "slot0")
            .ToList();
        Assert.Equal(SaveBackupService.MaxVersionsPerSlot, listed.Count);
        Assert.DoesNotContain(listed, b => b.BackupDirectory == entries[0].BackupDirectory);
        Assert.Contains(listed, b => b.BackupDirectory == entries[^1].BackupDirectory);
    }

    [Fact]
    public void ListBackups_NoRootYet_ReturnsEmpty()
    {
        // BackupRoot is created lazily; before any backup it doesn't exist.
        Assert.Empty(_service.ListBackups());
    }

    [Fact]
    public void ListBackups_MultiSlot_AllSnapshotsListed()
    {
        var s0 = CreateSaveFile("u1", "slot0");
        var s1 = CreateSaveFile("u1", "slot1");
        var s2 = CreateSaveFile("u2", "slot0");
        Assert.True(_service.BackupBeforeWrite(s0).IsSuccess);
        Assert.True(_service.BackupBeforeWrite(s1).IsSuccess);
        Assert.True(_service.BackupBeforeWrite(s2).IsSuccess);

        var listed = _service.ListBackups();
        Assert.Equal(3, listed.Count);
        Assert.Contains(listed, b => b.UserId == "u1" && b.SlotName == "slot0");
        Assert.Contains(listed, b => b.UserId == "u1" && b.SlotName == "slot1");
        Assert.Contains(listed, b => b.UserId == "u2" && b.SlotName == "slot0");
        Assert.All(listed, b => Assert.Equal(SavePlatform.Steam, b.Platform));
    }

    [Fact]
    public void ListBackups_LegacyLayoutTaggedAsSteam_StillReadable()
    {
        // Simulate a pre-multi-platform backup tree: drop a snapshot
        // directly under BackupRoot\<userId>\<slot>\<stamp>\ without a
        // platform layer. ListBackups should surface it, tagged Steam
        // (the only platform supported before this change).
        var legacyStamp = Path.Combine(_service.BackupRoot, "legacy-user", "slot0",
                                        "2020-01-02_03-04-05");
        Directory.CreateDirectory(legacyStamp);
        File.WriteAllBytes(Path.Combine(legacyStamp, "save.save"), new byte[] { 0x77 });

        var listed = _service.ListBackups();

        var legacy = listed.Single(b => b.UserId == "legacy-user");
        Assert.Equal("slot0", legacy.SlotName);
        Assert.Equal(SavePlatform.Steam, legacy.Platform);
        Assert.Equal(legacyStamp, legacy.BackupDirectory);
    }

    [Fact]
    public void ListBackups_MixedLayouts_BothSurface()
    {
        // Legacy entry + a fresh platform-scoped entry should coexist.
        var legacyStamp = Path.Combine(_service.BackupRoot, "legacy-user", "slot0",
                                        "2020-01-02_03-04-05");
        Directory.CreateDirectory(legacyStamp);
        File.WriteAllBytes(Path.Combine(legacyStamp, "save.save"), new byte[] { 0x99 });

        var savePath = CreateSaveFile("modern-user", "slot0");
        Assert.True(_service.BackupBeforeWrite(savePath).IsSuccess);

        var listed = _service.ListBackups();

        Assert.Equal(2, listed.Count);
        var legacy = listed.Single(b => b.UserId == "legacy-user");
        var modern = listed.Single(b => b.UserId == "modern-user");
        Assert.Equal(SavePlatform.Steam, legacy.Platform);
        Assert.Equal(SavePlatform.Steam, modern.Platform);
        // The modern entry is platform-scoped on disk; the legacy isn't.
        Assert.Contains(Path.DirectorySeparatorChar + "Steam" + Path.DirectorySeparatorChar,
                        modern.BackupDirectory);
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "Steam" + Path.DirectorySeparatorChar,
                              legacy.BackupDirectory);
    }

    [Fact]
    public void Restore_CopiesBackupBackToLiveLocation()
    {
        var content = new byte[] { 0x11, 0x22, 0x33 };
        var savePath = CreateSaveFile("102190433", "slot0", content);
        var backupOutcome = _service.BackupBeforeWrite(savePath);
        Assert.True(backupOutcome.IsSuccess);

        // Corrupt the live save.
        File.WriteAllBytes(savePath, new byte[] { 0xFF, 0xFF, 0xFF });

        var restoredPath = SaveBackupService.Restore(backupOutcome.Entry!, _paths.GameSaveRoot);

        Assert.Equal(savePath, restoredPath);
        Assert.Equal(content, File.ReadAllBytes(savePath));
    }

    [Fact]
    public void TryDeriveSlotCoordinates_MatchesCanonicalShape()
    {
        var ok = SaveBackupService.TryDeriveSlotCoordinates(
            @"C:\Users\Andyc\AppData\Local\Pearl Abyss\CD\save\102190433\slot100\save.save",
            out var user, out var slot);
        Assert.True(ok);
        Assert.Equal("102190433", user);
        Assert.Equal("slot100", slot);
    }

    [Fact]
    public void TryDeriveSlotCoordinates_EmptyPath_ReturnsFalse()
    {
        Assert.False(SaveBackupService.TryDeriveSlotCoordinates(string.Empty, out _, out _));
    }

    [Fact]
    public void FormatTimestamp_RoundTripsThroughParse()
    {
        var when = new DateTime(2026, 5, 14, 15, 30, 45);
        var stamp = SaveBackupService.FormatTimestamp(when);
        Assert.Equal("2026-05-14_15-30-45", stamp);
        Assert.True(SaveBackupService.TryParseTimestamp(stamp, out var parsed));
        Assert.Equal(when, parsed);
    }

    [Fact]
    public void TryParseTimestamp_RejectsBadInput()
    {
        Assert.False(SaveBackupService.TryParseTimestamp("not-a-stamp", out _));
        Assert.False(SaveBackupService.TryParseTimestamp(string.Empty, out _));
    }

    [Fact]
    public void DiscoverSaveRoots_ReturnsConfiguredPlatforms_MostRecentFirst()
    {
        // Wire up an Epic root and write one save under each.
        var epicRoot = Path.Combine(_root, "EpicSave");
        _paths.AddPlatform(SavePlatform.Epic, epicRoot);
        Directory.CreateDirectory(epicRoot);

        var steam = CreateSaveFile("steam-user", "slot0");
        File.SetLastWriteTimeUtc(steam, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var epicDir = Path.Combine(epicRoot, "epic-user", "slot0");
        Directory.CreateDirectory(epicDir);
        var epicSave = Path.Combine(epicDir, "save.save");
        File.WriteAllBytes(epicSave, new byte[] { 0xEE });
        File.SetLastWriteTimeUtc(epicSave, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var roots = _paths.DiscoverSaveRoots();

        Assert.Equal(2, roots.Count);
        Assert.Equal(SavePlatform.Epic, roots[0].Platform); // more recent
        Assert.Equal(SavePlatform.Steam, roots[1].Platform);
    }

    [Fact]
    public void ClassifySavePath_RecognisesConfiguredRoots()
    {
        var epicRoot = Path.Combine(_root, "EpicSave");
        _paths.AddPlatform(SavePlatform.Epic, epicRoot);

        var steamPath = Path.Combine(_paths.GameSaveRoot, "u", "slot0", "save.save");
        var epicPath = Path.Combine(epicRoot, "u", "slot0", "save.save");
        var unknownPath = Path.Combine(_root, "elsewhere", "save.save");

        Assert.Equal(SavePlatform.Steam, _paths.ClassifySavePath(steamPath));
        Assert.Equal(SavePlatform.Epic, _paths.ClassifySavePath(epicPath));
        Assert.Equal(SavePlatform.Unknown, _paths.ClassifySavePath(unknownPath));
    }

    /// <summary>
    /// Fake <see cref="IPlatformPaths"/> that mimics the multi-launcher
    /// behaviour without doing any real probing — tests configure the
    /// platforms they care about and the fake reports them faithfully.
    /// </summary>
    private sealed class FakePaths : IPlatformPaths
    {
        private readonly List<(SavePlatform Platform, string Root)> _platformRoots = new();

        public FakePaths(string localAppData, string steamSaveRoot)
        {
            LocalAppDataDirectory = localAppData;
            LogDirectory = Path.Combine(localAppData, "Logs");
            GameSaveRoot = steamSaveRoot;
            GameInstallRoot = null;
            _platformRoots.Add((SavePlatform.Steam, steamSaveRoot));
        }

        /// <summary>
        /// Per-test helper: register an additional platform → root mapping.
        /// Real <see cref="WindowsPlatformPaths"/> discovers these via
        /// directory probing under <c>%LOCALAPPDATA%\Pearl Abyss\</c>.
        /// </summary>
        public void AddPlatform(SavePlatform platform, string root)
        {
            _platformRoots.RemoveAll(p => p.Platform == platform);
            _platformRoots.Add((platform, root));
        }

        public string LocalAppDataDirectory { get; }
        public string LogDirectory { get; }
        public string? GameInstallRoot { get; }

        /// <summary>
        /// Convenience handle to the Steam root (back-compat for tests
        /// written before multi-platform). Not part of the
        /// <see cref="IPlatformPaths"/> contract.
        /// </summary>
        public string GameSaveRoot { get; }

        public IReadOnlyList<DiscoveredSaveRoot> DiscoverSaveRoots()
        {
            var found = new List<DiscoveredSaveRoot>(_platformRoots.Count);
            foreach (var (platform, root) in _platformRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }
                DateTime? mtime = null;
                foreach (var path in Directory.EnumerateFiles(root, "save.save", SearchOption.AllDirectories))
                {
                    var t = File.GetLastWriteTimeUtc(path);
                    if (mtime is null || t > mtime) mtime = t;
                }
                found.Add(new DiscoveredSaveRoot(platform, root, mtime));
            }
            found.Sort((a, b) =>
            {
                var aT = a.MostRecentSaveMtime ?? DateTime.MinValue;
                var bT = b.MostRecentSaveMtime ?? DateTime.MinValue;
                return bT.CompareTo(aT);
            });
            return found;
        }

        public SavePlatform ClassifySavePath(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                return SavePlatform.Unknown;
            }
            string normalized;
            try
            {
                normalized = Path.GetFullPath(savePath);
            }
            catch
            {
                return SavePlatform.Unknown;
            }
            foreach (var (platform, root) in _platformRoots)
            {
                var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return platform;
                }
            }
            return SavePlatform.Unknown;
        }
    }
}
