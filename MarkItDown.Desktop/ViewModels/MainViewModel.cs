namespace MarkItDown.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const long MaximumPreviewBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".pptx", ".xlsx", ".xls", ".msg", ".epub", ".zip", ".ipynb",
        ".html", ".htm", ".csv", ".json", ".xml", ".rss", ".atom", ".txt", ".md",
        ".jpg", ".jpeg", ".png",
    };

    private readonly IAppSettingsService _settingsService;
    private readonly IOutputFileService _outputFileService;
    private readonly IWorkerClient _workerClient;
    private readonly IAppLog _log;
    private CancellationTokenSource? _conversionCancellation;
    private ConversionJob? _selectedJob;
    private string _sourceText = string.Empty;
    private string _outputFolder = new AppSettings().OutputFolder;
    private string _progressText = "Add files to begin.";
    private bool _isBusy;
    private bool _suppressDirtyTracking;

    public MainViewModel(
        IAppSettingsService settingsService,
        IOutputFileService outputFileService,
        IWorkerClient workerClient,
        IAppLog log)
    {
        _settingsService = settingsService;
        _outputFileService = outputFileService;
        _workerClient = workerClient;
        _log = log;

        ConvertAllCommand = new AsyncRelayCommand(ConvertAllAsync, () => CanConvert);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ClearFinishedCommand = new RelayCommand(ClearFinished, () => CanClearFinished);
        RetrySelectedCommand = new RelayCommand(RetrySelected, () => CanRetrySelected);
    }

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    public IAsyncRelayCommand ConvertAllCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ClearFinishedCommand { get; }
    public IRelayCommand RetrySelectedCommand { get; }

    public ConversionJob? SelectedJob
    {
        get => _selectedJob;
        private set
        {
            if (SetProperty(ref _selectedJob, value))
            {
                _suppressDirtyTracking = true;
                SourceText = value?.Markdown ?? string.Empty;
                _suppressDirtyTracking = false;
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanSaveSelected));
                OnPropertyChanged(nameof(CanRetrySelected));
                RetrySelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (SetProperty(ref _sourceText, value) &&
                !_suppressDirtyTracking &&
                SelectedJob is { IsOversized: false, OutputPath: not null } job)
            {
                job.Markdown = value;
                job.IsDirty = true;
                OnPropertyChanged(nameof(CanSaveSelected));
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        private set => SetProperty(ref _outputFolder, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanClearFinished));
                ConvertAllCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                ClearFinishedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => SelectedJob is not null;
    public bool CanConvert => !IsBusy && Jobs.Any(job => job.Status == ConversionStatus.Pending);
    public bool CanSaveSelected => SelectedJob is { IsDirty: true, IsOversized: false, OutputPath: not null };
    public bool CanRetrySelected => !IsBusy && SelectedJob?.Status is
        ConversionStatus.Completed or ConversionStatus.Modified or ConversionStatus.Failed or ConversionStatus.Canceled;
    public bool CanClearFinished => !IsBusy && Jobs.Any(job => !job.IsDirty && job.Status is ConversionStatus.Completed or ConversionStatus.Failed or ConversionStatus.Canceled);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        OutputFolder = string.IsNullOrWhiteSpace(settings.OutputFolder)
            ? new AppSettings().OutputFolder
            : settings.OutputFolder;
        Directory.CreateDirectory(OutputFolder);
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var knownPaths = Jobs.Select(job => job.InputPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) || knownPaths.Contains(fullPath))
            {
                continue;
            }

            var job = new ConversionJob(fullPath);
            if (!SupportedExtensions.Contains(job.Extension))
            {
                job.Status = ConversionStatus.Failed;
                job.ErrorMessage = "This file type is not supported in the offline edition.";
            }

            Jobs.Add(job);
            knownPaths.Add(fullPath);
            SelectedJob ??= job;
        }

        ProgressText = Jobs.Count == 0 ? "Add files to begin." : $"{Jobs.Count} file(s) in the queue.";
        RefreshCommands();
    }

    public void SelectJob(ConversionJob? job)
    {
        if (!ReferenceEquals(SelectedJob, job))
        {
            SelectedJob = job;
            return;
        }

        _suppressDirtyTracking = true;
        SourceText = job?.Markdown ?? string.Empty;
        _suppressDirtyTracking = false;
        OnPropertyChanged(nameof(CanSaveSelected));
    }

    public async Task SetOutputFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        OutputFolder = Path.GetFullPath(folder);
        await _settingsService.SaveAsync(new AppSettings { OutputFolder = OutputFolder }, cancellationToken);
    }

    public async Task SaveSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedJob is not { OutputPath: not null, IsOversized: false } job)
        {
            return;
        }

        await _outputFileService.SaveAtomicAsync(job.OutputPath, SourceText, cancellationToken);
        job.Markdown = SourceText;
        job.IsDirty = false;
        job.Status = ConversionStatus.Completed;
        ProgressText = $"Saved {Path.GetFileName(job.OutputPath)}.";
        OnPropertyChanged(nameof(CanSaveSelected));
        RefreshCommands();
    }

    public async Task SaveSelectedAsAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        if (SelectedJob is not { } job)
        {
            return;
        }

        if (job.IsOversized && job.OutputPath is not null)
        {
            await _outputFileService.CopyAtomicAsync(job.OutputPath, destinationPath, cancellationToken);
            ProgressText = $"Saved {Path.GetFileName(destinationPath)}.";
            return;
        }

        await _outputFileService.SaveAtomicAsync(destinationPath, SourceText, cancellationToken);
        job.OutputPath = destinationPath;
        job.Markdown = SourceText;
        job.IsDirty = false;
        job.Status = ConversionStatus.Completed;
        ProgressText = $"Saved {Path.GetFileName(destinationPath)}.";
        OnPropertyChanged(nameof(CanSaveSelected));
        RefreshCommands();
    }

    public async Task DiscardSelectedChangesAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedJob is not { OutputPath: not null } job || !File.Exists(job.OutputPath))
        {
            return;
        }

        var savedMarkdown = await File.ReadAllTextAsync(job.OutputPath, cancellationToken);
        job.Markdown = savedMarkdown;
        job.IsDirty = false;
        job.Status = ConversionStatus.Completed;
        _suppressDirtyTracking = true;
        SourceText = savedMarkdown;
        _suppressDirtyTracking = false;
        OnPropertyChanged(nameof(CanSaveSelected));
        RefreshCommands();
    }

    private async Task ConvertAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var pending = Jobs.Where(job => job.Status == ConversionStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        IsBusy = true;
        _conversionCancellation = new CancellationTokenSource();
        var token = _conversionCancellation.Token;
        var completedCount = 0;
        try
        {
            foreach (var job in pending)
            {
                if (token.IsCancellationRequested)
                {
                    job.Status = ConversionStatus.Canceled;
                    continue;
                }

                job.Status = ConversionStatus.Converting;
                job.ErrorMessage = null;
                ProgressText = $"Converting {completedCount + 1} of {pending.Count}: {job.FileName}";

                try
                {
                    var result = await _workerClient.ConvertAsync(job.Id, job.InputPath, token);
                    var outputPath = _outputFileService.CreateUniqueOutputPath(job.InputPath, OutputFolder);
                    await _outputFileService.CopyAtomicNewAsync(result.MarkdownPath, outputPath, token);
                    job.OutputPath = outputPath;

                    var outputLength = new FileInfo(result.MarkdownPath).Length;
                    if (outputLength <= MaximumPreviewBytes)
                    {
                        job.Markdown = await File.ReadAllTextAsync(result.MarkdownPath, token);
                        job.IsOversized = false;
                    }
                    else
                    {
                        job.Markdown = null;
                        job.IsOversized = true;
                    }

                    job.IsDirty = false;
                    job.Status = ConversionStatus.Completed;
                    _log.Info("conversion_completed", job.Id, job.Extension, result.ElapsedMilliseconds);
                }
                catch (OperationCanceledException)
                {
                    job.Status = ConversionStatus.Canceled;
                    job.ErrorMessage = "Conversion canceled.";
                }
                catch (WorkerException exception)
                {
                    job.Status = ConversionStatus.Failed;
                    job.ErrorMessage = exception.Message;
                    _log.Error("conversion_failed", exception.ErrorCode, job.Id, job.Extension);
                }
                catch (Exception)
                {
                    job.Status = ConversionStatus.Failed;
                    job.ErrorMessage = "The document could not be saved. Check the output folder and try again.";
                    _log.Error("conversion_failed", "output_write_failed", job.Id, job.Extension);
                }

                completedCount++;
                if (ReferenceEquals(job, SelectedJob))
                {
                    SelectJob(job);
                }
            }

            if (token.IsCancellationRequested)
            {
                foreach (var job in pending.Where(item => item.Status == ConversionStatus.Pending))
                {
                    job.Status = ConversionStatus.Canceled;
                }
            }
        }
        finally
        {
            _conversionCancellation.Dispose();
            _conversionCancellation = null;
            IsBusy = false;
            ProgressText = token.IsCancellationRequested
                ? "Batch conversion canceled."
                : $"Finished {pending.Count} file(s).";
            RefreshCommands();
        }
    }

    private void Cancel()
    {
        _conversionCancellation?.Cancel();
        _ = _workerClient.CancelAsync();
        ProgressText = "Canceling conversion…";
    }

    private void ClearFinished()
    {
        var removable = Jobs
            .Where(job => !job.IsDirty && job.Status is ConversionStatus.Completed or ConversionStatus.Failed or ConversionStatus.Canceled)
            .ToList();
        foreach (var job in removable)
        {
            Jobs.Remove(job);
        }

        if (SelectedJob is not null && !Jobs.Contains(SelectedJob))
        {
            SelectJob(Jobs.FirstOrDefault());
        }

        ProgressText = Jobs.Count == 0 ? "Add files to begin." : $"{Jobs.Count} file(s) in the queue.";
        RefreshCommands();
    }

    private void RetrySelected()
    {
        if (SelectedJob is not { } job)
        {
            return;
        }

        job.Status = ConversionStatus.Pending;
        job.ErrorMessage = null;
        job.IsOversized = false;
        job.OutputPath = null;
        job.Markdown = null;
        job.IsDirty = false;
        _suppressDirtyTracking = true;
        SourceText = string.Empty;
        _suppressDirtyTracking = false;
        ProgressText = $"{job.FileName} is ready to retry.";
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanSaveSelected));
        OnPropertyChanged(nameof(CanRetrySelected));
        OnPropertyChanged(nameof(CanClearFinished));
        ConvertAllCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetrySelectedCommand.NotifyCanExecuteChanged();
        ClearFinishedCommand.NotifyCanExecuteChanged();
    }
}
