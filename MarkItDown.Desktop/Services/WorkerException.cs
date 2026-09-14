namespace MarkItDown.Desktop.Services;

public sealed class WorkerException(string errorCode, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
