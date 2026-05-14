using System.Globalization;
using CrimsonAtomtic.Core;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Per-slot rolling backup of save.save (+ lobby.save) snapshots.
///
/// <para>
/// Wired into <see cref="ViewModels.MainWindowViewModel"/>'s Save and
/// Save As commands: before each on-disk write, the existing target
/// file is copied into <see cref="BackupRoot"/> under
/// <c>{userId}/{slotName}/{timestamp}/</c>. Restore reverses the
/// pairing — picks a backup folder, copies it back to the original
/// slot folder, and (transitively) re-backs up the about-to-be-
/// overwritten current state so undo is itself undoable.
/// </para>
///
/// <para>
/// Path layout:
/// <code>
/// %LOCALAPPDATA%\CrimsonAtomtic\Backups\
///   {userId}\                  e.g. 102190433
///     {slotName}\              e.g. slot100
///       {timestamp}\           e.g. 2026-05-14_15-30-45
///         save.save
///         lobby.save           (only when the source slot has one)
/// </code>
/// Retention: at most <see cref="MaxVersionsPerSlot"/> timestamp
/// folders per (userId, slotName). New backups land first; the
/// oldest are deleted afterwards so a mid-write failure leaves the
/// previous version intact.
/// </para>
///
/// <para>
/// Failures during backup are reported via the returned
/// <see cref="BackupOutcome"/> but never bubble up as exceptions —
/// a Save with a missing backup is still better than a Save that
/// fails because the backup folder is read-only.
/// </para>
/// </summary>
public sealed class SaveBackupService
{
    /// <summary>Hard cap on per-slot backup versions kept on disk.</summary>
    public const int MaxVersionsPerSlot = 3;

    /// <summary>
    /// Subdirectory name under <see cref="IPlatformPaths.LocalAppDataDirectory"/>
    /// that holds the entire backup tree.
    /// </summary>
    public const string BackupsSubdirectory = "Backups";

    /// <summary>
    /// Files inside each slot folder that get backed up. The game writes
    /// both atomically together; restoring just one risks a desync, so
    /// the service captures whichever subset exists.
    /// </summary>
    private static readonly string[] BackupFileNames = ["save.save", "lobby.save"];

    private readonly IPlatformPaths _paths;

