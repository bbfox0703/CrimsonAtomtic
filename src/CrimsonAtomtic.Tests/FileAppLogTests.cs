using System.Diagnostics;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

public sealed class FileAppLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "crimson-applog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Dir(string category) =>
        Path.Combine(_root, category, Process.GetCurrentProcess().ProcessName);

    [Fact]
    public void Write_GoesUnderCategoryAndProcess_NoLooseFilesInRoot()
    {
        new FileAppLog(_root).Write("save", "hello");

        var log = Path.Combine(Dir("save"), "save.log");
        Assert.Contains("hello", File.ReadAllText(log));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void Write_OverLimit_RotatesThroughFourFiles()
    {
        var dir = Dir("errors");
        Directory.CreateDirectory(dir);
        var big = new string('x', 8 * 1024 * 1024);
        var log = new FileAppLog(_root);
        for (var n = 0; n < 6; n++)
        {
            File.WriteAllText(Path.Combine(dir, "errors.log"), big);
            log.Write("errors", "line " + n);
        }

        var files = Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(["errors.1.log", "errors.2.log", "errors.3.log", "errors.log"], files);
        Assert.Contains("line 5", File.ReadAllText(Path.Combine(dir, "errors.log")));
    }
}
