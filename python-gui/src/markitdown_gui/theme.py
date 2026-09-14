"""Theme tokens and preference persistence for the desktop application."""

from __future__ import annotations

from dataclasses import dataclass
import json
import os
from pathlib import Path


@dataclass(frozen=True)
class Palette:
    canvas: str
    panel: str
    panel_alt: str
    ink: str
    muted: str
    border: str
    accent: str
    accent_hover: str
    success: str
    danger: str
    selection: str


PALETTES = {
    "light": Palette(
        canvas="#F3F6FC",
        panel="#FFFFFF",
        panel_alt="#EAF0FB",
        ink="#13213C",
        muted="#61708F",
        border="#D7DFED",
        accent="#315BE8",
        accent_hover="#2449C7",
        success="#178F70",
        danger="#C44550",
        selection="#DCE6FF",
    ),
    "dark": Palette(
        canvas="#0E1422",
        panel="#171F31",
        panel_alt="#202B41",
        ink="#EFF4FF",
        muted="#94A2BD",
        border="#2A3750",
        accent="#6B8BFF",
        accent_hover="#87A0FF",
        success="#4BD3A6",
        danger="#FF7D88",
        selection="#293B67",
    ),
}


def default_preferences_path() -> Path:
    explicit = os.environ.get("MARKITDOWN_GUI_CONFIG_DIR")
    if explicit:
        root = Path(explicit)
    elif os.name == "nt" and os.environ.get("APPDATA"):
        root = Path(os.environ["APPDATA"]) / "MarkItDown"
    else:
        root = Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config")) / "markitdown"
    return root / "preferences.json"


class ThemePreference:
    """Load and save the user's color mode without making startup fragile."""

    def __init__(self, path: Path | None = None) -> None:
        self.path = path or default_preferences_path()

    def load(self) -> str:
        try:
            value = json.loads(self.path.read_text(encoding="utf-8")).get("theme")
        except (OSError, ValueError, AttributeError):
            return "light"
        return value if value in PALETTES else "light"

    def save(self, theme: str) -> None:
        if theme not in PALETTES:
            raise ValueError(f"Unknown theme: {theme}")
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_suffix(".tmp")
        temporary.write_text(json.dumps({"theme": theme}, indent=2), encoding="utf-8")
        temporary.replace(self.path)
