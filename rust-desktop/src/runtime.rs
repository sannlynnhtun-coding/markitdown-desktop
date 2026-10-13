use anyhow::{Context, Result, bail};
use pdfium_render::prelude::*;
use std::path::PathBuf;

#[derive(Clone, Debug)]
pub struct Runtime {
    pub root: PathBuf,
}

impl Runtime {
    pub fn discover() -> Self {
        if let Some(root) = std::env::var_os("MARKITDOWN_RUNTIME") {
            return Self { root: root.into() };
        }
        let exe_dir = std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(|p| p.to_path_buf()))
            .unwrap_or_default();
        let bundled = exe_dir.join("runtime");
        if bundled.is_dir() {
            Self { root: bundled }
        } else {
            Self {
                root: PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("runtime"),
            }
        }
    }

    pub fn pdfium(&self) -> Result<Pdfium> {
        let path = self.root.join("pdfium.dll");
        match Pdfium::bind_to_library(&path) {
            Ok(bindings) => Ok(Pdfium::new(bindings)),
            // pdfium-render keeps process-wide bindings. Later batches must reuse them.
            Err(PdfiumError::PdfiumLibraryBindingsAlreadyInitialized) => Ok(Pdfium::default()),
            Err(error) => Err(error).with_context(|| {
                format!(
                    "Cannot load {}. Run scripts/prepare-runtime.ps1 first.",
                    path.display()
                )
            }),
        }
    }

    pub fn tesseract(&self) -> PathBuf {
        self.root.join("tesseract").join("tesseract.exe")
    }

    pub fn tessdata(&self) -> PathBuf {
        self.root.join("tessdata")
    }

    pub fn check_ocr(&self, language: &str) -> Result<()> {
        if !self.tesseract().is_file() {
            bail!(
                "OCR is unavailable: missing {}. Prepare the bundled Tesseract runtime.",
                self.tesseract().display()
            );
        }
        for code in language.split('+') {
            let model = self.tessdata().join(format!("{code}.traineddata"));
            if !model.is_file() {
                bail!("OCR language model is missing: {}", model.display());
            }
        }
        Ok(())
    }

    pub fn status(&self) -> String {
        let pdf = self.root.join("pdfium.dll").is_file();
        let ocr = self.check_ocr("mya+eng").is_ok();
        match (pdf, ocr) {
            (true, true) => "PDF + Myanmar / English OCR ready".into(),
            (true, false) => "PDF ready · OCR runtime missing".into(),
            _ => "Runtime missing · see setup instructions".into(),
        }
    }
}
