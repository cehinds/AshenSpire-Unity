<#
Export matching players and stage a fresh local delivery candidate.
Does not publish or replace Published. Preserve prior exports before building.
Usage: .\tools\unity-build-candidate.ps1 [-EditorPath ...] [-CandidateRoot ...]
#>
param([string]$EditorPath,[string]$CandidateRoot)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$editorVersion = ((Get-Content -LiteralPath (Join-Path $repositoryRoot 'Unity/ProjectSettings/ProjectVersion.txt') | Select-Object -First 1) -split ': ')[1]
if (!$EditorPath) { $EditorPath = "D:/Unity/$editorVersion/Editor/Unity.exe" }
if (!(Test-Path -LiteralPath $EditorPath)) { throw 'Pinned Unity Editor not found; pass -EditorPath.' }
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'An Editor is running; preserve its project state before exporting.' }
$version = Get-Content -LiteralPath (Join-Path $repositoryRoot 'GameContent/Unity/version.json') -Raw | ConvertFrom-Json
if (!$CandidateRoot) { $CandidateRoot = Join-Path $repositoryRoot ("Builds/PlatformCandidates/build$($version.BuildNumber)-" + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')) }
$CandidateRoot = [IO.Path]::GetFullPath($CandidateRoot)
$buildsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Builds')) + [IO.Path]::DirectorySeparatorChar
if (!$CandidateRoot.StartsWith($buildsRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'CandidateRoot must be beneath repository Builds.' }
if (Test-Path -LiteralPath $CandidateRoot) { throw 'CandidateRoot already exists; preserve it and choose a new directory.' }
New-Item -ItemType Directory -Path $CandidateRoot | Out-Null
$temporaryRoot = Join-Path 'D:/repos/.codex/tmp' ('unity-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
$priorTemporary = @($env:TEMP,$env:TMP,$env:TMPDIR)
$env:TEMP = $temporaryRoot; $env:TMP = $temporaryRoot; $env:TMPDIR = $temporaryRoot
Write-Output "Short D: temporary directory: $temporaryRoot"
Push-Location -LiteralPath $repositoryRoot
try {
    foreach ($platform in @('Web','Windows','Android')) {
        $previous = Join-Path $repositoryRoot "Builds/$platform"
        if (Test-Path -LiteralPath $previous) {
            $backup = Join-Path $CandidateRoot "PreviousExports/$platform"
            New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $previous -Destination $backup -Recurse
            $receiptPath = Join-Path $backup 'build-source.json'
            if (Test-Path -LiteralPath $receiptPath) {
                $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
                foreach ($entry in $receipt.files.PSObject.Properties) {
                    if ((Get-FileHash -LiteralPath (Join-Path $backup $entry.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) { throw "Previous export backup mismatch: $platform/$($entry.Name)" }
                }
            }
        }
    }
    foreach ($platform in @('Web','Windows','Android')) {
        $target = @{Web='WebGL';Windows='StandaloneWindows64';Android='Android'}[$platform]
        $log = Join-Path $CandidateRoot "$platform-build.log"
        $arguments = @('-batchmode','-quit','-buildTarget',$target,'-projectPath',('"'+(Join-Path $repositoryRoot 'Unity')+'"'),'-executeMethod',"AshenSpire.Editor.BuildTools.Build$platform",'-logFile',('"'+$log+'"'))
        $editor = Start-Process -FilePath $EditorPath -WindowStyle Hidden -ArgumentList $arguments -PassThru
        Write-Output "$platform export started: PID $($editor.Id); log $log"
        $editor.WaitForExit()
        if ($editor.ExitCode -ne 0) { throw "$platform export failed; preserve candidate and inspect $log." }
        $receipt = Get-Content -LiteralPath (Join-Path $repositoryRoot "Builds/$platform/build-source.json") -Raw | ConvertFrom-Json
        if ($receipt.version -ne $version.Version -or $receipt.buildNumber -ne $version.BuildNumber -or $receipt.target -ne $platform) { throw "$platform exported a different version/target." }
        Write-Output "$platform export finished: $($receipt.sourceDigest)"
    }
    $companionRoot = Join-Path $CandidateRoot 'Companion'
    & (Join-Path $PSScriptRoot 'NativeLan/Build-Companion.ps1') -RepositoryRoot $repositoryRoot -OutputRoot $companionRoot
    $companion = Join-Path $companionRoot "AshenSpire-Companion-$($version.Version)-build$($version.BuildNumber)-Windows"
    $delivery = Join-Path $CandidateRoot 'Delivery'
    node tools/unity-stage-candidate.mjs $delivery $companion
    if ($LASTEXITCODE) { throw 'Candidate staging failed; preserve outputs and inspect receipts.' }
    node tools/unity-stage-candidate.mjs $delivery --check
    if ($LASTEXITCODE) { throw 'Candidate verification failed; preserve outputs and inspect receipts.' }
    Write-Output "Verified local delivery: $delivery"
} finally {
    Pop-Location
    $env:TEMP = $priorTemporary[0]; $env:TMP = $priorTemporary[1]; $env:TMPDIR = $priorTemporary[2]
}
