//! Native regression probe: cargo build --examples; security_probe worker_native.exe worker_fault.exe
use lopdf::{dictionary,Document,Object,Stream};
use pdf_forge::{model::{ForgeResult,JobRequest,SplitRange},supervisor::{JobManager,WorkerTask}};
use sha2::{Digest,Sha256};
use std::{fs,io::Write,path::{Path,PathBuf},process::{Command,Stdio},sync::Arc,time::{Duration,Instant}};

fn fixture(path:&Path,count:u32) {
    let mut d=Document::with_version("1.7");let pages=d.new_object_id();
    let font=d.add_object(dictionary!{"Type"=>"Font","Subtype"=>"Type1","BaseFont"=>"Helvetica"});
    let mut ids=Vec::new();
    for n in 1..=count {
        let text=format!("BT /F1 16 Tf 30 740 Td (PAGE-{n:04} Title) Tj 0 -30 Td (Chapter {n} rich paragraph.) Tj 0 -30 Td (Table item quantity price.) Tj ET");
        let content=d.add_object(Stream::new(dictionary!{},text.into_bytes()));
        ids.push(d.add_object(dictionary!{"Type"=>"Page","Parent"=>pages,"Contents"=>content,"MediaBox"=>vec![0.into(),0.into(),612.into(),792.into()],"Resources"=>dictionary!{"Font"=>dictionary!{"F1"=>font}}}));
    }
    d.objects.insert(pages,dictionary!{"Type"=>"Pages","Count"=>count,"Kids"=>ids.iter().copied().map(Object::Reference).collect::<Vec<_>>()}.into());
    let catalog=d.add_object(dictionary!{"Type"=>"Catalog","Pages"=>pages});d.trailer.set("Root",catalog);d.trailer.set("ID",vec![Object::string_literal(vec![1u8;16]),Object::string_literal(vec![1u8;16])]);d.save(path).unwrap();
}
fn hash(path:&Path)->Vec<u8>{Sha256::digest(fs::read(path).unwrap()).to_vec()}
fn query(manager:&JobManager,id:&str,path:&Path)->ForgeResult<pdf_forge::model::PdfInfo>{manager.register_pdf_info(id).unwrap();manager.pdf_info(id,path.to_path_buf())}
fn wait_job(manager:&JobManager,id:&str)->pdf_forge::model::JobEvent{let start=Instant::now();loop{let event=manager.get(id).unwrap();if matches!(event.status.as_str(),"completed"|"partial"|"failed"|"cancelled"){return event;}assert!(start.elapsed()<Duration::from_secs(60));std::thread::sleep(Duration::from_millis(20));}}
#[cfg(windows)] fn assert_dead(marker:&Path){use windows::Win32::System::Threading::{OpenProcess,GetExitCodeProcess,PROCESS_QUERY_LIMITED_INFORMATION};use windows::Win32::Foundation::{CloseHandle,STILL_ACTIVE};let pid:u32=fs::read_to_string(marker).unwrap().parse().unwrap();unsafe{if let Ok(handle)=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,false,pid){let mut code=0;GetExitCodeProcess(handle,&mut code).unwrap();assert_ne!(code,STILL_ACTIVE.0 as u32);let _=CloseHandle(handle);}}}

