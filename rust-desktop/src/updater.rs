use std::process::{Command, Stdio};

pub fn check(manual: bool) -> std::io::Result<()> {
    let exe = std::env::current_exe()?;
    let directory = exe.parent().expect("Executable has a parent directory");
    let script = directory.join("update.ps1");
    if !script.is_file() {
        return Err(std::io::Error::new(
            std::io::ErrorKind::NotFound,
            "Updater is available in packaged installations",
        ));
    }
    let mut command = Command::new("powershell.exe");
    command
        .args([
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
        ])
        .arg(script)
        .args([
            "-CurrentVersion",
            env!("CARGO_PKG_VERSION"),
            "-AppProcessId",
        ])
        .arg(std::process::id().to_string())
        .arg("-InstallDirectory")
        .arg(directory)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    if manual {
        command.arg("-Manual");
    }
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000); // CREATE_NO_WINDOW
    }
    command.spawn()?;
    Ok(())
}
