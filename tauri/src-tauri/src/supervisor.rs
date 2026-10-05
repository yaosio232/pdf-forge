use crate::{model::*, pdf, transaction::{check_outputs, safe_name, PreparedOutput}};
use serde::{Deserialize, Serialize};
use std::{collections::HashMap, fs::{self, OpenOptions}, io::Write, path::{Path, PathBuf}, process::{Command, Stdio}, sync::{atomic::{AtomicBool,Ordering}, Arc, Mutex}, thread, time::{Duration,Instant}};

#[derive(Serialize,Deserialize)]
#[serde(tag="kind",rename_all="camelCase")]
pub enum WorkerTask {
    Split { source: PathBuf, first: u32, last: u32, output: PathBuf },
    Merge { sources: Vec<PathBuf>, output: PathBuf },
    Unlock { source: PathBuf, password: String, output: PathBuf },
    WordToPdf { source: PathBuf, output: PathBuf },
    PdfToWord { source: PathBuf, output: PathBuf },
    Markdown { source: PathBuf, output: PathBuf, options: MarkdownOptions },
}
#[derive(Serialize,Deserialize)]
pub struct WorkerResponse { pub pages: Option<u32>, pub error: Option<ForgeError> }

struct Job { event: Mutex<JobEvent>, cancel: AtomicBool, commit_gate: Mutex<()> }
pub struct JobManager {
    jobs: Mutex<HashMap<String,Arc<Job>>>,
    pub log_directory: PathBuf,
    worker: PathBuf,
}
type Emit = Arc<dyn Fn(JobEvent) + Send + Sync>;
fn terminal(status: &str)->bool { matches!(status,"completed"|"partial"|"cancelled"|"failed") }
fn total(request:&JobRequest)->usize {
    match request { JobRequest::Split{ranges,..}=>ranges.len(), JobRequest::Merge{sources,..}|JobRequest::Unlock{sources,..}|JobRequest::Markdown{sources,..}=>sources.len(),_=>1 }
}
impl JobManager {
    pub fn new(log_directory:PathBuf,worker:PathBuf)->ForgeResult<Self> {
        fs::create_dir_all(&log_directory).map_err(|e|ForgeError::new("LOG_WRITE",e))?;
        Ok(Self { jobs:Mutex::new(HashMap::new()),log_directory,worker })
    }
    pub fn get(&self,id:&str)->ForgeResult<JobEvent> {
        let job = self.jobs.lock().map_err(|_|ForgeError::new("INTERNAL","Job registry unavailable"))?.get(id).cloned().ok_or_else(||ForgeError::new("JOB_MISSING","Unknown job"))?;
        let event = job.event.lock().map_err(|_|ForgeError::new("INTERNAL","Job state unavailable"))?.clone();
        Ok(event)
    }
    pub fn cancel(&self,id:&str)->ForgeResult<()> {
        let job = self.jobs.lock().map_err(|_|ForgeError::new("INTERNAL","Job registry unavailable"))?.get(id).cloned().ok_or_else(||ForgeError::new("JOB_MISSING","Unknown job"))?;
        let _gate=job.commit_gate.lock().map_err(|_|ForgeError::new("INTERNAL","Job commit state unavailable"))?;
        job.cancel.store(true,Ordering::SeqCst);
        Ok(())
    }
    pub fn cancel_all(&self) {
        let jobs:Vec<_>=self.jobs.lock().map(|jobs|jobs.values().cloned().collect()).unwrap_or_default();
        for job in jobs {if let Ok(_gate)=job.commit_gate.lock(){job.cancel.store(true,Ordering::SeqCst);}}
    }
    pub fn active(&self)->bool {
        self.jobs.lock().map(|jobs|jobs.values().any(|j|j.event.lock().map(|e|!terminal(&e.status)).unwrap_or(true))).unwrap_or(true)
    }
    pub fn start(self:&Arc<Self>,request:JobRequest,emit:Emit)->ForgeResult<String> {
        validate_request(&request)?;
        let mut jobs=self.jobs.lock().map_err(|_|ForgeError::new("INTERNAL","Job registry unavailable"))?;
        if jobs.values().any(|j|j.event.lock().map(|e|!terminal(&e.status)).unwrap_or(true)) { return Err(ForgeError::new("JOB_BUSY","Another job is running")); }
        // Keep recent terminal results for retrieval without retaining an unbounded history.
        if jobs.len()>50 { jobs.retain(|_,j|j.event.lock().map(|e|!terminal(&e.status)).unwrap_or(true)); }
        let id=uuid::Uuid::new_v4().to_string();
        let job=Arc::new(Job { event:Mutex::new(JobEvent{job_id:id.clone(),status:"preparing".into(),phase:"preparing".into(),completed:0,total:total(&request),items:vec![],error:None}),cancel:AtomicBool::new(false),commit_gate:Mutex::new(()) });
        jobs.insert(id.clone(),job.clone()); drop(jobs);
        emit(job.event.lock().unwrap().clone());
        let manager=self.clone();
        let result_id=id.clone();
        thread::spawn(move || {
            manager.log(&id,"job-start",None);
            let outcome=std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| manager.run(&job,request,&emit)));
            let error=match outcome { Ok(Ok(()))=>None,Ok(Err(error))=>Some(error),Err(_)=>Some(ForgeError::new("INTERNAL","Worker supervisor panicked")) };
            let mut event=job.event.lock().unwrap();
            if let Some(error)=error { event.status=if error.code=="CANCELLED" { "cancelled" } else { "failed" }.into(); event.error=Some(error); }
            else { let failures=event.items.iter().filter(|i|i.error.is_some()).count(); event.status=if failures==0 {"completed"} else if failures==event.items.len() {"failed"} else {"partial"}.into(); }
            event.phase=event.status.clone();
            manager.log(&id,&event.status,event.error.as_ref()); emit(event.clone());
        });
        Ok(result_id)
    }
    fn log(&self,id:&str,phase:&str,error:Option<&ForgeError>) {
        if let Ok(mut log)=OpenOptions::new().create(true).append(true).open(self.log_directory.join(format!("{id}.jsonl"))) {
            // Parser/COM diagnostics may quote document text. Persist only stable error codes.
            let error=error.map(|error|serde_json::json!({"code":error.code}));
            let _=writeln!(log,"{}",serde_json::json!({"jobId":id,"phase":phase,"time":std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_secs(),"error":error}));
            let _=log.flush();
        }
    }
    fn update(&self,job:&Job,emit:&Emit,phase:&str,item:Option<ItemResult>) {
        let mut event=job.event.lock().unwrap(); event.status="running".into(); event.phase=phase.into();
        if let Some(item)=item { event.items.push(item); event.completed+=1; }
        self.log(&event.job_id,phase,event.items.last().and_then(|i|i.error.as_ref())); emit(event.clone());
    }
    fn check_cancel(job:&Job)->ForgeResult<()> { if job.cancel.load(Ordering::SeqCst) { Err(ForgeError::new("CANCELLED","Job cancelled")) } else { Ok(()) } }
    fn commit(job:&Job,transaction:PreparedOutput)->ForgeResult<()> {
        let _gate=job.commit_gate.lock().map_err(|_|ForgeError::new("INTERNAL","Job commit state unavailable"))?;
        Self::check_cancel(job)?; transaction.commit()
    }
    fn execute(&self,job:&Job,task:WorkerTask,timeout:Duration)->ForgeResult<Option<u32>> {
        Self::check_cancel(job)?;
        let root=self.log_directory.parent().unwrap_or(&self.log_directory).join("jobs");
        fs::create_dir_all(&root).map_err(|e|ForgeError::new("WORKER_START",e))?;
        let dir=tempfile::Builder::new().prefix("job-").tempdir_in(&root).map_err(|e|ForgeError::new("WORKER_START",e))?;
        let request=dir.path().join("request.json"); let response=dir.path().join("result.json"); let identity=dir.path().join("word.json");
        fs::write(&request,serde_json::to_vec(&task).map_err(|e|ForgeError::new("INTERNAL",e))?).map_err(|e|ForgeError::new("WORKER_START",e))?;
        let mut command=Command::new(&self.worker);
        command.arg("--worker").arg(dir.path()).env("PDF_FORGE_WORD_IDENTITY",&identity).env("PDF_FORGE_CANCEL_FILE",dir.path().join("cancel")).stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
        #[cfg(windows)] { use std::os::windows::process::CommandExt; command.creation_flags(0x08000000); }
        let mut child=command.spawn().map_err(|e|ForgeError::new("WORKER_START",e))?;
        #[cfg(windows)] let _tree=match ProcessTree::attach(&child) {Ok(tree)=>tree,Err(error)=>{let _=child.kill();let _=child.wait();return Err(error);}};
        let started=Instant::now(); let mut stop:Option<(Instant,&str)>=None;
        loop {
            if let Some(status)=child.try_wait().map_err(|e|ForgeError::new("WORKER_EXIT",e))? {
                cleanup_word(&identity);
                if let Some((_,code))=stop { return Err(ForgeError::new(code,"Worker stopped")); }
                if !status.success() && !response.exists() { return Err(ForgeError::new("WORKER_EXIT",format!("Worker exited {status}"))); }
                let result:WorkerResponse=serde_json::from_slice(&fs::read(&response).map_err(|e|ForgeError::new("WORKER_EXIT",e))?).map_err(|e|ForgeError::new("WORKER_EXIT",e))?;
                return if let Some(error)=result.error { Err(error) } else { Ok(result.pages) };
            }
            if stop.is_none() && (job.cancel.load(Ordering::SeqCst) || started.elapsed()>=timeout) {
                let code=if job.cancel.load(Ordering::SeqCst) {"CANCELLED"} else {"TIMEOUT"};
                let _=fs::write(dir.path().join("cancel"),b"stop");
                stop=Some((Instant::now(),code));
            }
            if let Some((since,code))=stop {
                if since.elapsed()>=Duration::from_secs(5) { let _=child.kill(); let _=child.wait(); cleanup_word(&identity); return Err(ForgeError::new(code,"Worker did not stop within grace period")); }
            }
            thread::sleep(Duration::from_millis(50));
        }
    }
    fn pdf_item(&self,job:&Job,task:WorkerTask,source:&str,output:&Path,overwrite:bool)->ForgeResult<ItemResult> {
        let transaction=PreparedOutput::new(output,overwrite)?;
        let task=with_output(task,transaction.path().to_path_buf());
        let result=self.execute(job,task,Duration::from_secs(300))?;
        pdf::validate(transaction.path())?; Self::commit(job,transaction)?;
        Ok(ItemResult {source:source.into(),output:Some(output.to_string_lossy().into_owned()),pages:result,error:None})
    }
    fn run(&self,job:&Job,request:JobRequest,emit:&Emit)->ForgeResult<()> {
        self.update(job,emit,"preparing",None);
        match request {
            JobRequest::Split{source,directory,ranges,overwrite}=> {
                for range in ranges {
                    Self::check_cancel(job)?;
                    let output=Path::new(&directory).join(safe_name(&range.name)?);
                    let item=self.pdf_item(job,WorkerTask::Split{source:source.clone().into(),first:range.first,last:range.last,output:output.clone()},&source,&output,overwrite)?;
                    self.update(job,emit,"splitting",Some(item));
                }
            }
            JobRequest::Merge{sources,output,overwrite}=> {
                let temp=tempfile::tempdir().map_err(|e|ForgeError::new("OUTPUT_WRITE",e))?;
                let mut prepared=Vec::new();
                for (i,source) in sources.iter().enumerate() {
                    Self::check_cancel(job)?;
                    if source.to_lowercase().ends_with(".pdf") { prepared.push(PathBuf::from(source)); self.update(job,emit,"preparing",Some(ItemResult{source:source.clone(),output:None,pages:None,error:None})); }
                    else {
                        self.update(job,emit,"word",None);
                        let pdf=temp.path().join(format!("{i}.pdf"));
                        match self.execute(job,WorkerTask::WordToPdf{source:source.into(),output:pdf.clone()},Duration::from_secs(300)) {
                            Ok(_)=> {pdf::validate(&pdf)?; prepared.push(pdf); self.update(job,emit,"word",Some(ItemResult{source:source.clone(),output:None,pages:None,error:None}));},
                            Err(e) if e.code=="CANCELLED"=>return Err(e),
                            Err(e)=>self.update(job,emit,"word",Some(ItemResult{source:source.clone(),output:None,pages:None,error:Some(e)})),
                        }
                    }
                }
                if prepared.is_empty() { return Err(ForgeError::new("NO_OUTPUT","No files could be converted")); }
                self.update(job,emit,"merging",None);
                let item=self.pdf_item(job,WorkerTask::Merge{sources:prepared,output:output.clone().into()},"",Path::new(&output),overwrite)?;
                let mut event=job.event.lock().unwrap();
                for source in &mut event.items { if source.error.is_none() { source.output=item.output.clone(); } }
                drop(event);
            }
            JobRequest::Unlock{sources,password,directory}=> {
                for source in sources {
                    Self::check_cancel(job)?;
                    let output=unlock_output(&source,Path::new(&directory));
                    let result=self.pdf_item(job,WorkerTask::Unlock{source:source.clone().into(),password:password.clone(),output:output.clone()},&source,&output,false);
                    let item=item_or_failure(&source,result)?; self.update(job,emit,"unlocking",Some(item));
                }
            }
            JobRequest::PdfToWord{source,output,overwrite}=> {
                self.update(job,emit,"word",None);
                let transaction=PreparedOutput::new(Path::new(&output),overwrite)?;
                self.execute(job,WorkerTask::PdfToWord{source:source.clone().into(),output:transaction.path().to_path_buf()},Duration::from_secs(300))?;
                crate::conversions::validate_docx(transaction.path())?;
                Self::commit(job,transaction)?;
                self.update(job,emit,"committing",Some(ItemResult{source,output:Some(output),pages:None,error:None}));
            }
            JobRequest::Markdown{sources,output,merged,overwrite,options}=> {
                let temp=tempfile::tempdir().map_err(|e|ForgeError::new("OUTPUT_WRITE",e))?;
                let mut prepared=Vec::new(); let mut any_failed=false;
                for (i,source) in sources.iter().enumerate() {
                    Self::check_cancel(job)?;
                    self.update(job,emit,"markdown",None);
                    let target=if merged {temp.path().join(format!("{i}.pdf"))} else {Path::new(&output).join(format!("{}.pdf",Path::new(source).file_stem().unwrap().to_string_lossy()))};
                    let transaction=PreparedOutput::new(&target,if merged {false} else {overwrite})?;
                    let outcome=self.execute(job,WorkerTask::Markdown{source:source.into(),output:transaction.path().to_path_buf(),options:options.clone()},Duration::from_secs(120)).and_then(|_|pdf::validate(transaction.path())).and_then(|pages| { Self::commit(job,transaction)?;Ok(ItemResult{source:source.clone(),output:if merged {None} else {Some(target.to_string_lossy().into_owned())},pages:Some(pages),error:None}) });
                    let item=item_or_failure(source,outcome)?;
                    if item.error.is_some() {any_failed=true;} else {prepared.push(target);}
                    self.update(job,emit,"markdown",Some(item));
                }
                if merged {
                    if any_failed { return Err(ForgeError::new("BATCH_FAILED","Merged output was not written because a document failed")); }
                    self.update(job,emit,"merging",None);
                    let item=self.pdf_item(job,WorkerTask::Merge{sources:prepared,output:output.clone().into()},"",Path::new(&output),overwrite)?;
                    let mut event=job.event.lock().unwrap();for source in &mut event.items {source.output=item.output.clone();}
                }
            }
        }
        Ok(())
    }
}

