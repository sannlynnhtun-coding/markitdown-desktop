"""Batch conversion orchestration kept independent from the Tk event loop."""

from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path
import threading
import time
from typing import Any, Callable, Iterable
from uuid import uuid4


SUPPORTED_EXTENSIONS = {
    ".pdf",
    ".docx",
    ".pptx",
    ".xlsx",
    ".xls",
    ".html",
    ".htm",
    ".csv",
    ".json",
    ".xml",
    ".txt",
    ".rtf",
    ".epub",
    ".zip",
    ".jpg",
    ".jpeg",
    ".png",
    ".gif",
    ".webp",
    ".tiff",
    ".bmp",
    ".wav",
    ".mp3",
    ".m4a",
}


@dataclass(frozen=True)
class ConversionEvent:
    kind: str
    message: str
    percent: float
    completed: int
    total: int
    elapsed: float
    source: Path | None = None
    output: Path | None = None
    file_elapsed: float | None = None


@dataclass(frozen=True)
class BatchSummary:
    total: int
    processed: int
    succeeded: int
    failed: int
    cancelled: bool
    elapsed: float


EventSink = Callable[[ConversionEvent], None]
DocumentConverter = Callable[[Path], Any]


def format_duration(seconds: float) -> str:
    seconds = max(0.0, seconds)
    if seconds < 60:
        return f"{seconds:.1f}s"
    minutes, remaining = divmod(int(seconds), 60)
    if minutes < 60:
        return f"{minutes}m {remaining:02d}s"
    hours, minutes = divmod(minutes, 60)
    return f"{hours}h {minutes:02d}m"


def format_bytes(size: int) -> str:
    value = float(max(size, 0))
    for unit in ("B", "KB", "MB", "GB"):
        if value < 1024 or unit == "GB":
            return f"{value:.0f} {unit}" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1024
    return f"{value:.1f} GB"


def unique_output_path(source: Path, output_directory: Path, reserved: set[str]) -> Path:
    """Return a non-destructive output name, including same-stem batch collisions."""

    base = source.stem or "converted"
    candidate = output_directory / f"{base}.md"
    suffix = 2
    while os.path.normcase(str(candidate)) in reserved or candidate.exists():
        candidate = output_directory / f"{base}-{suffix}.md"
        suffix += 1
    reserved.add(os.path.normcase(str(candidate)))
    return candidate


class BatchConverter:
    """Convert a queue sequentially while publishing granular progress events."""

    def __init__(self, convert_document: DocumentConverter) -> None:
        self.convert_document = convert_document

    def run(
        self,
        sources: Iterable[Path],
        output_directory: Path,
        emit: EventSink,
        stop_event: threading.Event | None = None,
    ) -> BatchSummary:
        files = [Path(source) for source in sources]
        if not files:
            raise ValueError("At least one source file is required")

        stop_event = stop_event or threading.Event()
        total = len(files)
        started = time.perf_counter()
        processed = succeeded = failed = 0
        reserved: set[str] = set()
        output_directory.mkdir(parents=True, exist_ok=True)

        def publish(
            kind: str,
            message: str,
            percent: float,
            *,
            source: Path | None = None,
            output: Path | None = None,
            file_elapsed: float | None = None,
        ) -> None:
            emit(
                ConversionEvent(
                    kind=kind,
                    message=message,
                    percent=max(0.0, min(100.0, percent)),
                    completed=processed,
                    total=total,
                    elapsed=time.perf_counter() - started,
                    source=source,
                    output=output,
                    file_elapsed=file_elapsed,
                )
            )

        publish("batch_started", f"Prepared {total} file{'s' if total != 1 else ''} for conversion", 0)

        for index, source in enumerate(files):
            if stop_event.is_set():
                publish("batch_cancelled", "Conversion stopped before the next file", processed / total * 100)
                break

            output = unique_output_path(source, output_directory, reserved)
            file_started = time.perf_counter()
            base = index / total * 100
            step = 100 / total
            position = f"{index + 1}/{total}"
            publish(
                "file_started",
                f"{position}  Preparing {source.name}",
                base + step * 0.05,
                source=source,
                output=output,
            )

            try:
                if not source.is_file():
                    raise FileNotFoundError(f"Source file is no longer available: {source}")

                publish(
                    "file_converting",
                    f"{position}  Extracting content from {source.name}",
                    base + step * 0.18,
                    source=source,
                    output=output,
                )
                result = self.convert_document(source)
                markdown = result if isinstance(result, str) else getattr(result, "markdown")
                if not isinstance(markdown, str):
                    raise TypeError("The converter did not return Markdown text")

                publish(
                    "file_writing",
                    f"{position}  Writing {output.name}",
                    base + step * 0.86,
                    source=source,
                    output=output,
                )
                self._write_atomic(output, markdown)
            except Exception as exc:
                processed += 1
                failed += 1
                duration = time.perf_counter() - file_started
                publish(
                    "file_failed",
                    f"{position}  Failed {source.name}: {exc}",
                    processed / total * 100,
                    source=source,
                    output=output,
                    file_elapsed=duration,
                )
            else:
                processed += 1
                succeeded += 1
                duration = time.perf_counter() - file_started
                publish(
                    "file_succeeded",
                    f"{position}  Saved {output.name} in {format_duration(duration)}",
                    processed / total * 100,
                    source=source,
                    output=output,
                    file_elapsed=duration,
                )

        cancelled = processed < total
        elapsed = time.perf_counter() - started
        summary = BatchSummary(total, processed, succeeded, failed, cancelled, elapsed)
        if not cancelled:
            detail = f"{succeeded} succeeded"
            if failed:
                detail += f", {failed} failed"
            publish("batch_finished", f"Conversion complete — {detail} in {format_duration(elapsed)}", 100)
        return summary

    @staticmethod
    def _write_atomic(destination: Path, markdown: str) -> None:
        temporary = destination.with_name(f".{destination.name}.{uuid4().hex}.tmp")
        try:
            temporary.write_text(markdown, encoding="utf-8", newline="\n")
            os.replace(temporary, destination)
        finally:
            if temporary.exists():
                temporary.unlink()
