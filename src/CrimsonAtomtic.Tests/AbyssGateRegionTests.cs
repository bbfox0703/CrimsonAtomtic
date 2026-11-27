using CrimsonAtomtic.Ui.ViewModels;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// The Abyss gate editor's region guess from <c>_ownerLevelName</c>
/// (2.06, slot102): ground ruins carry a region code, the sky islands and
/// bridges are in the Abyss.
/// </summary>
public sealed class AbyssGateRegionTests
{
    [Theory]
    [InlineData("AbyssRuins_Her_0030", "Hernand")]
    [InlineData("AbyssRuins_Dem_0019", "Demeniss")]
    [InlineData("AbyssRuins_Kwe_0038", "Kweiden")]
    [InlineData("AbyssRuins_Del_0004", "Delesyia")]
    [InlineData("AbyssRuins_CD_0034", "Crimson Desert")]
    [InlineData("Challenge_Sealed_Artifact_Her", "Hernand")]
    [InlineData("AbyssIsland_0151_Phase00_00", "Abyss")]
    [InlineData("AbyssBridge_0002_Phase00_00", "Abyss")]
    [InlineData("AbyssOne_Light_Connect_Lv1", "Abyss")]
    [InlineData("TrollUniversity_Brain", "")]
    [InlineData("", "")]
    public void LevelName_NamesTheRegion(string levelName, string region) =>
        Assert.Equal(region, AbyssGatesViewModel.RegionOfLevel(levelName));
}
