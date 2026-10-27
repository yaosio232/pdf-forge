//! Test-only child that verifies the supervisor rejects crash, timeout and excess output.
use std::{fs,io::Read,path::Path,time::Duration};

fn main() {
    let args: Vec<_> = std::env::args_os().collect();
    if args.len()!=3 || args[1]!="--worker" {std::process::exit(2);}
    let mut gate=[0];
    if std::io::stdin().read_exact(&mut gate).is_err() || gate!=[1] {std::process::exit(3);}
    let dir=Path::new(&args[2]);
    let request:serde_json::Value=serde_json::from_slice(&fs::read(dir.join("request.json")).unwrap()).unwrap();
    let name=Path::new(request["source"].as_str().unwrap_or("")).file_name().unwrap_or_default().to_string_lossy();
    if name=="hang.pdf" || name=="hang-timeout.pdf" {let _=fs::write(Path::new(request["source"].as_str().unwrap()).with_extension("pid"),std::process::id().to_string());}
    match name.as_ref() {
        "crash.pdf"=>{fs::write(dir.join("result.json"),br#"{"pages":null,"info":{"pages":20,"encrypted":false},"error":null}"#).unwrap();std::process::exit(7);}
        "large-response.pdf"=>{fs::write(dir.join("result.json"),vec![b'x';65537]).unwrap();}
        "cpu.pdf"=>{let workers=(0..4).map(|_|std::thread::spawn(||{let start=std::time::Instant::now();while start.elapsed()<Duration::from_secs(30){std::hint::black_box(123456789u64.wrapping_mul(987654321));}})).collect::<Vec<_>>();for worker in workers{let _=worker.join();}}
        "hang.pdf"|"hang-timeout.pdf"=>std::thread::sleep(Duration::from_secs(30)),
        _=>{}
    }
}
