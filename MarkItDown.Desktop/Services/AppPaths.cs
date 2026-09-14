namespace MarkItDown.Desktop.Services;

public static class AppPaths
{
    public static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarkItDown Desktop");

    public static string CacheDirectory { get; } = Path.Combine(LocalDataDirectory, "Cache");
    public static string LogsDirectory { get; } = Path.Combine(LocalDataDirectory, "Logs");
    public static string SettingsFile { get; } = Path.Combine(LocalDataDirectory, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(LocalDataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
