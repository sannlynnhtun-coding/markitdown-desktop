# Changelog

## 0.3.0 - 2026-10-13

- Replace the current desktop release with a Rust + GPUI Kit Windows app.
- Add direct PDF text extraction and native Myanmar/English OCR for scans.
- Add Auto, Force OCR and Text only modes with an OCR language selector.
- Add PDF/Markdown page previews, source toggle, page copy and UTF-8 exports.
- Add batch progress, cancellation, per-file status, elapsed time and activity log.
- Restore the MarkItDown name and logo throughout the app and installer.
- Align toolbar rows and standardize button heights and vertical padding.
- Add the How to use popup, Myanmar step-by-step guide and F1/Esc shortcuts.
- Fix PDFium initialization for repeated batches in one session.
- Bundle native PDF/OCR engines, language models and the VC runtime without Python.
- Scope this Rust version to PDF; retain legacy implementations as reference.
## 0.2.0 - 2026-10-07

### Added

- Native Explorer drag-and-drop for files and folders on the source queue.
- Recursive, deterministic discovery of supported documents in dropped folders.
- Duplicate filtering across directly dropped files and folder contents.
- File and folder drag-and-drop support in the reference Uno queue.

## 0.1.1

- Refreshed the Windows installer, application branding, and package dependencies.
