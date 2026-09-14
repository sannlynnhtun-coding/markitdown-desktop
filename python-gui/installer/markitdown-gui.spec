# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path

from PyInstaller.utils.hooks import collect_data_files, collect_submodules, copy_metadata


package = Path(SPECPATH).resolve().parents[0]
gui_source = package / "src"
assets = gui_source / "markitdown_gui" / "assets"

datas = [
    (str(assets / "markitdown-app-64.png"), "markitdown_gui/assets"),
    (str(assets / "markitdown-app.ico"), "markitdown_gui/assets"),
]
datas += collect_data_files("magika")
datas += copy_metadata("markitdown")
datas += copy_metadata("magika")

hiddenimports = collect_submodules("markitdown")

analysis = Analysis(
    [str(package / "installer" / "entrypoint.py")],
    pathex=[str(gui_source)],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["pytest", "unittest", "IPython"],
    noarchive=False,
    optimize=1,
)

pyz = PYZ(analysis.pure)

executable = EXE(
    pyz,
    analysis.scripts,
    [],
    exclude_binaries=True,
    name="MarkItDown",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch="x86_64",
    icon=str(assets / "markitdown-app.ico"),
)

bundle = COLLECT(
    executable,
    analysis.binaries,
    analysis.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name="MarkItDown",
)
