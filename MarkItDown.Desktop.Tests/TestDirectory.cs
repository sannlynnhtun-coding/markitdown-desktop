namespace MarkItDown.Desktop.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MarkItDownDesktopTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        var resolved = System.IO.Path.GetFullPath(Path);
        var expectedRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MarkItDownDesktopTests"));
        if (resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}
