using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Known dye colours are knowledge entries (2.06: every dyecolorgroupinfo
/// colour is gated by CheckKnowledge). These pin how the names parse and
/// what the live knowledgeinfo holds.
/// </summary>
public sealed class KnownDyeServiceTests
{
    [Theory]
    [InlineData("Knowledge_Her_DyeColor", "Her", 0)]
    [InlineData("Knowledge_Her_DyeColor_4", "Her", 4)]
    [InlineData("Knowledge_Dem_DyeColor_II", "Dem_II", 0)]
    [InlineData("Knowledge_Dem_DyeColor_III_5", "Dem_III", 5)]
    [InlineData("Knowledge_Grey_DyeColor_10", "Grey", 10)]
    public void DyeColourNames_Parse(string name, string theme, int shade)
    {
        Assert.Equal((theme, shade), KnownDyeService.Parse(name));
    }

    [Theory]
    [InlineData("Knowledge_Node_Dem_SunsetDyehouse")]
    [InlineData("Knowledge_Collection_Prop_Dyewater_0001")]
    [InlineData("Knowledge_Recipe_Item_gimmick_management_dye_vat_01")]
    [InlineData("Knowledge_Her_DyeColorX")]
    [InlineData("Knowledge_Her_DyeColor_x")]
    [InlineData("NHM_Unique_GreyMane_Dye")]
    public void OtherNames_DoNotParse(string name)
    {
        Assert.Null(KnownDyeService.Parse(name));
    }

    [Fact]
    public void KeysToLearn_AddsTheThemeAndSkipsWhatIsKnown()
    {
        var entries = new[]
        {
            new KnownDyeEntry("Her", 900, "Theoric", 1, 10, "Bright Red", "Knowledge_Her_DyeColor_1", 50),
            new KnownDyeEntry("Her", 900, "Theoric", 2, 11, "Rich Red", "Knowledge_Her_DyeColor_2", 51),
            new KnownDyeEntry("Grey", null, "Grey", 1, 20, "Grey", "Knowledge_Grey_DyeColor_1", null),
        };
        Assert.Equal([10u, 11u, 20u, 900u], KnownDyeService.KeysToLearn(entries, new HashSet<uint>(), false));
        Assert.Equal([11u, 20u, 51u], KnownDyeService.KeysToLearn(entries, new HashSet<uint> { 10, 50, 900 }, true));
    }

    /// <summary>
    /// 2.06 through the palette mapping: ten themes, each
    /// with six keys — its own grey (palette 0–8, 9 colours) and five shades of
    /// 20 — so the ten greys belong to one theme each (Her = Grey_1).
    /// </summary>
    [Fact]
    public void LivePalette_MapsSixKeysPerTheme()
    {
        if (!File.Exists("crimson_rs.dll")
            || LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup) is not { } pamt)
        {
            return;
        }
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(Path.GetDirectoryName(Path.GetDirectoryName(pamt))));
        var entries = KnownDyeService.Scan(loc);

        Assert.Equal(60, entries.Count);
        Assert.Equal(
            ["Bar", "Cal", "Del", "Dem_I", "Dem_II", "Dem_III", "Her", "Kwe", "Por", "Tom"],
            entries.Select(e => e.ThemeId).Distinct().Order(StringComparer.Ordinal).ToList());
        Assert.All(entries, e => Assert.NotNull(e.ThemeKey));
        Assert.All(entries.GroupBy(e => e.ThemeId), g =>
        {
            Assert.Equal([0, 1, 2, 3, 4, 5], g.Select(e => e.Shade));
            Assert.Equal([9, 20, 20, 20, 20, 20], g.Select(e => e.Swatches!.Count));
        });

        var her = entries.Where(e => e.ThemeId == "Her").ToList();
        Assert.Equal([1001287u, 1000297u, 1000298u, 1000299u, 1000300u, 1000301u], her.Select(e => e.Key));
        Assert.Equal("Knowledge_Grey_DyeColor_1", her[0].InternalName);
        Assert.Equal(2147483454u, her[1].ThemeKey);
        Assert.Equal(1001352u, her[1].DyeItemKey); // Knowledge_Collection_Prop_Dyewater_0001, "Bright Red Dye"
        Assert.StartsWith("Hernandian Dye", her[1].ThemeName, StringComparison.Ordinal);
        Assert.Equal("Knowledge_Grey_DyeColor_10", entries.Single(e => e.ThemeId == "Bar" && e.Shade == 0).InternalName);
    }
}
