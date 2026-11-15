#!/bin/bash
# build.sh — pravi CloudAndBear (zasnovano na Debianu 13) live ISO pomoću live-build.
# Pokreće se UNUTAR Debian kontejnera (debian/build.ps1): live-build traži Linux fajl-sistem (chroot, linkovi), ne Windows folder.
# Ulaz: /src = folder debian/ (samo čitanje), izlaz: /out/cloudandbear-debian.iso
# Pravilo: nikakvih drugih GUI-a (nema desktop okruženja, X11, menadžera prijave ni grafičkog instalatera); GUI će dodati samo tvoj paket.
set -euo pipefail
src=${SRC:-/src}
out=${OUT:-/out}
work=/build
export DEBIAN_FRONTEND=noninteractive

echo "== alati za izgradnju"
apt-get update -qq
apt-get install -y -qq live-build debootstrap xorriso squashfs-tools mtools dosfstools syslinux syslinux-common isolinux \
  grub-pc-bin grub-efi-amd64-bin ca-certificates > /dev/null

rm -rf "$work"
mkdir -p "$work" "$out"
cd "$work"

echo "== lb config"
lb config \
  --distribution trixie \
  --architectures amd64 \
  --archive-areas "main contrib non-free-firmware" \
  --binary-images iso-hybrid \
  --bootloaders "syslinux,grub-efi" \
  --debian-installer live \
  --debian-installer-gui false \
  --apt-recommends false \
  --security true --updates true \
  --iso-application "CloudAndBear Linux" \
  --iso-publisher "CloudAndBear; https://github.com/Marokos999/CloudAndBearLinux" \
  --iso-volume "CLOUDANDBEAR" \
  --image-name cloudandbear \
  --bootappend-live "boot=live components quiet"

# naša konfiguracija preko one koju je napravio lb config; Windows može da ostavi CRLF, pa se uklanja
cp -a "$src/config-overlay/." config/
find config -type f -exec sed -i 's/\r$//' {} +
find config/hooks -type f -name '*.chroot' -exec chmod +x {} + 2> /dev/null || true

echo "== provera spiska paketa (svaki mora da postoji u trixie)"
sed -i 's/^Components: main$/Components: main contrib non-free-firmware/' /etc/apt/sources.list.d/debian.sources 2> /dev/null || true
apt-get update -qq
missing=0
while read -r p; do
  [ -n "$p" ] || continue
  apt-cache show "$p" > /dev/null 2>&1 || { echo "NEMA paketa: $p"; missing=1; }
done < <(grep -hvE '^\s*(#|$)' config/package-lists/*.list.chroot)
[ "$missing" -eq 0 ] || { echo "ispravi config-overlay/package-lists pa ponovi"; exit 1; }
[ "${CHECK_ONLY:-0}" = "1" ] && { echo "== spisak paketa je ispravan (CHECK_ONLY)"; exit 0; }

echo "== lb build (traje 15-30 min)"
lb build 2>&1 | tee build.log
iso=$(ls ./*.iso 2> /dev/null | head -n 1)
[ -n "$iso" ] || { echo "ISO nije napravljen, vidi $work/build.log"; exit 1; }
cp -v "$iso" "$out/cloudandbear-debian.iso"
cp -v build.log "$out/cloudandbear-debian.build.log"
(cd "$out" && sha256sum cloudandbear-debian.iso | tee cloudandbear-debian.iso.sha256)
echo "== spisak paketa u slici (provera da nema GUI-ja)"
grep -E '^Package: ' chroot/var/lib/dpkg/status | sed 's/^Package: //' | sort > "$out/cloudandbear-debian.packages.txt" || true
wc -l "$out/cloudandbear-debian.packages.txt" 2> /dev/null || true
