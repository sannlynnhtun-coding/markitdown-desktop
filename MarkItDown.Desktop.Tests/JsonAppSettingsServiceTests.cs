using MarkItDown.Desktop.Models;
using MarkItDown.Desktop.Services;

namespace MarkItDown.Desktop.Tests;

public sealed class JsonAppSettingsServiceTests
{
    [Test]
    public async Task Settings_RoundTripUnicodeOutputFolder()
    {
        using var directory = new TestDirectory();
        var settingsFile = directory.File("settings.json");
        var service = new JsonAppSettingsService(settingsFile);
        var expected = new AppSettings { OutputFolder = directory.File("စာရွက်စာတမ်း") };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        actual.Should().Be(expected);
    }

    [Test]
    public async Task LoadAsync_InvalidJsonFallsBackToDefaults()
    {
        using var directory = new TestDirectory();
        var settingsFile = directory.File("settings.json");
        File.WriteAllText(settingsFile, "not json");

        var settings = await new JsonAppSettingsService(settingsFile).LoadAsync();

        settings.OutputFolder.Should().NotBeNullOrWhiteSpace();
    }
}
