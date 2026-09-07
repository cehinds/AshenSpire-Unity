<#
Build matching local Unity players and the portable co-op companion.
Usage: .\tools\build-unity.ps1 -Target All
Fast browser iteration: .\tools\build-unity.ps1 -Target Web -PreviewOnly
Needs the pinned Unity Editor and that target's build module. Does not push or publish.
Preview: python -m http.server 8787 --directory Builds/Web
#>
param([ValidateSet('All','Web','Windows','Android')][string]$Target='All',[string]$EditorPath,[switch]$PreviewOnly)
$ErrorActionPreference='Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$version = ((Get-Content -LiteralPath (Join-Path $repositoryRoot 'Unity\ProjectSettings\ProjectVersion.txt') | Select-Object -First 1) -split ': ')[1]
if (!$EditorPath) { $EditorPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (!(Test-Path -LiteralPath $EditorPath)) { throw "Install Unity $version or pass -EditorPath." }
Push-Location $repositoryRoot
try {
    dotnet run --project UnityTests/Domain
    if ($LASTEXITCODE -ne 0) { throw 'Domain tests failed.' }
    dotnet run --project UnityTests/Parity
    if ($LASTEXITCODE -ne 0) { throw 'Original parity tests failed.' }
    dotnet run --project UnityTests/MapKnowledge
    if ($LASTEXITCODE -ne 0) { throw 'Original map knowledge tests failed.' }
    dotnet run --project UnityTests/MapViewport
    if ($LASTEXITCODE -ne 0) { throw 'Original map camera tests failed.' }
    $targets = if ($Target -eq 'All') { @('Windows','Android','Web') } else { @($Target) }
    New-Item -ItemType Directory -Path Builds -Force | Out-Null
    foreach ($platform in $targets) {
    $buildLog = Join-Path $repositoryRoot "Builds\$platform-build.log"
    $unityProject = Join-Path $repositoryRoot 'Unity'
    $unityTarget = @{ Windows='StandaloneWindows64'; Android='Android'; Web='WebGL' }[$platform]
    $arguments = @('-batchmode','-nographics','-quit','-buildTarget',$unityTarget,'-projectPath',('"'+$unityProject+'"'),'-executeMethod',"AshenSpire.Editor.BuildTools.Build$platform",'-logFile',('"'+$buildLog+'"'))
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    # Wait for this Editor only. Start-Process -Wait also waits for descendants,
    # including a Hub helper that can survive an Editor compilation failure.
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity build failed; see $buildLog" }
    if ($platform -eq 'Windows' -and !$PreviewOnly) {
        Compress-Archive -Path Builds/Windows/* -DestinationPath Published/Windows.zip -CompressionLevel Optimal -Force
        Copy-Item -LiteralPath Builds/Windows/build-source.json -Destination Published/Windows.build-source.json -Force
    }
    if ($platform -eq 'Android' -and !$PreviewOnly) {
        Copy-Item -LiteralPath Builds/Android/AshenSpire.apk -Destination Published/Android.apk -Force
        Copy-Item -LiteralPath Builds/Android/build-source.json -Destination Published/Android.build-source.json -Force
    }
    if ($platform -eq 'Web' -and !$PreviewOnly) {
        $companionOutput = Join-Path $repositoryRoot ('Builds/Companion/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
        & (Join-Path $PSScriptRoot 'NativeLan/Build-Companion.ps1') -RepositoryRoot $repositoryRoot -OutputRoot $companionOutput
        $companionZips = @(Get-ChildItem -LiteralPath $companionOutput -Filter '*.zip' -File)
        if ($companionZips.Count -ne 1) { throw 'Expected exactly one companion package.' }
        $companionZip = $companionZips[0]
        Copy-Item -LiteralPath $companionZip.FullName -Destination Published/Companion.zip -Force
        $companionFolder = Join-Path $companionOutput $companionZip.BaseName
        Copy-Item -LiteralPath (Join-Path $companionFolder 'BuildStamp.json') -Destination Published/Companion.build.json -Force
        node tools/unity-package.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Packaging failed. Build -Target All when other targets are stale.' }
    }
    Write-Output "Build ready: $repositoryRoot\Builds\$platform"
    }
} finally { Pop-Location }
