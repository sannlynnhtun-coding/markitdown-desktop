# MarkItDown Desktop

MarkItDown Desktop is an independent, Windows-only community GUI powered by [Microsoft MarkItDown](https://github.com/microsoft/markitdown). It converts local documents to Markdown without asking the user to install Python, pip, the .NET runtime, or use a command line.

## Use the app

1. Run `MarkItDownDesktop-Setup-x64.exe` and complete the all-users installation.
2. Open **MarkItDown Desktop** from the Start menu.
3. Choose an output folder, then select or drag files into the queue.
4. Select **Convert all**. Each Markdown file is saved immediately with a collision-safe name.
5. Review the rendered result or edit the Markdown source. Use **Ctrl+S** to save.

The default output folder is `Documents\MarkItDown Desktop`. Existing files are preserved by adding `(1)`, `(2)`, and so on.

Supported offline inputs include PDF, DOCX, PPTX, XLSX/XLS, MSG, EPUB, ZIP, IPYNB, HTML, CSV, JSON, XML, RSS/Atom, text/Markdown, JPG, and PNG. URL conversion, plugins, OCR, cloud services, YouTube, and audio transcription are intentionally disabled.

## Privacy and safety

- Conversion runs locally using the bundled Python 3.13.13 runtime and MarkItDown 0.1.7.
- Telemetry is not collected. Rotating logs contain only request IDs, file extensions, durations, and error codes—not document contents or full paths.
- Inputs are limited to 512 MiB. ZIP entry count, expanded size, per-entry size, compression ratio, and nesting depth are validated before conversion.
- Rendered Markdown disables raw HTML, scripts, remote resources, and automatic navigation. External links require confirmation and open in the system browser.
- Outputs larger than 5 MiB are saved but not loaded into the in-app renderer/editor.

## Build and test

Build requirements are Visual Studio with the Windows App SDK workload, .NET SDK 10.0.401, `uv`, and WiX Toolset SDK 7 (restored through NuGet). The end-user installer includes all runtimes.

```powershell
.\build\Build-PythonRuntime.ps1
.\build\Test.ps1
.\build\Build-Release.ps1
```

Release outputs are written to `artifacts\installer`. The MSI requires WebView2 to already be installed; the primary Setup executable embeds and installs the verified x64 offline WebView2 prerequisite. Builds are unsigned unless a certificate thumbprint is passed to the release script.

## License

The app is MIT licensed. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled component notices.
