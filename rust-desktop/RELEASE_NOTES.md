# MarkItDown v0.3.0 — Rust + GPUI Kit

MarkItDown now has a Rust Windows desktop app built with **GPUI Kit**,
with local PDF-to-Markdown conversion and **Myanmar + English OCR**.
The installer bundles PDFium, Tesseract and both OCR language models.
No Python, pip, .NET runtime or API key is required to use this release.

## What's new

- **Rust desktop UI:** file queue, batch progress, per-file status, elapsed time,
  cancellation, activity log, and light/dark themes.
- **PDF conversion:** direct text extraction for text PDFs and OCR for scanned
  pages. Choose **Auto**, **Force OCR** or **Text only**, and select Myanmar,
  English or both OCR languages.
- **Side-by-side review:** rendered PDF pages beside their Markdown output,
  page navigation, Source/Preview toggle, and Copy page.
- **UTF-8 export:** export one completed document or all completed documents.
  Existing Markdown files are preserved with numbered filenames.
- **MarkItDown branding:** the original logo appears in the header, executable,
  Windows taskbar/shortcuts and installer. The application name is **MarkItDown**.
- **Consistent spacing:** aligned controls, equal button height and top/bottom
  padding, matching toolbar and preview-header heights.
- **How to use popup:** a scrollable, step-by-step guide in Myanmar explains
  import, reading mode, OCR language, conversion, review, export and troubleshooting.
  Open it with **How to use** or **F1**; close it with **Close**, **×** or **Esc**.
- **Repeated batch fix:** PDFium bindings are reused correctly when another
  conversion batch starts in the same app session.

## Install and use

1. Download **MarkItDown-Setup-x64.exe** below and run the installer.
2. Open **MarkItDown** on Windows 10/11 x64.
3. Click **Add PDFs**, or drag PDF files into the window.
4. Start with **Auto** and **Myanmar + English**, then click **Convert all**.
5. Review the PDF and Markdown previews; use **Force OCR** if extracted text is broken.
6. Click **Export** or **Export all** and choose an output folder.
7. Click **How to use** whenever you need the detailed instructions.

Conversion runs on your computer without uploading documents. Downloads and
setup require internet; converting with the installed runtime works offline.

## Scope and known limitations

This Rust release currently supports **PDF files only**. The earlier release's
Word, Excel, PowerPoint and other converters have not been ported to this app.

Myanmar OCR can contain errors, especially on poor scans or unusual fonts;
review names, numbers and other important text. This version preserves text
and page breaks rather than reconstructing complex tables/columns.
Legacy encodings such as Zawgyi are not automatically converted.
Password-protected PDFs are not supported yet.

## Validation

- Four unit tests and three native PDF/OCR integration tests passed locally.
- Text PDFs, scanned PDFs, mixed PDFs, forced OCR, Myanmar OCR samples,
  Unicode filenames, cancellation and failed-file continuation were exercised.
- Formatting, Clippy with warnings denied, Windows release build and app
  startup checks passed. Synthetic Myanmar samples do not replace accuracy
  review of real documents.

## Download verification

**MarkItDown-Setup-x64.exe.sha256** contains the installer's SHA-256 checksum.
The installer is unsigned. Source and build instructions are included in this repository.
