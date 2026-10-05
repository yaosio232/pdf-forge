param([string]$Configuration = 'release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$tauriRoot = Join-Path $repo 'tauri'
$manifest = Get-Content (Join-Path $tauriRoot 'src-tauri\tauri.conf.json') -Raw | ConvertFrom-Json
$version = $manifest.version
$target = Join-Path $tauriRoot "src-tauri\target\$Configuration"
$binary = Join-Path $target 'pdf-forge.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Build with npm run tauri -- build first.' }
$dist = Join-Path $repo "dist\v$version"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$staging = Join-Path $dist 'portable'
if (Test-Path -LiteralPath $staging) { throw "Staging already exists: $staging. Choose a clean delivery directory before repackaging." }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath $binary -Destination (Join-Path $staging 'PDF Forge 2.exe')
# GNU builds link the WebView2 loader dynamically; Tauri places it beside the executable.
Get-ChildItem -LiteralPath $target -Filter '*.dll' -File | Copy-Item -Destination $staging
$resource = Join-Path $staging 'resources\markdown'
New-Item -ItemType Directory -Force -Path $resource | Out-Null
Copy-Item -Path (Join-Path $tauriRoot 'public\markdown\*') -Destination $resource -Recurse
$licenseFolder = Join-Path $staging 'licenses'
New-Item -ItemType Directory -Path $licenseFolder | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE.md'),(Join-Path $repo 'NOTICE.md') -Destination $licenseFolder
Copy-Item -LiteralPath (Join-Path $tauriRoot 'README.md') -Destination $staging
Copy-Item -Path (Join-Path $tauriRoot 'public\licenses\*') -Destination $licenseFolder -Recurse
[IO.File]::WriteAllText((Join-Path $staging 'PENDING-VALIDATION.txt'), 'Basic unit tests only. UI, Word, Markdown printing and cross-machine acceptance are not validated. Requires Windows x64 and WebView2 Runtime. Word desktop required for Word conversions.', [Text.UTF8Encoding]::new($false))
$zip = Join-Path $dist "PDF-Forge-$version-windows-x64-portable.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Get-ChildItem (Join-Path $target 'bundle\nsis') -Filter '*.exe' -ErrorAction SilentlyContinue | Copy-Item -Destination $dist
Get-ChildItem -LiteralPath $dist -File | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash | ConvertTo-Json | Set-Content (Join-Path $dist 'hashes.json') -Encoding utf8
Write-Output $zip
