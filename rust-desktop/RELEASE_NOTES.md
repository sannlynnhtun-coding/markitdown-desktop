# MarkItDown v0.4.0

- Check GitHub Releases for updates automatically at startup, or click **Updates**.
- Confirm before downloading; verify the installer with its SHA-256 checksum.
- Setup waits for you to export your work and close MarkItDown, then updates the same installation directory. No forced exit or silent installation.
- Update checks tolerate offline startup; manual checks and download failures show an error.
- Upgrade GPUI Kit from 0.7.3 to 0.8.1 and unicode-normalization from 0.1.25 to 0.1.26.
- Fix Dependabot configuration referencing labels that did not exist.

## Install

Download **MarkItDown-Setup-x64.exe** for Windows 10/11 x64. Existing v0.3.0
users must install this version manually once to receive the updater.
PDFium, Tesseract, Myanmar/English models and the native VC runtime are bundled.
No Python, pip, .NET runtime or API key is required.

## Limitations

PDF only. OCR can make mistakes; review important text against the original.
Complex layouts and legacy encodings such as Zawgyi are not reconstructed.
Password-protected PDFs are not supported. Conversion remains local; update
checks and downloads contact GitHub. Updates require Windows PowerShell and
network access. The installer is unsigned; the checksum verifies the download
against the asset published in this repository, not a publisher signature.
