<#
Build a local Unity Web checkpoint, then stamp its source and output hashes.
Usage: .\tools\build-unity.ps1 -Target Web
Needs the pinned Unity Editor and that target's build module. Does not push or publish.
Preview: python -m http.server 8787 --directory Published/Web
#>
param([ValidateSet('Web','Windows')][string]$Target='Web',[string]$EditorPath)
$ErrorActionPreference='Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$version = ((Get-Content -LiteralPath (Join-Path $repositoryRoot 'Unity\ProjectSettings\ProjectVersion.txt') | Select-Object -First 1) -split ': ')[1]
if (!$EditorPath) { $EditorPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (!(Test-Path -LiteralPath $EditorPath)) { throw "Install Unity $version or pass -EditorPath." }
Push-Location $repositoryRoot
try {
    dotnet run --project UnityTests/Domain
    if ($LASTEXITCODE -ne 0) { throw 'Domain tests failed.' }
    New-Item -ItemType Directory -Path Builds -Force | Out-Null
    $buildLog = Join-Path $repositoryRoot "Builds\$Target-build.log"
    $unityProject = Join-Path $repositoryRoot 'Unity'
    $arguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$unityProject+'"'),'-executeMethod',"AshenSpire.Editor.BuildTools.Build$Target",'-logFile',('"'+$buildLog+'"'))
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Unity build failed; see $buildLog" }
    if ($Target -eq 'Web') { node tools/unity-package.mjs; if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' } }
    Write-Output "Build ready: $repositoryRoot\Builds\$Target"
} finally { Pop-Location }
