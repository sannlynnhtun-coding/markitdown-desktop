from pathlib import Path
import tempfile
import unittest

from markitdown_gui.theme import PALETTES, ThemePreference


class ThemePreferenceTests(unittest.TestCase):
    def test_theme_round_trip(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            preference = ThemePreference(Path(directory) / "preferences.json")
            self.assertEqual(preference.load(), "light")
            preference.save("dark")
            self.assertEqual(preference.load(), "dark")

    def test_invalid_saved_theme_falls_back_to_light(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "preferences.json"
            path.write_text('{"theme": "sepia"}', encoding="utf-8")
            self.assertEqual(ThemePreference(path).load(), "light")

    def test_palettes_have_distinct_readable_tokens(self) -> None:
        self.assertEqual(set(PALETTES), {"light", "dark"})
        self.assertNotEqual(PALETTES["light"].canvas, PALETTES["dark"].canvas)
        self.assertNotEqual(PALETTES["light"].ink, PALETTES["light"].canvas)


if __name__ == "__main__":
    unittest.main()
