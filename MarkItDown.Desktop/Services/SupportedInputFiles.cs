namespace MarkItDown.Desktop.Services;

public static class SupportedInputFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".pptx", ".xlsx", ".xls", ".msg", ".epub", ".zip", ".ipynb",
        ".html", ".htm", ".csv", ".json", ".xml", ".rss", ".atom", ".txt", ".md",
        ".jpg", ".jpeg", ".png",
    };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));
}
