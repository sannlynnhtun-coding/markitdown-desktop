using System.Text;

namespace MarkItDown.Desktop.Services;

public sealed class OutputFileService : IOutputFileService
{
    public string CreateUniqueOutputPath(string inputPath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(outputDirectory, baseName + ".md");
        for (var suffix = 1; File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(outputDirectory, $"{baseName} ({suffix}).md");
        }

        return candidate;
    }

    public async Task SaveAtomicAsync(
        string destinationPath,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task CopyAtomicAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        await CopyAtomicCoreAsync(sourcePath, destinationPath, overwrite: true, cancellationToken);

    public async Task CopyAtomicNewAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        await CopyAtomicCoreAsync(sourcePath, destinationPath, overwrite: false, cancellationToken);

    private static async Task CopyAtomicCoreAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