fn item_or_failure(source:&str,result:ForgeResult<ItemResult>)->ForgeResult<ItemResult> {
    match result {Ok(item)=>Ok(item),Err(e) if e.code=="CANCELLED"=>Err(e),Err(e)=>Ok(ItemResult{source:source.into(),output:None,pages:None,error:Some(e)})}
}
fn unlock_output(source:&str,directory:&Path)->PathBuf {
    let stem=Path::new(source).file_stem().unwrap_or_default().to_string_lossy();
    for i in 0.. { let suffix=if i==0 {String::new()} else {format!("_{i}")}; let path=directory.join(format!("{stem}_unlocked{suffix}.pdf")); if !path.exists() {return path;} } unreachable!()
}
pub fn validate_request(request:&JobRequest)->ForgeResult<()> {
    let (sources,outputs,overwrite):(Vec<PathBuf>,Vec<PathBuf>,bool)=match request {
        JobRequest::Split{source,directory,ranges,overwrite}=> {
            if ranges.is_empty() || ranges.iter().any(|r|r.first==0 || r.last<r.first) {return Err(ForgeError::new("RANGE","Choose valid page ranges"));}
            (vec![source.into()],ranges.iter().map(|r|safe_name(&r.name).map(|n|Path::new(directory).join(n))).collect::<ForgeResult<_>>()?,*overwrite)
        },
        JobRequest::Merge{sources,output,overwrite}=>(sources.iter().map(PathBuf::from).collect(),vec![output.into()],*overwrite),
        JobRequest::Unlock{sources,password,directory}=> {
            if password.is_empty() {return Err(ForgeError::new("PASSWORD_REQUIRED","Enter the known password"));}
            if !Path::new(directory).is_dir() {return Err(ForgeError::new("PATH","Select an output directory"));}
            (sources.iter().map(PathBuf::from).collect(),vec![],false)
        },
        JobRequest::PdfToWord{source,output,overwrite}=>(vec![source.into()],vec![output.into()],*overwrite),
        JobRequest::Markdown{sources,output,merged,overwrite,..}=>(sources.iter().map(PathBuf::from).collect(),if *merged {vec![output.into()]} else {sources.iter().map(|s|Path::new(output).join(format!("{}.pdf",Path::new(s).file_stem().unwrap_or_default().to_string_lossy()))).collect()},*overwrite),
    };
    if sources.is_empty() {return Err(ForgeError::new("NO_FILES","Choose input files"));}
    for path in &sources {
        if !path.is_file() {return Err(ForgeError::new("SOURCE_MISSING","Selected file is unavailable"));}
        let extension=path.extension().unwrap_or_default().to_string_lossy().to_lowercase();
        let allowed=match request {JobRequest::Merge{..}=>["pdf","doc","docx"].as_slice(),JobRequest::Markdown{..}=>["md","markdown"].as_slice(),_=>["pdf"].as_slice()};
        if !allowed.contains(&extension.as_str()) {return Err(ForgeError::new("FILE_TYPE","Unsupported file type"));}
    }
    let expected_ext=if matches!(request,JobRequest::PdfToWord{..}) {"docx"} else {"pdf"};
    if outputs.iter().any(|p| !p.extension().unwrap_or_default().to_string_lossy().eq_ignore_ascii_case(expected_ext)) {return Err(ForgeError::new("FILE_TYPE","Invalid output extension"));}
    check_outputs(&sources,&outputs,overwrite)
}
fn with_output(task:WorkerTask,output:PathBuf)->WorkerTask {
    match task {WorkerTask::Split{source,first,last,..}=>WorkerTask::Split{source,first,last,output},WorkerTask::Merge{sources,..}=>WorkerTask::Merge{sources,output},WorkerTask::Unlock{source,password,..}=>WorkerTask::Unlock{source,password,output},WorkerTask::WordToPdf{source,..}=>WorkerTask::WordToPdf{source,output},WorkerTask::PdfToWord{source,..}=>WorkerTask::PdfToWord{source,output},WorkerTask::Markdown{source,options,..}=>WorkerTask::Markdown{source,output,options}}
}

