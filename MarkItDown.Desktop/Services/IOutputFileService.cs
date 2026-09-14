namespace MarkItDown.Desktop.Services;

public interface IOutputFileService
{
    string CreateUniqueOutputPath(string inputPath, string outputDirectory);
    Task SaveAtomicAsync(string destinationPath, string markdown, CancellationToken cancellationToken = default);
    Task CopyAtomicAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    Task CopyAtomicNewAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
