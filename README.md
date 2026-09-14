# MarkItDown Desktop

MarkItDown Desktop is an independent community GUI powered by
[Microsoft MarkItDown](https://github.com/microsoft/markitdown). The current
Windows release is a compact batch converter that runs locally and does not
require users to install Python or use a command line.

## Use the app

1. Download `MarkItDownDesktop-Setup-x64.exe` from the
   [latest release](https://github.com/sannlynnhtun-coding/markitdown-desktop/releases/latest).
2. Install and open **MarkItDown**.
3. Add individual files or a folder to the source queue.
4. Choose an output folder and select **Convert all**.
5. Follow the percentage, per-file status, activity log, and elapsed time.

Right-click a completed row to open its Markdown output. Right-click selected
rows and choose **Remove File** to remove them from the queue without deleting
the source or output files.

The interface supports light and dark modes. Existing output files are
preserved by adding a numeric suffix to the new Markdown filename.

## Source and builds

The current application, tests, icon assets, and reproducible Windows installer
scripts live in [`python-gui`](python-gui). See
[`python-gui/README.md`](python-gui/README.md) for source and build commands.

The original Uno Platform implementation remains in `MarkItDown.Desktop` for
reference and is no longer used to produce the current installer.

## Privacy

Conversion happens on the local computer. The application does not add
telemetry or upload documents to a service.

## License

The app is MIT licensed. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
for bundled component notices.
