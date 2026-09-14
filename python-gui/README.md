# MarkItDown GUI

A focused Windows desktop companion for Microsoft MarkItDown. It keeps batch
conversion visible with per-file status, a true percentage, a timestamped
activity ledger, and elapsed time for both the complete run and each document.

## Features

- Queue individual files or recursively add a folder.
- Convert the complete queue with visible progress and step-by-step logs.
- Switch between light and dark themes.
- Right-click completed rows to open their Markdown output.
- Right-click selected rows to remove them from the queue without deleting files.

## Run from source

```powershell
python -m pip install -e ".\python-gui"
markitdown-gui
```

Install MarkItDown with its `all` extra to enable every supported format:

```powershell
python -m pip install "markitdown[all]>=0.1,<0.2"
```

## Keyboard shortcuts

- `Ctrl+O`: add files
- `Ctrl+Shift+O`: add a folder
- `Ctrl+Enter`: convert all
- `Ctrl+D`: switch light/dark mode

## Tests

```powershell
$env:PYTHONPATH = ".\python-gui\src"
python -m unittest discover .\python-gui\tests -v
```
