use gpui_kit::component::{
    ActiveTheme, Disableable, Theme, ThemeMode, WindowExt,
    button::{Button, ButtonVariants},
    progress::Progress,
    text::TextView,
};
use gpui_kit::*;
use markitdown_desktop::{
    conversion::{self, *},
    runtime::Runtime,
};
use std::{
    path::PathBuf,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant},
};

// Shared sizing keeps every toolbar label centered with equal top/bottom space.
fn control_button(id: impl Into<ElementId>) -> Button {
    Button::new(id)
        .h(px(36.))
        .px(px(12.))
        .py(px(6.))
        .text_size(px(14.))
        .line_height(px(20.))
        .font_family("Segoe UI")
        .flex_shrink_0()
}

fn control_row() -> Div {
    div()
        .flex()
        .items_center()
        .h(px(60.))
        .flex_shrink_0()
        .gap_2()
        .px_4()
        .py(px(10.))
}

fn show_help(window: &mut Window, cx: &mut App) {
    if window.has_active_dialog(cx) {
        return;
    }
    window.open_dialog(cx, |dialog, _, _| {
        dialog
            .title("How to use MarkItDown")
            .width(px(720.))
            .close_button(true)
            .keyboard(true)
            .child(
                div()
                    .h(px(360.))
                    .w_full()
                    .font_family("Myanmar Text")
                    .child(
                        TextView::markdown("how-to-use-guide", include_str!("../HOW_TO_USE.md"))
                            .scrollable(true)
                            .h_full()
                            .w_full(),
                    ),
            )
            .footer(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .gap_3()
                    .child(div().text_sm().child("F1: Help · Esc: Close"))
                    .child(
                        control_button("close-help")
                            .primary()
                            .label("Close")
                            .on_click(|_, window, cx| window.close_dialog(cx)),
                    ),
            )
    });
}

struct QueueItem {
    source: PathBuf,
    status: String,
    fraction: f32,
    pages: Vec<PageResult>,
    result: Option<DocumentResult>,
    output: Option<PathBuf>,
    // Retain rendered pages until this item leaves the workspace.
    _scratch: Option<Arc<tempfile::TempDir>>,
}

pub struct Workspace {
    queue: Vec<QueueItem>,
    selected: Option<usize>,
    page: usize,
    options: Options,
    runtime: Runtime,
    running: bool,
    dialog_open: bool,
    cancel: Arc<AtomicBool>,
    started: Option<Instant>,
    elapsed: f64,
    message: String,
    log: Vec<String>,
    show_log: bool,
    show_source: bool,
    dark: bool,
    focus: FocusHandle,
    logo: Arc<Image>,
    task: Option<Task<()>>,
    _clock: Task<()>,
}

impl Workspace {
    pub fn new(window: &mut Window, cx: &mut Context<Self>) -> Self {
        Theme::change(ThemeMode::Light, Some(window), cx);
        let mut view = Self {
            queue: vec![],
            selected: None,
            page: 0,
            options: Options::default(),
            runtime: Runtime::discover(),
            running: false,
            dialog_open: false,
            cancel: Arc::new(AtomicBool::new(false)),
            started: None,
            elapsed: 0.,
            message: "Add PDF files to begin.".into(),
            log: vec![],
            show_log: false,
            show_source: false,
            dark: false,
            focus: cx.focus_handle(),
            logo: Arc::new(Image::from_bytes(
                ImageFormat::Png,
                include_bytes!("../assets/markitdown.png").to_vec(),
            )),
            task: None,
            _clock: cx.spawn(async move |this, cx| {
                loop {
                    cx.background_executor()
                        .timer(Duration::from_millis(500))
                        .await;
                    if this
                        .update(cx, |view, cx| {
                            if view.running {
                                view.elapsed = view
                                    .started
                                    .map(|t| t.elapsed().as_secs_f64())
                                    .unwrap_or(0.);
                                cx.notify();
                            }
                        })
                        .is_err()
                    {
                        break;
                    }
                }
            }),
        };
        let files = std::env::args_os().skip(1).map(PathBuf::from).collect();
        view.add_paths(files);
        view.focus.focus(window, cx);
        view
    }