    public SaveBackupService(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <summary>
    /// Absolute path to the root of all backups.
    /// Created lazily on first write — querying this is safe even when
    /// no backup has ever happened.
    /// </summary>
    public string BackupRoot => Path.Combine(_paths.LocalAppDataDirectory, BackupsSubdirectory);

    /// <summary>
    /// Snapshot <paramref name="targetSavePath"/>'s on-disk state into
    /// the backup tree, then prune to keep at most
    /// <see cref="MaxVersionsPerSlot"/> versions per slot.
    ///
    /// <para>
    /// "Target" because this is called <em>before</em> a write — the
    /// file is what's <em>about</em> to be overwritten. If the file
    /// doesn't exist (e.g. Save As to a brand-new path) the outcome
    /// is <see cref="BackupOutcomeKind.SkippedNoSource"/>; if the
    /// path doesn't look like a Crimson Desert slot folder the
    /// outcome is <see cref="BackupOutcomeKind.SkippedBadPath"/>.
    /// Both are no-ops, not errors.
    /// </para>
    /// </summary>
    public BackupOutcome BackupBeforeWrite(string targetSavePath)
    {
        if (string.IsNullOrEmpty(targetSavePath))
        {
            return BackupOutcome.Skipped(BackupOutcomeKind.SkippedBadPath,
                "Empty save path; nothing to back up.");
        }
        if (!File.Exists(targetSavePath))
        {
            return BackupOutcome.Skipped(BackupOutcomeKind.SkippedNoSource,
                $"No existing file at {targetSavePath}; nothing to back up.");
        }

        if (!TryDeriveSlotCoordinates(targetSavePath, out var userId, out var slotName))
        {
            return BackupOutcome.Skipped(BackupOutcomeKind.SkippedBadPath,
                $"Path doesn't match the expected ...\\<userId>\\<slotName>\\save.save shape: {targetSavePath}");
        }

        var sourceDir = Path.GetDirectoryName(targetSavePath)!;
        var timestamp = DateTime.Now;
        var stamp = FormatTimestamp(timestamp);
        var slotDir = Path.Combine(BackupRoot, userId, slotName);
        var destDir = Path.Combine(slotDir, stamp);

        try
        {
            Directory.CreateDirectory(destDir);
            var copied = new List<string>(BackupFileNames.Length);
            long totalBytes = 0;
            foreach (var name in BackupFileNames)
            {
                var src = Path.Combine(sourceDir, name);
                if (!File.Exists(src))
                {
                    continue;
                }
                var dst = Path.Combine(destDir, name);
                File.Copy(src, dst, overwrite: true);
                copied.Add(name);
                totalBytes += new FileInfo(dst).Length;
            }
            if (copied.Count == 0)
            {
                // Source dir didn't have any backup-eligible files.
                // Clean up the empty timestamp folder so List doesn't
                // surface it as a phantom version.
                TryDeleteDirectoryQuietly(destDir);
                return BackupOutcome.Skipped(BackupOutcomeKind.SkippedNoSource,
                    $"Source folder {sourceDir} has no save.save / lobby.save to back up.");
            }

            // Prune AFTER the new copy lands so a partial-prune failure
            // can't leave us with zero backups for the slot.
            var pruned = PruneBackups(userId, slotName);

            return BackupOutcome.Ok(new BackupEntry(
                UserId: userId,
                SlotName: slotName,
                Timestamp: timestamp,
                BackupDirectory: destDir,
                FileNames: copied.ToArray(),
                TotalBytes: totalBytes),
                pruned: pruned);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectoryQuietly(destDir);
            return BackupOutcome.Failed($"Backup write to {destDir} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// All backups currently on disk, newest-first. Walks the entire
    /// <see cref="BackupRoot"/>; cheap (a handful of dirs per slot,
    /// a handful of slots per user).
    /// </summary>
    public IReadOnlyList<BackupEntry> ListBackups()
    {
        var root = BackupRoot;
        if (!Directory.Exists(root))
        {
            return Array.Empty<BackupEntry>();
        }
        var entries = new List<BackupEntry>();
        foreach (var userDir in Directory.EnumerateDirectories(root))
        {
            var userId = Path.GetFileName(userDir);
            foreach (var slotDir in Directory.EnumerateDirectories(userDir))
            {
                var slotName = Path.GetFileName(slotDir);
                foreach (var stampDir in Directory.EnumerateDirectories(slotDir))
                {
                    var stamp = Path.GetFileName(stampDir);
                    if (!TryParseTimestamp(stamp, out var when))
                    {
                        continue;
                    }
                    var files = new List<string>(BackupFileNames.Length);
                    long bytes = 0;
                    foreach (var name in BackupFileNames)
                    {
                        var p = Path.Combine(stampDir, name);
                        if (File.Exists(p))
                        {
                            files.Add(name);
                            bytes += new FileInfo(p).Length;
                        }
                    }
                    if (files.Count == 0)
                    {
                        continue;
                    }
                    entries.Add(new BackupEntry(
                        UserId: userId,
                        SlotName: slotName,
                        Timestamp: when,
                        BackupDirectory: stampDir,
                        FileNames: files.ToArray(),
                        TotalBytes: bytes));
                }
            }
        }
        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return entries;
    }

    /// <summary>
    /// Restore <paramref name="entry"/>'s backed-up files into the
    /// original slot folder under <paramref name="gameSaveRoot"/>.
    /// Returns the target save.save path on success; throws on
    /// failure (the UI surfaces the error in a confirm dialog).
    ///
    /// <para>
    /// Atomicity model: the restore happens via straightforward
    /// <see cref="File.Copy(string, string, bool)"/>. We don't take a
    /// transactional lock — if a copy fails partway, the slot folder
    /// may have one file from the new backup + one from the old
    /// state. The caller (MainWindowViewModel) snapshots the current
    /// state into a fresh backup BEFORE calling Restore so even that
    /// half-failed state is recoverable.
    /// </para>
    /// </summary>
    public static string Restore(BackupEntry entry, string gameSaveRoot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(gameSaveRoot);

        var slotDir = Path.Combine(gameSaveRoot, entry.UserId, entry.SlotName);
        Directory.CreateDirectory(slotDir);

        foreach (var name in entry.FileNames)
        {
            var src = Path.Combine(entry.BackupDirectory, name);
            var dst = Path.Combine(slotDir, name);
            File.Copy(src, dst, overwrite: true);
        }
        return Path.Combine(slotDir, "save.save");
    }

    /// <summary>
    /// Delete oldest timestamp folders for <c>(userId, slotName)</c>
    /// until at most <see cref="MaxVersionsPerSlot"/> remain. Returns
    /// the number of folders deleted.
    /// </summary>
    public int PruneBackups(string userId, string slotName)
    {
        var slotDir = Path.Combine(BackupRoot, userId, slotName);
        if (!Directory.Exists(slotDir))
        {
            return 0;
        }
        var stampDirs = Directory.EnumerateDirectories(slotDir)
            .Select(d => (Dir: d, Stamp: TryParseTimestamp(Path.GetFileName(d), out var t) ? t : (DateTime?)null))
            .Where(x => x.Stamp is not null)
            .OrderByDescending(x => x.Stamp)
            .ToList();
        if (stampDirs.Count <= MaxVersionsPerSlot)
        {
            return 0;
        }
        var pruned = 0;
        foreach (var (dir, _) in stampDirs.Skip(MaxVersionsPerSlot))
        {
            if (TryDeleteDirectoryQuietly(dir))
            {
                pruned++;
            }
        }
        return pruned;
    }

    /// <summary>
    /// Split a save.save path into (userId, slotName), checking it
    /// matches the <c>...\&lt;userId&gt;\&lt;slotName&gt;\save.save</c>
    /// shape Crimson Desert uses. Returns false for any path that
    /// doesn't fit — backup is silently skipped rather than risking a
    /// mis-grouped restore.
    /// </summary>
    public static bool TryDeriveSlotCoordinates(
        string savePath,
        out string userId,
        out string slotName)
    {
        userId = string.Empty;
        slotName = string.Empty;
        if (string.IsNullOrEmpty(savePath))
        {
            return false;
        }
        // Don't require the filename to be exactly save.save: Save As
        // might write to a copy with a different name. But the parent
        // directories MUST be {userId}\{slotName}. Use directory
        // names only.
        var slotDir = Path.GetDirectoryName(savePath);
        if (string.IsNullOrEmpty(slotDir))
        {
            return false;
        }
        var userDir = Path.GetDirectoryName(slotDir);
        if (string.IsNullOrEmpty(userDir))
        {
            return false;
        }
        var slot = Path.GetFileName(slotDir);
        var user = Path.GetFileName(userDir);
        if (string.IsNullOrEmpty(slot) || string.IsNullOrEmpty(user))
        {
            return false;
        }
        // Sanity: don't accept paths that don't look like slot folders.
        // Real saves are `slot0..slot199` (observed slot0..slot105 in
        // 1.06); a user ID is a numeric string. Tolerate any non-empty
        // slot/user names — the backup tree just mirrors whatever the
        // game produces.
        slotName = slot;
        userId = user;
        return true;
    }

    /// <summary>Render <paramref name="when"/> as <c>YYYY-MM-DD_HH-mm-ss</c>.</summary>
    public static string FormatTimestamp(DateTime when) =>
        when.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

    /// <summary>Inverse of <see cref="FormatTimestamp"/>.</summary>
    public static bool TryParseTimestamp(string s, out DateTime when) =>
        DateTime.TryParseExact(
            s, "yyyy-MM-dd_HH-mm-ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out when);

    private static bool TryDeleteDirectoryQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

/// <summary>
/// One backup snapshot on disk: which slot it came from, when it was
/// taken, where the files live, and what they're called.
/// </summary>
public sealed record BackupEntry(
    string UserId,
    string SlotName,
    DateTime Timestamp,
    string BackupDirectory,
    IReadOnlyList<string> FileNames,
    long TotalBytes);

/// <summary>
/// Result of a <see cref="SaveBackupService.BackupBeforeWrite"/> call.
/// Non-throwing by design — the UI inspects <see cref="Kind"/> to
/// decide whether to surface a status message.
/// </summary>
public sealed record BackupOutcome(
    BackupOutcomeKind Kind,
    string Message,
    BackupEntry? Entry,
    int VersionsPruned)
{
    public bool IsSuccess => Kind == BackupOutcomeKind.Ok;

    public static BackupOutcome Ok(BackupEntry entry, int pruned) =>
        new(BackupOutcomeKind.Ok,
            $"Backed up {entry.SlotName} ({entry.TotalBytes:N0} bytes); pruned {pruned} old version(s).",
            entry, pruned);

    public static BackupOutcome Skipped(BackupOutcomeKind kind, string message) =>
        new(kind, message, null, 0);

    public static BackupOutcome Failed(string message) =>
        new(BackupOutcomeKind.Failed, message, null, 0);
}

public enum BackupOutcomeKind
{
    Ok,
    SkippedNoSource,
    SkippedBadPath,
    Failed,
}
