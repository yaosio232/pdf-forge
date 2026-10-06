#![cfg_attr(windows, windows_subsystem = "windows")]
use sha2::{Digest,Sha256};
use std::{fs,io::{self,Cursor,Read},path::{Path,PathBuf},process::Command};

static PAYLOAD:&[u8]=include_bytes!(env!("PDF_FORGE_PAYLOAD"));
type Result<T> = std::result::Result<T,Box<dyn std::error::Error>>;
fn digest(bytes:&[u8])->String{format!("{:x}",Sha256::digest(bytes))}
fn plain_tree(root:&Path)->Result<bool>{
    for entry in fs::read_dir(root)?{
        let path=entry?.path();let metadata=fs::symlink_metadata(&path)?;
        if metadata.file_type().is_symlink(){return Ok(false);}
        #[cfg(windows)]{use std::os::windows::fs::MetadataExt;if metadata.file_attributes()&0x400!=0{return Ok(false);}}
        if metadata.is_dir() && !plain_tree(&path)?{return Ok(false);}
    }
    Ok(true)
}
fn entries(root:&Path,extract:bool)->Result<bool>{
    if !extract && !plain_tree(root)?{return Ok(false);}
    let mut archive=zip::ZipArchive::new(Cursor::new(PAYLOAD))?;
    let mut expected_paths=std::collections::HashSet::new();
    for i in 0..archive.len(){
        let mut entry=archive.by_index(i)?;
        let enclosed=entry.enclosed_name().ok_or("Unsafe portable resource path")?;
        if enclosed.to_string_lossy().contains(':'){return Err("Unsafe Windows resource path".into());}
        if entry.unix_mode().is_some_and(|mode|mode&0o170000==0o120000){return Err("Symbolic link in portable resources".into());}
        let target=root.join(enclosed);
        if entry.is_dir(){if extract{fs::create_dir_all(target)?;}continue;}
        if !expected_paths.insert(target.clone()){return Err("Duplicate portable resource path".into());}
        let mut expected=Vec::new();entry.read_to_end(&mut expected)?;
        if extract{
            fs::create_dir_all(target.parent().ok_or("Missing resource parent")?)?;
            let mut output=fs::OpenOptions::new().write(true).create_new(true).open(target)?;
            io::copy(&mut Cursor::new(expected),&mut output)?;output.sync_all()?;
        }else if fs::read(target).map(|bytes|digest(&bytes)==digest(&expected)).unwrap_or(false)==false{return Ok(false);}
    }
    if !extract{
        let mut directories=vec![root.to_path_buf()];
        while let Some(directory)=directories.pop(){for entry in fs::read_dir(directory)?{
            let entry=entry?;if entry.file_type()?.is_dir(){directories.push(entry.path());}
            else if !expected_paths.contains(&entry.path()){return Ok(false);}
        }}
    }
    Ok(true)
}
fn prepare()->Result<PathBuf>{
    let base=std::env::var_os("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(std::env::temp_dir).join("PDFForge2").join("portable");
    fs::create_dir_all(&base)?;
    let cache=base.join(format!("{}-{}",env!("CARGO_PKG_VERSION"),digest(PAYLOAD)));
    if cache.is_dir() && entries(&cache,false)?{return Ok(cache);}
    // Build privately; publish the directory only after every resource is complete.
    let staging=tempfile::Builder::new().prefix("resources-").tempdir_in(&base)?;
    entries(staging.path(),true)?;
    if fs::rename(staging.path(),&cache).is_ok(){return Ok(cache);}
    // Another launcher may have published it. Never overwrite a corrupted cache
    // or an instance's loaded files; keep this complete private copy instead.
    if cache.is_dir() && entries(&cache,false)?{return Ok(cache);}
    Ok(staging.keep())
}
fn run()->Result<i32>{
    let directory=prepare()?;
    let arguments:Vec<_>=std::env::args_os().skip(1).collect();
    if arguments.first().is_some_and(|arg|arg=="--prepare"){
        let result=arguments.get(1).ok_or("--prepare requires an output JSON path")?;
        fs::write(result,serde_json::to_vec_pretty(&serde_json::json!({"directory":directory,"payloadSha256":digest(PAYLOAD),"version":env!("CARGO_PKG_VERSION")}))?)?;
        return Ok(0);
    }
    let status=Command::new(directory.join("PDF Forge 2.exe")).args(arguments)
        .env("PDF_FORGE_MARKDOWN_ASSETS",directory.join("resources/markdown")).status()?;
    Ok(status.code().unwrap_or(1))
}
fn main(){
    match run(){Ok(code)=>std::process::exit(code),Err(error)=>{
        let detail=format!("PDF Forge could not prepare its bundled resources: {error}");
        #[cfg(windows)]unsafe{
            #[link(name="user32")]extern "system"{fn MessageBoxW(window:*mut std::ffi::c_void,text:*const u16,title:*const u16,kind:u32)->i32;}
            let text:Vec<u16>=detail.encode_utf16().chain(Some(0)).collect();let title:Vec<u16>="PDF Forge 2".encode_utf16().chain(Some(0)).collect();
            MessageBoxW(std::ptr::null_mut(),text.as_ptr(),title.as_ptr(),0x10);
        }
        #[cfg(not(windows))]eprintln!("{detail}");
        std::process::exit(1);
    }}
}

#[cfg(test)]mod tests{
    use super::*;
    #[test]fn embedded_resources_detect_corruption_and_extra_dlls(){
        let root=tempfile::tempdir().unwrap();entries(root.path(),true).unwrap();
        assert!(entries(root.path(),false).unwrap());
        let readme=root.path().join("README.md");let original=fs::read(&readme).unwrap();
        fs::write(&readme,b"corrupted").unwrap();assert!(!entries(root.path(),false).unwrap());
        fs::write(&readme,original).unwrap();assert!(entries(root.path(),false).unwrap());
        fs::write(root.path().join("injected.dll"),b"unexpected").unwrap();assert!(!entries(root.path(),false).unwrap());
    }
    #[test]fn payload_contains_runtime_and_no_project_sources_or_evidence(){
        let mut zip=zip::ZipArchive::new(Cursor::new(PAYLOAD)).unwrap();let mut names=std::collections::HashSet::new();
        for i in 0..zip.len(){let entry=zip.by_index(i).unwrap();let name=entry.name().to_owned();
            assert!(!["TestResults/","tests/","tauri/","node_modules/","target/","src/",".git/"].iter().any(|prefix|name.starts_with(prefix)));names.insert(name);
        }
        for name in ["PDF Forge 2.exe","WebView2Loader.dll","resources/markdown/renderer.js","resources/markdown/renderer.css","licenses/LICENSE.md"]{assert!(names.contains(name));}
    }
}