    fn note(&mut self, message: impl Into<String>) {
        let message = message.into();
        let time = self.started.map(|t| t.elapsed().as_secs()).unwrap_or(0);
        self.log
            .push(format!("{:02}:{:02}  {message}", time / 60, time % 60));
        if self.log.len() > 300 {
            self.log.remove(0);
        }
        self.message = message;
    }

    fn add_paths(&mut self, files: Vec<PathBuf>) {
        if self.running || self.dialog_open {
            return;
        }
        let mut rejected = 0;
        for source in files {
            if !source.is_file()
                || !source
                    .extension()
                    .is_some_and(|e| e.eq_ignore_ascii_case("pdf"))
            {
                rejected += 1;
                continue;
            }
            let source = source.canonicalize().unwrap_or(source);
            if self.queue.iter().any(|item| item.source == source) {
                continue;
            }
            self.queue.push(QueueItem {
                source,
                status: "Ready".into(),
                fraction: 0.,
                pages: vec![],
                result: None,
                output: None,
                _scratch: None,
            });
        }
        if self.selected.is_none() && !self.queue.is_empty() {
            self.selected = Some(0);
        }
        if rejected > 0 {
            self.note(format!(
                "Ignored {rejected} item(s): only PDF files are accepted."
            ));
        } else if !self.queue.is_empty() {
            self.note(format!("{} PDF file(s) in queue", self.queue.len()));
        }
    }

    fn pick_files(&mut self, cx: &mut Context<Self>) {
        if self.running || self.dialog_open {
            return;
        }
        self.dialog_open = true;
        cx.spawn(async move |this, cx| {
            let files = rfd::AsyncFileDialog::new()
                .set_title("Add PDF files")
                .add_filter("PDF documents", &["pdf"])
                .pick_files()
                .await;
            let _ = this.update(cx, |view, cx| {
                view.dialog_open = false;
                if let Some(files) = files {
                    view.add_paths(files.into_iter().map(|f| f.path().to_path_buf()).collect());
                }
                cx.notify();
            });
        })
        .detach();
    }

    fn start(&mut self, cx: &mut Context<Self>) {
        if self.running || self.dialog_open || self.queue.is_empty() {
            return;
        }
        let scratch = match tempfile::Builder::new()
            .prefix("markitdown-pages-")
            .tempdir()
        {
            Ok(dir) => Arc::new(dir),
            Err(error) => {
                self.note(format!("Cannot create preview cache: {error}"));
                cx.notify();
                return;
            }
        };
        self.running = true;
        self.cancel = Arc::new(AtomicBool::new(false));
        self.started = Some(Instant::now());
        self.elapsed = 0.;
        self.page = 0;
        self.log.clear();
        self.note("Conversion started · files stay on this computer");
        for item in &mut self.queue {
            item.status = "Queued".into();
            item.fraction = 0.;
            item.pages.clear();
            item.result = None;
            item.output = None;
            item._scratch = Some(scratch.clone());
        }
        let files = self.queue.iter().map(|item| item.source.clone()).collect();
        let runtime = self.runtime.clone();
        let options = self.options.clone();
        let cancel = self.cancel.clone();
        let (send, receive) = async_channel::bounded(8);
        // Native PDF/OCR calls run on a dedicated worker, never the UI thread.
        std::thread::spawn(move || {
            conversion::convert_batch(
                files,
                runtime,
                options,
                scratch.path().to_path_buf(),
                cancel,
                |event| {
                    let _ = send.send_blocking(event);
                },
            );
        });
        self.task = Some(cx.spawn(async move |this, cx| {
            while let Ok(event) = receive.recv().await {
                let finished = matches!(event, Event::Finished { .. });
                if this
                    .update(cx, |view, cx| {
                        view.apply_event(event);
                        cx.notify();
                    })
                    .is_err()
                {
                    break;
                }
                if finished {
                    break;
                }
            }
            let _ = this.update(cx, |view, cx| {
                if view.running {
                    view.running = false;
                    for item in &mut view.queue {
                        if item.result.is_none() && item.status != "Failed" {
                            item.status = "Interrupted".into();
                        }
                    }
                    view.note("Conversion worker stopped unexpectedly; retry or inspect the activity log.");
                    cx.notify();
                }
            });
        }));
        cx.notify();
    }

