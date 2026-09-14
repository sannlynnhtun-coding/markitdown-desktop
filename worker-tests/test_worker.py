from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import uuid
import zipfile


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
WORKER_PATH = REPOSITORY_ROOT / "runtime" / "worker" / "markitdown_worker.py"


def load_worker():
    spec = importlib.util.spec_from_file_location("markitdown_desktop_worker", WORKER_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Unable to load worker module.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


worker = load_worker()


class WorkerValidationTests(unittest.TestCase):
    def test_rejects_input_larger_than_512_mib(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            input_path = Path(temporary_directory) / "oversized.bin"
            with input_path.open("wb") as stream:
                stream.truncate(worker.MAX_INPUT_BYTES + 1)

            with self.assertRaises(worker.WorkerFailure) as context:
                worker.validate_local_input(str(input_path))

        self.assertEqual("input_too_large", context.exception.code)

    def test_rejects_relative_paths(self) -> None:
        with self.assertRaises(worker.WorkerFailure) as context:
            worker.validate_local_input("relative.txt")

        self.assertEqual("invalid_path", context.exception.code)

    def test_rejects_cache_outside_local_app_data(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            old_value = os.environ.get("LOCALAPPDATA")
            os.environ["LOCALAPPDATA"] = temporary_directory
            try:
                with self.assertRaises(worker.WorkerFailure) as context:
                    worker.validate_cache_directory(str(Path(temporary_directory).parent / "other"))
            finally:
                if old_value is None:
                    os.environ.pop("LOCALAPPDATA", None)
                else:
                    os.environ["LOCALAPPDATA"] = old_value

        self.assertEqual("invalid_cache_path", context.exception.code)

    def test_rejects_high_compression_ratio_zip(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            archive_path = Path(temporary_directory) / "ratio.zip"
            with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("zeros.txt", b"0" * (1024 * 1024))

            with self.assertRaises(worker.WorkerFailure) as context:
                worker.validate_zip(archive_path)

        self.assertEqual("zip_ratio_limit", context.exception.code)

    def test_rejects_corrupt_zip(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            archive_path = Path(temporary_directory) / "corrupt.zip"
            archive_path.write_bytes(b"not a zip archive")

            with self.assertRaises(worker.WorkerFailure) as context:
                worker.validate_zip(archive_path)

        self.assertEqual("corrupt_archive", context.exception.code)

    def test_rejects_more_than_one_thousand_zip_entries(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            archive_path = Path(temporary_directory) / "many.zip"
            with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_STORED) as archive:
                for index in range(worker.MAX_ZIP_ENTRIES + 1):
                    archive.writestr(f"{index}.txt", b"")

            with self.assertRaises(worker.WorkerFailure) as context:
                worker.validate_zip(archive_path)

        self.assertEqual("zip_entry_limit", context.exception.code)

    def test_rejects_nested_zip_beyond_depth_two(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_path = Path(temporary_directory)
            payload = b"plain text"
            for depth in range(4):
                nested_path = temporary_path / f"nested-{depth}.zip"
                with zipfile.ZipFile(nested_path, "w", compression=zipfile.ZIP_STORED) as archive:
                    archive.writestr("nested.zip" if depth else "content.txt", payload)
                payload = nested_path.read_bytes()

            outer_path = temporary_path / "nested-3.zip"
            with self.assertRaises(worker.WorkerFailure) as context:
                worker.validate_zip(outer_path)

        self.assertEqual("zip_nesting_limit", context.exception.code)

    def test_maps_expected_converter_and_access_errors(self) -> None:
        file_conversion_error = type("FileConversionException", (Exception,), {})
        unsupported_error = type("UnsupportedFormatException", (Exception,), {})

        self.assertEqual("access_denied", worker.error_details(PermissionError())[0])
        self.assertEqual("conversion_failed", worker.error_details(file_conversion_error())[0])
        self.assertEqual("unsupported_format", worker.error_details(unsupported_error())[0])


class WorkerProtocolIntegrationTests(unittest.TestCase):
    def test_unicode_path_text_conversion_uses_cache_file(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_path = Path(temporary_directory)
            local_app_data = temporary_path / "Local App Data"
            cache_path = local_app_data / "MarkItDown Desktop" / "Cache"
            input_path = temporary_path / "မြန်မာ space.txt"
            input_path.write_text("Hello မြန်မာ", encoding="utf-8")

            environment = os.environ.copy()
            environment["LOCALAPPDATA"] = str(local_app_data)
            exiftool = REPOSITORY_ROOT / "runtime" / "exiftool" / "exiftool.exe"
            if exiftool.exists():
                environment["EXIFTOOL_PATH"] = str(exiftool)

            process = subprocess.Popen(
                [sys.executable, "-I", str(WORKER_PATH)],
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                env=environment,
            )
            try:
                assert process.stdout is not None
                assert process.stdin is not None
                ready = json.loads(process.stdout.readline())
                self.assertEqual("ready", ready["event"])
                self.assertEqual(1, ready["protocolVersion"])

                request_id = str(uuid.uuid4())
                process.stdin.write(json.dumps({
                    "protocolVersion": 1,
                    "requestId": request_id,
                    "command": "convert",
                    "inputPath": str(input_path),
                    "cacheDirectory": str(cache_path),
                }, ensure_ascii=False) + "\n")
                process.stdin.flush()

                result = json.loads(process.stdout.readline())
                self.assertEqual("completed", result["event"], result)
                self.assertEqual(request_id, result["requestId"])
                markdown_path = Path(result["markdownPath"])
                self.assertTrue(markdown_path.is_relative_to(cache_path.resolve()))
                self.assertEqual("Hello မြန်မာ", markdown_path.read_text(encoding="utf-8").strip())
                self.assertNotIn("markdown", result)
            finally:
                if process.stdin is not None:
                    process.stdin.close()
                process.wait(timeout=10)
                if process.stdout is not None:
                    process.stdout.close()
                if process.stderr is not None:
                    process.stderr.close()


if __name__ == "__main__":
    unittest.main()
