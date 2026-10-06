"""Force a private native worker to hang; exercise real cancel/timeout, never UI."""
import argparse, ctypes, hashlib, json, os, pathlib, shutil, subprocess, time
from ctypes import wintypes as W
from worker_resources import runtime_directory

kernel = ctypes.WinDLL('kernel32', use_last_error=True)
ntdll = ctypes.WinDLL('ntdll')
kernel.OpenProcess.argtypes=[W.DWORD,W.BOOL,W.DWORD];kernel.OpenProcess.restype=W.HANDLE
kernel.CloseHandle.argtypes=[W.HANDLE]
kernel.WaitForSingleObject.argtypes=[W.HANDLE,W.DWORD]
kernel.GetProcessTimes.argtypes=[W.HANDLE,ctypes.POINTER(W.FILETIME),ctypes.POINTER(W.FILETIME),ctypes.POINTER(W.FILETIME),ctypes.POINTER(W.FILETIME)]
ntdll.NtSuspendProcess.argtypes=[W.HANDLE];ntdll.NtResumeProcess.argtypes=[W.HANDLE]
class Entry(ctypes.Structure):
    _fields_=[('size',W.DWORD),('usage',W.DWORD),('pid',W.DWORD),('heap',ctypes.c_size_t),('module',W.DWORD),('threads',W.DWORD),('parent',W.DWORD),('priority',W.LONG),('flags',W.DWORD),('name',W.WCHAR*260)]
kernel.CreateToolhelp32Snapshot.argtypes=[W.DWORD,W.DWORD];kernel.CreateToolhelp32Snapshot.restype=W.HANDLE
kernel.Process32FirstW.argtypes=[W.HANDLE,ctypes.POINTER(Entry)];kernel.Process32NextW.argtypes=[W.HANDLE,ctypes.POINTER(Entry)]
def children(parent,name):
    snapshot=kernel.CreateToolhelp32Snapshot(2,0);entry=Entry();entry.size=ctypes.sizeof(entry);found=[]
    try:
        more=kernel.Process32FirstW(snapshot,ctypes.byref(entry))
        while more:
            if entry.parent==parent and entry.name.lower()==name.lower():found.append(entry.pid)
            more=kernel.Process32NextW(snapshot,ctypes.byref(entry))
    finally:kernel.CloseHandle(snapshot)
    return found
def own_handle(pid,creation=None):
    handle=kernel.OpenProcess(0x0800|0x1000|0x100000,False,pid);assert handle,ctypes.get_last_error()
    values=[W.FILETIME() for _ in range(4)]
    assert kernel.GetProcessTimes(handle,*[ctypes.byref(v) for v in values])
    ticks=(values[0].dwHighDateTime<<32)|values[0].dwLowDateTime
    assert creation is None or ticks==creation,'PID identity changed'
    return handle,ticks
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=pathlib.Path);parser.add_argument('runner',type=pathlib.Path);parser.add_argument('worker',type=pathlib.Path);parser.add_argument('fixtures',type=pathlib.Path);a=parser.parse_args()
    root=a.directory.resolve();root.mkdir(parents=True,exist_ok=False);reports=[]
    runtime=runtime_directory(a.worker,root)
    env={**os.environ,'PATH':str(runtime)+os.pathsep+os.environ['PATH'],'PDF_FORGE_MARKDOWN_ASSETS':str(runtime/'resources/markdown')}
    for kind in ['word-cancel','markdown-timeout']:
        folder=root/kind;folder.mkdir();result=folder/'result.json';request=folder/'request.json'
        if kind=='word-cancel':
            source=a.fixtures.resolve()/'synthetic.pdf';output=folder/'existing.docx';shutil.copyfile(a.fixtures/'synthetic.docx',output)
            task=dict(kind='pdfToWord',source=str(source),output=str(output),overwrite=True)
        else:
            source=a.fixtures.resolve()/'advanced.md';output=folder/'existing.pdf';shutil.copyfile(a.fixtures/'synthetic.pdf',output)
            task=dict(kind='markdown',sources=[str(source)],output=str(output),merged=True,overwrite=True,options=dict(toc=True,header='',footer='',pageNumbers=True))
        before=sha(source);existing=sha(output);request.write_text(json.dumps(task),encoding='utf-8')
        command=[str(a.runner.resolve()),str(request),str(a.worker.resolve()),str(result)]
        if kind=='word-cancel':command.append('--cancel-on-signal')
        started=time.monotonic();process=subprocess.Popen(command,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=subprocess.CREATE_NO_WINDOW)
        handle=None
        try:
            deadline=time.monotonic()+20
            while handle is None:
                assert process.poll() is None,'Job finished before the hang injection'
                assert time.monotonic()<deadline,'Private worker not found'
                if kind=='word-cancel':
                    markers=list((folder/'jobs').glob('job-*/word.json'))
                    if markers:
                        identity=json.loads(markers[0].read_text());handle,ticks=own_handle(identity['pid'],identity['creationTime'])
                else:
                    pids=children(process.pid,a.worker.name)
                    if runtime!=a.worker.resolve().parent:
                        pids=[pid for parent in pids for pid in children(parent,'PDF Forge 2.exe')]
                    if pids:
                        assert len(pids)==1;handle,ticks=own_handle(pids[0]);identity=dict(pid=pids[0],creationTime=ticks)
                time.sleep(.01)
            assert ntdll.NtSuspendProcess(handle)==0
            suspended=time.monotonic()
            if kind=='word-cancel':result.with_suffix('.cancel').touch()
            out,err=process.communicate(timeout=145)
            assert process.returncode==0,(process.returncode,err.decode(errors='replace'))
            event=json.loads(result.read_text());expected='CANCELLED' if kind=='word-cancel' else 'TIMEOUT'
            errors=[event.get('error')]+[item.get('error') for item in event['items']]
            assert any(error and error['code']==expected for error in errors),event
            assert sha(source)==before and sha(output)==existing,'Source or existing output changed'
            assert kernel.WaitForSingleObject(handle,5000)==0,'Hung owned process survived'
            elapsed=time.monotonic()-suspended
            assert elapsed<14 if kind=='word-cancel' else 119<=elapsed<140
            reports.append(dict(id=kind,status='PASS',forcedHangIdentity=identity,seconds=round(elapsed,2),event=event,sourceSha256=before,existingOutputSha256=existing,workerExeSha256=sha(a.worker)))
        finally:
            if handle:
                if kernel.WaitForSingleObject(handle,0)!=0:ntdll.NtResumeProcess(handle)
                kernel.CloseHandle(handle)
            if process.poll() is None:process.kill();process.communicate()
        print(json.dumps(reports[-1]),flush=True)
    (root/'results.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')
if __name__=='__main__':main()
