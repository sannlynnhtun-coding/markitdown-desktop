using System.Text.Json;
using MarkItDown.Desktop.Interop;

namespace MarkItDown.Desktop.Services;

public sealed class RotatingFileLog : IAppLog
{
    private const long MaximumBytes = 1024 * 1024;
    private const int MaximumFiles = 5;
    private readonly object _sync = new();

    public void Info(string eventName, Guid? requestId = null, string? extension = null, long? elapsedMs = null) =>
        Write("information", eventName, null, requestId, extension, elapsedMs);

    public void Error(string eventName, string errorCode, Guid? requestId = null, string? extension = null) =>
        Write("error", eventName, errorCode, requestId, extension, null);

    private void Write(
        string level,
        string eventName,
        string? errorCode,
        Guid? requestId,
        string? extension,
        long? elapsedMs)
    {
        try
        {
            lock (_sync)
            {
                AppPaths.EnsureCreated();
                var path = Path.Combine(AppPaths.LogsDirectory, "app.log");
                RotateIfNeeded(path);
                var entry = new LogEntry(
                    DateTimeOffset.UtcNow,
                    level,
                    eventName,
                    requestId,
                    extension,
                    elapsedMs,
                    errorCode);
                File.AppendAllText(
                    path,
                    JsonSerializer.Serialize(entry, AppJsonSerializerContext.Default.LogEntry) + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never stop a conversion or expose document content elsewhere.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaximumBytes)
        {
            return;
        }

        var oldest = $"{path}.{MaximumFiles - 1}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = MaximumFiles - 2; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, $"{path}.{index + 1}", overwrite: true);
            }
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }
}
