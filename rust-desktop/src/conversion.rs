use crate::{
    markdown::{escape_text, needs_ocr, page_markdown},
    runtime::Runtime,
};
use anyhow::{Context, Result, bail};
use pdfium_render::prelude::*;
use std::{
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant},
};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum OcrMode {
    Auto,
    Force,
    TextOnly,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Language {
    MyanmarEnglish,
    Myanmar,
    English,
}

impl Language {
    pub fn code(self) -> &'static str {
        match self {
            Self::MyanmarEnglish => "mya+eng",
            Self::Myanmar => "mya",
            Self::English => "eng",
        }
    }
    pub fn label(self) -> &'static str {
        match self {
            Self::MyanmarEnglish => "Myanmar + English",
            Self::Myanmar => "Myanmar",
            Self::English => "English",
        }
    }
}

#[derive(Clone, Debug)]
pub struct Options {
    pub mode: OcrMode,
    pub language: Language,
}
impl Default for Options {
    fn default() -> Self {
        Self {
            mode: OcrMode::Auto,
            language: Language::MyanmarEnglish,
        }
    }
}

#[derive(Clone, Debug)]
pub struct PageResult {
    pub number: usize,
    pub markdown: String,
    pub preview: PathBuf,
    pub used_ocr: bool,
    pub warning: Option<String>,
}

#[derive(Clone, Debug)]
pub struct DocumentResult {
    pub source: PathBuf,
    pub pages: Vec<PageResult>,
    pub elapsed: Duration,
}
impl DocumentResult {
    pub fn markdown(&self) -> String {
        let title = self
            .source
            .file_stem()
            .unwrap_or_default()
            .to_string_lossy();
        let mut result = format!("# {}\n\n", escape_text(&title));
        for page in &self.pages {
            result.push_str(&page.markdown);
        }
        result
    }
}

#[derive(Debug)]
pub enum Event {
    Started {
        index: usize,
        pages: usize,
    },
    Page {
        index: usize,
        total: usize,
        result: PageResult,
    },
    Complete {
        index: usize,
        result: DocumentResult,
    },
    Failed {
        index: usize,
        message: String,
    },
    Finished {
        cancelled: bool,
    },
}

pub fn convert_batch(
    files: Vec<PathBuf>,
    runtime: Runtime,
    options: Options,
    scratch: PathBuf,
    cancel: Arc<AtomicBool>,
    mut emit: impl FnMut(Event),
) {
    let pdfium = match runtime.pdfium() {
        Ok(pdfium) => pdfium,
        Err(error) => {
            for index in 0..files.len() {
                emit(Event::Failed {
                    index,
                    message: format!("{error:#}"),
                });
            }
            emit(Event::Finished { cancelled: false });
            return;
        }
    };
    for (index, source) in files.into_iter().enumerate() {
        if cancel.load(Ordering::Relaxed) {
            break;
        }
        match convert_document(
            &pdfium,
            &runtime,
            &source,
            &options,
            &scratch.join(index.to_string()),
            &cancel,
            |event| {
                emit(match event {
                    DocumentEvent::Started(pages) => Event::Started { index, pages },
                    DocumentEvent::Page(total, result) => Event::Page {
                        index,
                        total,
                        result,
                    },
                })
            },
        ) {
            Ok(result) => emit(Event::Complete { index, result }),
            Err(error) if !cancel.load(Ordering::Relaxed) => emit(Event::Failed {
                index,
                message: format!("{error:#}"),
            }),
            Err(_) => break,
        }
    }
    emit(Event::Finished {
        cancelled: cancel.load(Ordering::Relaxed),
    });
}

pub enum DocumentEvent {
    Started(usize),
    Page(usize, PageResult),
}

