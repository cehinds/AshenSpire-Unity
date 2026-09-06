<# Start a finite owner-controlled Web preview session. Ctrl+C stops the server.
Use -ShareOnLan to test from your phone on the same trusted network. Windows firewall
and Wi-Fi isolation may prevent access; this script does not change firewall rules.
#>
param([int]$Port=8788,[switch]$ShareOnLan)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path $PSScriptRoot -Parent
$previewRoot=Join-Path $repositoryRoot 'Published\Web'
if (!(Test-Path -LiteralPath (Join-Path $previewRoot 'index.html'))) { throw 'Build Web first with tools/build-unity.ps1.' }
$previewBind=if($ShareOnLan){'0.0.0.0'}else{'127.0.0.1'}
Write-Output "Open http://localhost:$Port . Press Ctrl+C to stop."
if($ShareOnLan){Get-NetIPAddress -AddressFamily IPv4 | Where-Object {$_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*'} | ForEach-Object { Write-Output "Phone address: http://$($_.IPAddress):$Port" }}
python -m http.server $Port --bind $previewBind --directory $previewRoot