    fn apply_event(&mut self, event: Event) {
        self.elapsed = self
            .started
            .map(|t| t.elapsed().as_secs_f64())
            .unwrap_or(0.);
        match event {
            Event::Started { index, pages } => {
                self.queue[index].status = format!("Reading · {pages} pages");
                self.note(format!(
                    "{} · {pages} pages",
                    self.queue[index]
                        .source
                        .file_name()
                        .unwrap_or_default()
                        .to_string_lossy()
                ));
            }
            Event::Page {
                index,
                total,
                result,
            } => {
                let method = if result.used_ocr { "OCR" } else { "Text" };
                self.queue[index].status = format!("{method} · {}/{}", result.number, total);
                self.queue[index].fraction = result.number as f32 / total as f32;
                self.note(format!(
                    "File {} · page {}/{} · {method}",
                    index + 1,
                    result.number,
                    total
                ));
                if let Some(warning) = &result.warning {
                    self.note(format!("Page {}: {warning}", result.number));
                }
                self.queue[index].pages.push(result);
            }
            Event::Complete { index, result } => {
                let warnings = result.pages.iter().filter(|p| p.warning.is_some()).count();
                self.queue[index].status = if warnings == 0 {
                    "Done".into()
                } else {
                    format!("Done · {warnings} warnings")
                };
                self.queue[index].fraction = 1.;
                self.note(format!(
                    "File {} completed in {:.1}s",
                    index + 1,
                    result.elapsed.as_secs_f64()
                ));
                self.queue[index].result = Some(result);
            }
            Event::Failed { index, message } => {
                self.queue[index].status = "Failed".into();
                self.queue[index].fraction = 1.;
                self.note(format!("File {} failed: {message}", index + 1));
            }
            Event::Finished { cancelled } => {
                self.running = false;
                for item in &mut self.queue {
                    if item.result.is_none() && item.status != "Failed" {
                        item.status = "Cancelled".into();
                    }
                }
                let succeeded = self
                    .queue
                    .iter()
                    .filter(|item| item.result.is_some())
                    .count();
                let failed = self
                    .queue
                    .iter()
                    .filter(|item| item.status == "Failed")
                    .count();
                self.note(format!(
                    "{} · {succeeded} completed, {failed} failed · {:.1}s",
                    if cancelled { "Cancelled" } else { "Finished" },
                    self.elapsed
                ));
            }
        }
    }

    fn cancel(&mut self, cx: &mut Context<Self>) {
        if self.running {
            self.cancel.store(true, Ordering::Relaxed);
            self.note("Cancelling…");
            cx.notify();
        }
    }

    fn export(&mut self, all: bool, cx: &mut Context<Self>) {
        if self.running || self.dialog_open {
            return;
        }
        let indices: Vec<usize> = if all {
            (0..self.queue.len()).collect()
        } else {
            self.selected.into_iter().collect()
        };
        let documents: Vec<_> = indices
            .into_iter()
            .filter_map(|i| self.queue[i].result.clone().map(|d| (i, d)))
            .collect();
        if documents.is_empty() {
            return;
        }
        self.dialog_open = true;
        cx.spawn(async move |this, cx| {
            let folder = rfd::AsyncFileDialog::new()
                .set_title("Export Markdown into folder")
                .pick_folder()
                .await;
            if let Some(folder) = folder {
                let folder = folder.path().to_path_buf();
                let results = cx
                    .background_spawn(async move {
                        documents
                            .into_iter()
                            .map(|(i, doc)| {
                                (i, export_document(&doc, &folder).map_err(|e| e.to_string()))
                            })
                            .collect::<Vec<_>>()
                    })
                    .await;
                let _ = this.update(cx, |view, cx| {
                    view.dialog_open = false;
                    for (i, result) in results {
                        match result {
                            Ok(path) => {
                                view.note(format!("Saved {}", path.display()));
                                if let Some(item) = view.queue.get_mut(i) {
                                    item.output = Some(path);
                                }
                            }
                            Err(error) => view.note(format!("Export failed: {error}")),
                        }
                    }
                    cx.notify();
                });
            } else {
                let _ = this.update(cx, |view, cx| {
                    view.dialog_open = false;
                    cx.notify();
                });
            }
        })
        .detach();
    }

