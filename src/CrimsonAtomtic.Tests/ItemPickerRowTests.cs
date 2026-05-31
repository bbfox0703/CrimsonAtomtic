using CrimsonAtomtic.Ui.ViewModels;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Unit tests for <see cref="ItemPickerRow.DisplayLabel"/> — the combined
/// "English / Secondary" label the unified Add-Item top bar shows.
/// </summary>
public sealed class ItemPickerRowTests
{
    [Fact]
    public void DisplayLabel_WithSecondary_CombinesBothNames()
    {
        var row = new ItemPickerRow(1000399, "1000399", "MagicBullet_Ice", "Ice Magic Bullet", "冰霜魔彈");
        Assert.Equal("Ice Magic Bullet / 冰霜魔彈", row.DisplayLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DisplayLabel_WithoutSecondary_UsesEnglishOnly(string? secondary)
    {
        var row = new ItemPickerRow(1000372, "1000372", "Bullet", "Bullet", secondary);
        Assert.Equal("Bullet", row.DisplayLabel);
    }
}
