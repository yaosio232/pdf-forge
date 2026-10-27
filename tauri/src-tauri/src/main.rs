#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use pdf_forge::{model::*, supervisor::JobManager};
use std::{collections::HashSet,path::{Path,PathBuf},sync::{atomic::{AtomicBool,Ordering},Arc,Mutex},time::{Instant,Duration}};
use tauri::{Emitter,Manager,State};
use tauri_plugin_dialog::DialogExt;
use tauri_plugin_opener::OpenerExt;

struct Access { files:Mutex<HashSet<PathBuf>>, outputs:Mutex<HashSet<PathBuf>> }
struct AppState { manager:Arc<JobManager>, access:Access, closing:AtomicBool }
impl Access {
    fn register(&self,paths:&[PathBuf]) {if let Ok(mut allowed)=self.files.lock(){for path in paths{if let Ok(full)=path.canonicalize(){allowed.insert(full);}}}}
    fn source(&self,path:&str)->ForgeResult<PathBuf> {
        let full=Path::new(path).canonicalize().map_err(|e|ForgeError::new("SOURCE_MISSING",e))?;
        if !self.files.lock().unwrap().contains(&full){return Err(ForgeError::new("ACCESS","Choose or drop this file first"));} Ok(full)
    }
}
#[tauri::command]
fn capabilities()->serde_json::Value {
    #[cfg(windows)] let word=unsafe {windows::Win32::System::Com::CLSIDFromProgID(windows::core::w!("Word.Application")).is_ok()};
    #[cfg(not(windows))] let word=false;
    serde_json::json!({"word":word,"webview2":cfg!(windows),"version":env!("CARGO_PKG_VERSION")})
}
#[tauri::command]
async fn select_files(app:tauri::AppHandle,state:State<'_,AppState>,kind:String)->ForgeResult<Vec<String>> {
    let extensions=match kind.as_str(){"split"|"unlock"|"pdfToWord"=>vec!["pdf"],"merge"=>vec!["pdf","doc","docx"],"markdown"=>vec!["md","markdown"],_=>return Err(ForgeError::new("FILE_TYPE","Unknown operation"))};
    // Blocking native dialogs must not run on Tauri's event-loop thread.
    let selection=tauri::async_runtime::spawn_blocking(move ||app.dialog().file().add_filter("Documents",&extensions).blocking_pick_files().unwrap_or_default()).await.map_err(|e|ForgeError::new("INTERNAL",e))?;
    let paths:Vec<PathBuf>=selection.into_iter().filter_map(|p|p.into_path().ok()).collect();
    state.access.register(&paths); Ok(paths.iter().map(|p|p.to_string_lossy().into_owned()).collect())
}
#[tauri::command]
async fn pdf_info(state:State<'_,AppState>,path:String,request_id:String)->ForgeResult<PdfInfo> {
    let source=state.access.source(&path)?;
    let manager=state.manager.clone();
    manager.register_pdf_info(&request_id)?;
    tauri::async_runtime::spawn_blocking(move ||manager.pdf_info(&request_id,source)).await.map_err(|e|ForgeError::new("INTERNAL",e))?
}
#[tauri::command]
fn cancel_pdf_info(state:State<AppState>,request_id:String)->ForgeResult<()> {
    state.manager.cancel_pdf_info(&request_id)
}
#[tauri::command]
fn read_pdf(state:State<AppState>,path:String)->ForgeResult<Vec<u8>> {
    let path=state.access.source(&path)?;
    pdf_forge::pdf::preview_bytes(&path)
}
#[tauri::command]
fn start_job(app:tauri::AppHandle,state:State<AppState>,request:JobRequest)->ForgeResult<String> {
    let sources:Vec<_>=match &request{
        JobRequest::Split{source,..}|JobRequest::PdfToWord{source,..}=>vec![source],
        JobRequest::Merge{sources,..}|JobRequest::Unlock{sources,..}|JobRequest::Markdown{sources,..}=>sources.iter().collect(),
    };
    for source in sources{state.access.source(source)?;}
    let request=pdf_forge::output_plan::automatic_output(request)?;
    let output=match &request {
        JobRequest::Split{directory,..}|JobRequest::Unlock{directory,..}=>directory,
        JobRequest::Merge{output,..}|JobRequest::PdfToWord{output,..}|JobRequest::Markdown{output,..}=>output,
    };
    state.access.outputs.lock().unwrap().insert(PathBuf::from(output));
    state.manager.start(request,Arc::new(move |event|{let _=app.emit("job-event",event);}))
}
#[tauri::command]
async fn cancel_job(state:State<'_,AppState>,job_id:String)->ForgeResult<()> {
    let manager=state.manager.clone();
    tauri::async_runtime::spawn_blocking(move ||manager.cancel(&job_id)).await.map_err(|e|ForgeError::new("INTERNAL",e))?
}
#[tauri::command]
fn get_job(state:State<AppState>,job_id:String)->ForgeResult<JobEvent> {state.manager.get(&job_id)}
#[tauri::command]
fn log_folder(state:State<AppState>)->String {state.manager.log_directory.to_string_lossy().into_owned()}
#[tauri::command]
fn open_folder(app:tauri::AppHandle,state:State<AppState>,path:String)->ForgeResult<()> {
    let input=PathBuf::from(&path);
    let permitted=input==state.manager.log_directory || state.access.outputs.lock().unwrap().iter().any(|p|input==*p || input==p.parent().unwrap_or(p) || (p.is_dir() && input.parent()==Some(p.as_path()))) || state.access.files.lock().unwrap().contains(&input);
    if !permitted{return Err(ForgeError::new("ACCESS","Folder was not selected in this session"));}
    let folder=if input.is_file(){input.parent().unwrap_or(&input)}else{&input};
    app.opener().open_path(folder.to_string_lossy().to_string(),None::<&str>).map_err(|e|ForgeError::new("OPEN_FOLDER",e))
}

