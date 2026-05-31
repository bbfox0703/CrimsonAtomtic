using System.Globalization;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// End-to-end mechanics test for the dragon mount-unlock path at the loader
/// level (the genuinely-new runtime pieces: extract the embedded donor →
/// temp file → load as a source loader → <see cref="ISaveLoader.TransplantListElement"/>
/// into a real save → write + reload). Mirrors what
/// <c>MainWindowViewModel.UnlockDragonAsync</c> does, minus the VM glue, so
/// the transplant + HMAC round-trip is guarded without needing
/// InternalsVisibleTo.
///
/// <para>Skips cleanly when no live save is present (CI / fresh machine),
/// matching every other save-touching test. Never writes the user's real
/// save — it loads read-only and writes only to a temp output.</para>
/// </summary>
public sealed class MountUnlockMechanicsTests
{
    private const string MercClass = "MercenaryClanSaveData";
    private const string MercListField = "_mercenaryDataList";
    private const string CharKeyField = "_characterKey";

    private static string? FindLiveSave()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(local))
        {
            return null;
        }
        var root = Path.Combine(local, "Pearl Abyss", "CD", "save");
        if (!Directory.Exists(root))
        {
            return null;
        }
        foreach (var user in Directory.EnumerateDirectories(root))
        {
            // Prefer the manual slots (more likely fully-progressed), then
            // the autosaves.
            foreach (var slot in new[] { "slot105", "slot100", "slot0", "slot1", "slot2" })
            {
                var p = Path.Combine(user, slot, "save.save");
                if (File.Exists(p))
                {
                    return p;
                }
            }
        }
        return null;
    }

    private static string ExtractDonorToTemp()
    {
        var asm = typeof(MountCatalog).Assembly;
        using var res = asm.GetManifestResourceStream(MountCatalog.DragonDonorResourceName);
        Assert.NotNull(res);
        var tmp = Path.Combine(Path.GetTempPath(), $"cd_dragon_donor_test_{Guid.NewGuid():N}.save");
        using var fs = File.Create(tmp);
        res!.CopyTo(fs);
        return tmp;
    }

    private static (int BlockIndex, int FieldIndex, int Count) FindMercList(
        NativeSaveLoader loader, string path, IReadOnlyList<BlockSummary> blocks)
    {
        foreach (var b in blocks)
        {
            if (!string.Equals(b.ClassName, MercClass, StringComparison.Ordinal))
            {
                continue;
            }
            var details = loader.LoadBlockDetails(path, b.Index);
            foreach (var f in details.Fields)
            {
                if (string.Equals(f.Name, MercListField, StringComparison.Ordinal)
                    && f.Elements is { } els)
                {
                    return (b.Index, f.FieldIndex, els.Count);
                }
            }
        }
        return (-1, -1, -1);
    }

    private static int FindCharKeyElement(
        NativeSaveLoader loader, string path, int blockIdx, uint charKey)
    {
        var details = loader.LoadBlockDetails(path, blockIdx);
        foreach (var f in details.Fields)
        {
            if (!string.Equals(f.Name, MercListField, StringComparison.Ordinal)
                || f.Elements is not { } els)
            {
                continue;
            }
            for (var i = 0; i < els.Count; i++)
            {
                foreach (var cf in els[i].Fields)
                {
                    if (string.Equals(cf.Name, CharKeyField, StringComparison.Ordinal)
                        && ParseLeadingUInt(cf.Value) == charKey)
                    {
                        return i;
                    }
                }
            }
        }
        return -1;
    }

    private static uint ParseLeadingUInt(string formatted)
    {
        var token = formatted.Split(' ', 2)[0];
        return uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }

    [Fact]
    public void TransplantDragon_GrowsMercList_AndSurvivesRoundTrip()
    {
        var targetPath = FindLiveSave();
        if (targetPath is null)
        {
            return; // No live save — skip on CI / fresh machine.
        }

        var donorPath = ExtractDonorToTemp();
        var outPath = Path.Combine(Path.GetTempPath(), $"cd_dragon_out_{Guid.NewGuid():N}.save");
        try
        {
            using var target = new NativeSaveLoader();
            var targetSummary = target.Load(targetPath);

            using var source = new NativeSaveLoader();
            var sourceSummary = source.Load(donorPath);

            var tgt = FindMercList(target, targetPath, targetSummary.Blocks);
            var src = FindMercList(source, donorPath, sourceSummary.Blocks);
            Assert.True(tgt.BlockIndex >= 0, "target has no MercenaryClanSaveData merc list");
            Assert.True(src.BlockIndex >= 0, "donor has no MercenaryClanSaveData merc list");

            var dragonIdx = FindCharKeyElement(source, donorPath, src.BlockIndex,
                MountCatalog.DragonCharacterKey);
            Assert.True(dragonIdx >= 0,
                "embedded donor no longer contains the dragon element (charKey 1000799)");

            var insertAt = tgt.Count;
            target.TransplantListElement(
                source,
                tgt.BlockIndex, ReadOnlySpan<PathStep>.Empty, tgt.FieldIndex, insertAt,
                src.BlockIndex, ReadOnlySpan<PathStep>.Empty, src.FieldIndex, dragonIdx);

            // The list grew by exactly one and the tail is the dragon.
            var afterCount = FindMercList(target, targetPath, targetSummary.Blocks).Count;
            Assert.Equal(insertAt + 1, afterCount);
            var newDragonIdx = FindCharKeyElement(target, targetPath, tgt.BlockIndex,
                MountCatalog.DragonCharacterKey);
            Assert.Equal(insertAt, newDragonIdx);

            // Persisted shape survives a full encode → HMAC → reload.
            target.WriteToFile(outPath);
            using var reloaded = new NativeSaveLoader();
            var reSummary = reloaded.Load(outPath);
            Assert.True(reSummary.HmacOk, "round-tripped save failed HMAC");
            var reBlock = FindMercList(reloaded, outPath, reSummary.Blocks);
            Assert.Equal(insertAt + 1, reBlock.Count);
            Assert.True(
                FindCharKeyElement(reloaded, outPath, reBlock.BlockIndex,
                    MountCatalog.DragonCharacterKey) >= 0,
                "dragon element missing after reload");
        }
        finally
        {
            TryDelete(donorPath);
            TryDelete(outPath);
        }
    }

    private static void TryDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); }
        catch (IOException) { /* temp leak is harmless */ }
    }
}