    fn select(&mut self, index: usize, cx: &mut Context<Self>) {
        self.selected = Some(index);
        self.page = 0;
        cx.notify();
    }

    fn progress(&self) -> f32 {
        if self.queue.is_empty() {
            0.
        } else {
            self.queue.iter().map(|i| i.fraction).sum::<f32>() / self.queue.len() as f32 * 100.
        }
    }

    fn set_mode(&mut self, mode: OcrMode, cx: &mut Context<Self>) {
        if !self.running {
            self.options.mode = mode;
            cx.notify();
        }
    }

    fn toggle_theme(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        self.dark = !self.dark;
        Theme::change(
            if self.dark {
                ThemeMode::Dark
            } else {
                ThemeMode::Light
            },
            Some(window),
            cx,
        );
        cx.notify();
    }

    fn current_page(&self) -> Option<&PageResult> {
        self.selected
            .and_then(|i| self.queue.get(i))
            .and_then(|i| i.pages.get(self.page))
    }

    fn header(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let ready = self.queue.iter().any(|i| i.result.is_some());
        div()
            .flex()
            .items_center()
            .justify_between()
            .px_4()
            .py_3()
            .min_h(px(80.))
            .flex_shrink_0()
            .border_b_1()
            .border_color(cx.theme().border)
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap_3()
                    .child(
                        img(self.logo.clone())
                            .size(px(44.))
                            .flex_shrink_0()
                            .object_fit(ObjectFit::Contain),
                    )
                    .child(
                        div()
                            .flex()
                            .flex_col()
                            .gap_1()
                            .child(
                                div()
                                    .text_xl()
                                    .font_weight(FontWeight::SEMIBOLD)
                                    .child("MarkItDown"),
                            )
                            .child(
                                div()
                                    .text_sm()
                                    .text_color(cx.theme().muted_foreground)
                                    .child("PDF → Markdown · မြန်မာစာ OCR"),
                            ),
                    ),
            )
            .child(
                div()
                    .flex()
                    .gap_2()
                    .items_center()
                    .child(
                        control_button("add")
                            .label("Add PDFs")
                            .disabled(self.running || self.dialog_open)
                            .on_click(cx.listener(|v, _, _, cx| v.pick_files(cx))),
                    )
                    .child(
                        control_button("convert")
                            .primary()
                            .label(if self.running {
                                "Converting…"
                            } else {
                                "Convert all"
                            })
                            .disabled(self.running || self.dialog_open || self.queue.is_empty())
                            .on_click(cx.listener(|v, _, _, cx| v.start(cx))),
                    )
                    .child(
                        control_button("cancel")
                            .label("Cancel")
                            .disabled(!self.running)
                            .on_click(cx.listener(|v, _, _, cx| v.cancel(cx))),
                    )
                    .child(
                        control_button("export-all")
                            .label("Export all")
                            .disabled(!ready || self.running || self.dialog_open)
                            .on_click(cx.listener(|v, _, _, cx| v.export(true, cx))),
                    )
                    .child(
                        control_button("how-to-use")
                            .label("How to use")
                            .disabled(self.dialog_open)
                            .on_click(|_, window, cx| show_help(window, cx)),
                    )
                    .child(
                        control_button("theme")
                            .label(if self.dark { "Light" } else { "Dark" })
                            .on_click(cx.listener(|v, _, w, cx| v.toggle_theme(w, cx))),
                    ),
            )
    }

    fn sidebar(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let rows = self.queue.iter().enumerate().map(|(index, item)| {
            let selected = self.selected == Some(index);
            div()
                .id(("file", index))
                .px_3()
                .py_3()
                .rounded_md()
                .cursor_pointer()
                .bg(if selected {
                    cx.theme().secondary
                } else {
                    cx.theme().background
                })
                .border_1()
                .border_color(if selected {
                    cx.theme().primary
                } else {
                    cx.theme().border
                })
                .on_click(cx.listener(move |view, _, _, cx| view.select(index, cx)))
                .child(
                    div()
                        .text_sm()
                        .font_weight(FontWeight::MEDIUM)
                        .overflow_hidden()
                        .child(
                            item.source
                                .file_name()
                                .unwrap_or_default()
                                .to_string_lossy()
                                .to_string(),
                        ),
                )
                .child(
                    div()
                        .mt_1()
                        .text_xs()
                        .text_color(cx.theme().muted_foreground)
                        .child(item.status.clone()),
                )
                .child(
                    Progress::new(("file-progress", index))
                        .value(item.fraction * 100.)
                        .mt_2(),
                )
        });
        div().flex().flex_col().w(px(250.)).flex_shrink_0().border_r_1().border_color(cx.theme().border)
            .child(control_row().justify_between().border_b_1().border_color(cx.theme().border).child(div().text_sm().font_weight(FontWeight::SEMIBOLD).child(format!("Documents ({})", self.queue.len())))
                .child(control_button("clear").label("Clear").disabled(self.running || self.dialog_open || self.queue.is_empty()).on_click(cx.listener(|v, _, _, cx| { v.queue.clear(); v.selected = None; v.page = 0; v.note("Queue cleared"); cx.notify(); }))))
            .child(div().id("queue").flex_1().min_h_0().overflow_y_scroll().flex().flex_col().gap_2().p_4().children(rows))
            .child(div().flex().flex_col().gap_3().p_4().border_t_1().border_color(cx.theme().border).child(control_button("remove").label("Remove selected").disabled(self.running || self.dialog_open || self.selected.is_none()).on_click(cx.listener(|v, _, _, cx| {
                if let Some(i) = v.selected { v.queue.remove(i); v.selected = if v.queue.is_empty() { None } else { Some(i.min(v.queue.len() - 1)) }; v.page = 0; cx.notify(); }
            })))
            .child(div().text_xs().text_color(cx.theme().muted_foreground).child("Drop PDF files anywhere in this window.\nCtrl+O to add · Ctrl+Enter to convert")))
    }

    fn settings(&self, cx: &mut Context<Self>) -> impl IntoElement {
        control_row()
            .border_b_1()
            .border_color(cx.theme().border)
            .child(
                div()
                    .text_sm()
                    .text_color(cx.theme().muted_foreground)
                    .child("Reading mode"),
            )
            .child(
                control_button("auto")
                    .label(if self.options.mode == OcrMode::Auto {
                        "✓ Auto"
                    } else {
                        "Auto"
                    })
                    .disabled(self.running)
                    .on_click(cx.listener(|v, _, _, cx| v.set_mode(OcrMode::Auto, cx))),
            )
            .child(
                control_button("force")
                    .label(if self.options.mode == OcrMode::Force {
                        "✓ Force OCR"
                    } else {
                        "Force OCR"
                    })
                    .disabled(self.running)
                    .on_click(cx.listener(|v, _, _, cx| v.set_mode(OcrMode::Force, cx))),
            )
            .child(
                control_button("text-only")
                    .label(if self.options.mode == OcrMode::TextOnly {
                        "✓ Text only"
                    } else {
                        "Text only"
                    })
                    .disabled(self.running)
                    .on_click(cx.listener(|v, _, _, cx| v.set_mode(OcrMode::TextOnly, cx))),
            )
            .child(div().flex_1())
            .child(
                control_button("language")
                    .label(self.options.language.label())
                    .disabled(self.running)
                    .on_click(cx.listener(|v, _, _, cx| {
                        v.options.language = match v.options.language {
                            Language::MyanmarEnglish => Language::Myanmar,
                            Language::Myanmar => Language::English,
                            Language::English => Language::MyanmarEnglish,
                        };
                        cx.notify();
                    })),
            )
    }

    fn previews(&self, cx: &mut Context<Self>) -> impl IntoElement {
        let page = self.current_page();
        let count = self
            .selected
            .and_then(|i| self.queue.get(i))
            .map(|i| i.pages.len())
            .unwrap_or(0);
        let navigation = div()
            .flex()
            .items_center()
            .gap_2()
            .child(
                control_button("previous")
                    .label("←")
                    .disabled(self.page == 0)
                    .on_click(cx.listener(|v, _, _, cx| {
                        v.page = v.page.saturating_sub(1);
                        cx.notify();
                    })),
            )
            .child(div().text_sm().child(if count == 0 {
                "No pages yet".into()
            } else {
                format!("{} / {count}", self.page + 1)
            }))
            .child(
                control_button("next")
                    .label("→")
                    .disabled(self.page + 1 >= count)
                    .on_click(cx.listener(|v, _, _, cx| {
                        v.page += 1;
                        cx.notify();
                    })),
            );
        let mut pdf = div()
            .id("pdf-preview")
            .flex_1()
            .min_h_0()
            .overflow_y_scroll()
            .p_4()
            .bg(cx.theme().secondary);
        if let Some(page) = page {
            pdf = pdf.child(
                img(page.preview.clone())
                    .w_full()
                    .object_fit(ObjectFit::Contain),
            );
        } else {
            pdf = pdf.child(
                div().flex().flex_col().gap_3().child("Original PDF").child(
                    div()
                        .text_sm()
                        .text_color(cx.theme().muted_foreground)
                        .child("Convert a document to inspect each rendered page beside its text."),
                ),
            );
        }
        let mut output = div()
            .id("markdown-preview")
            .flex_1()
            .min_h_0()
            .overflow_y_scroll()
            .p_4()
            .font_family("Myanmar Text");
        if let Some(page) = page {
            if self.show_source {
                output = output.child(div().whitespace_normal().child(page.markdown.clone()));
            } else {
                output = output.child(
                    TextView::markdown(("markdown-page", page.number), page.markdown.clone())
                        .on_link_click(|_, _, _, _| {}),
                );
            }
            if let Some(warning) = &page.warning {
                output = output.child(
                    div()
                        .mt_4()
                        .text_sm()
                        .text_color(cx.theme().warning)
                        .child(warning.clone()),
                );
            }
        } else {
            output = output.child(div().flex().flex_col().gap_3().child("Your Markdown appears here")
                .child(div().text_sm().text_color(cx.theme().muted_foreground).child("Text PDFs are read directly. Scanned pages use Myanmar + English OCR. Everything runs locally.")));
        }
        div()
            .flex()
            .flex_1()
            .min_h_0()
            .child(
                div()
                    .flex()
                    .flex_col()
                    .flex_1()
                    .min_w_0()
                    .border_r_1()
                    .border_color(cx.theme().border)
                    .child(
                        control_row()
                            .justify_between()
                            .border_b_1()
                            .border_color(cx.theme().border)
                            .child("SOURCE PDF")
                            .child(navigation),
                    )
                    .child(pdf),
            )
            .child(
                div()
                    .flex()
                    .flex_col()
                    .flex_1()
                    .min_w_0()
                    .child(
                        control_row()
                            .justify_between()
                            .border_b_1()
                            .border_color(cx.theme().border)
                            .child("MARKDOWN")
                            .child(
                                div()
                                    .flex()
                                    .items_center()
                                    .gap_2()
                                    .child(
                                        control_button("source-toggle")
                                            .label(if self.show_source {
                                                "Preview"
                                            } else {
                                                "Source"
                                            })
                                            .on_click(cx.listener(|v, _, _, cx| {
                                                v.show_source = !v.show_source;
                                                cx.notify();
                                            })),
                                    )
                                    .child(
                                        control_button("copy")
                                            .label("Copy page")
                                            .disabled(page.is_none())
                                            .on_click(cx.listener(|v, _, _, cx| {
                                                if let Some(p) = v.current_page() {
                                                    cx.write_to_clipboard(
                                                        ClipboardItem::new_string(
                                                            p.markdown.clone(),
                                                        ),
                                                    );
                                                }
                                            })),
                                    )
                                    .child(
                                        control_button("export")
                                            .label("Export")
                                            .disabled(
                                                self.running
                                                    || self.dialog_open
                                                    || !self.selected.is_some_and(|i| {
                                                        self.queue[i].result.is_some()
                                                    }),
                                            )
                                            .on_click(
                                                cx.listener(|v, _, _, cx| v.export(false, cx)),
                                            ),
                                    ),
                            ),
                    )
                    .child(output),
            )
    }
}

