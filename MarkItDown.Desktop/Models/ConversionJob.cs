namespace MarkItDown.Desktop.Models;

public sealed class ConversionJob : ObservableObject
{
    private ConversionStatus _status = ConversionStatus.Pending;
    private string? _outputPath;
    private string? _markdown;
    private string? _errorMessage;
    private bool _isDirty;
    private bool _isOversized;

    public ConversionJob(string inputPath)
    {
        InputPath = Path.GetFullPath(inputPath);
        FileName = Path.GetFileName(inputPath);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string InputPath { get; }
    public string FileName { get; }
    public string Extension => Path.GetExtension(InputPath).ToLowerInvariant();

    public ConversionStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => Status switch
    {
        ConversionStatus.Pending => "Pending",
        ConversionStatus.Converting => "Converting",
        ConversionStatus.Completed => "Completed",
        ConversionStatus.Modified => "Modified — not saved",
        ConversionStatus.Failed => "Failed",
        ConversionStatus.Canceled => "Canceled",
        _ => Status.ToString(),
    };

    public string? OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public string? Markdown
    {
        get => _markdown;
        set => SetProperty(ref _markdown, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value) && value)
            {
                Status = ConversionStatus.Modified;
            }
        }
    }

    public bool IsOversized
    {
        get => _isOversized;
        set => SetProperty(ref _isOversized, value);
    }
}
