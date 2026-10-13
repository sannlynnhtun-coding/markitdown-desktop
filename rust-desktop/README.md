# MarkItDown — Rust desktop

The replacement desktop application uses Rust and GPUI Kit on Windows 10/11
x64. It converts text PDFs and scanned PDFs to UTF-8 Markdown, with Myanmar
and English OCR. Python is not used by this application or its installer.
The original Python MarkItDown library remains a separate package.

## Build and run

Install Rust (MSVC, 1.92+), Visual Studio C++ build tools with a Windows SDK,
and CMake. The first build downloads Rust dependencies. Runtime preparation
downloads native PDF/OCR binaries and language models; conversion itself
works offline. No cloud account or API key is needed.

From the repository root in PowerShell:

```powershell
.\rust-desktop\scripts\prepare-runtime.ps1
.\rust-desktop\scripts\build.ps1 -Mode run
```

Runtime and build outputs default to `%LOCALAPPDATA%\MarkItDown`, so building
does not fill the repository drive. `MARKITDOWN_RUNTIME` selects an alternate
runtime directory when invoking Cargo directly. A packaged executable looks
for `runtime` beside itself. Paths do not depend on the current directory.

`prepare-runtime.ps1 -TesseractDirectory 'C:\Program Files\Tesseract-OCR'`
can copy an existing native installation instead of using the build-time
Micromamba resolver. Micromamba and Conda are not shipped with the app.
If certificate revocation servers are unreachable on your network,
`-OfflineRevocation` permits the runtime downloader to proceed with normal
certificate chain validation and verified download hashes.

## Use

Click **How to use** in the toolbar, or press **F1**, for the built-in Myanmar
step-by-step guide. The scrollable popup covers import, OCR mode/language,
conversion, preview, export and troubleshooting. Close it with **Close**, the
**×** button or **Esc**. The guide is also saved in [HOW_TO_USE.md](HOW_TO_USE.md).

- Add PDFs with the native file picker, Ctrl+O, drag/drop, or executable arguments.
- Auto reads usable text directly and applies OCR to missing/broken text.
- Force OCR reads every page visually, including PDFs with broken font mapping.
- Text only disables OCR. The language button cycles Myanmar + English,
  Myanmar, and English. The default is Myanmar + English.
- Convert all (Ctrl+Enter) keeps the interface responsive. Cancel stops
  between native PDF operations and terminates an active OCR process.
- Select a document and page to compare its rendered PDF page with Markdown.
  Page previews become available as conversion progresses.
- Copy page copies Markdown. Export saves a completed document; Export all
  saves all completed documents. Existing `.md` files are never overwritten.
- Activity shows progress and per-file errors. A failed file does not stop
  other files. Light and dark themes are included.

## Scope and limitations

This first version preserves text and source line breaks with explicit page
headings. It does not reconstruct tables, columns, semantic headings or
embedded images. Auto's text-quality detection is heuristic: plausible but
incorrect font mappings require Force OCR. A page mixing usable text with a
scanned image may also require Force OCR to recover the image's text.

Myanmar Unicode is preserved; Zawgyi/legacy encodings are not automatically
converted. OCR accuracy depends on scan quality and font. Use the PDF preview
to review results. Myanmar OCR support is not a guarantee of perfect text.
Controls use Segoe UI with consistent 36-pixel button height and equal vertical
padding. Toolbar rows share a 60-pixel height and 16-pixel horizontal gutters.
Document text uses Windows' Myanmar Text font, with OS font fallback.

Password-protected PDFs currently report an error. OCR has a 180-second
per-page timeout and bounded render dimensions. Rendered pages use a temporary
cache removed when the document is cleared or the app closes. Large batches
need sufficient temporary disk space.

## Verify

```powershell
.\rust-desktop\scripts\build.ps1 -Mode check
.\rust-desktop\scripts\build.ps1 -Mode test
$env:MARKITDOWN_RUNTIME = "$env:LOCALAPPDATA\MarkItDown\runtime"
$env:MARKITDOWN_MYANMAR_TEST_IMAGE = .\rust-desktop\tests\make-myanmar-fixture.ps1
cargo test --manifest-path rust-desktop/Cargo.toml --no-default-features --test pdf_pipeline -- --ignored
```

The ignored integration tests require the prepared PDFium/Tesseract runtime.
They exercise a text PDF, a scan PDF, a mixed document, cancellation,
Unicode filenames, and a synthetic Myanmar scan. These fixtures do not
replace accuracy review of real Myanmar documents. To smoke-test a real Myanmar PDF:

```powershell
cargo run --manifest-path rust-desktop/Cargo.toml --no-default-features --bin pdf-to-markdown -- 'C:\docs\မြန်မာ.pdf' 'C:\docs\output' --force-ocr --myanmar
```

## Package

```powershell
.\rust-desktop\scripts\build.ps1 -Mode release
# With Inno Setup 6 installed:
.\rust-desktop\scripts\build.ps1 -Mode release -Installer
```

The default portable release is `%LOCALAPPDATA%\MarkItDown\dist`.
Distribute the entire folder, including runtime DLLs, models and licenses.
See [THIRD_PARTY.md](THIRD_PARTY.md) for bundled notices.
