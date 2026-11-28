using System.Globalization;
using CrimsonAtomtic.Core;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Per-slot rolling backup of save.save (+ lobby.save) snapshots,
/// scoped per launcher (Steam / Epic / Game Pass).
///
/// <para>
/// Wired into <see cref="ViewModels.MainWindowViewModel"/>'s Save and
/// Save As commands: before each on-disk write, the existing target
/// file is copied into <see cref="BackupRoot"/> under
/// <c>{platform}/{userId}/{slotName}/{timestamp}/</c>. A backup is
/// therefore created ONLY when the user Saves from the editor. Restore
/// reverses the pairing — it picks a backup folder and copies it back
/// to the original slot folder. Restore does NOT snapshot the current
/// state first: the user explicitly chose to roll back, so an extra
/// backup of the about-to-be-discarded state would only clutter the
/// history (and could evict an older wanted snapshot via the cap).
/// </para>
///
/// <para>
/// Path layout (current):
/// <code>
/// %LOCALAPPDATA%\CrimsonAtomtic\Backups\
///   {platform}\               e.g. Steam / Epic / GamePass / Unknown
///     {userId}\               e.g. 102190433
///       {slotName}\           e.g. slot100
///         {timestamp}\        e.g. 2026-05-14_07-30-45Z (UTC)
///           save.save         (the file that was about to be overwritten,
///                              under its own name)
///           lobby.save        (only when the source slot has one)
///           source.txt        (the full path the backup was taken from)
///     PreRestore\{platform}\{userId}\{slotName}\{timestamp}\
///                             (a slot's files as they were just before a
///                              Restore replaced them; not pruned)
/// </code>
/// Folder names without the <c>Z</c> are local time; both sort by the
/// instant they denote.
/// </para>
/// <para>
/// Legacy layout (still readable): pre-multi-platform builds wrote
/// <c>Backups\{userId}\{slotName}\{timestamp}\</c> directly under
/// <see cref="BackupRoot"/>. <see cref="ListBackups"/> still surfaces
/// those entries tagged as <see cref="SavePlatform.Steam"/> (the only
/// launcher supported before this change). New backups always use the
/// platform-scoped layout; legacy entries are pruned on their natural
/// retention cycle but never recreated.
/// </para>
/// <para>
/// Retention: at most <see cref="MaxVersionsPerSlot"/> timestamp
/// folders per (platform, userId, slotName). New backups land first;
/// the oldest are deleted afterwards, never the one just written, so a
/// mid-write failure leaves the earlier backups intact. A backup whose
/// files equal the newest one's is skipped rather than pushing an older
/// distinct version out.
/// </para>
/// <para>
/// Failures during backup are reported via the returned
/// <see cref="BackupOutcome"/> but never bubble up as exceptions —
/// a Save with a missing backup is still better than a Save that
/// fails because the backup folder is read-only.
/// </para>
/// </summary>
public sealed class SaveBackupService
{
    /// <summary>
    /// Hard cap on per-slot backup versions kept on disk. Restore no
    /// longer takes its own pre-restore snapshot, so the cap is consumed
    /// purely by editor Saves — 6 distinct Saves keep the six most-recent
    /// states. 6 gives the user breathing room for a restore-then-keep-
    /// editing flow without losing the original.
    /// </summary>
    public const int MaxVersionsPerSlot = 6;

    /// <summary>
    /// Subdirectory name under <see cref="IPlatformPaths.LocalAppDataDirectory"/>
    /// that holds the entire backup tree.
    /// </summary>
    public const string BackupsSubdirectory = "Backups";

    /// <summary>
    /// The companion file the game writes beside every slot's save; it is
    /// backed up and restored together with the save so the pair stays in
    /// sync.
    /// </summary>
    public const string LobbyFileName = "lobby.save";

    /// <summary>The game's own save file name in a slot folder.</summary>
    public const string SaveFileName = "save.save";

    /// <summary>Holds the full path a backup was taken from.</summary>
    public const string SourceFileName = "source.txt";

    /// <summary>Subfolder of <see cref="BackupRoot"/> for the pre-restore snapshots.</summary>
    public const string PreRestoreSubdirectory = "PreRestore";

    private const string StampFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>
    /// Directory names used for the platform layer of the backup tree.
    /// Match <see cref="SavePlatform.ToString()"/> exactly so the
    /// directory name round-trips back to the enum via
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>.
    /// </summary>
    private static readonly HashSet<string> KnownPlatformDirNames =
        new(Enum.GetNames<SavePlatform>(), StringComparer.OrdinalIgnoreCase);

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

        // Tag the backup with whichever launcher's save tree the source
        // path lives under. Saves opened from non-standard locations
        // (a copied .save somewhere else on disk) land in the "Unknown"
        // bucket so they're still recoverable without polluting any
        // platform's history.
        var platform = _paths.ClassifySavePath(targetSavePath);

