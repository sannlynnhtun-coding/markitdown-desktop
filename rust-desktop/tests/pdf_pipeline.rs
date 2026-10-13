//! Native pipeline tests. Run explicitly after preparing the bundled runtime.
use markitdown_desktop::{conversion::*, runtime::Runtime};
use pdfium_render::prelude::*;
use std::{
    fs,
    path::PathBuf,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
};

fn assemble(objects: Vec<Vec<u8>>) -> Vec<u8> {
    let mut pdf = b"%PDF-1.4\n".to_vec();
    let mut offsets = vec![0];
    for (i, object) in objects.iter().enumerate() {
        offsets.push(pdf.len());
        pdf.extend_from_slice(format!("{} 0 obj\n", i + 1).as_bytes());
        pdf.extend_from_slice(object);
        pdf.extend_from_slice(b"\nendobj\n");
    }
    let xref = pdf.len();
    pdf.extend_from_slice(format!("xref\n0 {}\n0000000000 65535 f \n", offsets.len()).as_bytes());
    for offset in offsets.iter().skip(1) {
        pdf.extend_from_slice(format!("{offset:010} 00000 n \n").as_bytes());
    }
    pdf.extend_from_slice(
        format!(
            "trailer\n<< /Size {} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF",
            offsets.len()
        )
        .as_bytes(),
    );
    pdf
}

fn stream(dictionary: &str, data: &[u8]) -> Vec<u8> {
    let mut object = format!("<< {dictionary} /Length {} >>\nstream\n", data.len()).into_bytes();
    object.extend_from_slice(data);
    object.extend_from_slice(b"\nendstream");
    object
}

fn text_pdf() -> Vec<u8> {
    assemble(vec![
        b"<< /Type /Catalog /Pages 2 0 R >>".to_vec(),
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>".to_vec(),
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>".to_vec(),
        stream("", b"BT /F1 28 Tf 60 700 Td (Hello scanned PDF) Tj ET"),
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>".to_vec(),
    ])
}

fn image_pdf(image: &image::RgbImage, mixed: bool) -> Vec<u8> {
    let kids = if mixed {
        "[3 0 R 6 0 R] /Count 2"
    } else {
        "[3 0 R] /Count 1"
    };
    let mut objects = vec![
        b"<< /Type /Catalog /Pages 2 0 R >>".to_vec(),
        format!("<< /Type /Pages /Kids {kids} >>").into_bytes(),
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /XObject << /Im 5 0 R >> >> /Contents 4 0 R >>".to_vec(),
        stream("", b"q 600 0 0 800 0 0 cm /Im Do Q"),
        stream(&format!("/Type /XObject /Subtype /Image /Width {} /Height {} /ColorSpace /DeviceRGB /BitsPerComponent 8", image.width(), image.height()), image.as_raw()),
    ];
    if mixed {
        objects.extend([
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 8 0 R >> >> /Contents 7 0 R >>".to_vec(),
            stream("", b"BT /F1 28 Tf 60 700 Td (Digital second page) Tj ET"),
            b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>".to_vec(),
        ]);
    }
    assemble(objects)
}

