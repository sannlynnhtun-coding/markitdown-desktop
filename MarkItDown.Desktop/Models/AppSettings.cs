namespace MarkItDown.Desktop.Models;

public sealed record AppSettings
{
    public string OutputFolder { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "MarkItDown Desktop");
}