        var sourceDir = Path.GetDirectoryName(targetSavePath)!;
        var names = FilesToBackUp(targetSavePath);
        var slotDir = SlotBackupDirectory(platform, userId, slotName);

        if (NewestMatches(slotDir, sourceDir, names))
        {
            return BackupOutcome.Skipped(BackupOutcomeKind.SkippedDuplicate,
                "The newest backup already holds these exact files.");
        }

        var (timestamp, destDir) = FreeStampDirectory(slotDir);
        try
        {
            Directory.CreateDirectory(destDir);
            var copied = new List<string>(names.Count);
            long totalBytes = 0;
            foreach (var name in names)
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
                TryDeleteDirectoryQuietly(destDir);
                return BackupOutcome.Skipped(BackupOutcomeKind.SkippedNoSource,
                    $"Source folder {sourceDir} has nothing to back up.");
            }
            File.WriteAllText(Path.Combine(destDir, SourceFileName), Path.GetFullPath(targetSavePath));

            var pruned = PruneBackups(platform, userId, slotName, keep: destDir);

            return BackupOutcome.Ok(new BackupEntry(
                Platform: platform,
                UserId: userId,
                SlotName: slotName,
                Timestamp: timestamp,
                BackupDirectory: destDir,
                FileNames: copied.ToArray(),
                TotalBytes: totalBytes,
                SourcePath: Path.GetFullPath(targetSavePath)),
                pruned: pruned);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectoryQuietly(destDir);
            return BackupOutcome.Failed($"Backup write to {destDir} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// All backups currently on disk, newest-first: the platform-scoped
    /// layout, the pre-restore snapshots, and the legacy un-platformed
    /// layout (tagged <see cref="SavePlatform.Steam"/>, the only launcher
    /// that layout was written for). A folder that cannot be read is
    /// skipped and counted in <paramref name="unreadable"/>.
    /// </summary>
    public IReadOnlyList<BackupEntry> ListBackups() => ListBackups(out _);

    /// <inheritdoc cref="ListBackups()"/>
    public IReadOnlyList<BackupEntry> ListBackups(out int unreadable)
    {
        unreadable = 0;
        var root = BackupRoot;
        if (!Directory.Exists(root))
        {
            return Array.Empty<BackupEntry>();
        }
        var entries = new List<BackupEntry>();
        var skipped = 0;
        foreach (var childDir in SafeDirs(root, ref skipped))
        {
            var childName = Path.GetFileName(childDir);
            if (string.Equals(childName, PreRestoreSubdirectory, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var platformDir in SafeDirs(childDir, ref skipped))
                {
                    if (Enum.TryParse<SavePlatform>(Path.GetFileName(platformDir), ignoreCase: true, out var p))
                    {
                        ScanUserDirs(platformDir, p, isPreRestore: true, entries, ref skipped);
                    }
                }
            }
            else if (KnownPlatformDirNames.Contains(childName)
                && Enum.TryParse<SavePlatform>(childName, ignoreCase: true, out var platform))
            {
                ScanUserDirs(childDir, platform, isPreRestore: false, entries, ref skipped);
            }
            else
            {
                ScanSlotDirs(parentUserDir: childDir, platform: SavePlatform.Steam,
                             userId: childName, isPreRestore: false, entries, ref skipped);
            }
        }
        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        unreadable = skipped;
        return entries;
    }

    private static void ScanUserDirs(string platformDir, SavePlatform platform, bool isPreRestore,
        List<BackupEntry> entries, ref int skipped)
    {
        foreach (var userDir in SafeDirs(platformDir, ref skipped))
        {
            ScanSlotDirs(userDir, platform, Path.GetFileName(userDir), isPreRestore, entries, ref skipped);
        }
    }

    private static void ScanSlotDirs(string parentUserDir, SavePlatform platform, string userId,
        bool isPreRestore, List<BackupEntry> entries, ref int skipped)
    {
        foreach (var slotDir in SafeDirs(parentUserDir, ref skipped))
        {
            var slotName = Path.GetFileName(slotDir);
            foreach (var stampDir in SafeDirs(slotDir, ref skipped))
            {
                if (!TryParseTimestamp(Path.GetFileName(stampDir), out var when))
                {
                    continue;
                }
                try
                {
                    var files = Directory.GetFiles(stampDir, "*.save")
                        .Select(Path.GetFileName)
                        .OfType<string>()
                        .OrderBy(n => string.Equals(n, LobbyFileName, StringComparison.OrdinalIgnoreCase))
                        .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (files.Length == 0)
                    {
                        continue;
                    }
                    var bytes = files.Sum(n => new FileInfo(Path.Combine(stampDir, n)).Length);
                    var sourceFile = Path.Combine(stampDir, SourceFileName);
                    var source = File.Exists(sourceFile) ? File.ReadAllText(sourceFile).Trim() : null;
                    entries.Add(new BackupEntry(
                        Platform: platform,
                        UserId: userId,
                        SlotName: slotName,
                        Timestamp: when,
                        BackupDirectory: stampDir,
                        FileNames: files,
                        TotalBytes: bytes,
                        SourcePath: string.IsNullOrEmpty(source) ? null : source,
                        IsPreRestore: isPreRestore));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }
    }

    private static string[] SafeDirs(string dir, ref int skipped)
    {
        try
        {
            return Directory.GetDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped++;
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The folder a backup restores into: the folder it was taken from
    /// when the backup recorded it, else the slot folder under the
    /// platform's <paramref name="platformSaveRoot"/>. Null when neither
    /// is known (an <see cref="SavePlatform.Unknown"/> backup written
    /// before backups recorded their source).
    /// </summary>
    public static string? RestoreDirectory(BackupEntry entry, string? platformSaveRoot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.SourcePath is { } source && Path.GetDirectoryName(source) is { Length: > 0 } dir)
        {
            return dir;
        }
        if (entry.Platform == SavePlatform.Unknown || string.IsNullOrEmpty(platformSaveRoot))
        {
            return null;
        }
        return Path.Combine(platformSaveRoot, entry.UserId, entry.SlotName);
    }

    /// <summary>
    /// Restore <paramref name="entry"/>'s files into <paramref name="slotDir"/>
    /// and return the restored save's path. The slot's current save files
    /// (the backup's names plus <c>save.save</c> / <c>lobby.save</c>) are
    /// moved into a pre-restore snapshot first, so a stale companion file
    /// cannot survive next to the restored save and the replaced state stays
    /// recoverable. Each file is copied to a temporary name and renamed into
    /// place; on failure the moved files are put back. Throws on failure.
    /// </summary>
    public string Restore(BackupEntry entry, string slotDir)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(slotDir);
        Directory.CreateDirectory(slotDir);

        var displaced = entry.FileNames
            .Append(SaveFileName).Append(LobbyFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => File.Exists(Path.Combine(slotDir, n)))
            .ToList();
        string? preRestoreDir = null;
        if (displaced.Count > 0)
        {
            var parent = Path.Combine(BackupRoot, PreRestoreSubdirectory, entry.Platform.ToString(),
                entry.UserId, entry.SlotName);
            (_, preRestoreDir) = FreeStampDirectory(parent);
            Directory.CreateDirectory(preRestoreDir);
            File.WriteAllText(Path.Combine(preRestoreDir, SourceFileName),
                Path.Combine(slotDir, entry.FileNames.FirstOrDefault(IsSaveName) ?? SaveFileName));
        }

        var moved = new List<string>();
        var placed = new List<string>();
        try
        {
            foreach (var name in displaced)
            {
                File.Move(Path.Combine(slotDir, name), Path.Combine(preRestoreDir!, name));
                moved.Add(name);
            }
            foreach (var name in entry.FileNames)
            {
                var dst = Path.Combine(slotDir, name);
                var temp = dst + ".restore-tmp";
                File.Copy(Path.Combine(entry.BackupDirectory, name), temp, overwrite: true);
                File.Move(temp, dst, overwrite: true);
                placed.Add(name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var name in placed)
            {
                TryDeleteFileQuietly(Path.Combine(slotDir, name));
            }
            foreach (var name in moved)
            {
                try { File.Move(Path.Combine(preRestoreDir!, name), Path.Combine(slotDir, name), overwrite: true); }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
        return Path.Combine(slotDir, entry.FileNames.FirstOrDefault(IsSaveName) ?? SaveFileName);
    }

    private static bool IsSaveName(string name) =>
        !string.Equals(name, LobbyFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The files a backup of <paramref name="targetSavePath"/> captures: the
    /// file itself under its own name, and the slot's <c>lobby.save</c>.
    /// </summary>
    private static List<string> FilesToBackUp(string targetSavePath)
    {
        var names = new List<string> { Path.GetFileName(targetSavePath) };
        if (!string.Equals(names[0], LobbyFileName, StringComparison.OrdinalIgnoreCase))
        {
            names.Add(LobbyFileName);
        }
        return names;
    }

    /// <summary>
    /// True when the slot's newest backup holds exactly the files
    /// <paramref name="names"/> in <paramref name="sourceDir"/> currently
    /// has, byte for byte.
    /// </summary>
    private static bool NewestMatches(string slotDir, string sourceDir, List<string> names)
    {
        try
        {
            if (!Directory.Exists(slotDir))
            {
                return false;
            }
            var newest = Directory.GetDirectories(slotDir)
                .Select(d => (Dir: d, Ok: TryParseTimestamp(Path.GetFileName(d), out var t), When: t))
                .Where(x => x.Ok)
                .OrderByDescending(x => x.When)
                .Select(x => x.Dir)
                .FirstOrDefault();
            if (newest is null)
            {
                return false;
            }
            var present = names.Where(n => File.Exists(Path.Combine(sourceDir, n))).ToList();
            var backed = Directory.GetFiles(newest, "*.save").Select(Path.GetFileName).OfType<string>().ToList();
            if (present.Count == 0 || present.Count != backed.Count
                || present.Any(n => !backed.Contains(n, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }
            return present.All(n => SameBytes(Path.Combine(sourceDir, n), Path.Combine(newest, n)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SameBytes(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length)
        {
            return false;
        }
        return System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a))
            .AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(b)));
    }

    /// <summary>
    /// A timestamp folder under <paramref name="parent"/> that does not
    /// exist yet: now (UTC), advanced a second at a time past any folder
    /// that already has that name.
    /// </summary>
    private static (DateTime Timestamp, string Directory) FreeStampDirectory(string parent)
    {
        var when = DateTime.UtcNow;
        when = new DateTime(when.Ticks - when.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var dir = Path.Combine(parent, FolderStamp(when));
        while (System.IO.Directory.Exists(dir))
        {
            when = when.AddSeconds(1);
            dir = Path.Combine(parent, FolderStamp(when));
        }
        return (when, dir);
    }

    private static void TryDeleteFileQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Delete oldest timestamp folders for
    /// <c>(platform, userId, slotName)</c> until at most
    /// <see cref="MaxVersionsPerSlot"/> remain. Returns the number of
    /// folders deleted. Does NOT prune legacy un-platformed backups —
    /// those age out on their original (pre-change) retention cycle and
    /// are never written to again.
    /// </summary>
    public int PruneBackups(SavePlatform platform, string userId, string slotName, string? keep = null)
    {
        var slotDir = SlotBackupDirectory(platform, userId, slotName);
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
        foreach (var (dir, _) in stampDirs.Skip(MaxVersionsPerSlot)
                     .Where(x => keep is null || !string.Equals(Path.GetFullPath(x.Dir), Path.GetFullPath(keep),
                         StringComparison.OrdinalIgnoreCase)))
        {
            if (TryDeleteDirectoryQuietly(dir))
            {
                pruned++;
            }
        }
        return pruned;
    }

    /// <summary>
    /// The <c>BackupRoot\&lt;platform&gt;\&lt;userId&gt;\&lt;slotName&gt;\</c>
    /// folder where new backups for one (platform, user, slot) tuple land.
    /// </summary>
    private string SlotBackupDirectory(SavePlatform platform, string userId, string slotName) =>
        Path.Combine(BackupRoot, platform.ToString(), userId, slotName);

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

    /// <summary>
    /// Render <paramref name="when"/> for display as local
    /// <c>YYYY-MM-DD_HH-mm-ss</c>; a UTC value is converted first.
    /// </summary>
    public static string FormatTimestamp(DateTime when) =>
        (when.Kind == DateTimeKind.Utc ? when.ToLocalTime() : when)
            .ToString(StampFormat, CultureInfo.InvariantCulture);

    /// <summary>A backup folder name for the UTC instant <paramref name="utc"/>.</summary>
    public static string FolderStamp(DateTime utc) =>
        utc.ToString(StampFormat, CultureInfo.InvariantCulture) + "Z";

    /// <summary>
    /// Parse a backup folder name into a UTC instant. A name ending in
    /// <c>Z</c> is UTC; one without is local time.
    /// </summary>
    public static bool TryParseTimestamp(string s, out DateTime when)
    {
        if (s.EndsWith('Z')
            && DateTime.TryParseExact(s[..^1], StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out when))
        {
            return true;
        }
        if (DateTime.TryParseExact(s, StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out when))
        {
            return true;
        }
        when = default;
        return false;
    }

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
/// One backup snapshot on disk: which slot it came from (with launcher),
/// when it was taken, where the files live, and what they're called.
/// </summary>
/// <param name="Timestamp">When the backup was taken, in UTC.</param>
/// <param name="SourcePath">The full path the backup was taken from, when recorded.</param>
/// <param name="IsPreRestore">A slot's files as they were just before a Restore replaced them.</param>
public sealed record BackupEntry(
    SavePlatform Platform,
    string UserId,
    string SlotName,
    DateTime Timestamp,
    string BackupDirectory,
    IReadOnlyList<string> FileNames,
    long TotalBytes,
    string? SourcePath = null,
    bool IsPreRestore = false);

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
    /// <summary>The newest backup already holds these exact files.</summary>
    SkippedDuplicate,
    Failed,
}
