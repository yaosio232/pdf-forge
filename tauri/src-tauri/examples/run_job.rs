//! Direct functional runner. Uses the same supervisor and actual worker executable as the desktop.
use pdf_forge::{model::JobRequest,supervisor::JobManager};
use std::{path::PathBuf,sync::Arc,time::{Duration,Instant}};
fn main(){
    let args:Vec<_>=std::env::args_os().collect();
    assert!(args.len()>=4,"run_job request.json worker.exe result.json [cancel-after-ms]");
    let request:JobRequest=serde_json::from_slice(&std::fs::read(&args[1]).unwrap()).unwrap();
    let request=if args.iter().any(|arg|arg=="--auto"){pdf_forge::output_plan::automatic_output(request).unwrap()}else{request};
    let result=PathBuf::from(&args[3]);
    let manager=Arc::new(JobManager::new(result.parent().unwrap().join("logs"),PathBuf::from(&args[2])).unwrap());
    let id=match manager.start(request,Arc::new(|_|{})){Ok(id)=>id,Err(error)=>{
        std::fs::write(result,serde_json::to_vec_pretty(&serde_json::json!({"status":"rejected","error":error})).unwrap()).unwrap();return;
    }};
    let started=Instant::now();let mut cancelled=false;
    loop{
        if !cancelled && args.iter().any(|arg|arg=="--cancel-on-signal") && result.with_extension("cancel").exists(){
            manager.cancel(&id).unwrap();cancelled=true;
        }
        if !cancelled && args.iter().any(|arg|arg=="--cancel-after-worker-start") && std::fs::read_to_string(manager.log_directory.join(format!("{id}.jsonl"))).unwrap_or_default().contains("\"phase\":\"worker-start\""){
            manager.cancel(&id).unwrap();cancelled=true;
        }
        if !cancelled && args.get(4).and_then(|a|a.to_str()).and_then(|a|a.parse::<u64>().ok()).is_some_and(|ms|started.elapsed()>=Duration::from_millis(ms)){
            manager.cancel(&id).unwrap();cancelled=true;
        }
        let event=manager.get(&id).unwrap();
        if matches!(event.status.as_str(),"completed"|"partial"|"cancelled"|"failed"){
            std::fs::write(&result,serde_json::to_vec_pretty(&event).unwrap()).unwrap();println!("{}",event.status);return;
        }
        assert!(started.elapsed()<Duration::from_secs(900),"Supervisor did not terminate");
        std::thread::sleep(Duration::from_millis(20));
    }
}
