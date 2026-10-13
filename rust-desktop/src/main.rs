#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
extern crate gpui_kit as gpui;

mod ui;

use gpui_kit::*;

fn main() {
    application().with_assets(assets::Assets).run(|cx| {
        init(cx);
        let bounds = Bounds::centered(None, size(px(1280.), px(840.)), cx);
        let options = WindowOptions {
            window_bounds: Some(WindowBounds::Windowed(bounds)),
            window_min_size: Some(size(px(960.), px(640.))),
            titlebar: Some(TitlebarOptions {
                title: Some("MarkItDown".into()),
                ..Default::default()
            }),
            ..Default::default()
        };
        open_window(options, cx, |window, cx| {
            cx.new(|cx| ui::Workspace::new(window, cx))
        })
        .expect("Cannot open MarkItDown window");
        cx.activate(true);
    });
}
