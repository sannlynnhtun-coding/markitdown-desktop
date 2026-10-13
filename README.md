# MarkItDown

MarkItDown is an independent Windows desktop PDF-to-Markdown app built with
**Rust and GPUI Kit**, with local **Myanmar + English OCR**. PDFium, Tesseract
and both language models are bundled. No Python, pip, .NET runtime or API key
is needed to use the app.

![MarkItDown logo](rust-desktop/assets/markitdown.png)

## Install and use

1. Download **MarkItDown-Setup-x64.exe** from the [latest release](https://github.com/sannlynnhtun-coding/markitdown-desktop/releases/latest).
2. Install and open **MarkItDown** on Windows 10/11 x64.
3. Click **Add PDFs** or drag PDF files into the app.
4. Start with **Auto** and **Myanmar + English**, then click **Convert all**.
5. Select a document and compare its PDF pages with the Markdown preview.
6. Click **Export** or **Export all**, then choose an output folder.

Click **How to use** or press **F1** for the built-in step-by-step Myanmar guide.
Close the popup with **Close**, **×**, or **Esc**.

## What's new in v0.3.0

- Rust/GPUI Kit UI with aligned controls, equal button height and vertical padding.
- Direct PDF text extraction and scanned-page OCR with Myanmar/English models.
- Auto, Force OCR and Text only modes with an OCR language selector.
- Side-by-side PDF/Markdown previews, Source/Preview toggle and Copy page.
- Batch progress, per-file status, elapsed time, cancellation and activity log.
- UTF-8 exports with numbered filenames that preserve existing files.
- Original MarkItDown logo in the header, executable, taskbar and installer.
- How to use popup with step-by-step instructions and troubleshooting.
- Reused PDFium bindings for repeated conversion batches.
- Light and dark themes.

See the [release notes](rust-desktop/RELEASE_NOTES.md) and [changelog](CHANGELOG.md).

## Supported files and accuracy

This Rust release supports **PDF only**. Other formats supported by the legacy
app have not been ported. Earlier implementations remain in this repository
for reference.

OCR can make mistakes, including in Myanmar text. Review names and numbers
against the PDF. Complex tables/columns and legacy encodings such as Zawgyi
are not automatically reconstructed. Password-protected PDFs are not supported yet.

## Source and builds

Source, tests, logo and Windows build scripts are in [rust-desktop](rust-desktop/README.md).
Install Rust MSVC, Visual Studio C++ tools, a Windows SDK, CMake and Inno Setup 6.

```powershell
.\rust-desktop\scripts\prepare-runtime.ps1
.\rust-desktop\scripts\build.ps1 -Mode run
.\rust-desktop\scripts\build.ps1 -Mode release -Installer
```

The Python/Tkinter and Uno implementations remain as legacy reference source.
They are not bundled in the Rust installer.

## Privacy

Conversion runs locally without uploading documents or adding telemetry.
The bundled runtime converts documents offline. Internet is needed for initial
downloads and developer dependency setup.

## License

The app is MIT licensed. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
and [rust-desktop/THIRD_PARTY.md](rust-desktop/THIRD_PARTY.md).