impl Drop for Workspace {
    fn drop(&mut self) {
        self.cancel.store(true, Ordering::Relaxed);
    }
}

impl Render for Workspace {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let mut root = div()
            .id("workspace")
            .track_focus(&self.focus)
            .flex()
            .flex_col()
            .size_full()
            .font_family("Segoe UI")
            .text_color(cx.theme().foreground)
            .bg(cx.theme().background)
            .on_key_down(cx.listener(|v, event: &KeyDownEvent, window, cx| {
                if event.keystroke.key == "f1" {
                    show_help(window, cx);
                    return;
                }
                if event.keystroke.modifiers.control {
                    match event.keystroke.key.as_str() {
                        "o" => v.pick_files(cx),
                        "enter" => v.start(cx),
                        _ => {}
                    }
                }
            }))
            .on_drop::<ExternalPaths>(cx.listener(|v, paths: &ExternalPaths, _, cx| {
                v.add_paths(paths.paths().to_vec());
                cx.notify();
            }))
            .child(self.header(cx))
            .child(
                div()
                    .flex()
                    .flex_1()
                    .min_h_0()
                    .child(self.sidebar(cx))
                    .child(
                        div()
                            .flex()
                            .flex_col()
                            .flex_1()
                            .min_w_0()
                            .child(self.settings(cx))
                            .child(self.previews(cx)),
                    ),
            )
            .child(
                div()
                    .px_4()
                    .py_2()
                    .border_t_1()
                    .border_color(cx.theme().border)
                    .child(Progress::new("batch-progress").value(self.progress())),
            )
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap_3()
                    .px_4()
                    .py_2()
                    .text_xs()
                    .child(div().flex_1().overflow_hidden().child(self.message.clone()))
                    .child(format!("{:.0}% · {:.1}s", self.progress(), self.elapsed))
                    .child(
                        control_button("activity")
                            .label(if self.show_log {
                                "Hide activity"
                            } else {
                                "Activity"
                            })
                            .on_click(cx.listener(|v, _, _, cx| {
                                v.show_log = !v.show_log;
                                cx.notify();
                            })),
                    ),
            )
            .child(
                div()
                    .px_4()
                    .pb_2()
                    .text_xs()
                    .text_color(cx.theme().muted_foreground)
                    .child(self.runtime.status()),
            );
        if self.show_log {
            root = root.child(
                div()
                    .id("activity-log")
                    .h(px(150.))
                    .overflow_y_scroll()
                    .px_4()
                    .py_2()
                    .border_t_1()
                    .border_color(cx.theme().border)
                    .children(
                        self.log
                            .iter()
                            .rev()
                            .map(|line| div().text_xs().py_1().child(line.clone())),
                    ),
            );
        }
        root
    }
}
