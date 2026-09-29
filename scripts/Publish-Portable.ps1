[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PopplerRoot,

    [Parameter(Mandatory)]
    [string]$PopplerPackageInfoDirectory,

    [Parameter(Mandatory)]
    [string]$PopplerSourceArchive,

    [Parameter(Mandatory)]
    [string]$PopplerPackageArchive,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string]$BuildDate,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
$expectedPackageSha256 = '3FDBB4CAF50ECAE4FA447CB7A7D6D5B521A39AE8D4DE4F6565E0CE9CD6D7F903'
$expectedSourceSha256 = '304832F48F8A47FDCA90C6B6D1F684E68F37C10C9A0726F345F4CA9DF4CA01E2'
$expectedPackageFileName = 'poppler-26.07.0-h6618ce5_3.conda'
$expectedSourceFileName = 'poppler-26.07.0.tar.xz'
$expectedFeedstockRevision = '4f5f76cc7b8aa5ac5090988bd78ab7bde271c10a'
$expectedRecipeSha256 = 'B666027F1CF87D1EDA24B303F3EA3983897F54F31796A605064DCA1D1959576D'
$expectedSdk = '8.0.409'
$expectedPatches = @(
    'disable-libtiff-win32-io.patch',
    'exportsymbols.patch',
    'includesystembeforejpeg.patch',
    'windows-data.patch'
)

