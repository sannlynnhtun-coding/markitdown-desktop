# Bundled conversion runtime

`build/Build-PythonRuntime.ps1` creates a private x64 Python runtime in this
directory. It pins MarkItDown 0.1.7 with the PDF, Word, PowerPoint, Excel and
Outlook extras. End users do not need Python or pip.

The generated `python/` and `exiftool/` directories are build artifacts and are
not committed. The worker protocol on standard input/output is private to the
desktop application and is versioned independently from MarkItDown.
