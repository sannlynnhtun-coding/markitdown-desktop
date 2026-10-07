namespace MarkItDown.Desktop.Services;

public sealed class InputPathCollector : IInputPathCollector
{
    public Task<IReadOnlyList<string>> CollectAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var pathSnapshot = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();

        return Task.Run<IReadOnlyList<string>>(
            () => Collect(pathSnapshot, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<string> Collect(
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = TryGetFullPath(path);
            if (fullPath is null)
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                AddFile(fullPath, result, knownPaths);
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                AddFolderFiles(fullPath, result, knownPaths, cancellationToken);
            }
        }

        return result;
    }

    private static void AddFolderFiles(
        string root,
        ICollection<string> result,
        ISet<string> knownPaths,
        CancellationToken cancellationToken)
    {
        var pendingFolders = new Stack<string>();
        pendingFolders.Push(root);

        while (pendingFolders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentFolder = pendingFolders.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(currentFolder);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            var childFolders = new List<string>();
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                System.IO.FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                if ((attributes & System.IO.FileAttributes.Directory) != 0)
                {
                    if ((attributes & System.IO.FileAttributes.ReparsePoint) == 0)
                    {
                        childFolders.Add(entry);
                    }

                    continue;
                }

                if (SupportedInputFiles.IsSupported(entry))
                {
                    AddFile(entry, result, knownPaths);
                }
            }

            for (var index = childFolders.Count - 1; index >= 0; index--)
            {
                pendingFolders.Push(childFolders[index]);
            }
        }
    }

    private static void AddFile(string path, ICollection<string> result, ISet<string> knownPaths)
    {
        var fullPath = Path.GetFullPath(path);
        if (knownPaths.Add(fullPath))
        {
            result.Add(fullPath);
        }
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
