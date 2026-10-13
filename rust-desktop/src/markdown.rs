use unicode_normalization::UnicodeNormalization;

/// Preserve source line breaks; do not guess columns or invent table structure.
pub fn normalize_text(text: &str) -> String {
    let text = text.replace("\r\n", "\n").replace('\r', "\n");
    let mut result = String::new();
    let mut blank = false;
    for line in text.lines() {
        let clean: String = line
            .chars()
            .filter(|c| !c.is_control() || *c == '\t')
            .collect::<String>()
            .nfc()
            .collect();
        let clean = clean.trim();
        if clean.is_empty() {
            if !blank && !result.is_empty() {
                result.push('\n');
            }
            blank = true;
        } else {
            result.push_str(clean);
            result.push('\n');
            blank = false;
        }
    }
    result.trim().to_string()
}

/// Escape PDF/OCR text so Markdown preview cannot interpret it as HTML/images.
pub fn escape_text(text: &str) -> String {
    let mut out = String::new();
    for c in text.chars() {
        match c {
            '&' => out.push_str("&amp;"),
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '\\' | '`' | '*' | '_' | '[' | ']' | '#' | '!' | '|' | '~' => {
                out.push('\\');
                out.push(c);
            }
            _ => out.push(c),
        }
    }
    // A hard break preserves PDF lines without pretending to reconstruct layout.
    out.replace('\n', "  \n")
}

pub fn page_markdown(page: usize, text: &str) -> String {
    let text = normalize_text(text);
    if text.is_empty() {
        format!("## Page {page}\n\n*No readable text detected.*\n\n")
    } else {
        format!("## Page {page}\n\n{}\n\n", escape_text(&text))
    }
}

/// Detect absent or obviously broken text, without penalizing short Myanmar pages.
pub fn needs_ocr(text: &str) -> bool {
    let chars: Vec<char> = text.chars().filter(|c| !c.is_whitespace()).collect();
    if chars.is_empty() {
        return true;
    }
    let broken = chars
        .iter()
        .filter(|c| **c == '\u{fffd}' || c.is_control() || ('\u{e000}'..='\u{f8ff}').contains(c))
        .count();
    broken * 20 > chars.len() || !chars.iter().any(|c| c.is_alphanumeric())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn burmese_survives_normalization_and_markdown() {
        let text = "မြန်မာစာ စမ်းသပ်ခြင်း\r\n\r\nEnglish text";
        let markdown = page_markdown(2, text);
        assert!(markdown.contains("မြန်မာစာ စမ်းသပ်ခြင်း"));
        assert!(!needs_ocr(text));
        assert!(!needs_ocr("က"));
        assert!(markdown.starts_with("## Page 2\n\n"));
    }

    #[test]
    fn broken_text_routes_to_ocr() {
        assert!(needs_ocr(" \n\t"));
        assert!(needs_ocr("\u{e001}\u{e002}"));
        assert!(needs_ocr("hello\u{fffd}"));
        assert!(!needs_ocr("Hello PDF"));
    }

    #[test]
    fn document_content_is_literal_and_cannot_fetch_images() {
        let result = page_markdown(1, "![x](https://example.org/x)\n<script>\n# title");
        assert!(result.contains("\\!\\[x\\]"));
        assert!(result.contains("&lt;script&gt;"));
        assert!(result.contains("\\# title"));
    }
}
