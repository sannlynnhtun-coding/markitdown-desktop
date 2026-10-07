using MarkItDown.Desktop.Services;

namespace MarkItDown.Desktop.Tests;

public sealed class InputPathCollectorTests
{
    [Test]
    public async Task CollectAsync_ExpandsFoldersRecursivelyAndKeepsExplicitFiles()
    {
        using var directory = new TestDirectory();
        var droppedFolder = directory.File("drop");
        var nestedFolder = Path.Combine(droppedFolder, "nested");
        Directory.CreateDirectory(nestedFolder);
        var first = Path.Combine(droppedFolder, "a.pdf");
        var second = Path.Combine(nestedFolder, "b.docx");
        var unsupported = Path.Combine(nestedFolder, "ignored.exe");
        var explicitFile = directory.File("standalone.txt");
        File.WriteAllText(first, "pdf");
        File.WriteAllText(second, "docx");
        File.WriteAllText(unsupported, "binary");
        File.WriteAllText(explicitFile, "text");
        var collector = new InputPathCollector();

        var result = await collector.CollectAsync([droppedFolder, explicitFile]);

        result.Should().Equal(first, second, explicitFile);
    }

    [Test]
    public async Task CollectAsync_DeduplicatesAFileDroppedDirectlyAndThroughItsFolder()
    {
        using var directory = new TestDirectory();
        var droppedFolder = directory.File("drop");
        Directory.CreateDirectory(droppedFolder);
        var input = Path.Combine(droppedFolder, "report.pdf");
        File.WriteAllText(input, "pdf");
        var collector = new InputPathCollector();

        var result = await collector.CollectAsync([input, droppedFolder, input]);

        result.Should().ContainSingle().Which.Should().Be(input);
    }
}
