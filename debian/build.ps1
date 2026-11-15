# build.ps1 — pokreće debian/build.sh u Debian 13 kontejneru (Docker Desktop mora da radi); ISO izlazi u D:\CloudAndBear Linux\iso-debian
# Upotreba (PowerShell): & "D:\CloudAndBear Linux\CloudAndBearLinux\debian\build.ps1"            # pun ISO
#                         & "D:\CloudAndBear Linux\CloudAndBearLinux\debian\build.ps1" -CheckOnly  # samo provera spiska paketa
param([string]$Out = "D:\CloudAndBear Linux\iso-debian", [switch]$CheckOnly)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
docker info --format "{{.ServerVersion}}" 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Docker ne radi: pokreni Docker Desktop" }
$check = if ($CheckOnly) { "1" } else { "0" }
# --privileged: live-build koristi chroot i mount; radni folder (/build) ostaje u kontejneru, van Windows fajl-sistema
docker run --rm --privileged --name cab-debian-build -e CHECK_ONLY=$check `
  -v "${here}:/src:ro" -v "${Out}:/out" `
  debian:trixie bash /src/build.sh
if ($LASTEXITCODE -ne 0) { throw "izgradnja nije uspela (vidi ispis iznad)" }
Get-ChildItem $Out | Select-Object Name,@{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
