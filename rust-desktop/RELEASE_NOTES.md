# MarkItDown v0.4.1

- Upgrade pdfium-render from 0.9.4 to 0.9.5.
- Fix updater checksum verification when the app inherits a PowerShell 7 module path. The updater now lets Windows PowerShell resolve its own modules.

## Update from the app

In v0.4.0, click **Updates**, approve the download, then export your work and close
MarkItDown when prompted. Setup installs this release in the same directory.
Startup also checks for available updates. Users on v0.3.0 or earlier must
install manually once to receive the updater.

Windows 10/11 x64. PDFium, Tesseract, Myanmar/English OCR models and the native
VC runtime remain bundled. No Python, .NET runtime or API key is needed.

The installer remains unsigned. GitHub-hosted SHA-256 checksums verify download
integrity. Conversion stays local; update checks contact GitHub. PDF only;
review OCR results against the original document.
