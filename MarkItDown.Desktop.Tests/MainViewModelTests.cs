using MarkItDown.Desktop.Interop;
using MarkItDown.Desktop.Models;
using MarkItDown.Desktop.Services;
using MarkItDown.Desktop.ViewModels;

namespace MarkItDown.Desktop.Tests;

public sealed class MainViewModelTests
{
    [Test]
    public void AddFiles_DeduplicatesCanonicalPathsAndRejectsUnknownExtensions()
    {
        using var directory = new TestDirectory();
        var input = directory.File("sample.unknown");
        File.WriteAllText(input, "content");
        var viewModel = CreateViewModel(directory, new FakeWorkerClient(directory.File("worker.md")));

        viewModel.AddFiles([input, input]);

        viewModel.Jobs.Should().ContainSingle();
        viewModel.Jobs[0].Status.Should().Be(ConversionStatus.Failed);
        viewModel.Jobs[0].ErrorMessage.Should().Contain("not supported");
    }

    [Test]
    public async Task ConvertAll_SavesOutputAndLoadsEditableMarkdown()
    {
        using var directory = new TestDirectory();
        var input = directory.File("report.txt");
        var workerOutput = directory.File("worker.md");
        File.WriteAllText(input, "hello");
        File.WriteAllText(workerOutput, "# Converted");
        var viewModel = CreateViewModel(directory, new FakeWorkerClient(workerOutput));
        await viewModel.SetOutputFolderAsync(directory.File("output"));
        viewModel.AddFiles([input]);

        await viewModel.ConvertAllCommand.ExecuteAsync(null);

        var job = viewModel.Jobs.Single();
        job.Status.Should().Be(ConversionStatus.Completed);
        job.OutputPath.Should().NotBeNull();
        File.ReadAllText(job.OutputPath!).Should().Be("# Converted");
        viewModel.SourceText.Should().Be("# Converted");

        viewModel.SourceText = "# Edited";
        job.Status.Should().Be(ConversionStatus.Modified);
        job.IsDirty.Should().BeTrue();
    }

    [Test]
    public async Task DiscardSelectedChanges_RestoresSavedMarkdown()
    {
        using var directory = new TestDirectory();
        var input = directory.File("report.txt");
        var workerOutput = directory.File("worker.md");
        File.WriteAllText(input, "hello");
        File.WriteAllText(workerOutput, "# Saved");
        var viewModel = CreateViewModel(directory, new FakeWorkerClient(workerOutput));
        await viewModel.SetOutputFolderAsync(directory.File("output"));
        viewModel.AddFiles([input]);
        await viewModel.ConvertAllCommand.ExecuteAsync(null);
        viewModel.SourceText = "# Unsaved";

        await viewModel.DiscardSelectedChangesAsync();

        viewModel.SourceText.Should().Be("# Saved");
        viewModel.SelectedJob!.IsDirty.Should().BeFalse();
        viewModel.SelectedJob.Status.Should().Be(ConversionStatus.Completed);
    }

    [Test]
    public async Task ConversionFailure_DoesNotStopFollowingQueueItem()
    {
        using var directory = new TestDirectory();
        var first = directory.File("first.txt");
        var second = directory.File("second.txt");
        var workerOutput = directory.File("worker.md");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        File.WriteAllText(workerOutput, "converted");
        var viewModel = CreateViewModel(directory, new FailFirstWorkerClient(workerOutput));
        await viewModel.SetOutputFolderAsync(directory.File("output"));
        viewModel.AddFiles([first, second]);

        await viewModel.ConvertAllCommand.ExecuteAsync(null);

        viewModel.Jobs[0].Status.Should().Be(ConversionStatus.Failed);
        viewModel.Jobs[1].Status.Should().Be(ConversionStatus.Completed);
    }

