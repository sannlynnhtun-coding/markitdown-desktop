fn main() {
    println!("cargo:rerun-if-changed=assets/markitdown.rc");
    println!("cargo:rerun-if-changed=assets/markitdown.ico");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows")
        && std::env::var_os("CARGO_FEATURE_DESKTOP").is_some()
    {
        embed_resource::compile_for(
            "assets/markitdown.rc",
            ["markitdown-desktop"],
            embed_resource::NONE,
        )
        .manifest_required()
        .expect("Cannot embed MarkItDown Windows icon");
    }
}
