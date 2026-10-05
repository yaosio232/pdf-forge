use pdf_forge::{model::*,supervisor::{validate_request,JobManager}};
use std::{fs,sync::Arc};

#[test]
fn markdown_batch_rejects_duplicate_names_without_touching_outputs() {
    let dir=tempfile::tempdir().unwrap();
    let a=dir.path().join("a");let b=dir.path().join("b");fs::create_dir(&a).unwrap();fs::create_dir(&b).unwrap();
    let sources=vec![a.join("same.md"),b.join("same.md")];for source in &sources{fs::write(source,"# A document").unwrap();}
    let request=JobRequest::Markdown{sources:sources.iter().map(|s|s.to_string_lossy().into_owned()).collect(),output:dir.path().to_string_lossy().into_owned(),merged:false,overwrite:false,options:MarkdownOptions::default()};
    assert_eq!(validate_request(&request).unwrap_err().code,"DUPLICATE_OUTPUT");
    assert!(!dir.path().join("same.pdf").exists());
}
#[test]
fn job_state_reports_missing_job_and_starts_idle() {
    let dir=tempfile::tempdir().unwrap();
    let manager=Arc::new(JobManager::new(dir.path().join("logs"),dir.path().join("worker.exe")).unwrap());
    assert!(!manager.active());
    assert_eq!(manager.get("unknown").unwrap_err().code,"JOB_MISSING");
    assert_eq!(manager.cancel("unknown").unwrap_err().code,"JOB_MISSING");
}
#[test]
fn tagged_requests_roundtrip_the_frontend_wire_format() {
    let value=serde_json::json!({"kind":"markdown","sources":["a.md"],"output":"out.pdf","merged":true,"overwrite":false,"options":{"toc":true,"header":"Title","footer":"Footer","pageNumbers":true}});
    let request:JobRequest=serde_json::from_value(value.clone()).unwrap();
    assert_eq!(serde_json::to_value(request).unwrap(),value);
}
