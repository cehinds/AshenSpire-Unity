# Start-Companion.ps1 — portable local native cooperative host.
# Edit parameters at launch; no Unity Editor, repository or .NET installation needed.
param(
    [string]$WebRoot = (Join-Path $PSScriptRoot '../Web'),
    [ValidateRange(1024,65535)][int]$Port = 8795,
    [string]$StatePath = (Join-Path $PSScriptRoot 'State/host-state.json'),
    [switch]$ListenOnLan,
    [switch]$RecoverBackup
)
$ErrorActionPreference = 'Stop'
$WebRoot = [IO.Path]::GetFullPath($WebRoot)
if (!(Test-Path -LiteralPath (Join-Path $WebRoot 'index.html') -PathType Leaf)) {
    throw 'Web build not found. Extract the matching Web folder next to this companion folder, or run Start-Companion.ps1 -WebRoot C:\Path\To\Web.'
}
$hostAddress = if ($ListenOnLan) { '0.0.0.0' } else { '127.0.0.1' }
$launchArguments = @('--web-root', $WebRoot, '--content-root', (Join-Path $PSScriptRoot 'Content'), '--url', "http://${hostAddress}:$Port", '--state', [IO.Path]::GetFullPath($StatePath))
if ($RecoverBackup) { $launchArguments += @('--recover-backup', 'true') }
Write-Host "Open http://127.0.0.1:$Port/ in your browser. Leave this window open while playing."
if ($ListenOnLan) { Write-Host "Other players use this computer's LAN IP and port $Port. No firewall or router settings are changed." }
& (Join-Path $PSScriptRoot 'AshenSpire.Companion.exe') @launchArguments
exit $LASTEXITCODE
