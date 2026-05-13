using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// End-to-end test for the icon-extraction pipeline. Runs the full
/// orchestrator against the live 1.06 install if it's present, writes
/// every item icon to a temp directory, and asserts the result. Heavy
/// (~30-60 s for 6,400 items) so it's env-gated — set
/// <c>CRIMSON_RUN_EXTRACTION_TEST=1</c> before running.
/// </summary>
public sealed class IconExtractionServiceTests
{
    private const string GateEnvVar = "CRIMSON_RUN_EXTRACTION_TEST";

    private static string? FindGameRoot()
    {
        string[] candidates =
        [
            @"D:\SteamLibrary\steamapps\common\Crimson Desert",
            @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
            @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
            @"E:\SteamLibrary\steamapps\common\Crimson Desert",
            @"F:\SteamLibrary\steamapps\common\Crimson Desert",
        ];
        foreach (var root in candidates)
        {
            if (Directory.Exists(Path.Combine(root, "0008"))
                && Directory.Exists(Path.Combine(root, "0012")))
            {
                return root;
            }
        }
        return null;
    }

    [Fact]
    public async Task RunAsync_LiveInstall_WritesIcons()
    {
        if (Environment.GetEnvironmentVariable(GateEnvVar) != "1")
        {
            // Heavy test — opt in via env var.
            return;
        }
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var gameRoot = FindGameRoot();
        if (gameRoot is null)
        {
            return;
        }

        // Bootstrap a fresh LocalizationProvider against the live
        // install. Same code path as App.OnFrameworkInitializationCompleted
        // — exercising the full chain ensures the menu action in the
        // app behaves the same way.
        var paz = new NativePazExtractor();
        using var localization = new LocalizationProvider(paz);
        var bootstrapped = localization.TryBootstrapFromGameRoot(gameRoot);
        Assert.True(bootstrapped, "expected bootstrap against the live install to succeed");
        Assert.True(localization.HasStringInfo, "stringinfo bridge must be loaded");
        Assert.True(localization.ItemCount > 5_000,
            $"expected >5k items in iteminfo, got {localization.ItemCount}");

        // Optional target-path override + keep-on-completion mode. Used
        // for one-off cache warm-ups (e.g. populating the user's
        // configured icon directory). When CRIMSON_EXTRACTION_TARGET is
        // set, write there and don't clean up at the end; otherwise the
        // test uses a temp dir and tidies up.
        var targetOverride = Environment.GetEnvironmentVariable("CRIMSON_EXTRACTION_TARGET");
        var keepOutput = !string.IsNullOrEmpty(targetOverride);
        var cacheDir = keepOutput
            ? targetOverride!
            : Path.Combine(Path.GetTempPath(),
                $"crimson-icon-extraction-test-{Guid.NewGuid():N}");
        try
        {
            var progressUpdates = new List<IconExtractionProgress>();
            var progress = new Progress<IconExtractionProgress>(progressUpdates.Add);

            var result = await IconExtractionService.RunAsync(
                localization, paz, gameRoot, cacheDir,
                overwriteExisting: false, progress: progress,
                cancellationToken: CancellationToken.None);

            // Sanity: the bulk of items have icons. 1.06 ships ~6,400
            // items and most have an entry; expect >1,000 successful
            // writes even after subtracting skips/failures.
            Assert.True(result.Written > 1_000,
                $"expected >1,000 writes, got {result.Written}. "
                + $"Result: Total={result.Total} Written={result.Written} "
                + $"SkippedNoIcon={result.SkippedNoIcon} "
                + $"SkippedNoString={result.SkippedNoString} "
                + $"SkippedNotInArchive={result.SkippedNotInArchive} "
                + $"Failed={result.Failed}");

            // Pyeonjeon_Arrow (ItemKey 2200) has a known icon —
            // ItemIcon_Prefab_cd_phm_04_arw_0020.dds. Its webp should
            // exist on disk and be non-trivial.
            var arrowWebp = Path.Combine(cacheDir, "2200.webp");
            Assert.True(File.Exists(arrowWebp),
                $"expected {arrowWebp} to be written");
            Assert.True(new FileInfo(arrowWebp).Length > 100,
                "extracted webp should be more than 100 bytes");

            // Progress was reported at least a handful of times for a
            // run that big.
            Assert.True(progressUpdates.Count > 10,
                $"expected >10 progress updates, got {progressUpdates.Count}");
        }
        finally
        {
            if (!keepOutput)
            {
                try { Directory.Delete(cacheDir, recursive: true); }
                catch (IOException) { /* leave the dir around if cleanup fails */ }
            }
        }
    }
}
