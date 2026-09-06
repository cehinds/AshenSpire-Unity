<#
Read connected Android hardware before a manual player test. Does not install,
launch, clear app data, or capture the device screen. No device is a pending test.
Usage: .\tools\android-preflight.ps1 [-AdbPath ...] [-OutputPath ...]
#>
param([string]$AdbPath,[string]$OutputPath)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path $PSScriptRoot -Parent
if(!$OutputPath){$OutputPath=Join-Path $repositoryRoot 'Builds/Android-device-preflight.json'}
if(!$AdbPath){
    $version=((Get-Content (Join-Path $repositoryRoot 'Unity/ProjectSettings/ProjectVersion.txt') | Select-Object -First 1) -split ': ')[1]
    $AdbPath="C:\Program Files\Unity\Hub\Editor\$version\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
}
if(!(Test-Path -LiteralPath $AdbPath)){throw 'ADB was not found. Install the Unity Android module or pass -AdbPath.'}
$inventory=& $AdbPath devices -l
if($LASTEXITCODE -ne 0){throw 'ADB inventory failed.'}
$devices=@(foreach($line in $inventory){
    if($line -match '^(\S+)\s+(device|unauthorized|offline)(?:\s|$)'){
        $serial=$Matches[1];$state=$Matches[2]
        $record=[ordered]@{Serial=$serial;State=$state;Model=$null;AndroidVersion=$null;ApiLevel=$null;Abis=$null;GameInstalled=$null}
        if($state -eq 'device'){
            foreach($pair in @(@('Model','ro.product.model'),@('AndroidVersion','ro.build.version.release'),@('ApiLevel','ro.build.version.sdk'),@('Abis','ro.product.cpu.abilist'))){
                $value=& $AdbPath -s $serial shell getprop $pair[1]
                if($LASTEXITCODE -eq 0){$record[$pair[0]]=($value -join '').Trim()}
            }
            $package=& $AdbPath -s $serial shell pm path com.ashenspire.expedition
            if($LASTEXITCODE -eq 0){$record.GameInstalled=($package -join '').StartsWith('package:')}
        }
        [pscustomobject]$record
    }
})
$report=[ordered]@{CapturedAt=[DateTime]::UtcNow.ToString('o');Status=if(@($devices | Where-Object State -eq 'device').Count){'Ready for manual device testing'}else{'Physical testing pending: no authorized device'};PhysicalGameplayTested=$false;Devices=$devices}
$directory=Split-Path ([System.IO.Path]::GetFullPath($OutputPath)) -Parent
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$report | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $OutputPath -Encoding utf8
$report | ConvertTo-Json -Depth 5
