namespace MarkItDown.Desktop.Services;

public interface IInputPathCollector
{
    Task<IReadOnlyList<string>> CollectAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default);
}
