using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Markdig;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarkItDown.Desktop;

public sealed partial class MainPage : Page
{
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();
    private CancellationTokenSource? _previewDebounce;
    private bool _initialized;
    private bool _restoringSelection;
    private bool _previewReady;
    private bool _closeApproved;

    public MainPage()
    {
        InitializeComponent();
        ViewModel = ((App)Application.Current).Services.GetRequiredService<MainViewModel>();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public MainViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync();
        var app = (App)Application.Current;
        if (app.MainWindow is not null)
        {
            app.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1280, Height = 800 });
            app.MainWindow.AppWindow.Closing += OnAppWindowClosing;
        }

        await InitializePreviewAsync();
        RenderPreview();
    }

    private async Task InitializePreviewAsync()
    {
        if (_previewReady)
        {
            return;
        }

        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            var coreWebView = PreviewWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 initialization did not return a core instance.");
#if WINDOWS
            coreWebView.Settings.IsScriptEnabled = false;
            coreWebView.Settings.AreDefaultContextMenusEnabled = false;
            coreWebView.Settings.AreDevToolsEnabled = false;
#endif
            coreWebView.NavigationStarting += OnPreviewNavigationStarting;
            _previewReady = true;
        }
        catch
        {
            ShowDocumentNotice(
                InfoBarSeverity.Error,
                "Preview unavailable",
                "Microsoft Edge WebView2 is missing. Repair the installation to restore rendered preview.");
        }
    }

    private async void OnPreviewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri is null || e.Uri is "about:blank")
        {
            return;
        }

        e.Cancel = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Open external link?",
            Content = uri.AbsoluteUri,
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SourceText) or nameof(MainViewModel.SelectedJob))
        {
            SchedulePreview();
        }
    }

    private void OnSourceEditorTextChanged(object sender, TextChangedEventArgs e) => SchedulePreview();

    private void SchedulePreview()
    {
        _previewDebounce?.Cancel();
        _previewDebounce?.Dispose();
        _previewDebounce = new CancellationTokenSource();
        var token = _previewDebounce.Token;
        _ = DebouncePreviewAsync(token);
    }

    private async Task DebouncePreviewAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
            {
                DispatcherQueue.TryEnqueue(RenderPreview);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RenderPreview()
    {
        var job = ViewModel.SelectedJob;
        SourceEditor.IsEnabled = job is { IsOversized: false, OutputPath: not null };

        if (job?.IsOversized == true)
        {
            ShowDocumentNotice(
                InfoBarSeverity.Warning,
                "Large output",
                "This Markdown file is larger than 5 MiB. It was saved successfully, but in-app rendering and editing are disabled.");
            OpenFullOutputButton.Visibility = Visibility.Visible;
            NavigatePreviewHtml("<p>The full Markdown output is available in the output folder.</p>");
            return;
        }

        if (!string.IsNullOrWhiteSpace(job?.ErrorMessage))
        {
            ShowDocumentNotice(InfoBarSeverity.Error, "Conversion failed", job.ErrorMessage);
        }
        else
        {
            DocumentInfoBar.IsOpen = false;
            OpenFullOutputButton.Visibility = Visibility.Collapsed;
        }

        var markdown = ViewModel.SourceText;
        if (string.IsNullOrEmpty(markdown))
        {
            NavigatePreviewHtml("<p class=\"empty\">Converted Markdown will appear here.</p>");
            return;
        }

        NavigatePreviewHtml(Markdown.ToHtml(markdown, _markdownPipeline));
    }

    private void NavigatePreviewHtml(string body)
    {
        if (!_previewReady)
        {
            return;
        }

        const string style = """
            :root { color-scheme: light dark; font-family: 'Segoe UI', sans-serif; }
            body { margin: 0 auto; max-width: 920px; padding: 28px 34px 64px; line-height: 1.58; overflow-wrap: anywhere; }
            h1, h2, h3, h4 { line-height: 1.24; margin-top: 1.5em; }
            h1 { border-bottom: 1px solid GrayText; padding-bottom: .3em; }
            pre, code { font-family: Consolas, monospace; }
            pre { overflow: auto; padding: 14px; border-radius: 6px; background: color-mix(in srgb, CanvasText 8%, Canvas); }
            code { background: color-mix(in srgb, CanvasText 8%, Canvas); padding: .12em .32em; border-radius: 3px; }
            table { border-collapse: collapse; width: 100%; display: block; overflow-x: auto; }
            th, td { border: 1px solid GrayText; padding: 7px 10px; text-align: left; }
            blockquote { border-left: 4px solid AccentColor; margin-left: 0; padding-left: 16px; color: GrayText; }
            a { color: LinkText; }
            .empty { color: GrayText; text-align: center; margin-top: 20vh; }
            @media (prefers-reduced-motion: reduce) { * { scroll-behavior: auto !important; } }
            """;

        var html = $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:">
              <title>Markdown preview</title>
              <style>{style}</style>
            </head>
            <body>{body}</body>
            </html>
            """;
        PreviewWebView.NavigateToString(html);
    }

    private void ShowDocumentNotice(InfoBarSeverity severity, string title, string message)
    {
        DocumentInfoBar.Severity = severity;
        DocumentInfoBar.Title = title;
        DocumentInfoBar.Message = message;
        DocumentInfoBar.IsOpen = true;
        OpenFullOutputButton.Visibility = Visibility.Collapsed;
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e) => await AddFilesFromPickerAsync();

    private async Task AddFilesFromPickerAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        var files = await picker.PickMultipleFilesAsync();
        ViewModel.AddFiles(files.Select(file => file.Path));
        if (QueueList.SelectedItem is null && ViewModel.SelectedJob is not null)
        {
            QueueList.SelectedItem = ViewModel.SelectedJob;
        }
    }

    private async void OnChooseOutputFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await ViewModel.SetOutputFolderAsync(folder.Path);
        }
    }

    private static void InitializePicker(object picker)
    {
        var app = (App)Application.Current;
        if (app.MainWindow is null)
        {
            throw new InvalidOperationException("The main window is unavailable.");
        }

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(app.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add files to the conversion queue";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        ViewModel.AddFiles(items.OfType<StorageFile>().Select(file => file.Path));
        if (QueueList.SelectedItem is null && ViewModel.SelectedJob is not null)
        {
            QueueList.SelectedItem = ViewModel.SelectedJob;
        }
    }

    private async void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringSelection)
        {
            return;
        }

        var requested = QueueList.SelectedItem as ConversionJob;
        if (ReferenceEquals(requested, ViewModel.SelectedJob))
        {
            return;
        }

        if (!await ResolveUnsavedChangesAsync())
        {
            _restoringSelection = true;
            QueueList.SelectedItem = ViewModel.SelectedJob;
            _restoringSelection = false;
            return;
        }

        ViewModel.SelectJob(requested);
        RenderPreview();
    }

    private async Task<bool> ResolveUnsavedChangesAsync()
    {
        if (ViewModel.SelectedJob?.IsDirty != true)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save Markdown changes?",
            Content = "Your edits will be lost if you continue without saving.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return await TrySaveSelectedAsync();
        }

        if (result == ContentDialogResult.Secondary)
        {
            await ViewModel.DiscardSelectedChangesAsync();
            return true;
        }

        return false;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await TrySaveSelectedAsync();

    private async Task<bool> TrySaveSelectedAsync()
    {
        try
        {
            await ViewModel.SaveSelectedAsync();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowDocumentNotice(
                InfoBarSeverity.Error,
                "Save failed",
                "The Markdown file could not be saved. Check the output folder permissions and available disk space.");
            return false;
        }
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e) => await SaveAsAsync();

    private async Task SaveAsAsync()
    {
        if (ViewModel.SelectedJob is not { } job)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(job.OutputPath ?? job.InputPath),
        };
        picker.FileTypeChoices.Add("Markdown document", [".md"]);
        InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            try
            {
                await ViewModel.SaveSelectedAsAsync(file.Path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ShowDocumentNotice(
                    InfoBarSeverity.Error,
                    "Save failed",
                    "The Markdown file could not be saved to the selected location.");
            }
        }
    }

    private void OnOpenOutputFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ViewModel.OutputFolder);
        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = false,
            ArgumentList = { ViewModel.OutputFolder },
        });
    }

    private void OnOpenFullOutputClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedJob?.OutputPath is { } outputPath && File.Exists(outputPath))
        {
            Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
        }
    }

    private async void OnConvertAllClick(object sender, RoutedEventArgs e) => await ViewModel.ConvertAllCommand.ExecuteAsync(null);
    private void OnCancelClick(object sender, RoutedEventArgs e) => ViewModel.CancelCommand.Execute(null);
    private void OnClearFinishedClick(object sender, RoutedEventArgs e) => ViewModel.ClearFinishedCommand.Execute(null);
    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (await ResolveUnsavedChangesAsync())
        {
            ViewModel.RetrySelectedCommand.Execute(null);
            RenderPreview();
            await ViewModel.ConvertAllCommand.ExecuteAsync(null);
        }
    }

    private async void OnOpenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await AddFilesFromPickerAsync();
    }

    private async void OnConvertAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.CanConvert)
        {
            await ViewModel.ConvertAllCommand.ExecuteAsync(null);
        }
    }

    private async void OnSaveAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.CanSaveSelected)
        {
            await TrySaveSelectedAsync();
        }
    }

    private async void OnSaveAsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveAsAsync();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeApproved || ViewModel.SelectedJob?.IsDirty != true)
        {
            return;
        }

        args.Cancel = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (await ResolveUnsavedChangesAsync())
            {
                _closeApproved = true;
                ((App)Application.Current).MainWindow?.Close();
            }
        });
    }
}
