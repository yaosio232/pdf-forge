"""Prepare the real single-file delivery without opening the application UI."""
import json,pathlib,subprocess
def runtime_directory(executable,report_folder):
    executable=pathlib.Path(executable).resolve()
    if (executable.parent/'resources/markdown/renderer.js').is_file():return executable.parent
    prepared=pathlib.Path(report_folder)/'prepared-runtime.json'
    result=subprocess.run([str(executable),'--prepare',str(prepared.resolve())],capture_output=True,timeout=40,creationflags=subprocess.CREATE_NO_WINDOW)
    assert result.returncode==0,(result.returncode,result.stderr)
    directory=pathlib.Path(json.loads(prepared.read_text(encoding='utf-8'))['directory'])
    assert (directory/'PDF Forge 2.exe').is_file() and (directory/'resources/markdown/renderer.js').is_file()
    return directory
