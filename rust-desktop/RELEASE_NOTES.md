# MarkItDown v0.4.2

- Clicking **Updates** during a startup check waits for it to finish instead of showing "already running".
- If an update prompt is already open, **Updates** brings it forward and reuses the same update flow.
- Queued checks stop when their originating app closes.
- Setup waits only for the installer process, releasing the updater lock after installation even when setup reopens MarkItDown.

## Update from the app

In v0.4.0 or v0.4.1, click **Updates**, approve the download, export your work,
and close MarkItDown when prompted. Setup updates the same installation folder.
If an older updater remains active after setup opens v0.4.2, close and reopen
that app once to let the older updater finish. Future updates use the fixed wait.
Users on v0.3.0 or earlier need one manual installation to receive the updater.

Windows 10/11 x64. PDFium, Tesseract, Myanmar/English models and native VC runtime
are bundled. The installer is unsigned; SHA-256 checksums verify download integrity.
Conversion stays local. PDF only; review OCR results against the original.