    [Test]
    public async Task Cancel_MarksCurrentAndRemainingItemsCanceled()
    {
        using var directory = new TestDirectory();
        var first = directory.File("first.txt");
        var second = directory.File("second.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        var worker = new BlockingWorkerClient();
        var viewModel = CreateViewModel(directory, worker);
        viewModel.AddFiles([first, second]);

        var conversion = viewModel.ConvertAllCommand.ExecuteAsync(null);
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CancelCommand.Execute(null);
        await conversion;

        viewModel.Jobs.Should().OnlyContain(job => job.Status == ConversionStatus.Canceled);
        viewModel.IsBusy.Should().BeFalse();
    }

    [Test]
    public async Task OversizedMarkdown_IsSavedWithoutLoadingEditor()
    {
        using var directory = new TestDirectory();
        var input = directory.File("large.txt");
        var workerOutput = directory.File("worker-large.md");
        File.WriteAllText(input, "large");
        using (var stream = new FileStream(workerOutput, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(MainViewModel.MaximumPreviewBytes + 1);
        }
        var viewModel = CreateViewModel(directory, new FakeWorkerClient(workerOutput));
        await viewModel.SetOutputFolderAsync(directory.File("output"));
        viewModel.AddFiles([input]);

        await viewModel.ConvertAllCommand.ExecuteAsync(null);

        viewModel.SelectedJob!.IsOversized.Should().BeTrue();
        viewModel.SelectedJob.OutputPath.Should().NotBeNull();
        viewModel.SourceText.Should().BeEmpty();
    }

    [Test]
    public async Task RetrySelected_ClearsThePreviousResultWithoutDeletingItsFile()
    {
        using var directory = new TestDirectory();
        var input = directory.File("report.txt");
        var workerOutput = directory.File("worker.md");
        File.WriteAllText(input, "hello");
        File.WriteAllText(workerOutput, "# Converted");
        var viewModel = CreateViewModel(directory, new FakeWorkerClient(workerOutput));
        await viewModel.SetOutputFolderAsync(directory.File("output"));
        viewModel.AddFiles([input]);
        await viewModel.ConvertAllCommand.ExecuteAsync(null);
        var previousOutput = viewModel.SelectedJob!.OutputPath!;

        viewModel.RetrySelectedCommand.Execute(null);

        viewModel.SelectedJob.Status.Should().Be(ConversionStatus.Pending);
        viewModel.SelectedJob.OutputPath.Should().BeNull();
        viewModel.SelectedJob.Markdown.Should().BeNull();
        viewModel.SourceText.Should().BeEmpty();
        File.Exists(previousOutput).Should().BeTrue();
    }

    private static MainViewModel CreateViewModel(TestDirectory directory, IWorkerClient worker) =>
        new(
            new InMemorySettingsService(directory.Path),
            new OutputFileService(),
            worker,
            new NullLog());

    private sealed class InMemorySettingsService(string outputFolder) : IAppSettingsService
    {
        private AppSettings _settings = new() { OutputFolder = outputFolder };
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWorkerClient(string markdownPath) : IWorkerClient
    {
        public Task<WorkerConversionResult> ConvertAsync(Guid requestId, string inputPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkerConversionResult(markdownPath, "Test", 12));
        public Task CancelAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailFirstWorkerClient(string markdownPath) : IWorkerClient
    {
        private int _calls;
        public Task<WorkerConversionResult> ConvertAsync(Guid requestId, string inputPath, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new WorkerException("worker_crashed", "The conversion worker stopped unexpectedly.");
            }

            return Task.FromResult(new WorkerConversionResult(markdownPath, "Test", 12));
        }

        public Task CancelAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingWorkerClient : IWorkerClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WorkerConversionResult> ConvertAsync(Guid requestId, string inputPath, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }

        public Task CancelAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullLog : IAppLog
    {
        public void Info(string eventName, Guid? requestId = null, string? extension = null, long? elapsedMs = null) { }
        public void Error(string eventName, string errorCode, Guid? requestId = null, string? extension = null) { }
    }
}