function Test-PathWithin {
    param([string]$Candidate, [string]$Root)

    $candidatePath = [System.IO.Path]::GetFullPath($Candidate).TrimEnd('\', '/')
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $candidatePath.Equals($rootPath, [StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith($rootPath + [System.IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'Pdf_Merger.csproj'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$popplerRootPath = [System.IO.Path]::GetFullPath($PopplerRoot)
$packageInfoPath = [System.IO.Path]::GetFullPath($PopplerPackageInfoDirectory)
$sourceArchivePath = [System.IO.Path]::GetFullPath($PopplerSourceArchive)
$packageArchivePath = [System.IO.Path]::GetFullPath($PopplerPackageArchive)

$requiredInputs = @(
    (Join-Path $popplerRootPath 'Library\bin\pdftoppm.exe'),
    (Join-Path $popplerRootPath 'Library\bin\poppler.dll'),
    (Join-Path $popplerRootPath 'share\poppler\cMap'),
    (Join-Path $popplerRootPath 'share\poppler\cidToUnicode'),
    (Join-Path $popplerRootPath 'share\poppler\nameToUnicode'),
    (Join-Path $popplerRootPath 'share\poppler\unicodeMap'),
    (Join-Path $popplerRootPath 'manifest.json'),
    (Join-Path $packageInfoPath 'index.json'),
    (Join-Path $packageInfoPath 'about.json'),
    (Join-Path $packageInfoPath 'paths.json'),
    (Join-Path $packageInfoPath 'licenses\COPYING'),
    (Join-Path $packageInfoPath 'recipe'),
    $sourceArchivePath,
    $packageArchivePath,
    (Join-Path $repositoryRoot 'licenses\Poppler-26.07.0-COPYING.txt'),
    (Join-Path $repositoryRoot 'licenses\Poppler-26.07.0-SOURCE.md'),
    (Join-Path $repositoryRoot 'licenses\Poppler-26.07.0-DEPENDENCIES.md')
)

foreach ($path in $requiredInputs) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Portable package input is missing: $path"
    }
}

if ((Test-PathWithin $outputRoot $popplerRootPath) -or
    (Test-PathWithin $outputRoot $packageInfoPath)) {
    throw 'OutputDirectory must not be equal to or inside a recursively copied input directory.'
}

$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne $expectedSdk) {
    throw "Portable packages require .NET SDK $expectedSdk; found '$sdkVersion'."
}

$packageMetadata = Get-Content -LiteralPath (Join-Path $packageInfoPath 'index.json') -Raw | ConvertFrom-Json
$aboutMetadata = Get-Content -LiteralPath (Join-Path $packageInfoPath 'about.json') -Raw | ConvertFrom-Json
$metadataIdentity = @(
    $packageMetadata.name,
    $packageMetadata.version,
    $packageMetadata.build,
    $packageMetadata.subdir,
    $packageMetadata.license
)
$expectedIdentity = @('poppler', '26.07.0', 'h6618ce5_3', 'win-64', 'GPL-2.0-or-later')
if (Compare-Object $expectedIdentity $metadataIdentity -SyncWindow 0) {
    throw "Unexpected Poppler package metadata: $($metadataIdentity -join ', ')."
}
if ($aboutMetadata.extra.sha -ne $expectedFeedstockRevision) {
    throw "Unexpected Poppler feedstock revision: $($aboutMetadata.extra.sha)."
}

if ((Get-FileHash -LiteralPath $packageArchivePath -Algorithm SHA256).Hash -ne $expectedPackageSha256) {
    throw 'The Poppler conda package SHA-256 does not match the pinned package.'
}
if ((Get-FileHash -LiteralPath $sourceArchivePath -Algorithm SHA256).Hash -ne $expectedSourceSha256) {
    throw 'The Poppler upstream source SHA-256 does not match the pinned source.'
}
if ((Split-Path $packageArchivePath -Leaf) -cne $expectedPackageFileName -or
    (Split-Path $sourceArchivePath -Leaf) -cne $expectedSourceFileName) {
    throw 'The Poppler package and source archives must use their canonical filenames.'
}

$patches = @(Get-ChildItem -LiteralPath (Join-Path $packageInfoPath 'recipe') -Recurse -File -Filter '*.patch' |
    Select-Object -ExpandProperty Name | Sort-Object)
if (Compare-Object ($expectedPatches | Sort-Object) $patches -SyncWindow 0) {
    throw "The Poppler conda recipe patch set is not the pinned Windows patch set: $($patches -join ', ')."
}
$recipeRoot = Join-Path $packageInfoPath 'recipe'
$recipeHashLines = @(Get-ChildItem -LiteralPath $recipeRoot -Recurse -File | Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($recipeRoot.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    })
$recipeHashBytes = [Text.Encoding]::UTF8.GetBytes(($recipeHashLines -join "`n") + "`n")
$recipeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($recipeHashBytes))
if ($recipeHash -ne $expectedRecipeSha256) {
    throw "The Poppler conda recipe content SHA-256 is not pinned: $recipeHash"
}

$runtimeManifest = Get-Content -LiteralPath (Join-Path $popplerRootPath 'manifest.json') -Raw | ConvertFrom-Json
if ($runtimeManifest.name -ne 'poppler' -or
    $runtimeManifest.version -ne '26.07.0' -or
    $runtimeManifest.targetPlatform -ne 'win32' -or
    $runtimeManifest.targetArch -ne 'x64' -or
    $runtimeManifest.packageSpec -ne 'poppler=26.07.0=h6618ce5_3') {
    throw 'The Poppler runtime manifest does not match the pinned Windows x64 package.'
}
foreach ($dataDirectory in 'cMap', 'cidToUnicode', 'nameToUnicode', 'unicodeMap') {
    if (-not (Get-ChildItem -LiteralPath (Join-Path $popplerRootPath "share\poppler\$dataDirectory") -File -Recurse | Select-Object -First 1)) {
        throw "The Poppler runtime data directory is empty: $dataDirectory"
    }
}

$trackedLicense = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'licenses\Poppler-26.07.0-COPYING.txt')).Replace("`r`n", "`n").TrimEnd()
$packageLicense = [System.IO.File]::ReadAllText(
    (Join-Path $packageInfoPath 'licenses\COPYING')).Replace("`r`n", "`n").TrimEnd()
