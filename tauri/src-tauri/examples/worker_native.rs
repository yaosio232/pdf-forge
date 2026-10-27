//! Small native entry point for testing the same worker function used by Tauri.
use std::path::Path;

fn main() {
    let args: Vec<_> = std::env::args_os().collect();
    let ok = args.len() == 3 && args[1] == "--worker" && match pdf_forge::supervisor::worker_main(Path::new(&args[2])) {
        Ok(())=>true,
        Err(error)=>{if let Some(path)=std::env::var_os("PDF_FORGE_TEST_DIAG") {let _=std::fs::write(path,error.code.as_bytes());}false}
    };
    std::process::exit(if ok { 0 } else { 1 });
}
