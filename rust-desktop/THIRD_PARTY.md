This app uses GPUI Kit (Apache-2.0), pdfium-render (MIT/Apache-2.0),
PDFium (BSD-style and bundled third-party notices), Tesseract (Apache-2.0),
and Tesseract tessdata_best Myanmar/English models (Apache-2.0).

The prepared runtime includes PDFium notices, OCR native dependency licenses,
and the model license under `runtime/licenses`. The release builder also
copies available Rust dependency licenses into `runtime/licenses/rust`.
`native-packages.txt` records
the resolved native package URLs. Preserve these files when distributing.
If supplying your own Tesseract directory, include its dependency license
notices in `runtime/licenses/tesseract` before distribution.

Sources:
- https://github.com/longbridge/gpui-kit
- https://github.com/ajrcarey/pdfium-render
- https://github.com/bblanchon/pdfium-binaries
- https://github.com/tesseract-ocr/tesseract
- https://github.com/tesseract-ocr/tessdata_best
- https://github.com/conda-forge/tesseract-feedstock
