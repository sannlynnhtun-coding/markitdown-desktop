using MarkItDown.Desktop.Services;

namespace MarkItDown.Desktop.Tests;

public sealed class OutputFileServiceTests
{
    [Test]
    public void CreateUniqueOutputPath_NeverOverwritesExistingFiles()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.File("report.md"), "existing");
        File.WriteAllText(directory.File("report (1).md"), "existing");

        var service = new OutputFileService();
        var result = service.CreateUniqueOutputPath(directory.File("report.pdf"), directory.Path);

        result.Should().Be(directory.File("report (2).md"));
    }

    [Test]
    public async Task SaveAtomicAsync_ReplacesContentWithoutLeavingTemporaryFiles()
    {
        using var directory = new TestDirectory();
        var destination = directory.File("result.md");
        File.WriteAllText(destination, "old");

        await new OutputFileService().SaveAtomicAsync(destination, "# New");

        File.ReadAllText(destination).Should().Be("# New");
        Directory.EnumerateFiles(directory.Path, "*.tmp").Should().BeEmpty();
    }

    [Test]
    public async Task CopyAtomicNewAsync_DoesNotOverwriteAFileCreatedAfterNameSelection()
    {
        using var directory = new TestDirectory();
        var source = directory.File("source.md");
        var destination = directory.File("result.md");
        File.WriteAllText(source, "new");
        File.WriteAllText(destination, "existing");

        var action = () => new OutputFileService().CopyAtomicNewAsync(source, destination);

        await action.Should().ThrowAsync<IOException>();
        File.ReadAllText(destination).Should().Be("existing");
        Directory.EnumerateFiles(directory.Path, "*.tmp").Should().BeEmpty();
    }
}
