"""Tk desktop application for visible, responsive MarkItDown batch conversion."""

from __future__ import annotations

from importlib.resources import files as resource_files
import ctypes
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from typing import Iterable

from .conversion import (
    BatchConverter,
    ConversionEvent,
    SUPPORTED_EXTENSIONS,
    format_bytes,
    format_duration,
)
from .theme import PALETTES, ThemePreference


class MarkItDownApp:
    POLL_INTERVAL_MS = 70

    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("MarkItDown — Desktop converter")
        self.root.geometry("1180x790")
        self.root.minsize(900, 650)
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

        self.preference = ThemePreference()
        self.theme = self.preference.load()
        self.style = ttk.Style(self.root)
        self.style.theme_use("clam")

        self.sources: list[Path] = []
        self.file_items: dict[str, str] = {}
        self.output_files: dict[str, Path] = {}
        self.events: queue.Queue[ConversionEvent | tuple[str, str]] = queue.Queue()
        self.stop_event = threading.Event()
        self.worker: threading.Thread | None = None
        self.run_started_at: float | None = None
        self.running = False

        documents = Path.home() / "Documents"
        self.output_var = tk.StringVar(value=str(documents / "MarkItDown"))
        self.file_count_var = tk.StringVar(value="No files in the queue")
        self.percent_var = tk.StringVar(value="0%")
        self.status_var = tk.StringVar(value="Ready when you are")
        self.completed_var = tk.StringVar(value="0 / 0 files")
        self.elapsed_var = tk.StringVar(value="00:00.0")

        self._load_icon()
        self._build_interface()
        self._apply_theme(persist=False)
        self._bind_shortcuts()
        self.root.after(self.POLL_INTERVAL_MS, self._drain_events)

    def _load_icon(self) -> None:
        self.app_icon = None
        try:
            assets = resource_files("markitdown_gui").joinpath("assets")
            png_asset = assets.joinpath("markitdown-app-64.png")
            self.app_icon = tk.PhotoImage(file=str(png_asset))
            if os.name == "nt":
                ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(  # type: ignore[attr-defined]
                    "Microsoft.MarkItDown.Desktop"
                )
                ico_asset = str(assets.joinpath("markitdown-app.ico"))

                def apply_form_icon() -> None:
                    self.root.iconbitmap(ico_asset)
                    self.root.iconbitmap(default=ico_asset)

                apply_form_icon()
                self.root.after_idle(apply_form_icon)
            else:
                self.root.iconphoto(True, self.app_icon)
        except (OSError, tk.TclError):
            self.app_icon = None

    def _build_interface(self) -> None:
        self.root.grid_columnconfigure(0, weight=1)
        self.root.grid_rowconfigure(1, weight=1)

        header = ttk.Frame(self.root, style="App.TFrame", padding=(26, 17, 26, 14))
        header.grid(row=0, column=0, sticky="ew")
        header.grid_columnconfigure(2, weight=1)

        if self.app_icon is not None:
            ttk.Label(header, image=self.app_icon, style="App.TLabel").grid(
                row=0, column=0, rowspan=2, sticky="w", padx=(0, 12)
            )

        ttk.Label(header, text="MarkItDown", style="Brand.TLabel").grid(
            row=0, column=1, sticky="sw"
        )
        ttk.Label(
            header,
            text="Turn working files into useful Markdown",
            style="AppMeta.TLabel",
        ).grid(row=1, column=1, sticky="nw")

        self.theme_button = ttk.Button(
            header,
            text="Dark mode",
            style="Theme.TButton",
            command=self._toggle_theme,
        )
        self.theme_button.grid(row=0, column=3, rowspan=2, sticky="e")

        main = ttk.Frame(self.root, style="App.TFrame", padding=(26, 8, 26, 25))
        main.grid(row=1, column=0, sticky="nsew")
        main.grid_columnconfigure(0, weight=1)
        main.grid_rowconfigure(0, weight=3)
        main.grid_rowconfigure(1, weight=2)

        self.queue_card = ttk.Frame(main, style="Card.TFrame", padding=18)
        self.queue_card.grid(row=0, column=0, sticky="nsew", pady=(0, 14))
        self.queue_card.grid_columnconfigure(0, weight=1)
        self.queue_card.grid_rowconfigure(2, weight=1)
        self._build_queue_card(self.queue_card)

        lower = ttk.Frame(main, style="App.TFrame")
        lower.grid(row=1, column=0, sticky="nsew")
        lower.grid_rowconfigure(0, weight=1)
        lower.grid_columnconfigure(0, weight=5)
        lower.grid_columnconfigure(1, weight=6)

        self.progress_card = ttk.Frame(lower, style="Card.TFrame", padding=18)
        self.progress_card.grid(row=0, column=0, sticky="nsew", padx=(0, 7))
        self.progress_card.grid_columnconfigure(1, weight=1)
        self._build_progress_card(self.progress_card)

        self.ledger_card = ttk.Frame(lower, style="Card.TFrame", padding=18)
        self.ledger_card.grid(row=0, column=1, sticky="nsew", padx=(7, 0))
        self.ledger_card.grid_columnconfigure(0, weight=1)
        self.ledger_card.grid_rowconfigure(1, weight=1)
        self._build_ledger_card(self.ledger_card)

    def _build_queue_card(self, parent: ttk.Frame) -> None:
        heading = ttk.Frame(parent, style="CardBody.TFrame")
        heading.grid(row=0, column=0, sticky="ew", pady=(0, 13))
        heading.grid_columnconfigure(1, weight=1)

        title_group = ttk.Frame(heading, style="CardBody.TFrame")
        title_group.grid(row=0, column=0, sticky="w")
        ttk.Label(title_group, text="Source queue", style="CardTitle.TLabel").grid(
            row=0, column=0, sticky="w"
        )
        ttk.Label(
            title_group,
            text="Files are converted in this order",
            style="CardMeta.TLabel",
        ).grid(row=1, column=0, sticky="w")

        actions = ttk.Frame(heading, style="CardBody.TFrame")
        actions.grid(row=0, column=2, sticky="e")
        self.add_button = ttk.Button(
            actions, text="Add files", style="Secondary.TButton", command=self._add_files
        )
        self.add_button.grid(row=0, column=0, padx=(0, 7))
        self.folder_button = ttk.Button(
            actions, text="Add folder", style="Secondary.TButton", command=self._add_folder
        )
        self.folder_button.grid(row=0, column=1, padx=(0, 7))
        self.remove_button = ttk.Button(
            actions,
            text="Remove",
            style="Secondary.TButton",
            command=self._remove_selected,
        )
        self.remove_button.grid(row=0, column=2, padx=(0, 7))
        self.clear_button = ttk.Button(
            actions, text="Clear", style="Secondary.TButton", command=self._clear_sources
        )
        self.clear_button.grid(row=0, column=3, padx=(0, 14))
        self.convert_button = ttk.Button(
            actions,
            text="Convert all",
            style="Primary.TButton",
            command=self._start_conversion,
        )
        self.convert_button.grid(row=0, column=4)

        table_frame = ttk.Frame(parent, style="CardBody.TFrame")
        table_frame.grid(row=2, column=0, sticky="nsew")
        table_frame.grid_columnconfigure(0, weight=1)
        table_frame.grid_rowconfigure(0, weight=1)

        columns = ("name", "type", "size", "status", "time")
        self.file_tree = ttk.Treeview(
            table_frame,
            columns=columns,
            show="headings",
            selectmode="extended",
            style="Queue.Treeview",
        )
        self.file_tree.heading("name", text="FILE")
        self.file_tree.heading("type", text="TYPE")
        self.file_tree.heading("size", text="SIZE")
        self.file_tree.heading("status", text="STATUS")
        self.file_tree.heading("time", text="TIME")
        self.file_tree.column("name", minwidth=260, width=470, stretch=True)
        self.file_tree.column("type", minwidth=65, width=75, stretch=False, anchor="center")
        self.file_tree.column("size", minwidth=80, width=90, stretch=False, anchor="e")
        self.file_tree.column("status", minwidth=110, width=130, stretch=False)
        self.file_tree.column("time", minwidth=65, width=75, stretch=False, anchor="e")
        self.file_tree.grid(row=0, column=0, sticky="nsew")
        scrollbar = ttk.Scrollbar(table_frame, orient="vertical", command=self.file_tree.yview)
        scrollbar.grid(row=0, column=1, sticky="ns")
        self.file_tree.configure(yscrollcommand=scrollbar.set)
        self.file_tree.bind("<Button-3>", self._show_file_context_menu)

        self.file_context_menu = tk.Menu(
            self.root,
            tearoff=False,
            borderwidth=1,
            relief="solid",
            font=("Segoe UI", 9),
        )
        self.file_context_menu.add_command(
            label="Open output file",
            command=self._open_selected_outputs,
        )
        self.file_context_menu.add_separator()
        self.file_context_menu.add_command(
            label="Remove File",
            command=self._remove_selected,
        )

        footer = ttk.Frame(parent, style="CardBody.TFrame")
        footer.grid(row=3, column=0, sticky="ew", pady=(13, 0))
        footer.grid_columnconfigure(1, weight=1)
        ttk.Label(footer, textvariable=self.file_count_var, style="CardMeta.TLabel").grid(
            row=0, column=0, sticky="w", padx=(0, 20)
        )
        ttk.Label(footer, text="Output folder", style="FieldLabel.TLabel").grid(
            row=0, column=2, sticky="e", padx=(0, 8)
        )
        self.output_entry = ttk.Entry(footer, textvariable=self.output_var, width=36)
        self.output_entry.grid(row=0, column=3, sticky="ew", padx=(0, 7))
        self.output_button = ttk.Button(
            footer,
            text="Browse",
            style="Secondary.TButton",
            command=self._choose_output,
        )
        self.output_button.grid(row=0, column=4)

    def _build_progress_card(self, parent: ttk.Frame) -> None:
        ttk.Label(parent, text="Conversion run", style="CardTitle.TLabel").grid(
            row=0, column=0, sticky="w"
        )
        self.cancel_button = ttk.Button(
            parent,
            text="Stop after current",
            style="Quiet.TButton",
            command=self._cancel_conversion,
            state="disabled",
        )
        self.cancel_button.grid(row=0, column=1, sticky="e")
        ttk.Label(parent, textvariable=self.percent_var, style="Percent.TLabel").grid(
            row=1, column=0, sticky="sw", pady=(7, 0)
        )
        ttk.Label(
            parent,
            textvariable=self.status_var,
            style="RunStatus.TLabel",
            anchor="e",
        ).grid(row=1, column=1, sticky="sew", padx=(16, 0), pady=(7, 5))

        self.progress = ttk.Progressbar(
            parent,
            orient="horizontal",
            mode="determinate",
            maximum=100,
            value=0,
            style="Run.Horizontal.TProgressbar",
        )
        self.progress.grid(row=2, column=0, columnspan=2, sticky="ew", pady=(6, 14))

        metrics = ttk.Frame(parent, style="CardBody.TFrame")
        metrics.grid(row=3, column=0, columnspan=2, sticky="ew")
        metrics.grid_columnconfigure(1, weight=1)
        ttk.Label(metrics, text="PROCESSED", style="UtilityLabel.TLabel").grid(
            row=0, column=0, sticky="w"
        )
        ttk.Label(metrics, text="ELAPSED", style="UtilityLabel.TLabel").grid(
            row=0, column=2, sticky="e"
        )
        ttk.Label(metrics, textvariable=self.completed_var, style="Metric.TLabel").grid(
            row=1, column=0, sticky="w"
        )
        ttk.Label(metrics, textvariable=self.elapsed_var, style="Metric.TLabel").grid(
            row=1, column=2, sticky="e"
        )

    def _build_ledger_card(self, parent: ttk.Frame) -> None:
        heading = ttk.Frame(parent, style="CardBody.TFrame")
        heading.grid(row=0, column=0, sticky="ew", pady=(0, 10))
        heading.grid_columnconfigure(0, weight=1)
        ttk.Label(heading, text="Activity ledger", style="CardTitle.TLabel").grid(
            row=0, column=0, sticky="w"
        )
        self.clear_log_button = ttk.Button(
            heading,
            text="Clear log",
            style="Quiet.TButton",
            command=self._clear_log,
        )
        self.clear_log_button.grid(row=0, column=1, sticky="e")

        ledger_frame = ttk.Frame(parent, style="CardBody.TFrame")
        ledger_frame.grid(row=1, column=0, sticky="nsew")
        ledger_frame.grid_columnconfigure(0, weight=1)
        ledger_frame.grid_rowconfigure(0, weight=1)
        self.ledger = tk.Text(
            ledger_frame,
            height=9,
            wrap="word",
            borderwidth=0,
            highlightthickness=0,
            padx=12,
            pady=10,
            font=("Cascadia Mono", 9),
            state="disabled",
            cursor="arrow",
        )
        self.ledger.grid(row=0, column=0, sticky="nsew")
        scrollbar = ttk.Scrollbar(ledger_frame, orient="vertical", command=self.ledger.yview)
        scrollbar.grid(row=0, column=1, sticky="ns")
        self.ledger.configure(yscrollcommand=scrollbar.set)
        self._log("Queue is ready. Add files to begin.", "muted", elapsed=0)

    def _bind_shortcuts(self) -> None:
        self.root.bind("<Control-o>", lambda _event: self._add_files())
        self.root.bind("<Control-Shift-O>", lambda _event: self._add_folder())
        self.root.bind("<Control-Return>", lambda _event: self._start_conversion())
        self.root.bind("<Control-d>", lambda _event: self._toggle_theme())
        self.file_tree.bind("<Delete>", lambda _event: self._remove_selected())

    def _apply_theme(self, *, persist: bool = True) -> None:
        palette = PALETTES[self.theme]
        self.root.configure(background=palette.canvas)

        self.style.configure("App.TFrame", background=palette.canvas)
        self.style.configure(
            "Card.TFrame",
            background=palette.panel,
            bordercolor=palette.border,
            borderwidth=1,
            relief="solid",
        )
        self.style.configure("CardBody.TFrame", background=palette.panel)
        self.style.configure("App.TLabel", background=palette.canvas, foreground=palette.ink)
        self.style.configure(
            "Brand.TLabel",
            background=palette.canvas,
            foreground=palette.ink,
            font=("Georgia", 20, "bold"),
        )
        self.style.configure(
            "AppMeta.TLabel",
            background=palette.canvas,
            foreground=palette.muted,
            font=("Segoe UI", 9),
        )
        self.style.configure("Card.TLabel", background=palette.panel, foreground=palette.ink)
        self.style.configure(
            "CardTitle.TLabel",
            background=palette.panel,
            foreground=palette.ink,
            font=("Segoe UI Semibold", 11),
        )
        self.style.configure(
            "CardMeta.TLabel",
            background=palette.panel,
            foreground=palette.muted,
            font=("Segoe UI", 9),
        )
        self.style.configure(
            "FieldLabel.TLabel",
            background=palette.panel,
            foreground=palette.muted,
            font=("Segoe UI Semibold", 9),
        )
        self.style.configure(
            "Percent.TLabel",
            background=palette.panel,
            foreground=palette.accent,
            font=("Georgia", 29, "bold"),
        )
        self.style.configure(
            "RunStatus.TLabel",
            background=palette.panel,
            foreground=palette.muted,
            font=("Segoe UI", 9),
        )
        self.style.configure(
            "UtilityLabel.TLabel",
            background=palette.panel,
            foreground=palette.muted,
            font=("Cascadia Mono", 8, "bold"),
        )
        self.style.configure(
            "Metric.TLabel",
            background=palette.panel,
            foreground=palette.ink,
            font=("Cascadia Mono", 10, "bold"),
        )

        self.style.configure(
            "Primary.TButton",
            background=palette.accent,
            foreground="#FFFFFF",
            bordercolor=palette.accent,
            focuscolor=palette.accent,
            padding=(13, 8),
            font=("Segoe UI Semibold", 9),
        )
        self.style.map(
            "Primary.TButton",
            background=[("active", palette.accent_hover), ("disabled", palette.border)],
            bordercolor=[("active", palette.accent_hover), ("focus", palette.ink)],
            foreground=[("disabled", palette.muted)],
        )
        self.style.configure(
            "Secondary.TButton",
            background=palette.panel_alt,
            foreground=palette.ink,
            bordercolor=palette.border,
            focuscolor=palette.border,
            padding=(11, 7),
            font=("Segoe UI", 9),
        )
        self.style.map(
            "Secondary.TButton",
            background=[("active", palette.selection), ("disabled", palette.panel_alt)],
            foreground=[("disabled", palette.muted)],
            bordercolor=[("focus", palette.accent)],
        )
        self.style.configure(
            "Theme.TButton",
            background=palette.panel,
            foreground=palette.ink,
            bordercolor=palette.border,
            padding=(12, 7),
            font=("Segoe UI", 9),
        )
        self.style.map(
            "Theme.TButton",
            background=[("active", palette.panel_alt)],
            bordercolor=[("focus", palette.accent)],
        )
        self.style.configure(
            "Quiet.TButton",
            background=palette.panel,
            foreground=palette.muted,
            borderwidth=0,
            padding=(7, 4),
            font=("Segoe UI", 8),
        )
        self.style.map(
            "Quiet.TButton",
            foreground=[("active", palette.accent)],
            background=[("active", palette.panel_alt)],
        )
        self.style.configure(
            "TEntry",
            fieldbackground=palette.panel_alt,
            foreground=palette.ink,
            insertcolor=palette.ink,
            bordercolor=palette.border,
            lightcolor=palette.border,
            darkcolor=palette.border,
            padding=7,
        )
        self.style.map("TEntry", bordercolor=[("focus", palette.accent)])
        self.style.configure(
            "Queue.Treeview",
            background=palette.panel,
            fieldbackground=palette.panel,
            foreground=palette.ink,
            bordercolor=palette.border,
            rowheight=31,
            font=("Segoe UI", 9),
        )
        self.style.map(
            "Queue.Treeview",
            background=[("selected", palette.selection)],
            foreground=[("selected", palette.ink)],
        )
        self.style.configure(
            "Queue.Treeview.Heading",
            background=palette.panel_alt,
            foreground=palette.muted,
            bordercolor=palette.border,
            relief="flat",
            padding=(8, 7),
            font=("Cascadia Mono", 8, "bold"),
        )
        self.style.map("Queue.Treeview.Heading", background=[("active", palette.selection)])
        self.style.configure(
            "Run.Horizontal.TProgressbar",
            troughcolor=palette.panel_alt,
            background=palette.accent,
            bordercolor=palette.panel_alt,
            lightcolor=palette.accent,
            darkcolor=palette.accent,
            thickness=10,
        )

        self.file_tree.tag_configure("ready", foreground=palette.muted)
        self.file_tree.tag_configure("working", foreground=palette.accent)
        self.file_tree.tag_configure("success", foreground=palette.success)
        self.file_tree.tag_configure("error", foreground=palette.danger)
        self.ledger.configure(
            background=palette.panel_alt,
            foreground=palette.ink,
            insertbackground=palette.ink,
            selectbackground=palette.selection,
            selectforeground=palette.ink,
        )
        self.ledger.tag_configure("muted", foreground=palette.muted)
        self.ledger.tag_configure("info", foreground=palette.ink)
        self.ledger.tag_configure("working", foreground=palette.accent)
        self.ledger.tag_configure("success", foreground=palette.success)
        self.ledger.tag_configure("error", foreground=palette.danger)
        self.file_context_menu.configure(
            background=palette.panel,
            foreground=palette.ink,
            activebackground=palette.accent,
            activeforeground="#FFFFFF",
            disabledforeground=palette.muted,
            selectcolor=palette.accent,
        )

        self.theme_button.configure(text="Light mode" if self.theme == "dark" else "Dark mode")
        if persist:
            try:
                self.preference.save(self.theme)
            except OSError:
                pass

    def _toggle_theme(self) -> None:
        self.theme = "dark" if self.theme == "light" else "light"
        self._apply_theme()

    @staticmethod
    def _source_key(path: Path) -> str:
        return os.path.normcase(str(path.resolve()))

    def _selected_source_keys(self) -> list[str]:
        selected = set(self.file_tree.selection())
        return [key for key, item in self.file_items.items() if item in selected]

    def _show_file_context_menu(self, event: tk.Event) -> str:
        row = self.file_tree.identify_row(event.y)
        if not row:
            return "break"
        if row not in self.file_tree.selection():
            self.file_tree.selection_set(row)
        self.file_tree.focus(row)

        has_output = any(
            (output := self.output_files.get(key)) is not None and output.is_file()
            for key in self._selected_source_keys()
        )
        self.file_context_menu.entryconfigure(
            "Open output file",
            state="normal" if has_output else "disabled",
        )
        self.file_context_menu.entryconfigure(
            "Remove File",
            state="disabled" if self.running else "normal",
        )
        try:
            self.file_context_menu.tk_popup(event.x_root, event.y_root)
        finally:
            self.file_context_menu.grab_release()
        return "break"

    def _open_selected_outputs(self) -> None:
        outputs = [
            output
            for key in self._selected_source_keys()
            if (output := self.output_files.get(key)) is not None and output.is_file()
        ]
        if not outputs:
            messagebox.showinfo(
                "No output file",
                "Convert the selected file before opening its Markdown output.",
                parent=self.root,
            )
            return

        failures: list[str] = []
        for output in dict.fromkeys(outputs):
            try:
                self._open_with_default_application(output)
                self._log(f"Opened {output.name}.", "info", elapsed=0)
            except OSError as exc:
                failures.append(f"{output.name}: {exc}")
        if failures:
            messagebox.showerror(
                "Could not open output",
                "\n".join(failures),
                parent=self.root,
            )

    @staticmethod
    def _open_with_default_application(path: Path) -> None:
        if os.name == "nt":
            os.startfile(str(path))  # type: ignore[attr-defined]
        elif sys.platform == "darwin":
            subprocess.Popen(["open", str(path)])
        else:
            subprocess.Popen(["xdg-open", str(path)])

    def _add_files(self) -> None:
        if self.running:
            return
        patterns = " ".join(f"*{extension}" for extension in sorted(SUPPORTED_EXTENSIONS))
        selected = filedialog.askopenfilenames(
            parent=self.root,
            title="Choose files to convert",
            filetypes=(("Supported files", patterns), ("All files", "*.*")),
        )
        self._append_sources(Path(path) for path in selected)

    def _add_folder(self) -> None:
        if self.running:
            return
        selected = filedialog.askdirectory(parent=self.root, title="Choose a folder")
        if not selected:
            return
        folder = Path(selected)
        candidates = (
            path
            for path in sorted(folder.rglob("*"), key=lambda value: str(value).lower())
            if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS
        )
        self._append_sources(candidates)

    def _append_sources(self, paths: Iterable[Path]) -> None:
        added = 0
        for path in paths:
            resolved = path.resolve()
            key = self._source_key(resolved)
            if key in self.file_items or not resolved.is_file():
                continue
            try:
                size = format_bytes(resolved.stat().st_size)
            except OSError:
                size = "—"
            item = self.file_tree.insert(
                "",
                "end",
                values=(resolved.name, resolved.suffix.lstrip(".").upper() or "FILE", size, "Ready", "—"),
                tags=("ready",),
            )
            self.sources.append(resolved)
            self.file_items[key] = item
            added += 1
        self._update_file_count()
        if added:
            self._log(f"Added {added} file{'s' if added != 1 else ''} to the queue.", "info", elapsed=0)

    def _remove_selected(self) -> None:
        if self.running:
            return
        selected = set(self.file_tree.selection())
        if not selected:
            return
        removed_keys = [
            key for key, item in self.file_items.items() if item in selected
        ]
        self.sources = [
            source
            for source in self.sources
            if self.file_items.get(self._source_key(source)) not in selected
        ]
        for item in selected:
            self.file_tree.delete(item)
        self.file_items = {
            self._source_key(source): item
            for source in self.sources
            if (item := self.file_items.get(self._source_key(source))) is not None
        }
        for key in removed_keys:
            self.output_files.pop(key, None)
        self._update_file_count()
        self._log(
            f"Removed {len(removed_keys)} file{'s' if len(removed_keys) != 1 else ''} from the queue.",
            "muted",
            elapsed=0,
        )

    def _clear_sources(self) -> None:
        if self.running:
            return
        self.sources.clear()
        self.file_items.clear()
        self.output_files.clear()
        self.file_tree.delete(*self.file_tree.get_children())
        self._update_file_count()
        self._reset_run_display()

    def _update_file_count(self) -> None:
        count = len(self.sources)
        self.file_count_var.set(
            "No files in the queue" if count == 0 else f"{count} file{'s' if count != 1 else ''} in the queue"
        )
        self.completed_var.set(f"0 / {count} files")

    def _choose_output(self) -> None:
        selected = filedialog.askdirectory(
            parent=self.root,
            title="Choose where Markdown files are saved",
            initialdir=self.output_var.get(),
        )
        if selected:
            self.output_var.set(selected)

    def _start_conversion(self) -> None:
        if self.running:
            return
        if not self.sources:
            messagebox.showinfo("No files selected", "Add at least one file before converting.", parent=self.root)
            return
        output_text = self.output_var.get().strip()
        if not output_text:
            messagebox.showinfo("Choose an output folder", "Choose where the Markdown files should be saved.", parent=self.root)
            return

        self.running = True
        self.stop_event.clear()
        self.run_started_at = time.perf_counter()
        self._clear_log()
        self._log("Loading the conversion engine…", "working", elapsed=0)
        self.progress.configure(value=0)
        self.percent_var.set("0%")
        self.status_var.set("Starting conversion…")
        self.completed_var.set(f"0 / {len(self.sources)} files")
        for source in self.sources:
            self.output_files.pop(self._source_key(source), None)
            self._set_file_state(source, "Queued", "—", "ready")
        self._set_running_controls(True)

        sources = list(self.sources)
        output_directory = Path(output_text).expanduser()
        self.worker = threading.Thread(
            target=self._run_conversion,
            args=(sources, output_directory),
            name="markitdown-conversion",
            daemon=True,
        )
        self.worker.start()
        self._tick_elapsed()

    def _run_conversion(self, sources: list[Path], output_directory: Path) -> None:
        try:
            from markitdown import MarkItDown

            engine = MarkItDown()
            batch = BatchConverter(lambda source: engine.convert(str(source)).markdown)
            batch.run(sources, output_directory, self.events.put, self.stop_event)
        except Exception as exc:
            self.events.put(("fatal", str(exc)))

    def _cancel_conversion(self) -> None:
        if not self.running or self.stop_event.is_set():
            return
        self.stop_event.set()
        self.cancel_button.configure(state="disabled")
        self.status_var.set("Stopping after the current file…")
        elapsed = time.perf_counter() - (self.run_started_at or time.perf_counter())
        self._log("Stop requested. The current file will finish safely.", "working", elapsed)

    def _drain_events(self) -> None:
        try:
            while True:
                event = self.events.get_nowait()
                if isinstance(event, tuple):
                    self._handle_fatal_error(event[1])
                else:
                    self._handle_event(event)
        except queue.Empty:
            pass
        finally:
            self.root.after(self.POLL_INTERVAL_MS, self._drain_events)

    def _handle_event(self, event: ConversionEvent) -> None:
        self.progress.configure(value=event.percent)
        self.percent_var.set(f"{round(event.percent):d}%")
        self.completed_var.set(f"{event.completed} / {event.total} files")

        tag = "info"
        if event.kind in {"file_started", "file_converting", "file_writing"}:
            tag = "working"
        elif event.kind in {"file_succeeded", "batch_finished"}:
            tag = "success"
        elif event.kind == "file_failed":
            tag = "error"
        elif event.kind == "batch_cancelled":
            tag = "muted"
        self._log(event.message, tag, event.elapsed)

        if event.source is not None:
            if event.kind == "file_started":
                self._set_file_state(event.source, "Preparing", "—", "working")
            elif event.kind == "file_converting":
                self._set_file_state(event.source, "Converting", "—", "working")
            elif event.kind == "file_writing":
                self._set_file_state(event.source, "Saving", "—", "working")
            elif event.kind == "file_succeeded":
                duration = format_duration(event.file_elapsed or 0)
                if event.output is not None:
                    self.output_files[self._source_key(event.source)] = event.output
                self._set_file_state(event.source, "Done", duration, "success")
            elif event.kind == "file_failed":
                duration = format_duration(event.file_elapsed or 0)
                self._set_file_state(event.source, "Failed", duration, "error")

        if event.kind == "batch_started":
            self.status_var.set("Preparing the first file…")
        elif event.kind == "file_started" and event.source:
            self.status_var.set(f"Preparing {event.source.name}")
        elif event.kind == "file_converting" and event.source:
            self.status_var.set(f"Converting {event.source.name}")
        elif event.kind == "file_writing" and event.output:
            self.status_var.set(f"Saving {event.output.name}")
        elif event.kind == "file_failed" and event.source:
            self.status_var.set(f"Could not convert {event.source.name}")
        elif event.kind == "batch_finished":
            self.status_var.set("Conversion complete")
            self.elapsed_var.set(self._clock_text(event.elapsed))
            self._finish_run()
        elif event.kind == "batch_cancelled":
            self.status_var.set("Conversion stopped")
            self._finish_run()

    def _handle_fatal_error(self, message: str) -> None:
        elapsed = time.perf_counter() - (self.run_started_at or time.perf_counter())
        self._log(f"Conversion could not start: {message}", "error", elapsed)
        self.status_var.set("Conversion could not start")
        self._finish_run()
        messagebox.showerror(
            "Conversion could not start",
            f"{message}\n\nInstall MarkItDown and the dependencies for the selected file types, then try again.",
            parent=self.root,
        )

    def _set_file_state(self, source: Path, status: str, duration: str, tag: str) -> None:
        item = self.file_items.get(self._source_key(source))
        if item is None or not self.file_tree.exists(item):
            return
        values = list(self.file_tree.item(item, "values"))
        values[3] = status
        values[4] = duration
        self.file_tree.item(item, values=values, tags=(tag,))
        self.file_tree.see(item)

    def _set_running_controls(self, running: bool) -> None:
        normal_or_disabled = "disabled" if running else "normal"
        for control in (
            self.add_button,
            self.folder_button,
            self.remove_button,
            self.clear_button,
            self.output_entry,
            self.output_button,
            self.convert_button,
        ):
            control.configure(state=normal_or_disabled)
        self.cancel_button.configure(state="normal" if running else "disabled")

    def _finish_run(self) -> None:
        self.running = False
        self.run_started_at = None
        self._set_running_controls(False)

    def _tick_elapsed(self) -> None:
        if not self.running or self.run_started_at is None:
            return
        self.elapsed_var.set(self._clock_text(time.perf_counter() - self.run_started_at))
        self.root.after(100, self._tick_elapsed)

    @staticmethod
    def _clock_text(seconds: float) -> str:
        minutes, seconds = divmod(max(0.0, seconds), 60)
        hours, minutes = divmod(int(minutes), 60)
        if hours:
            return f"{hours:02d}:{minutes:02d}:{seconds:04.1f}"
        return f"{minutes:02d}:{seconds:04.1f}"

    def _log(self, message: str, tag: str, elapsed: float) -> None:
        timestamp = self._clock_text(elapsed)
        self.ledger.configure(state="normal")
        self.ledger.insert("end", f"{timestamp}  ", "muted")
        self.ledger.insert("end", f"{message}\n", tag)
        self.ledger.configure(state="disabled")
        self.ledger.see("end")

    def _clear_log(self) -> None:
        self.ledger.configure(state="normal")
        self.ledger.delete("1.0", "end")
        self.ledger.configure(state="disabled")

    def _reset_run_display(self) -> None:
        self.progress.configure(value=0)
        self.percent_var.set("0%")
        self.status_var.set("Ready when you are")
        self.elapsed_var.set("00:00.0")

    def _on_close(self) -> None:
        if self.running:
            should_close = messagebox.askyesno(
                "Close MarkItDown?",
                "A conversion is still running. Close the app and stop after the current file?",
                parent=self.root,
            )
            if not should_close:
                return
            self.stop_event.set()
        self.root.destroy()


def main() -> None:
    root = tk.Tk()
    MarkItDownApp(root)
    root.mainloop()
