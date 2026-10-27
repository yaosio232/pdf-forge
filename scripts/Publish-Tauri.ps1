param([string]$Configuration = 'release', [string]$NativeRuntimeDirectory, [string]$NativeRuntimeLicense)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$tauriRoot = Join-Path $repo 'tauri'
$manifest = Get-Content (Join-Path $tauriRoot 'src-tauri\tauri.conf.json') -Raw | ConvertFrom-Json
$version = $manifest.version
$target = Join-Path $tauriRoot "src-tauri\target\$Configuration"
$binary = Join-Path $target 'pdf-forge.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Build with npm run tauri -- build first.' }
if ($env:RUSTUP_TOOLCHAIN -like '*gnullvm*' -and -not $NativeRuntimeDirectory) { throw 'GNU LLVM builds require -NativeRuntimeDirectory and -NativeRuntimeLicense for libunwind.' }
if ($NativeRuntimeDirectory -and (-not (Test-Path -LiteralPath (Join-Path $NativeRuntimeDirectory 'libunwind.dll')) -or -not (Test-Path -LiteralPath $NativeRuntimeLicense))) { throw 'The native runtime DLL and original license must both be available.' }
$dist = Join-Path $repo "dist\v$version"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$staging = Join-Path $dist 'portable'
if (Test-Path -LiteralPath $staging) { throw "Staging already exists: $staging. Choose a clean delivery directory before repackaging." }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath $binary -Destination (Join-Path $staging 'PDF Forge 2.exe')
# GNU builds link the WebView2 loader dynamically; Tauri places it beside the executable.
Get-ChildItem -LiteralPath $target -Filter '*.dll' -File | Copy-Item -Destination $staging
if ($NativeRuntimeDirectory) { Copy-Item -LiteralPath (Join-Path $NativeRuntimeDirectory 'libunwind.dll') -Destination $staging }
$resource = Join-Path $staging 'resources\markdown'
New-Item -ItemType Directory -Force -Path $resource | Out-Null
Copy-Item -Path (Join-Path $tauriRoot 'public\markdown\*') -Destination $resource -Recurse
$licenseFolder = Join-Path $staging 'licenses'
New-Item -ItemType Directory -Path $licenseFolder | Out-Null
if ($NativeRuntimeDirectory) { Copy-Item -LiteralPath $NativeRuntimeLicense -Destination (Join-Path $licenseFolder 'LLVM-MinGW-LICENSE.txt') }
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE.md'),(Join-Path $repo 'NOTICE.md') -Destination $licenseFolder
Copy-Item -LiteralPath (Join-Path $tauriRoot 'README.md') -Destination $staging
Copy-Item -Path (Join-Path $tauriRoot 'public\licenses\*') -Destination $licenseFolder -Recurse
[IO.File]::WriteAllText((Join-Path $staging 'PENDING-VALIDATION.txt'), 'Real function/worker tests cover PDF split/merge/unlock, Word conversions and advanced Markdown printing. Application UI, DPI and cross-machine acceptance remain unvalidated. Requires Windows x64 and WebView2 Runtime. Word desktop required for Word conversions.', [Text.UTF8Encoding]::new($false))
$zip = Join-Path $dist "PDF-Forge-$version-windows-x64-portable.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
$previousPayload = $env:PDF_FORGE_PAYLOAD
$previousRustFlags = $env:RUSTFLAGS
try {
    $env:PDF_FORGE_PAYLOAD = $zip
    # The outer launcher must start without any sidecar runtime to extract its payload.
    $env:RUSTFLAGS = (($previousRustFlags + ' -C target-feature=+crt-static').Trim())
    & cargo build --manifest-path (Join-Path $repo 'portable-launcher\Cargo.toml') --target-dir (Join-Path $tauriRoot 'src-tauri\target') --release --locked
    if ($LASTEXITCODE -ne 0) { throw 'Single-file portable launcher build failed.' }
    Copy-Item -LiteralPath (Join-Path $tauriRoot 'src-tauri\target\release\pdf-forge-portable.exe') -Destination (Join-Path $dist "PDF-Forge-$version-windows-x64-portable.exe")
} finally { $env:PDF_FORGE_PAYLOAD = $previousPayload; $env:RUSTFLAGS = $previousRustFlags }
Get-ChildItem (Join-Path $target 'bundle\nsis') -Filter "*_${version}_x64-setup.exe" -ErrorAction SilentlyContinue | Copy-Item -Destination $dist
Get-ChildItem -LiteralPath $dist -File | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash | ConvertTo-Json | Set-Content (Join-Path $dist 'hashes.json') -Encoding utf8
Write-Output $zip
