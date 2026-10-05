param([string]$EditorPath='D:/Unity/6000.6.0f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$appearanceRoot=Split-Path $PSScriptRoot -Parent
$appearanceVersion=Get-Content (Join-Path $appearanceRoot 'GameContent/Unity/version.json') -Raw | ConvertFrom-Json
$appearanceOutput=Join-Path $appearanceRoot ('Builds/OwnerAppearance/build'+$appearanceVersion.BuildNumber)
$appearanceTemp=Join-Path $appearanceRoot 'Builds/Temp/OwnerAppearance'
New-Item -ItemType Directory -Path $appearanceOutput,$appearanceTemp -Force | Out-Null
$env:TEMP=$appearanceTemp; $env:TMP=$appearanceTemp; $env:TMPDIR=$appearanceTemp
$env:UPM_CACHE_ROOT='D:/repos/.codex/cache/unity/upm'
New-Item -ItemType Directory -Path $env:UPM_CACHE_ROOT -Force | Out-Null
$appearanceProject=Join-Path $appearanceRoot 'Unity'
$appearanceLog=Join-Path $appearanceOutput 'Unity-Web.log'
$appearanceArgs=@('-batchmode','-nographics','-quit','-buildTarget','WebGL','-projectPath',('"'+$appearanceProject+'"'),'-executeMethod','AshenSpire.Editor.BuildTools.BuildOwnerAppearancePreview','-logFile',('"'+$appearanceLog+'"'))
$appearanceProcess=Start-Process -FilePath $EditorPath -ArgumentList $appearanceArgs -WindowStyle Hidden -PassThru
Write-Output "Owned Unity export PID $($appearanceProcess.Id); log: $appearanceLog"
$appearanceProcess.WaitForExit()
if($appearanceProcess.ExitCode -ne 0) { throw "Unity export failed ($($appearanceProcess.ExitCode)); inspect $appearanceLog" }
Write-Output 'Owner appearance export finished successfully.'


