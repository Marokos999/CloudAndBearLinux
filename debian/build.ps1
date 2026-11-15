# build.ps1 — pokreće debian/build.sh u Debian 13 kontejneru (Docker Desktop mora da radi); ISO izlazi u D:\CloudAndBear Linux\iso-debian
# Pre toga objavljuje GUI (dotnet publish, linux-x64) u gui\publish, jer on ulazi u sliku.
# Upotreba (PowerShell): & "D:\CloudAndBear Linux\CloudAndBearLinux\debian\build.ps1"            # pun ISO
#                         & "D:\CloudAndBear Linux\CloudAndBearLinux\debian\build.ps1" -CheckOnly  # samo provera spiska paketa
param([string]$Out = "D:\CloudAndBear Linux\iso-debian", [switch]$CheckOnly)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$gui = (Resolve-Path (Join-Path $here "..\gui")).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
docker info --format "{{.ServerVersion}}" 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Docker ne radi: pokreni Docker Desktop" }
if (-not $CheckOnly) {
  Remove-Item -Recurse -Force (Join-Path $gui "publish") -ErrorAction SilentlyContinue
  dotnet publish (Join-Path $gui "CloudAndBear.Desktop") -c Release -r linux-x64 --self-contained -o (Join-Path $gui "publish")
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish nije uspeo (vidi ispis iznad)" }
}
$check = if ($CheckOnly) { "1" } else { "0" }
# --privileged: live-build koristi chroot i mount; radni folder (/build) ostaje u kontejneru, van Windows fajl-sistema
docker run --rm --privileged --name cab-debian-build -e CHECK_ONLY=$check `
  -v "${here}:/src:ro" -v "${gui}:/gui:ro" -v "${Out}:/out" `
  debian:trixie bash /src/build.sh
if ($LASTEXITCODE -ne 0) { throw "izgradnja nije uspela (vidi ispis iznad)" }
Get-ChildItem $Out | Select-Object Name,@{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