pub fn convert_document(
    pdfium: &Pdfium,
    runtime: &Runtime,
    source: &Path,
    options: &Options,
    scratch: &Path,
    cancel: &AtomicBool,
    mut emit: impl FnMut(DocumentEvent),
) -> Result<DocumentResult> {
    let started = Instant::now();
    if !source
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("pdf"))
    {
        bail!("Only PDF files are supported");
    }
    // Loading from bytes avoids ANSI file path handling in native libraries on Windows.
    let bytes = fs::read(source).with_context(|| format!("Cannot read {}", source.display()))?;
    let document = pdfium
        .load_pdf_from_byte_slice(&bytes, None)
        .context("Cannot open PDF (it may be corrupt or password protected)")?;
    let total = document.pages().len() as usize;
    if total == 0 {
        bail!("PDF contains no pages");
    }
    fs::create_dir_all(scratch)?;
    emit(DocumentEvent::Started(total));
    let mut pages = Vec::with_capacity(total);
    for (index, page) in document.pages().iter().enumerate() {
        if cancel.load(Ordering::Relaxed) {
            bail!("Cancelled");
        }
        let extracted = page.text().map(|t| t.all()).unwrap_or_default();
        let used_ocr = options.mode == OcrMode::Force
            || (options.mode == OcrMode::Auto && needs_ocr(&extracted));
        let preview = scratch.join(format!("page-{}.png", index + 1));
        let width = if used_ocr {
            ((page.width().value / 72.0) * 300.0).ceil() as i32
        } else {
            1100
        };
        // Bound memory for unusually large PDF page dimensions.
        page.render_with_config(
            &PdfRenderConfig::new()
                .set_target_width(width.clamp(600, 3500))
                .set_maximum_height(5000),
        )?
        .as_image()?
        .save(&preview)
        .context("Cannot render PDF page")?;
        let text = if used_ocr {
            run_ocr(runtime, &preview, options.language, cancel)?
        } else {
            extracted
        };
        if cancel.load(Ordering::Relaxed) {
            bail!("Cancelled");
        }
        let warning = if needs_ocr(&text) {
            Some(
                if used_ocr {
                    "OCR found no usable text; inspect the source page."
                } else {
                    "Text is missing or garbled; retry with Force OCR."
                }
                .into(),
            )
        } else {
            None
        };
        let result = PageResult {
            number: index + 1,
            markdown: page_markdown(index + 1, &text),
            preview,
            used_ocr,
            warning,
        };
        emit(DocumentEvent::Page(total, result.clone()));
        pages.push(result);
    }
    Ok(DocumentResult {
        source: source.to_path_buf(),
        pages,
        elapsed: started.elapsed(),
    })
}

fn run_ocr(
    runtime: &Runtime,
    image: &Path,
    language: Language,
    cancel: &AtomicBool,
) -> Result<String> {
    runtime.check_ocr(language.code())?;
    // ASCII-only temporary working paths keep Tesseract usable for Myanmar source filenames.
    let work = tempfile::Builder::new()
        .prefix("markitdown-ocr-")
        .tempdir()?;
    fs::copy(image, work.path().join("input.png"))?;
    let log = fs::File::create(work.path().join("stderr.log"))?;
    let mut command = Command::new(runtime.tesseract());
    command
        .current_dir(work.path())
        .args([
            "input.png",
            "output",
            "-l",
            language.code(),
            "--psm",
            "3",
            "--dpi",
            "300",
            "--tessdata-dir",
        ])
        .arg(runtime.tessdata())
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::from(log));
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000); // CREATE_NO_WINDOW
    }
    let mut child = command.spawn().context("Cannot start bundled Tesseract")?;
    let started = Instant::now();
    loop {
        if cancel.load(Ordering::Relaxed) || started.elapsed() > Duration::from_secs(180) {
            let _ = child.kill();
            let _ = child.wait();
            bail!(if cancel.load(Ordering::Relaxed) {
                "Cancelled"
            } else {
                "OCR timed out after 180 seconds"
            });
        }
        match child.try_wait() {
            Ok(Some(status)) => {
                if !status.success() {
                    let details =
                        fs::read_to_string(work.path().join("stderr.log")).unwrap_or_default();
                    bail!("OCR failed: {}", details.trim());
                }
                break;
            }
            Ok(None) => std::thread::sleep(Duration::from_millis(80)),
            Err(error) => {
                let _ = child.kill();
                let _ = child.wait();
                return Err(error.into());
            }
        }
    }
    fs::read_to_string(work.path().join("output.txt")).context("Cannot read OCR output as UTF-8")
}

/// Never replace an existing document; same-named inputs get numbered exports.
pub fn export_document(document: &DocumentResult, folder: &Path) -> Result<PathBuf> {
    fs::create_dir_all(folder)?;
    let stem = document
        .source
        .file_stem()
        .unwrap_or_default()
        .to_string_lossy();
    let content = document.markdown();
    for suffix in 0..10000 {
        let name = if suffix == 0 {
            format!("{stem}.md")
        } else {
            format!("{stem} ({suffix}).md")
        };
        let path = folder.join(name);
        match OpenOptions::new().write(true).create_new(true).open(&path) {
            Ok(mut file) => {
                if let Err(error) = file
                    .write_all(content.as_bytes())
                    .and_then(|_| file.sync_all())
                {
                    drop(file);
                    let _ = fs::remove_file(&path);
                    return Err(error.into());
                }
                return Ok(path);
            }
            Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => continue,
            Err(error) => return Err(error.into()),
        }
    }
    bail!("Too many exports with the same filename")
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn exports_utf8_without_overwriting() {
        let folder = tempfile::tempdir().unwrap();
        let result = DocumentResult {
            source: PathBuf::from("မြန်မာ.pdf"),
            pages: vec![PageResult {
                number: 1,
                markdown: page_markdown(1, "မြန်မာစာ"),
                preview: PathBuf::new(),
                used_ocr: false,
                warning: None,
            }],
            elapsed: Duration::ZERO,
        };
        let first = export_document(&result, folder.path()).unwrap();
        let second = export_document(&result, folder.path()).unwrap();
        assert_ne!(first, second);
        assert!(fs::read_to_string(first).unwrap().contains("မြန်မာစာ"));
    }
}
