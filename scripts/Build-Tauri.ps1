param([switch]$SkipInstall)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location (Join-Path $repo 'tauri')
try {
    if (-not $SkipInstall) { & npm.cmd ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' } }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
    $metadataPath = Join-Path $repo 'tmp\tauri-metadata.json'
    New-Item -ItemType Directory -Force (Split-Path $metadataPath -Parent) | Out-Null
    $metadata = & cargo metadata --manifest-path src-tauri\Cargo.toml --features desktop --locked --format-version 1
    if ($LASTEXITCODE -ne 0) { throw 'Rust dependency metadata failed' }
    [IO.File]::WriteAllText($metadataPath, ($metadata -join "`n"), [Text.UTF8Encoding]::new($false))
    $launcherMetadataPath = Join-Path $repo 'tmp\portable-metadata.json'
    $launcherMetadata = & cargo metadata --manifest-path (Join-Path $repo 'portable-launcher\Cargo.toml') --locked --format-version 1
    if ($LASTEXITCODE -ne 0) { throw 'Portable dependency metadata failed' }
    [IO.File]::WriteAllText($launcherMetadataPath, ($launcherMetadata -join "`n"), [Text.UTF8Encoding]::new($false))
    & python (Join-Path $repo 'scripts\collect-tauri-notices.py') $metadataPath $launcherMetadataPath
    if ($LASTEXITCODE -ne 0) { throw 'Dependency notice collection failed' }
    & npm.cmd run tauri -- build
    if ($LASTEXITCODE -ne 0) { throw 'Tauri build failed' }
} finally { Pop-Location }
