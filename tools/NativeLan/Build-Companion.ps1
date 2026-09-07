# Build-Companion.ps1 — produce a portable Windows companion ZIP with original data.
# Run after the root freezes native source/content; the stamp records dirty status
# and per-file hashes so an uncommitted source build is never called committed.
param(
    [string]$RepositoryRoot = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))),
    [string]$OutputRoot = (Join-Path $RepositoryRoot 'Builds/Companion'),
    [string]$Version,
    [int]$BuildNumber,
    [string]$SourceCommit
)
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$versionMetadata = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'GameContent/Unity/version.json') -Raw | ConvertFrom-Json
if (!$Version) { $Version = $versionMetadata.Version }
if (!$BuildNumber) { $BuildNumber = $versionMetadata.BuildNumber }
if ($Version -ne $versionMetadata.Version -or $BuildNumber -ne $versionMetadata.BuildNumber) { throw 'Companion and Unity version metadata must match.' }
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $BuildNumber -lt 1) { throw 'Use the four-part version and positive build number.' }
if (!(Test-Path -LiteralPath (Join-Path $RepositoryRoot 'Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalCoopRun.cs'))) { throw 'Integrate the real cooperative native sources before packaging; scratch replacements are not a release source.' }
if (!$SourceCommit) { $SourceCommit = (& git -C $RepositoryRoot -c "safe.directory=$RepositoryRoot" rev-parse HEAD).Trim(); if ($LASTEXITCODE) { throw 'Cannot read source commit.' } }
$dirty = [bool](& git -C $RepositoryRoot -c "safe.directory=$RepositoryRoot" status --porcelain)
$artifactName = "AshenSpire-Companion-$Version-build$BuildNumber-Windows"
$destination = Join-Path $OutputRoot $artifactName
$zip = "$destination.zip"
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $zip)) { throw 'Output already exists. Use a fresh output directory; existing artifacts are preserved.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
& dotnet publish (Join-Path $PSScriptRoot 'Companion/AshenSpire.Companion.csproj') -c Release -r win-x64 --self-contained true -o $destination "-p:RepositoryRoot=$RepositoryRoot" "-p:Version=$Version" "-p:AssemblyVersion=$Version" "-p:FileVersion=$Version" --nologo
if ($LASTEXITCODE) { throw 'Companion publish failed.' }
$content = Join-Path $destination 'Content'; New-Item -ItemType Directory -Path $content | Out-Null
Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'GameContent/Unity/Original') -Filter '*.json' -File | Copy-Item -Destination $content
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Packaging') -File | Copy-Item -Destination $destination
$files = @(Get-ChildItem -LiteralPath $destination -File -Recurse | Sort-Object FullName | ForEach-Object { @{path=$_.FullName.Substring($destination.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$_.Length} })
function Get-SourceHash([string]$Path) {
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($Path)).Replace("`r`n", "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}
$sourceRoot = Join-Path $RepositoryRoot 'Unity/Assets/AshenSpire/Runtime/Domain/Original'
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { @{path=$_.FullName.Substring($RepositoryRoot.Length+1).Replace('\','/');sha256=(Get-SourceHash $_.FullName)} })
$transportSources = @(foreach ($folder in @('Transport','Companion','Packaging','Domain')) { Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $folder) -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | ForEach-Object { @{path=$_.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/');sha256=(Get-SourceHash $_.FullName)} } })
$unitySources = @(foreach ($sourcePath in @('Unity/Assets/AshenSpire/Runtime/Presentation/Networking/NativeLanClient.cs','Unity/Assets/AshenSpire/Plugins/WebGL/NativeLan.jslib','tools/NativeLan/Build-Companion.ps1')) { @{path=$sourcePath;sha256=(Get-SourceHash (Join-Path $RepositoryRoot $sourcePath))} })
$contentSources = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'GameContent/Unity/Original') -Filter '*.json' -File | Sort-Object Name | ForEach-Object { @{path=$_.FullName.Substring($RepositoryRoot.Length+1).Replace('\','/');sha256=(Get-SourceHash $_.FullName)} })
@{sourceHashNormalization='utf8-lf';unitySources=$unitySources;contentSources=$contentSources;version=$Version;buildNumber=$BuildNumber;builtAtUtc=[DateTime]::UtcNow.ToString('O');sourceCommit=$SourceCommit;sourceWorktreeDirty=$dirty;runtime='win-x64';selfContained=$true;webBuildIncluded=$false;files=$files;nativeSources=$sources;transportSources=$transportSources} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'BuildStamp.json') -Encoding UTF8
Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $zip -CompressionLevel Optimal
$length=(Get-Item -LiteralPath $zip).Length
Write-Output (ConvertTo-Json @{path=$zip;bytes=$length;sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant();version=$Version;buildNumber=$BuildNumber})
