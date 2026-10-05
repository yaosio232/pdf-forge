use crate::model::{ForgeError, ForgeResult};
use std::{collections::HashSet, fs, path::{Path, PathBuf}};

fn normalized(path: &Path) -> ForgeResult<PathBuf> {
    if path.exists() { return path.canonicalize().map_err(|e| ForgeError::new("PATH",e)); }
    let parent = path.parent().filter(|p| !p.as_os_str().is_empty()).ok_or_else(|| ForgeError::new("PATH","Output requires a directory"))?;
    Ok(parent.canonicalize().map_err(|e| ForgeError::new("PATH",e))?.join(path.file_name().ok_or_else(|| ForgeError::new("PATH","Output requires a file name"))?))
}
pub fn check_outputs(sources: &[PathBuf], outputs: &[PathBuf], overwrite: bool) -> ForgeResult<()> {
    let mut names = HashSet::new();
    for output in outputs {
        let full = normalized(output)?;
        if !names.insert(full.to_string_lossy().to_lowercase()) { return Err(ForgeError::new("DUPLICATE_OUTPUT","Output names must be unique")); }
        for source in sources {
            if normalized(source)?.to_string_lossy().eq_ignore_ascii_case(&full.to_string_lossy()) || (output.exists() && same_file::is_same_file(source,output).unwrap_or(false)) {
                return Err(ForgeError::new("SOURCE_OUTPUT","Output must not replace a source"));
            }
        }
        if output.exists() && (!overwrite || !output.is_file()) { return Err(ForgeError::new("OUTPUT_EXISTS","Confirm replacement of the existing output")); }
    }
    Ok(())
}

pub fn safe_name(name: &str) -> ForgeResult<String> {
    let trimmed = name.trim();
    if trimmed.is_empty() || trimmed.ends_with(['.', ' ']) || trimmed.chars().any(|c| c.is_control() || "<>:\"/\\|?*".contains(c)) {
        return Err(ForgeError::new("NAME","Choose a valid Windows file name"));
    }
    let stem = trimmed.split('.').next().unwrap_or("").to_uppercase();
    if ["CON","PRN","AUX","NUL"].contains(&stem.as_str()) || ((stem.starts_with("COM") || stem.starts_with("LPT")) && stem.len()==4 && stem.as_bytes()[3].is_ascii_digit()) {
        return Err(ForgeError::new("NAME","Reserved Windows file name"));
    }
    Ok(if trimmed.to_lowercase().ends_with(".pdf") { trimmed.to_owned() } else { format!("{trimmed}.pdf") })
}

pub struct PreparedOutput { temporary: Option<tempfile::TempPath>, output: PathBuf, overwrite: bool }
impl PreparedOutput {
    pub fn new(output: &Path, overwrite: bool) -> ForgeResult<Self> {
        if output.exists() && !overwrite { return Err(ForgeError::new("OUTPUT_EXISTS","Confirm replacement")); }
        let parent = output.parent().ok_or_else(|| ForgeError::new("PATH","Output directory missing"))?;
        let suffix = format!(".{}",output.extension().unwrap_or_default().to_string_lossy());
        let temp = tempfile::Builder::new().prefix(".pdfforge-").suffix(&suffix).tempfile_in(parent).map_err(|e| ForgeError::new("OUTPUT_WRITE",e))?;
        let path = temp.into_temp_path();
        // Reserve then remove: PDF writers use CreateNew and COM/browser require an absent output.
        fs::remove_file(&path).map_err(|e| ForgeError::new("OUTPUT_WRITE",e))?;
        Ok(Self { temporary: Some(path), output: output.to_owned(), overwrite })
    }
    pub fn path(&self) -> &Path { self.temporary.as_ref().unwrap().as_ref() }
    pub fn commit(mut self) -> ForgeResult<()> {
        let file = fs::OpenOptions::new().read(true).write(true).open(self.path()).map_err(|e| ForgeError::new("OUTPUT_INVALID",e))?;
        if file.metadata().map_err(|e|ForgeError::new("OUTPUT_INVALID",e))?.len()==0 { return Err(ForgeError::new("OUTPUT_INVALID","Empty output")); }
        file.sync_all().map_err(|e| ForgeError::new("OUTPUT_WRITE",e))?;
        drop(file);
        let temporary = self.temporary.take().unwrap();
        if !self.overwrite { temporary.persist_noclobber(&self.output).map_err(|e| ForgeError::new("OUTPUT_EXISTS",e.error))?; }
        else {
            #[cfg(windows)] {
                use windows::{core::PCWSTR, Win32::Storage::FileSystem::{MoveFileExW,MOVEFILE_REPLACE_EXISTING,MOVEFILE_WRITE_THROUGH}};
                use std::os::windows::ffi::OsStrExt;
                let from: Vec<u16> = temporary.as_os_str().encode_wide().chain(Some(0)).collect();
                let to: Vec<u16> = self.output.as_os_str().encode_wide().chain(Some(0)).collect();
                unsafe { MoveFileExW(PCWSTR(from.as_ptr()), PCWSTR(to.as_ptr()), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) }.map_err(|e|ForgeError::new("OUTPUT_WRITE",e))?;
            }
            #[cfg(not(windows))] fs::rename(&temporary,&self.output).map_err(|e|ForgeError::new("OUTPUT_WRITE",e))?;
        }
        Ok(())
    }
}