if ($trackedLicense -cne $packageLicense) {
    throw 'The tracked Poppler license does not match the exact conda package license.'
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]($project.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'The project version could not be read from Pdf_Merger.csproj.'
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$workId = [Guid]::NewGuid().ToString('N')
$publishDirectory = Join-Path $outputRoot ('.portable-publish-' + $workId)
$stagingDirectory = Join-Path $outputRoot ('.portable-stage-' + $workId)
$zipPath = Join-Path $outputRoot "PDF.Forge-v$version-win-x64.zip"
$archiveTimestamp = [DateTimeOffset]::ParseExact(
    "$BuildDate 00:00:00 +00:00",
    'yyyy-MM-dd HH:mm:ss zzz',
    [Globalization.CultureInfo]::InvariantCulture)

try {
    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        -p:Deterministic=true -p:ContinuousIntegrationBuild=true -p:BuildDate=$BuildDate `
        --no-restore `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'PDF Forge.exe') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md'), `
        (Join-Path $repositoryRoot 'NOTICE.md'), `
        (Join-Path $repositoryRoot 'LICENSE.md') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'licenses') -Destination $stagingDirectory -Recurse
    Copy-Item -LiteralPath $popplerRootPath -Destination (Join-Path $stagingDirectory 'poppler') -Recurse

    $licenseDirectory = Join-Path $stagingDirectory 'licenses'
    Copy-Item -LiteralPath (Join-Path $packageInfoPath 'recipe') `
        -Destination (Join-Path $licenseDirectory 'Poppler-26.07.0-conda-recipe') -Recurse
    $metadataDirectory = Join-Path $licenseDirectory 'Poppler-26.07.0-conda-metadata'
    New-Item -ItemType Directory -Path $metadataDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $packageInfoPath 'index.json'), `
        (Join-Path $packageInfoPath 'about.json'), `
        (Join-Path $packageInfoPath 'paths.json') -Destination $metadataDirectory
    Copy-Item -LiteralPath $sourceArchivePath `
        -Destination (Join-Path $licenseDirectory $expectedSourceFileName)
    Copy-Item -LiteralPath $packageArchivePath `
        -Destination (Join-Path $licenseDirectory $expectedPackageFileName)

    $renderer = Join-Path $stagingDirectory 'poppler\Library\bin\pdftoppm.exe'
    $rendererVersion = (& $renderer -v 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $rendererVersion -notmatch 'pdftoppm version 26\.07\.0') {
        throw "The staged Poppler renderer did not start as version 26.07.0: $rendererVersion"
    }

    $hashLines = Get-ChildItem -LiteralPath (Join-Path $stagingDirectory 'poppler') -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($stagingDirectory.Length + 1).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    [System.IO.File]::WriteAllLines(
        (Join-Path $licenseDirectory 'Poppler-26.07.0-FILES.sha256'),
        $hashLines,
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.File]::WriteAllLines(
        (Join-Path $licenseDirectory 'BUILD-INFO.txt'),
        @(
            "PDF Forge version: $version",
            "Build date: $BuildDate",
            "Dotnet SDK: $sdkVersion",
            'Poppler package: poppler=26.07.0=h6618ce5_3',
            "Poppler package SHA-256: $expectedPackageSha256",
            "Poppler source SHA-256: $expectedSourceSha256",
            "Poppler feedstock revision: $expectedFeedstockRevision"
            "Poppler recipe tree SHA-256: $expectedRecipeSha256"
        ),
        [System.Text.UTF8Encoding]::new($false))

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [System.IO.File]::Open(
        $zipPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $zipArchive = [System.IO.Compression.ZipArchive]::new(
        $zipStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File | Sort-Object FullName) {
            $relative = $file.FullName.Substring($stagingDirectory.Length + 1).Replace('\', '/')
            $entry = $zipArchive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $archiveTimestamp
            $inputStream = [System.IO.File]::OpenRead($file.FullName)
            $entryStream = $entry.Open()
            try {
                $inputStream.CopyTo($entryStream)
            }
            finally {
                $entryStream.Dispose()
                $inputStream.Dispose()
            }
        }
    }
    finally {
        $zipArchive.Dispose()
        $zipStream.Dispose()
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        $archive.Dispose()
    }
    $requiredEntries = @(
        'PDF Forge.exe',
        'NOTICE.md',
        'LICENSE.md',
        'poppler/Library/bin/pdftoppm.exe',
        'licenses/Poppler-26.07.0-COPYING.txt',
        'licenses/Poppler-26.07.0-SOURCE.md',
        'licenses/Poppler-26.07.0-DEPENDENCIES.md',
        'licenses/Poppler-26.07.0-FILES.sha256',
        'licenses/BUILD-INFO.txt',
        ('licenses/' + $expectedSourceFileName),
        ('licenses/' + $expectedPackageFileName)
    )
    foreach ($entry in $requiredEntries) {
        if ($entries -notcontains $entry) {
            throw "Portable ZIP is missing required entry: $entry"
        }
    }
    if (-not ($entries | Where-Object { $_ -like 'licenses/Poppler-26.07.0-conda-recipe/*.patch' })) {
        throw 'Portable ZIP is missing the Poppler Windows patch files.'
    }

    $zip = Get-Item -LiteralPath $zipPath
    [pscustomobject]@{
        Path = $zip.FullName
        Bytes = $zip.Length
        Sha256 = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash
        PopplerFiles = $hashLines.Count
    }
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
}