#[test]
#[ignore = "requires prepared PDFium and OCR runtime"]
fn text_scan_mixed_and_force_ocr() {
    let runtime = Runtime::discover();
    let pdfium = runtime.pdfium().unwrap();
    let dir = tempfile::tempdir().unwrap();
    let source = dir.path().join("မြန်မာ text.pdf");
    fs::write(&source, text_pdf()).unwrap();
    let cancel = AtomicBool::new(false);
    let text = convert_document(
        &pdfium,
        &runtime,
        &source,
        &Options::default(),
        &dir.path().join("text"),
        &cancel,
        |_| {},
    )
    .unwrap();
    assert!(text.markdown().contains("Hello scanned PDF"));
    assert!(!text.pages[0].used_ocr);
    assert!(text.pages[0].preview.is_file());
    let bytes = text_pdf();
    let original = pdfium.load_pdf_from_byte_slice(&bytes, None).unwrap();
    let image = original
        .pages()
        .get(0)
        .unwrap()
        .render_with_config(&PdfRenderConfig::new().set_target_width(1800))
        .unwrap()
        .as_image()
        .unwrap()
        .to_rgb8();
    let scan_path = dir.path().join("scan.pdf");
    fs::write(&scan_path, image_pdf(&image, true)).unwrap();
    let options = Options {
        mode: OcrMode::Auto,
        language: Language::English,
    };
    let mixed = convert_document(
        &pdfium,
        &runtime,
        &scan_path,
        &options,
        &dir.path().join("mixed"),
        &cancel,
        |_| {},
    )
    .unwrap();
    assert_eq!(mixed.pages.len(), 2);
    assert!(mixed.pages[0].used_ocr);
    assert!(
        mixed.pages[0].markdown.contains("Hello scanned PDF"),
        "{}",
        mixed.pages[0].markdown
    );
    assert!(!mixed.pages[1].used_ocr);
    assert!(mixed.pages[1].markdown.contains("Digital second page"));
    let forced = convert_document(
        &pdfium,
        &runtime,
        &source,
        &Options {
            mode: OcrMode::Force,
            language: Language::English,
        },
        &dir.path().join("forced"),
        &cancel,
        |_| {},
    )
    .unwrap();
    assert!(forced.pages[0].used_ocr);
    assert!(forced.markdown().contains("Hello scanned PDF"));
    let saved = export_document(&mixed, &dir.path().join("output")).unwrap();
    assert!(
        fs::read_to_string(saved)
            .unwrap()
            .contains("Digital second page")
    );
}

#[test]
#[ignore = "requires prepared PDFium runtime"]
fn failed_file_does_not_stop_batch_and_cancel_is_reported() {
    let dir = tempfile::tempdir().unwrap();
    let bad = dir.path().join("bad.pdf");
    let good = dir.path().join("good.pdf");
    fs::write(&bad, b"not a PDF").unwrap();
    fs::write(&good, text_pdf()).unwrap();
    let mut events = Vec::new();
    convert_batch(
        vec![bad, good.clone()],
        Runtime::discover(),
        Options::default(),
        dir.path().join("batch"),
        Arc::new(AtomicBool::new(false)),
        |e| events.push(e),
    );
    assert!(
        events
            .iter()
            .any(|e| matches!(e, Event::Failed { index: 0, .. }))
    );
    assert!(
        events
            .iter()
            .any(|e| matches!(e, Event::Complete { index: 1, .. }))
    );
    assert!(matches!(
        events.last(),
        Some(Event::Finished { cancelled: false })
    ));
    let cancel = Arc::new(AtomicBool::new(false));
    let mut cancelled_events = Vec::new();
    convert_batch(
        vec![good.clone(), good],
        Runtime::discover(),
        Options::default(),
        dir.path().join("cancel"),
        cancel.clone(),
        |e| {
            if matches!(e, Event::Page { .. }) {
                cancel.store(true, Ordering::Relaxed);
            }
            cancelled_events.push(e);
        },
    );
    assert!(
        !cancelled_events
            .iter()
            .any(|e| matches!(e, Event::Started { index: 1, .. }))
    );
    assert!(matches!(
        cancelled_events.last(),
        Some(Event::Finished { cancelled: true })
    ));
}

#[test]
#[ignore = "set MARKITDOWN_MYANMAR_TEST_IMAGE to a Myanmar scan PNG"]
fn myanmar_scan_ocr() {
    let path = PathBuf::from(
        std::env::var_os("MARKITDOWN_MYANMAR_TEST_IMAGE")
            .expect("Myanmar fixture image is required"),
    );
    let image = image::open(path).unwrap().to_rgb8();
    let runtime = Runtime::discover();
    let pdfium = runtime.pdfium().unwrap();
    let dir = tempfile::tempdir().unwrap();
    let source = dir.path().join("မြန်မာ scan.pdf");
    fs::write(&source, image_pdf(&image, false)).unwrap();
    let result = convert_document(
        &pdfium,
        &runtime,
        &source,
        &Options {
            mode: OcrMode::Auto,
            language: Language::MyanmarEnglish,
        },
        &dir.path().join("pages"),
        &AtomicBool::new(false),
        |_| {},
    )
    .unwrap();
    assert!(result.pages[0].used_ocr);
    assert!(result.markdown().contains("မြန်မာ"), "{}", result.markdown());
    println!("{}", result.markdown());
}
