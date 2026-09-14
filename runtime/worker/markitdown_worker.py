"""Private JSON-lines worker for MarkItDown Desktop.

The worker accepts only absolute local file paths and writes converted Markdown to
the calling user's application cache. Standard output is reserved for protocol
messages; converter diagnostics are redirected to standard error.
"""

from __future__ import annotations

import contextlib
import io
import json
import os
from pathlib import Path
import sys
import time
import uuid
import zipfile

from markitdown import MarkItDown


PROTOCOL_VERSION = 1
MAX_INPUT_BYTES = 512 * 1024 * 1024
MAX_ZIP_ENTRIES = 1_000
MAX_ZIP_TOTAL_BYTES = 512 * 1024 * 1024
MAX_ZIP_ENTRY_BYTES = 128 * 1024 * 1024
MAX_ZIP_COMPRESSION_RATIO = 100
MAX_ZIP_NESTING_DEPTH = 2


class WorkerFailure(Exception):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


def emit(event: str, **fields: object) -> None:
    payload = {"protocolVersion": PROTOCOL_VERSION, "event": event, **fields}
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def allowed_cache_root() -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if not local_app_data:
        raise WorkerFailure("cache_unavailable", "The local application data directory is unavailable.")
    return (Path(local_app_data) / "MarkItDown Desktop" / "Cache").resolve()


def validate_cache_directory(value: object) -> Path:
    if not isinstance(value, str) or not value.strip():
        raise WorkerFailure("invalid_request", "A cache directory is required.")
    candidate = Path(value).resolve()
    root = allowed_cache_root()
    try:
        candidate.relative_to(root)
    except ValueError as exc:
        raise WorkerFailure("invalid_cache_path", "The cache directory is outside the app data folder.") from exc
    candidate.mkdir(parents=True, exist_ok=True)
    return candidate


def validate_local_input(value: object) -> Path:
    if not isinstance(value, str) or not value.strip():
        raise WorkerFailure("invalid_request", "An input file is required.")
    path = Path(value)
    if not path.is_absolute():
        raise WorkerFailure("invalid_path", "The input must be an absolute local file path.")
    path = path.resolve(strict=False)
    if not path.exists():
        raise WorkerFailure("file_not_found", "The selected file no longer exists.")
    if not path.is_file():
        raise WorkerFailure("invalid_path", "The selected item is not a file.")
    if path.stat().st_size > MAX_INPUT_BYTES:
        raise WorkerFailure("input_too_large", "Files larger than 512 MiB are not supported.")
    return path


def validate_zip(path: Path) -> None:
    totals = {"entries": 0, "bytes": 0}
    with path.open("rb") as stream:
        _validate_zip_stream(stream, depth=0, totals=totals)


def _validate_zip_stream(stream: io.BufferedIOBase | io.BytesIO, depth: int, totals: dict[str, int]) -> None:
    if depth > MAX_ZIP_NESTING_DEPTH:
        raise WorkerFailure("zip_nesting_limit", "The ZIP nesting depth exceeds the limit of 2.")
    try:
        archive = zipfile.ZipFile(stream, "r")
    except zipfile.BadZipFile as exc:
        raise WorkerFailure("corrupt_archive", "The ZIP file is corrupt or unsupported.") from exc

    with archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            totals["entries"] += 1
            totals["bytes"] += entry.file_size
            if totals["entries"] > MAX_ZIP_ENTRIES:
                raise WorkerFailure("zip_entry_limit", "The ZIP contains more than 1,000 files.")
            if totals["bytes"] > MAX_ZIP_TOTAL_BYTES:
                raise WorkerFailure("zip_size_limit", "The ZIP expands beyond the 512 MiB safety limit.")
            if entry.file_size > MAX_ZIP_ENTRY_BYTES:
                raise WorkerFailure("zip_entry_size_limit", "A ZIP entry exceeds the 128 MiB safety limit.")
            compressed = max(entry.compress_size, 1)
            if entry.file_size / compressed > MAX_ZIP_COMPRESSION_RATIO:
                raise WorkerFailure("zip_ratio_limit", "The ZIP compression ratio exceeds the 100:1 safety limit.")

            if Path(entry.filename).suffix.lower() == ".zip":
                if depth >= MAX_ZIP_NESTING_DEPTH:
                    raise WorkerFailure("zip_nesting_limit", "The ZIP nesting depth exceeds the limit of 2.")
                nested_data = archive.read(entry)
                _validate_zip_stream(io.BytesIO(nested_data), depth + 1, totals)


def convert(request: dict[str, object]) -> dict[str, object]:
    request_id = request.get("requestId")
    if not isinstance(request_id, str):
        raise WorkerFailure("invalid_request", "A request identifier is required.")
    try:
        uuid.UUID(request_id)
    except ValueError as exc:
        raise WorkerFailure("invalid_request", "The request identifier is invalid.") from exc

    input_path = validate_local_input(request.get("inputPath"))
    cache_directory = validate_cache_directory(request.get("cacheDirectory"))
    if input_path.suffix.lower() == ".zip":
        validate_zip(input_path)

    started = time.perf_counter()
    temporary_path = cache_directory / f"{request_id}.md.tmp"
    result_path = cache_directory / f"{request_id}.md"
    try:
        converter = MarkItDown(enable_plugins=False)
        with contextlib.redirect_stdout(sys.stderr):
            result = converter.convert_local(str(input_path), keep_data_uris=False)
        temporary_path.write_text(result.markdown, encoding="utf-8", newline="\n")
        os.replace(temporary_path, result_path)
    finally:
        temporary_path.unlink(missing_ok=True)

    return {
        "requestId": request_id,
        "markdownPath": str(result_path),
        "title": result.title,
        "elapsedMs": round((time.perf_counter() - started) * 1000),
    }


def error_details(exc: Exception) -> tuple[str, str]:
    if isinstance(exc, WorkerFailure):
        return exc.code, str(exc)
    if isinstance(exc, PermissionError):
        return "access_denied", "The app does not have permission to read the input or write the output."
    if isinstance(exc, FileNotFoundError):
        return "file_not_found", "The selected file no longer exists."
    if isinstance(exc, zipfile.BadZipFile):
        return "corrupt_archive", "The ZIP file is corrupt or unsupported."

    exception_name = type(exc).__name__
    if exception_name == "UnsupportedFormatException":
        return "unsupported_format", "MarkItDown does not support this file format."
    if exception_name == "MissingDependencyException":
        return "missing_dependency", "A bundled converter dependency is missing. Repair the installation."
    if exception_name == "FileConversionException":
        return "conversion_failed", "The document is corrupt, encrypted, or could not be converted."
    return "conversion_failed", "The document could not be converted."


def main() -> int:
    # The protocol is UTF-8 regardless of the user's Windows ANSI code page.
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    emit("ready")
    for line in sys.stdin:
        if not line.strip():
            continue
        request_id: str | None = None
        try:
            request = json.loads(line)
            request_id_value = request.get("requestId")
            request_id = request_id_value if isinstance(request_id_value, str) else None
            if request.get("protocolVersion") != PROTOCOL_VERSION:
                raise WorkerFailure("protocol_mismatch", "The worker protocol version is incompatible.")
            if request.get("command") != "convert":
                raise WorkerFailure("unsupported_command", "The worker command is not supported.")
            emit("completed", **convert(request))
        except json.JSONDecodeError:
            emit("error", requestId=request_id, errorCode="invalid_json", message="The worker request is invalid JSON.")
        except Exception as exc:  # The protocol must convert all failures into structured errors.
            code, message = error_details(exc)
            emit("error", requestId=request_id, errorCode=code, message=message)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
