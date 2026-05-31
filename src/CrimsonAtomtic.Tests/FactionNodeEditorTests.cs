using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.ViewModels;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Pure-logic tests for the Faction-node editor: the state-label map and
/// the per-row display props. The scan/apply paths need a loaded save, so
/// they're covered by the in-app run-through, not here.
/// </summary>
public sealed class FactionNodeStatesTests
{
    [Theory]
    [InlineData(0, "Undiscovered")]
    [InlineData(1, "Discovered")]
    [InlineData(2, "Active")]
    [InlineData(3, "Conquered")]
    [InlineData(4, "Lost")]
    public void Label_KnownStates_MapToName(byte state, string expected)
    {
        Assert.Equal(expected, MainWindowViewModel.FactionNodeStates.Label(state));
    }

    [Fact]
    public void Label_UnknownState_ReturnsNumber()
    {
        Assert.Equal("9", MainWindowViewModel.FactionNodeStates.Label(9));
    }

    [Fact]
    public void All_HasFiveStates_ActiveIsValue2()
    {
        Assert.Equal(5, MainWindowViewModel.FactionNodeStates.All.Count);
        var active = Assert.Single(MainWindowViewModel.FactionNodeStates.All, o => o.Label == "Active");
        Assert.Equal((byte)2, active.Value);
    }
}

public sealed class FactionNodeRowTests
{
    private static MainWindowViewModel.FactionNodeTarget Target(
        byte state = 2, uint owner = 1000044, uint conqueror = 0, bool capital = false) =>
        new(BlockIndex: 0, Path: [new PathStep(0, 0)], StateFieldIndex: 0,
            CurrentState: state, OwnerKey: owner, ConquerorKey: conqueror, IsCapital: capital);

    [Fact]
    public void OwnerName_FallsBackToKey_WhenResolvedNameEmpty()
    {
        var row = new FactionNodeRow(Target(owner: 1000044), ownerName: null, conquerorName: null);
        Assert.Equal("1000044", row.OwnerName);
        Assert.Equal("1000044", row.OwnerKeyText);
    }

    [Fact]
    public void OwnerName_UsesResolvedName_WhenPresent()
    {
        var row = new FactionNodeRow(Target(), "Node_Her_HernandCastle", null);
        Assert.Equal("Node_Her_HernandCastle", row.OwnerName);
    }

    [Fact]
    public void StateLabel_Conqueror_AndCapital_Reflect()
    {
        var row = new FactionNodeRow(Target(state: 3, capital: true), "X", "Graymane");
        Assert.Equal("Conquered", row.StateLabel);
        Assert.Equal("Graymane", row.ConquerorName);
        Assert.Equal("★", row.CapitalText);
    }

    [Fact]
    public void Capital_Empty_WhenNotCapital()
    {
        var row = new FactionNodeRow(Target(capital: false), "X", null);
        Assert.Equal(string.Empty, row.CapitalText);
        Assert.Null(row.ConquerorName);
    }
}
