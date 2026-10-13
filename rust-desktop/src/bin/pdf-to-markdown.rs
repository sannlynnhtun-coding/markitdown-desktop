use anyhow::{Result, bail};
use markitdown_desktop::{conversion::*, runtime::Runtime};
use std::{path::PathBuf, sync::atomic::AtomicBool};

fn main() -> Result<()> {
    let mut args = std::env::args_os().skip(1);
    let Some(source) = args.next() else {
        bail!("Usage: pdf-to-markdown INPUT.pdf OUTPUT_FOLDER [--force-ocr] [--english|--myanmar]");
    };
    let Some(folder) = args.next() else {
        bail!("Missing output folder");
    };
    let mut options = Options::default();
    for arg in args {
        match arg.to_str() {
            Some("--force-ocr") => options.mode = OcrMode::Force,
            Some("--english") => options.language = Language::English,
            Some("--myanmar") => options.language = Language::Myanmar,
            _ => bail!("Unknown option: {}", arg.to_string_lossy()),
        }
    }
    let runtime = Runtime::discover();
    let pdfium = runtime.pdfium()?;
    let scratch = tempfile::tempdir()?;
    let document = convert_document(
        &pdfium,
        &runtime,
        &PathBuf::from(source),
        &options,
        scratch.path(),
        &AtomicBool::new(false),
        |event| {
            if let DocumentEvent::Page(total, page) = event {
                eprintln!(
                    "Page {}/{} · {}",
                    page.number,
                    total,
                    if page.used_ocr { "OCR" } else { "text" }
                );
                if let Some(warning) = page.warning {
                    eprintln!("Warning: {warning}");
                }
            }
        },
    )?;
    println!(
        "{}",
        export_document(&document, &PathBuf::from(folder))?.display()
    );
    Ok(())
}
