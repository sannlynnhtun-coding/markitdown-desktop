using System.Text.Json;
using MarkItDown.Desktop.Interop;

namespace MarkItDown.Desktop.Services;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private readonly string _settingsFile;

    public JsonAppSettingsService()
        : this(AppPaths.SettingsFile)
    {
    }

    public JsonAppSettingsService(string settingsFile)
    {
        _settingsFile = settingsFile;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        if (!File.Exists(_settingsFile))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_settingsFile);
            return await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.AppSettings,
                cancellationToken)
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        var temporaryPath = _settingsFile + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                AppJsonSerializerContext.Default.AppSettings,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _settingsFile, overwrite: true);
    }
}
