using MarkItDown.Desktop.Interop;

namespace MarkItDown.Desktop.Services;

public interface IWorkerClient : IAsyncDisposable
{
    Task<WorkerConversionResult> ConvertAsync(
        Guid requestId,
        string inputPath,
        CancellationToken cancellationToken = default);

    Task CancelAsync();
}
