using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// A settings file that is not settings JSON must not cost the user their
/// file: it is renamed aside for inspection and the app starts on defaults.
/// </summary>
public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "crimson-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string SettingsPath => Path.Combine(_dir, AppSettingsStore.FileName);

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    public void Load_UnreadableJson_ReturnsDefaultsAndKeepsTheFileAside(string content)
    {
        File.WriteAllText(SettingsPath, content);

        var settings = AppSettingsStore.Load(_dir);

        Assert.Null(settings.UiLanguage);
        Assert.False(File.Exists(SettingsPath));
        var aside = Assert.Single(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
        Assert.Equal(content, File.ReadAllText(aside));
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsAndWritesNothing()
    {
        Assert.Null(AppSettingsStore.Load(_dir).UiLanguage);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void TrySave_ThenLoad_RoundTrips_AndLeavesNoTempFile()
    {
        Assert.True(AppSettingsStore.TrySave(_dir, new AppSettings { UiLanguage = "ja" }));

        Assert.Equal("ja", AppSettingsStore.Load(_dir).UiLanguage);
        Assert.Equal([AppSettingsStore.FileName], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
