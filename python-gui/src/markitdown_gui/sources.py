"""Source discovery for files and folders dropped onto the conversion queue."""

from __future__ import annotations

import os
from pathlib import Path
from typing import Iterable

from .conversion import SUPPORTED_EXTENSIONS


def expand_dropped_sources(paths: Iterable[Path]) -> list[Path]:
    """Expand dropped folders recursively while preserving a stable input order."""

    sources: list[Path] = []
    known_paths: set[str] = set()

    def append_file(path: Path) -> None:
        try:
            resolved = path.expanduser().resolve()
        except (OSError, RuntimeError):
            return
        key = os.path.normcase(str(resolved))
        if key not in known_paths and resolved.is_file():
            known_paths.add(key)
            sources.append(resolved)

    for dropped_path in paths:
        try:
            resolved = dropped_path.expanduser().resolve()
        except (OSError, RuntimeError):
            continue

        if resolved.is_file():
            append_file(resolved)
            continue
        if not resolved.is_dir():
            continue

        try:
            descendants = sorted(
                resolved.rglob("*"),
                key=lambda value: str(value).casefold(),
            )
        except OSError:
            continue

        for descendant in descendants:
            try:
                is_supported_file = (
                    descendant.is_file()
                    and descendant.suffix.lower() in SUPPORTED_EXTENSIONS
                )
            except OSError:
                continue
            if is_supported_file:
                append_file(descendant)

    return sources
