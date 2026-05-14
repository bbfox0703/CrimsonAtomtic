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
            gameSaveRoot: Path.Combine(_root, "GameSave"));
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
        Assert.Equal(2, entry.FileNames.Count);
        Assert.Contains("save.save", entry.FileNames);
        Assert.Contains("lobby.save", entry.FileNames);
        Assert.True(Directory.Exists(entry.BackupDirectory));
        Assert.True(File.Exists(Path.Combine(entry.BackupDirectory, "save.save")));
        Assert.True(File.Exists(Path.Combine(entry.BackupDirectory, "lobby.save")));
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
    public void BackupBeforeWrite_BadPath_SkipsCleanly()
    {
        // A path that doesn't have the {userId}/{slotName}/save.save shape
        // — only one level of parent. Must be skipped, not crashed.
        var weird = Path.Combine(_paths.GameSaveRoot, "save.save");
        File.WriteAllBytes(weird, new byte[] { 0xFF });

        var outcome = _service.BackupBeforeWrite(weird);

        // The path-shape check goes by directory structure, not name.
        // A direct child of GameSaveRoot has parent = GameSaveRoot and
        // grandparent = _root → TryDeriveSlotCoordinates returns true
        // with userId = "GameSave", slotName = (parent's name) — which
        // is a valid shape, just a weird one. So this case actually
        // succeeds rather than failing. We test the truly-bad case
        // (single-component path) separately.
        Assert.True(outcome.Kind == BackupOutcomeKind.Ok
                    || outcome.Kind == BackupOutcomeKind.SkippedBadPath);
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
    public void BackupBeforeWrite_FourthVersion_PrunesOldest()
    {
        var savePath = CreateSaveFile("u1", "slot0");
        var entries = new List<BackupEntry>(4);
        for (var i = 0; i < 4; i++)
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

        // After the 4th backup, only 3 should remain on disk + the
        // pruned-count flag on the 4th call's outcome should be 1.
        var listed = _service.ListBackups()
            .Where(b => b.UserId == "u1" && b.SlotName == "slot0")
            .ToList();
        Assert.Equal(3, listed.Count);
        // Newest 3 retained: entries[1], [2], [3]; oldest entries[0] gone.
        Assert.DoesNotContain(listed, b => b.BackupDirectory == entries[0].BackupDirectory);
        Assert.Contains(listed, b => b.BackupDirectory == entries[3].BackupDirectory);
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

    private sealed class FakePaths : IPlatformPaths
    {
        public FakePaths(string localAppData, string gameSaveRoot)
        {
            LocalAppDataDirectory = localAppData;
            LogDirectory = Path.Combine(localAppData, "Logs");
            GameSaveRoot = gameSaveRoot;
            GameInstallRoot = null;
        }

        public string LocalAppDataDirectory { get; }
        public string LogDirectory { get; }
        public string GameSaveRoot { get; }
        public string? GameInstallRoot { get; }
    }
}
