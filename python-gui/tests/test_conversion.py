from pathlib import Path
import tempfile
import threading
import unittest

from markitdown_gui.conversion import (
    BatchConverter,
    format_bytes,
    format_duration,
    unique_output_path,
)


class BatchConverterTests(unittest.TestCase):
    def test_run_writes_each_result_and_reports_complete_progress(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            sources = [root / "first.txt", root / "second.txt"]
            for source in sources:
                source.write_text(source.stem, encoding="utf-8")
            output = root / "out"
            events = []

            summary = BatchConverter(
                lambda source: f"# {source.read_text(encoding='utf-8')}"
            ).run(sources, output, events.append)

            self.assertEqual((output / "first.md").read_text(encoding="utf-8"), "# first")
            self.assertEqual((output / "second.md").read_text(encoding="utf-8"), "# second")
            self.assertEqual(summary.succeeded, 2)
            self.assertEqual(summary.failed, 0)
            self.assertFalse(summary.cancelled)
            self.assertEqual(events[-1].kind, "batch_finished")
            self.assertEqual(events[-1].percent, 100)
            self.assertEqual(
                [event.kind for event in events].count("file_converting"), 2
            )
            self.assertEqual([event.kind for event in events].count("file_writing"), 2)

    def test_failure_is_logged_and_next_file_still_converts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bad = root / "bad.txt"
            good = root / "good.txt"
            bad.write_text("bad", encoding="utf-8")
            good.write_text("good", encoding="utf-8")
            events = []

            def convert(source: Path) -> str:
                if source == bad:
                    raise RuntimeError("broken document")
                return "converted"

            summary = BatchConverter(convert).run([bad, good], root / "out", events.append)

            self.assertEqual(summary.failed, 1)
            self.assertEqual(summary.succeeded, 1)
            self.assertTrue((root / "out" / "good.md").exists())
            self.assertIn("broken document", next(e.message for e in events if e.kind == "file_failed"))
            self.assertEqual(events[-1].percent, 100)

    def test_stop_event_cancels_before_next_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            sources = [root / "one.txt", root / "two.txt"]
            for source in sources:
                source.write_text("source", encoding="utf-8")
            stop = threading.Event()
            events = []

            def convert(_source: Path) -> str:
                stop.set()
                return "done"

            summary = BatchConverter(convert).run(sources, root / "out", events.append, stop)

            self.assertTrue(summary.cancelled)
            self.assertEqual(summary.processed, 1)
            self.assertEqual(events[-1].kind, "batch_cancelled")
            self.assertEqual(events[-1].percent, 50)

    def test_output_names_do_not_overwrite_existing_or_same_stem_files(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "report.md").write_text("keep", encoding="utf-8")
            reserved: set[str] = set()

            first = unique_output_path(Path("a/report.pdf"), root, reserved)
            second = unique_output_path(Path("b/report.docx"), root, reserved)

            self.assertEqual(first.name, "report-2.md")
            self.assertEqual(second.name, "report-3.md")

    def test_human_readable_formatters(self) -> None:
        self.assertEqual(format_duration(3.25), "3.2s")
        self.assertEqual(format_duration(65), "1m 05s")
        self.assertEqual(format_bytes(1536), "1.5 KB")


if __name__ == "__main__":
    unittest.main()
