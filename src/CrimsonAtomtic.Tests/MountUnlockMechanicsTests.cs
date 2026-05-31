using System.Collections.Generic;
using System.Globalization;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// End-to-end mechanics test for the dragon mount-unlock at the loader level
/// (mirrors <c>MainWindowViewModel.InsertDragonElementAsync</c> +
/// <c>FillDragonHpAsync</c>, minus the VM glue): remap the embedded
/// <see cref="MountCatalog.DragonElementHex"/>'s schema type-indices onto a
/// real save by class name, <see cref="ISaveLoader.ListInsertElement"/> it,
/// fill HP, then write + reload and confirm HMAC + the dragon decodes with its
/// nested objects resolving to the right classes (proving the remap). This is
/// the guard for the no-donor (no 1.47 MB embed) path.
///
/// <para>Skips when no live save is present. Never writes the user's real
/// save — loads read-only, writes only to a temp output.</para>
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

    private static (int BlockIndex, int FieldIndex, IReadOnlyList<BlockDetails> Elements)? FindMercList(
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
                    return (b.Index, f.FieldIndex, els);
                }
            }
        }
        return null;
    }

    private static void CollectClassIndices(BlockDetails obj, Dictionary<string, int> into)
    {
        into.TryAdd(obj.ClassName, obj.ClassIndex);
        foreach (var f in obj.Fields)
        {
            if (f.Child is { } child)
            {
                CollectClassIndices(child, into);
            }
            if (f.Elements is { } elements)
            {
                foreach (var e in elements)
                {
                    CollectClassIndices(e, into);
                }
            }
        }
    }

    private static uint ParseLeadingUInt(string formatted)
    {
        var token = formatted.Split(' ', 2)[0];
        return uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }

    private static ulong ParseLeadingUInt64(string formatted)
    {
        var token = formatted.Split(' ', 2)[0];
        return ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }

    [Fact]
    public void InsertDragonElement_RemapsTypeIndices_FillsHp_AndSurvivesRoundTrip()
    {
        var targetPath = FindLiveSave();
        if (targetPath is null)
        {
            return; // No live save — skip on CI / fresh machine.
        }

        var outPath = Path.Combine(Path.GetTempPath(), $"cd_dragon_insert_{Guid.NewGuid():N}.save");
        try
        {
            using var loader = new NativeSaveLoader();
            var summary = loader.Load(targetPath);
            var merc = FindMercList(loader, targetPath, summary.Blocks);
            Assert.NotNull(merc);
            Assert.True(merc!.Value.Elements.Count > 0);

            // Learn this save's type-indices for the dragon element's classes.
            var classIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var el in merc.Value.Elements)
            {
                CollectClassIndices(el, classIndices);
            }
            foreach (var fx in MountCatalog.DragonElementTypeIndexFixups)
            {
                Assert.True(classIndices.ContainsKey(fx.ClassName),
                    $"save lacks '{fx.ClassName}' to remap onto");
            }

            // Remap the captured element bytes onto this save.
            var bytes = Convert.FromHexString(MountCatalog.DragonElementHex);
            foreach (var (offset, className) in MountCatalog.DragonElementTypeIndexFixups)
            {
                var idx = classIndices[className];
                bytes[offset] = (byte)(idx & 0xFF);
                bytes[offset + 1] = (byte)((idx >> 8) & 0xFF);
            }

            var insertAt = merc.Value.Elements.Count;
            loader.ListInsertElement(merc.Value.BlockIndex, ReadOnlySpan<PathStep>.Empty,
                merc.Value.FieldIndex, insertAt, bytes);

            // Fill HP on the just-inserted element (current u16 at bytes 5..7).
            var afterInsert = loader.LoadBlockDetails(targetPath, merc.Value.BlockIndex);
            var listAfter = afterInsert.Fields.Single(f => f.Name == MercListField && f.Elements is not null);
            var dragonElem = listAfter.Elements![insertAt];
            Assert.Equal(MountCatalog.DragonCharacterKey,
                ParseLeadingUInt(dragonElem.Fields.Single(f => f.Name == CharKeyField).Value));
            // Nested objects must resolve to the right classes — proves the remap.
            var dragonClasses = new Dictionary<string, int>(StringComparer.Ordinal);
            CollectClassIndices(dragonElem, dragonClasses);
            Assert.Contains("ExperienceLevelSaveData", dragonClasses.Keys);
            Assert.Contains("FriendlyDailyCountSaveData", dragonClasses.Keys);

            var hpField = dragonElem.Fields.Single(f => f.Name == "_currentHp");
            var hp = BitConverter.GetBytes(ParseLeadingUInt64(hpField.Value));
            var full = BitConverter.GetBytes(MountCatalog.DragonFullHp);
            hp[5] = full[0];
            hp[6] = full[1];
            var dragonPath = new[] { new PathStep((uint)merc.Value.FieldIndex, (uint)insertAt) };
            loader.SetScalarField(merc.Value.BlockIndex, dragonPath, hpField.FieldIndex, hp);

            // Full encode → HMAC → reload.
            loader.WriteToFile(outPath);
            using var reloaded = new NativeSaveLoader();
            var reSummary = reloaded.Load(outPath);
            Assert.True(reSummary.HmacOk, "dragon-inserted save failed HMAC");

            var reMerc = FindMercList(reloaded, outPath, reSummary.Blocks);
            Assert.NotNull(reMerc);
            Assert.Equal(insertAt + 1, reMerc!.Value.Elements.Count);
            var reDetails = reloaded.LoadBlockDetails(outPath, reMerc.Value.BlockIndex);
            var reList = reDetails.Fields.Single(f => f.Name == MercListField && f.Elements is not null);
            var reDragon = reList.Elements![insertAt];
            Assert.Equal(MountCatalog.DragonCharacterKey,
                ParseLeadingUInt(reDragon.Fields.Single(f => f.Name == CharKeyField).Value));
            var reHp = BitConverter.GetBytes(
                ParseLeadingUInt64(reDragon.Fields.Single(f => f.Name == "_currentHp").Value));
            Assert.Equal(MountCatalog.DragonFullHp, (ushort)(reHp[5] | (reHp[6] << 8)));
        }
        finally
        {
            try { if (File.Exists(outPath)) File.Delete(outPath); }
            catch (IOException) { /* temp leak is harmless */ }
        }
    }
}