fn main() {
    let arguments:Vec<_>=std::env::args_os().collect();
    if arguments.len()==3 && arguments[1]=="--worker" {
        let result=pdf_forge::supervisor::worker_main(Path::new(&arguments[2]));
        std::process::exit(if result.is_ok(){0}else{1});
    }
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init()).plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let logs=app.path().app_local_data_dir()?.join("logs");
            let resource=app.path().resource_dir()?.join("resources").join("markdown");
            let development=Path::new(env!("CARGO_MANIFEST_DIR")).join("../public/markdown");
            std::env::set_var("PDF_FORGE_MARKDOWN_ASSETS",if resource.exists(){resource}else{development});
            let manager=JobManager::new(logs,std::env::current_exe()?).map_err(|e|std::io::Error::other(e.detail))?;
            app.manage(AppState{manager:Arc::new(manager),access:Access{files:Mutex::new(HashSet::new()),outputs:Mutex::new(HashSet::new())},closing:AtomicBool::new(false)});
            Ok(())
        })
        .on_window_event(|window,event| {
            let state=window.state::<AppState>();
            if let tauri::WindowEvent::DragDrop(tauri::DragDropEvent::Drop{paths,..})=event {state.access.register(paths);}
            if let tauri::WindowEvent::CloseRequested{api,..}=event {
                state.manager.stop_accepting();
                if state.manager.active() {
                    api.prevent_close();
                    if !state.closing.swap(true,Ordering::SeqCst) {
                        let app=window.app_handle().clone();let manager=state.manager.clone();
                        let cancellation=manager.clone();
                        std::thread::spawn(move ||cancellation.cancel_all());
                        std::thread::spawn(move ||{let start=Instant::now();while manager.active() && start.elapsed()<Duration::from_secs(8){std::thread::sleep(Duration::from_millis(50));}app.exit(0);});
                    }
                }
            }
        })
        .invoke_handler(tauri::generate_handler![capabilities,select_files,pdf_info,cancel_pdf_info,read_pdf,start_job,cancel_job,get_job,log_folder,open_folder])
        .run(tauri::generate_context!()).expect("PDF Forge could not start");
}