fn main(){
    let args:Vec<_>=std::env::args_os().collect();assert_eq!(args.len(),3,"security_probe actual_worker.exe fault_worker.exe");
    let temp=tempfile::tempdir().unwrap();let source=temp.path().join("forty.pdf");fixture(&source,40);let before=hash(&source);
    eprintln!("probe: fixture ready");
    let gated=temp.path().join("gated");fs::create_dir(&gated).unwrap();
    fs::write(gated.join("request.json"),serde_json::to_vec(&WorkerTask::Info{source:source.clone()}).unwrap()).unwrap();
    let mut gated_child=Command::new(&args[1]).arg("--worker").arg(&gated).stdin(Stdio::piped()).stdout(Stdio::null()).stderr(Stdio::null()).spawn().unwrap();
    std::thread::sleep(Duration::from_millis(200));assert!(gated_child.try_wait().unwrap().is_none());assert!(!gated.join("result.json").exists());
    gated_child.stdin.take().unwrap().write_all(&[1]).unwrap();assert!(gated_child.wait().unwrap().success());assert!(gated.join("result.json").exists());
    eprintln!("probe: process start gate ready");
    let actual=Arc::new(JobManager::new(temp.path().join("actual-logs"),PathBuf::from(&args[1])).unwrap());
    let diag=temp.path().join("diag.txt");std::env::set_var("PDF_FORGE_TEST_DIAG",&diag);
    let info_result=query(&actual,"forty",&source);if info_result.is_err() {eprintln!("worker diag={:?}",fs::read_to_string(&diag));}
    let info=info_result.unwrap();assert_eq!(info.pages,40);assert!(!info.encrypted);assert!(!actual.active());
    eprintln!("probe: 40 pages ready");
    let cuts=[7,14,21,28,34,40];let mut first=1;let mut ranges=Vec::new();
    for (i,last) in cuts.into_iter().enumerate(){ranges.push(SplitRange{first,last,name:format!("part-{i}.pdf")});first=last+1;}
    let id=actual.start(JobRequest::Split{source:source.to_string_lossy().into_owned(),directory:temp.path().to_string_lossy().into_owned(),ranges,overwrite:false},Arc::new(|_|{})).unwrap();
    let event=wait_job(&actual,&id);assert_eq!(event.status,"completed","{:?}",event.error);assert_eq!(event.items.len(),6);
    let mut parts=Vec::new();for (i,pages) in [7,7,7,7,6,6].into_iter().enumerate(){let path=temp.path().join(format!("part-{i}.pdf"));let doc=Document::load(&path).unwrap();assert_eq!(doc.get_pages().len(),pages);parts.push(path);}
    assert_eq!(hash(&source),before);
    let merged=temp.path().join("merged.pdf");let id=actual.start(JobRequest::Merge{sources:parts.iter().map(|p|p.to_string_lossy().into_owned()).collect(),output:merged.to_string_lossy().into_owned(),overwrite:false},Arc::new(|_|{})).unwrap();
    let event=wait_job(&actual,&id);assert_eq!(event.status,"completed","{:?}",event.error);assert_eq!(Document::load(&merged).unwrap().get_pages().len(),40);assert_eq!(hash(&source),before);
    eprintln!("probe: split and merge ready");
    let mut d=Document::load(&source).unwrap();
    let encrypted=temp.path().join("encrypted.pdf");
    let state=lopdf::EncryptionState::try_from(lopdf::EncryptionVersion::V2{document:&d,owner_password:"owner",user_password:"reader",key_length:128,permissions:lopdf::Permissions::PRINTABLE}).unwrap();
    d.encrypt(&state).unwrap();d.save(&encrypted).unwrap();assert!(query(&actual,"encrypted",&encrypted).unwrap().encrypted);
    eprintln!("probe: encrypted ready");
    let huge=temp.path().join("huge.pdf");fs::File::create(&huge).unwrap().set_len(256*1024*1024+1).unwrap();
    assert_eq!(query(&actual,"huge",&huge).unwrap_err().code,"FILE_SIZE");
    let over=temp.path().join("over.pdf");fixture(&over,1001);assert_eq!(query(&actual,"pages",&over).unwrap_err().code,"FILE_PAGES");
    let encrypted_over=temp.path().join("encrypted-over.pdf");let mut d=Document::load(&over).unwrap();
    let state=lopdf::EncryptionState::try_from(lopdf::EncryptionVersion::V2{document:&d,owner_password:"owner",user_password:"reader",key_length:128,permissions:lopdf::Permissions::PRINTABLE}).unwrap();
    d.encrypt(&state).unwrap();d.save(&encrypted_over).unwrap();assert!(query(&actual,"encrypted-over",&encrypted_over).unwrap().encrypted);
    eprintln!("probe: caps ready");
    let cyclic=temp.path().join("cyclic.pdf");fixture(&cyclic,20);
    let mut d=Document::load(&cyclic).unwrap();let first=d.get_pages()[&1];d.get_object_mut(first).unwrap().as_dict_mut().unwrap().set("Parent",first);d.save(&cyclic).unwrap();
    let cyclic_hash=hash(&cyclic);let result=query(&actual,"cyclic",&cyclic);assert!(result.is_err());assert_eq!(hash(&cyclic),cyclic_hash);
    eprintln!("probe: cyclic ready");
    let deep=temp.path().join("deep.pdf");fixture(&deep,20);
    let mut d=Document::load(&deep).unwrap();let root=d.catalog().unwrap().get(b"Pages").unwrap().as_reference().unwrap();
    let original_kids=d.get_object(root).unwrap().as_dict().unwrap().get(b"Kids").unwrap().as_array().unwrap().clone();
    let mut current=root;
    for _ in 0..200 {let next=d.new_object_id();d.objects.insert(next,dictionary!{"Type"=>"Pages","Parent"=>current,"Count"=>20,"Kids"=>Vec::<Object>::new()}.into());d.get_object_mut(current).unwrap().as_dict_mut().unwrap().set("Kids",vec![Object::Reference(next)]);current=next;}
    for kid in &original_kids {let id=kid.as_reference().unwrap();d.get_object_mut(id).unwrap().as_dict_mut().unwrap().set("Parent",current);}
    d.get_object_mut(current).unwrap().as_dict_mut().unwrap().set("Kids",original_kids);d.save(&deep).unwrap();
    let deep_hash=hash(&deep);assert_eq!(query(&actual,"deep",&deep).unwrap_err().code,"pdf_invalid");assert_eq!(hash(&deep),deep_hash);
    eprintln!("probe: deep ready");
    assert_eq!(hash(&source),before);assert!(!actual.active());

    let fault=Arc::new(JobManager::new(temp.path().join("fault-logs"),PathBuf::from(&args[2])).unwrap());
    let crash=temp.path().join("crash.pdf");assert_eq!(query(&fault,"crash",&crash).unwrap_err().code,"WORKER_EXIT");
    eprintln!("probe: crash ready");
    let excess=temp.path().join("large-response.pdf");assert_eq!(query(&fault,"excess",&excess).unwrap_err().code,"WORKER_EXIT");
    eprintln!("probe: excess ready");
    let cpu=temp.path().join("cpu.pdf");let began=Instant::now();assert_eq!(query(&fault,"cpu",&cpu).unwrap_err().code,"WORKER_EXIT");assert!(began.elapsed()<Duration::from_secs(15));
    eprintln!("probe: CPU cap ready");
    fault.register_pdf_info("cancel-before-start").unwrap();fault.cancel_pdf_info("cancel-before-start").unwrap();
    assert_eq!(fault.pdf_info("cancel-before-start",crash.clone()).unwrap_err().code,"CANCELLED");
    eprintln!("probe: prestart cancel ready");
    let timed=temp.path().join("hang-timeout.pdf");let began=Instant::now();
    assert_eq!(query(&fault,"timeout",&timed).unwrap_err().code,"TIMEOUT");assert!(began.elapsed()<Duration::from_secs(19));
    #[cfg(windows)] assert_dead(&timed.with_extension("pid"));
    eprintln!("probe: timeout ready");
    let hang=temp.path().join("hang.pdf");fault.register_pdf_info("cancel-running").unwrap();
    let runner=fault.clone();let source=hang.clone();let thread=std::thread::spawn(move ||runner.pdf_info("cancel-running",source));
    let marker=hang.with_extension("pid");let started=Instant::now();while !marker.exists() && started.elapsed()<Duration::from_secs(5){std::thread::sleep(Duration::from_millis(10));}assert!(marker.exists());
    fault.cancel_pdf_info("cancel-running").unwrap();assert_eq!(thread.join().unwrap().unwrap_err().code,"CANCELLED");
    #[cfg(windows)] assert_dead(&marker);
    assert!(!fault.active());
    println!("native security probe passed: 40-page metadata, encryption, caps, cyclic/deep/huge, crash, excess response, pre-start/running cancellation, source hash, no active worker");
}