pub fn worker_main(directory:&Path)->ForgeResult<()> {
    let task:WorkerTask=serde_json::from_slice(&fs::read(directory.join("request.json")).map_err(|e|ForgeError::new("WORKER_REQUEST",e))?).map_err(|e|ForgeError::new("WORKER_REQUEST",e))?;
    let result:ForgeResult<Option<u32>>=match task {
        WorkerTask::Split{source,first,last,output}=>pdf::split_to(&source,first,last,&output).map(Some),
        WorkerTask::Merge{sources,output}=>pdf::merge_to(&sources,&output).map(Some),
        WorkerTask::Unlock{source,password,output}=>pdf::unlock_to(&source,&password,&output).map(Some),
        WorkerTask::WordToPdf{source,output}=>crate::conversions::word_to_pdf(&source,&output).and_then(|_|normalize_generated(&output)).map(Some),
        WorkerTask::PdfToWord{source,output}=>pdf::validate(&source).and_then(|_|crate::conversions::pdf_to_word(&source,&output)).map(|_|None),
        WorkerTask::Markdown{source,output,options}=>crate::conversions::markdown_to_pdf(&source,&output,&options).and_then(|_|normalize_generated(&output)).map(Some),
    };
    let response=match result {Ok(pages)=>WorkerResponse{pages,error:None},Err(error)=>WorkerResponse{pages:None,error:Some(error)}};
    fs::write(directory.join("result.json"),serde_json::to_vec(&response).map_err(|e|ForgeError::new("INTERNAL",e))?).map_err(|e|ForgeError::new("WORKER_EXIT",e))
}

