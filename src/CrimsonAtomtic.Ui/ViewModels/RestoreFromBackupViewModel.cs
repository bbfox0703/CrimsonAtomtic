using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the "Restore from Backup" picker dialog.
///
/// <para>
/// Reads the on-disk backup tree via <see cref="SaveBackupService.ListBackups"/>,
/// surfaces one row per snapshot (newest first), and raises
/// <see cref="RestoreRequested"/> when the user clicks Restore on a
/// row. The main window subscribes and routes the request to
/// <c>MainWindowViewModel.RestoreFromBackupAsync</c> — same decoupling
/// pattern <see cref="ItemPickerViewModel"/> uses for "+ Bag".
/// </para>
///
/// <para>
/// The list is captured once at construction. Close + reopen the
/// dialog after a Save / Restore to refresh.
/// </para>
/// </summary>
public sealed partial class RestoreFromBackupViewModel : ObservableObject
{
    public RestoreFromBackupViewModel(SaveBackupService backupService)
    {
        ArgumentNullException.ThrowIfNull(backupService);
        BackupRoot = backupService.BackupRoot;
        var raw = backupService.ListBackups();
        Backups = new ObservableCollection<BackupRowViewModel>(
            raw.Select(b => new BackupRowViewModel(b)));
    }

    /// <summary>Display string for the dialog header.</summary>
    public string BackupRoot { get; }

    /// <summary>Backup snapshots, newest-first.</summary>
    public ObservableCollection<BackupRowViewModel> Backups { get; }

    public bool HasBackups => Backups.Count > 0;

    public string Subtitle => Backups.Count switch
    {
        0 => $"No backups yet under {BackupRoot}.",
        1 => $"1 backup under {BackupRoot}.",
        _ => $"{Backups.Count} backups under {BackupRoot}.",
    };

    /// <summary>
    /// Fires when the user clicks a row's Restore button. MainWindow
    /// subscribes; the picker stays decoupled from the main VM.
    /// </summary>
    public event Action<BackupEntry>? RestoreRequested;

    /// <summary>
    /// Invoked from the AXAML code-behind when a row's Restore button
    /// fires. Public so the view can reach it without using reflection.
    /// </summary>
    public void RequestRestore(BackupEntry entry) => RestoreRequested?.Invoke(entry);
}

/// <summary>
/// DataGrid row VM for one <see cref="BackupEntry"/>. Wraps the entry
/// so AXAML can bind to formatted display fields without re-running
/// the format string on every render.
/// </summary>
public sealed class BackupRowViewModel
{
    public BackupRowViewModel(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        SlotLabel = $"{entry.UserId} / {entry.SlotName}";
        TimestampLabel = SaveBackupService.FormatTimestamp(entry.Timestamp);
        FileSummary = string.Join(" + ", entry.FileNames);
        // Show as "1.42 MB" / "315 KB" so the user can spot truncations
        // (the engine writes ~5 MB bodies that compress to ~1.5 MB
        // .save files; anything radically smaller suggests a partial).
        TotalBytesLabel = FormatBytes(entry.TotalBytes);
    }

    public BackupEntry Entry { get; }

    public string SlotLabel { get; }
    public string TimestampLabel { get; }
    public string FileSummary { get; }
    public string TotalBytesLabel { get; }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    /// <summary>
    /// Convenience exposing the timestamp's date as a culture-invariant
    /// string for the DataGrid sort key. The format-string label above
    /// is for display; this one is for ordering / accessibility.
    /// </summary>
    public string TimestampSortKey =>
        Entry.Timestamp.ToString("o", CultureInfo.InvariantCulture);
}
