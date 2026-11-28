using System.Diagnostics;
using System.Globalization;
using System.Text;
using CrimsonAtomtic.Core;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// The file log Rule 6 describes: <c>Logs\&lt;category&gt;\&lt;process&gt;\&lt;category&gt;.log</c>,
/// rotated at 8 MB through four files (<c>.log</c>, <c>.1.log</c> … <c>.3.log</c>).
/// Writes are serialised; a failed write is dropped, since the log must
/// never be the reason an edit fails.
/// </summary>
public sealed class FileAppLog : IAppLog
{
    private const long MaxBytes = 8L * 1024 * 1024;
    private const int Generations = 4;

    private readonly string _root;
    private readonly string _process;
    private readonly object _gate = new();

    public FileAppLog(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(logDirectory);
        _root = logDirectory;
        _process = Process.GetCurrentProcess().ProcessName;
    }

    public void Write(string category, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                var dir = Path.Combine(_root, category, _process);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, category + ".log");
                if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)
                {
                    Rotate(dir, category);
                }
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dropped: see the class remarks.
            }
        }
    }

    private static void Rotate(string dir, string category)
    {
        string Name(int n) => Path.Combine(dir, n == 0 ? category + ".log" : $"{category}.{n}.log");
        File.Delete(Name(Generations - 1));
        for (var n = Generations - 2; n >= 0; n--)
        {
            if (File.Exists(Name(n)))
            {
                File.Move(Name(n), Name(n + 1));
            }
        }
    }
}

/// <summary>
/// The log the app writes to; set at startup, a no-op until then (tests run
/// without one).
/// </summary>
public static class AppLog
{
    private sealed class Nothing : IAppLog
    {
        public void Write(string category, string message) { }
    }

    public static IAppLog Current { get; set; } = new Nothing();

    public static void Write(string category, string message) => Current.Write(category, message);
}
