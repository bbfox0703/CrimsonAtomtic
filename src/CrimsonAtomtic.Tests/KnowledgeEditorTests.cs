using CrimsonAtomtic.Ui.ViewModels;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Unit tests for the Knowledge editor's category bucketing
/// (<see cref="KnowledgeEditorViewModel.CategoryFor"/>) — the pure logic that
/// maps a knowledge internal name to one of the 16 curated categories or
/// "Other".
/// </summary>
public sealed class KnowledgeEditorTests
{
    [Theory]
    [InlineData("Knowledge_Node_Kwe_Pailune", "Node")]
    [InlineData("Knowledge_Recipe_Dragon_OneHandSword", "Recipe")]
    [InlineData("Knowledge_AbyssRuins_HyperSpace_01", "AbyssRuins")]
    [InlineData("Knowledge_WantedPaper_Bandit", "WantedPaper")]
    [InlineData("Knowledge_Riding_Wolf_1", "Riding")]
    [InlineData("Knowledge_Unique_Varnia_Dragon", "Unique")]
    [InlineData("Knowledge_Skill_RidingDash", "Skill")]
    [InlineData("Knowledge_Living_FishingRod", "Living")]
    public void CategoryFor_CuratedPrefixes_MapToCategory(string internalName, string expected)
    {
        Assert.Equal(expected, KnowledgeEditorViewModel.CategoryFor(internalName));
    }

    [Theory]
    [InlineData("Knowledge_NatureCreature_StornWorm")] // prefix not in the 16
    [InlineData("LegendaryBear_Mural")]                // no Knowledge_ prefix
    [InlineData("Knowledge_Hp")]                       // single-token, not curated
    [InlineData("")]                                   // empty
    public void CategoryFor_UncuratedOrMalformed_FallsBackToOther(string internalName)
    {
        Assert.Equal("Other", KnowledgeEditorViewModel.CategoryFor(internalName));
    }
}
