param([Parameter(Mandatory=$true)][string]$PayloadDirectory)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path $PSScriptRoot -Parent
$payload=(Resolve-Path -LiteralPath $PayloadDirectory).Path
$version=([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Pdf_Merger.csproj') -Raw)).Project.PropertyGroup.Version | Select-Object -First 1
foreach ($required in @('PDF Forge.exe','README.md','LICENSE.md','NOTICE.md','poppler/Library/bin/pdftoppm.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) {throw "Missing payload resource: $required"}
}
$launcherLicenses=Join-Path $repositoryRoot 'licenses/portable-launcher'
if (-not (Test-Path -LiteralPath $launcherLicenses)) {throw 'Portable launcher third-party notices are required.'}
Copy-Item -LiteralPath $launcherLicenses -Destination (Join-Path $payload 'licenses') -Recurse -Force
$files=@(Get-ChildItem -LiteralPath $payload -File -Recurse)
foreach ($file in $files) {
    $relative=[IO.Path]::GetRelativePath($payload,$file.FullName).Replace('\','/')
    $runtimeLicense=$file.Name -ieq 'LICENSE.DOCX' -and $relative -match '(^|/)licenses/'
    if ($relative -match '^(tests|TestResults|node_modules|target|obj|bin|src|\.git)/' -or ($relative -match '\.(cs|rs|py|ps1|tsx|docx?|pdf|log|trx|pdb|conda|tar\.xz|patch)$' -and -not $runtimeLicense)) {throw "Forbidden delivery file: $relative"}
}
$hashes=@($files | Where-Object Name -ne 'PAYLOAD.sha256' | Sort-Object FullName | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/') })
[IO.File]::WriteAllLines((Join-Path $payload 'PAYLOAD.sha256'),$hashes,[Text.UTF8Encoding]::new($false))
$destination=Join-Path $repositoryRoot "dist/v$version"
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$zip=Join-Path $destination "PDF-Forge-$version-windows-x64-portable.zip"
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -Force
$previousPayload=$env:PDF_FORGE_PAYLOAD
$previousFlags=$env:RUSTFLAGS
$previousVersion=$env:PDF_FORGE_DELIVERY_VERSION
try {
    $env:PDF_FORGE_PAYLOAD=$zip
    $env:PDF_FORGE_DELIVERY_VERSION=$version
    $env:RUSTFLAGS=($previousFlags+' -C target-feature=+crt-static').Trim()
    $manifest=Join-Path $repositoryRoot 'portable-launcher/Cargo.toml'
    $target=Join-Path $repositoryRoot 'portable-launcher/target'
    & cargo build --manifest-path $manifest --target-dir $target --release --locked
    if ($LASTEXITCODE -ne 0) {throw 'Single-file launcher build failed.'}
    & cargo test --manifest-path $manifest --target-dir $target --release --locked
    if ($LASTEXITCODE -ne 0) {throw 'Embedded payload tests failed.'}
    $output=Join-Path $destination "PDF-Forge-$version-windows-x64-portable.exe"
    Copy-Item -LiteralPath (Join-Path $target 'release/pdf-forge-portable.exe') -Destination $output -Force
    Get-Item -LiteralPath $output | Select-Object Name,Length
    Get-FileHash -LiteralPath $output -Algorithm SHA256
} finally {$env:PDF_FORGE_PAYLOAD=$previousPayload;$env:RUSTFLAGS=$previousFlags;$env:PDF_FORGE_DELIVERY_VERSION=$previousVersion}
