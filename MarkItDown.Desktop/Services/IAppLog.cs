namespace MarkItDown.Desktop.Services;

public interface IAppLog
{
    void Info(string eventName, Guid? requestId = null, string? extension = null, long? elapsedMs = null);
    void Error(string eventName, string errorCode, Guid? requestId = null, string? extension = null);
}
