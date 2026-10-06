"""Copy exact dependency license/notice files from local build packages, no network."""
import hashlib, json, pathlib, shutil, sys
root = pathlib.Path(__file__).resolve().parents[1]
out = root / 'tauri/public/licenses'
out.mkdir(parents=True, exist_ok=True)
manifest = []
fallback_root = root / 'scripts/third-party-licenses'

def is_notice(path):
    return path.is_file() and (path.name.upper().startswith(('LICENSE', 'LICENCE', 'COPYING', 'COPYRIGHT', 'NOTICE')) or path.name.upper() == 'UNLICENSE')

def collect(name, version, license_expression, source, group):
    destination = out / group / (name.replace('/', '_') + '-' + version)
    destination.mkdir(parents=True, exist_ok=True)
    files = set()
    def copy(path, relative):
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        files.add(target.relative_to(out).as_posix())
    if source.exists():
        for path in source.iterdir():
            if is_notice(path): copy(path, path.name)
        if name == 'pdfjs-dist':
            for subdirectory in ('wasm', 'standard_fonts'):
                folder = source / subdirectory
                if folder.exists():
                    for path in folder.rglob('*'):
                        if is_notice(path): copy(path, path.relative_to(source))
    fallback = fallback_root / group / (name.replace('/', '_') + '-' + version)
    provenance = None
    if fallback.exists():
        record = fallback / 'provenance.json'
        if record.exists():
            provenance = json.loads(record.read_text(encoding='utf-8'))
            if provenance['name'] != name or provenance['version'] != version:
                raise ValueError(f'License fallback version mismatch: {name} {version}')
            for item in provenance['sources']:
                path = fallback / item['file']
                if hashlib.sha256(path.read_bytes()).hexdigest() != item['sha256']:
                    raise ValueError(f'License fallback checksum mismatch: {path}')
            shutil.copy2(record, destination / record.name)
        for path in fallback.iterdir():
            if is_notice(path): copy(path, path.name)
    entry = {'name': name, 'version': version, 'license': license_expression, 'group': group, 'files': sorted(files)}
    if provenance is not None:
        entry['provenance'] = (destination / 'provenance.json').relative_to(out).as_posix()
    manifest.append(entry)

lock = json.loads((root / 'tauri/package-lock.json').read_text(encoding='utf-8'))
for key, value in lock['packages'].items():
    if not key or value.get('dev') or not key.startswith('node_modules/'): continue
    source = root / 'tauri' / key
    package = source / 'package.json'
    if not package.exists(): continue
    metadata = json.loads(package.read_text(encoding='utf-8'))
    collect(metadata.get('name', key.removeprefix('node_modules/')), str(metadata.get('version', value['version'])), metadata.get('license', value.get('license')), source, 'npm')
seen_rust = set()
for metadata_path in sys.argv[1:]:
    metadata = json.loads(pathlib.Path(metadata_path).read_text(encoding='utf-8-sig'))
    for package in metadata['packages']:
        if package.get('source') and package['source'].startswith('registry+'):
            identity = (package['name'], package['version'])
            if identity in seen_rust: continue
            seen_rust.add(identity)
            collect(package['name'], package['version'], package['license'], pathlib.Path(package['manifest_path']).parent, 'rust')
# WebView2Loader is Microsoft SDK redistributable code, separate from the Rust MIT wrapper.
# Exact SDK package notices were acquired once and remain available without network access.
native = fallback_root / 'native'
if native.exists():
    for folder in sorted(native.iterdir()):
        record = folder / 'provenance.json'
        if record.exists():
            package = json.loads(record.read_text(encoding='utf-8'))
            collect(package['name'], package['version'], package.get('license', 'SEE LICENSE.txt'), folder, 'native')
(out / 'third-party-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'Collected notices for {len(manifest)} packages')
missing = [f"{p['group']}/{p['name']} {p['version']}" for p in manifest if not p['files']]
print(f'Packages without physical notices: {len(missing)}')
for package in missing: print(f'  {package}')