// Only call after converters created a NEW supervisor-owned staging file. User PDFs
// retain the strict unsupported-structure policy and are never normalized in place.
fn normalize_generated(path:&Path)->ForgeResult<u32> {
    let transaction=PreparedOutput::new(path,true)?;
    let pages=pdf::normalize_generated_pdf(path,transaction.path())?;
    transaction.commit()?;Ok(pages)
}

#[cfg(windows)]
struct ProcessTree(windows::Win32::Foundation::HANDLE);
#[cfg(windows)]
impl ProcessTree {
    fn attach(child:&std::process::Child)->ForgeResult<Self> {
        use std::os::windows::io::AsRawHandle;
        use windows::Win32::{Foundation::{CloseHandle,HANDLE}, System::JobObjects::*};
        unsafe {
            let handle=CreateJobObjectW(None,None).map_err(|e|ForgeError::new("WORKER_START",e))?;
            let mut info=JOBOBJECT_EXTENDED_LIMIT_INFORMATION::default(); info.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if let Err(error)=SetInformationJobObject(handle,JobObjectExtendedLimitInformation,&info as *const _ as *const _,std::mem::size_of_val(&info) as u32).and_then(|_|AssignProcessToJobObject(handle,HANDLE(child.as_raw_handle()))) {let _=CloseHandle(handle);return Err(ForgeError::new("WORKER_START",error));}
            Ok(Self(handle))
        }
    }
}
#[cfg(windows)] impl Drop for ProcessTree {fn drop(&mut self){unsafe{let _=windows::Win32::Foundation::CloseHandle(self.0);}}}
#[cfg(windows)]
fn cleanup_word(marker:&Path) {
    use windows::Win32::{Foundation::{CloseHandle,FILETIME}, System::Threading::{OpenProcess,GetProcessTimes,TerminateProcess,PROCESS_QUERY_LIMITED_INFORMATION,PROCESS_TERMINATE}};
    let Ok(bytes)=fs::read(marker) else {return;}; let Ok(value)=serde_json::from_slice::<serde_json::Value>(&bytes) else {return;};
    let (Some(pid),Some(created))=(value["pid"].as_u64(),value["creationTime"].as_u64()) else {return;};
    unsafe { if let Ok(handle)=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_TERMINATE,false,pid as u32) {
        let(mut creation,mut exit,mut kernel,mut user)=(FILETIME::default(),FILETIME::default(),FILETIME::default(),FILETIME::default());
        if GetProcessTimes(handle,&mut creation,&mut exit,&mut kernel,&mut user).is_ok() && (((creation.dwHighDateTime as u64)<<32)|creation.dwLowDateTime as u64)==created {let _=TerminateProcess(handle,1);}
        let _=CloseHandle(handle);
    }}
}
#[cfg(not(windows))] fn cleanup_word(_: &Path) {}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn logs_keep_error_codes_without_document_or_password_diagnostics() {
        let directory=tempfile::tempdir().unwrap();
        let manager=JobManager::new(directory.path().join("logs"),PathBuf::from("unused-worker")).unwrap();
        manager.log("test","markdown",Some(&ForgeError::new("MARKDOWN_MERMAID","sensitive-document-text private-password")));
        let log=fs::read_to_string(manager.log_directory.join("test.jsonl")).unwrap();
        let record:serde_json::Value=serde_json::from_str(log.trim()).unwrap();
        assert_eq!(record["error"]["code"],"MARKDOWN_MERMAID");
        assert!(!log.contains("sensitive-document-text"));assert!(!log.contains("private-password"));
    }
    #[test]
    fn cancel_all_waiting_for_commit_does_not_lock_job_registry() {
        let directory=tempfile::tempdir().unwrap();
        let manager=Arc::new(JobManager::new(directory.path().join("logs"),PathBuf::from("unused-worker")).unwrap());
        let job=Arc::new(Job{event:Mutex::new(JobEvent{job_id:"test".into(),status:"running".into(),phase:"committing".into(),completed:0,total:1,items:vec![],error:None}),cancel:AtomicBool::new(false),commit_gate:Mutex::new(())});
        manager.jobs.lock().unwrap().insert("test".into(),job.clone());
        let gate=job.commit_gate.lock().unwrap();
        let cancellation=manager.clone();
        let thread=thread::spawn(move ||cancellation.cancel_all());
        let deadline=Instant::now()+Duration::from_secs(2);
        while Arc::strong_count(&job)<3 && Instant::now()<deadline {thread::yield_now();}
        let mut registry_available=false;
        while Instant::now()<deadline {
            if manager.jobs.try_lock().is_ok(){registry_available=true;break;}
            thread::yield_now();
        }
        let reached_gate=Arc::strong_count(&job)>=3;
        drop(gate);thread.join().unwrap();
        assert!(reached_gate,"Cancellation never reached its blocked commit gate");
        assert!(registry_available,"Cancellation blocked readers of the job registry");
        assert!(job.cancel.load(Ordering::SeqCst));
    }
    #[test]
    fn cancellation_before_commit_preserves_previous_output_and_removes_staging() {
        let directory=tempfile::tempdir().unwrap();let output=directory.path().join("output.pdf");
        fs::write(&output,b"previous output").unwrap();
        let transaction=PreparedOutput::new(&output,true).unwrap();let staging=transaction.path().to_path_buf();
        fs::write(&staging,b"new output").unwrap();
        let job=Job{event:Mutex::new(JobEvent{job_id:"test".into(),status:"running".into(),phase:"committing".into(),completed:0,total:1,items:vec![],error:None}),cancel:AtomicBool::new(true),commit_gate:Mutex::new(())};
        assert_eq!(JobManager::commit(&job,transaction).unwrap_err().code,"CANCELLED");
        assert_eq!(fs::read(&output).unwrap(),b"previous output");assert!(!staging.exists());
    }
}
