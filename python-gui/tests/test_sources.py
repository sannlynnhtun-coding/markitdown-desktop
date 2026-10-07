from pathlib import Path
import tempfile
import unittest

from markitdown_gui.sources import expand_dropped_sources


class DroppedSourceTests(unittest.TestCase):
    def test_files_and_folders_expand_recursively_in_stable_order(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dropped_folder = root / "drop"
            nested_folder = dropped_folder / "nested"
            nested_folder.mkdir(parents=True)
            first = dropped_folder / "a.pdf"
            second = nested_folder / "b.docx"
            unsupported = nested_folder / "ignored.exe"
            explicit_file = root / "standalone.txt"
            for path in (first, second, unsupported, explicit_file):
                path.write_text(path.name, encoding="utf-8")

            result = expand_dropped_sources(
                [dropped_folder, explicit_file, first]
            )

            self.assertEqual([path.name for path in result], ["a.pdf", "b.docx", "standalone.txt"])
            for actual, expected in zip(result, (first, second, explicit_file), strict=True):
                self.assertTrue(actual.samefile(expected))

    def test_direct_unsupported_file_is_kept_for_converter_error_reporting(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "unknown.custom"
            source.write_text("content", encoding="utf-8")

            result = expand_dropped_sources([source])

            self.assertEqual(len(result), 1)
            self.assertTrue(result[0].samefile(source))


if __name__ == "__main__":
    unittest.main()
