#!/bin/bash
# build.sh — pravi CloudAndBear (zasnovano na Debianu 13) live ISO pomoću live-build.
# Pokreće se UNUTAR Debian kontejnera (debian/build.ps1): live-build traži Linux fajl-sistem (chroot, linkovi), ne Windows folder.
# Ulaz: /src = folder debian/ (samo čitanje), /gui = folder gui/ sa već objavljenim programom (gui/publish, vidi build.ps1 i workflow),
# izlaz: /out/cloudandbear-debian.iso
# Pravilo: nikakvih drugih GUI-a (nema desktop okruženja, X11, menadžera prijave ni grafičkog instalatera); u slici je samo tvoj GUI.
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

# GUI ide u sliku tek posle prethodne naredbe: ona menja fajlove u config/, a binarne datoteke ne smeju da se diraju
gui=${GUI:-/gui}
if [ "${CHECK_ONLY:-0}" != "1" ]; then
  echo "== GUI (CloudAndBear.Desktop) u sliku"
  [ -f "$gui/publish/CloudAndBear.Desktop" ] || { echo "NEMA $gui/publish/CloudAndBear.Desktop: prvo dotnet publish (vidi build.ps1 ili workflow)"; exit 1; }
  [ -f "$gui/deploy/cloudandbear-gui.service" ] || { echo "NEMA $gui/deploy/cloudandbear-gui.service"; exit 1; }
  mkdir -p config/includes.chroot/opt/cloudandbear-gui config/includes.chroot/etc/systemd/system
  cp -a "$gui/publish/." config/includes.chroot/opt/cloudandbear-gui/
  sed 's/\r$//' "$gui/deploy/cloudandbear-gui.service" > config/includes.chroot/etc/systemd/system/cloudandbear-gui.service
  du -sh config/includes.chroot/opt/cloudandbear-gui
fi

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
echo "== provera: GUI, servis i ispravke u slici"
[ -f chroot/opt/cloudandbear-gui/CloudAndBear.Desktop ] || { echo "GUI nije u slici"; exit 1; }
[ -L chroot/etc/systemd/system/multi-user.target.wants/cloudandbear-gui.service ] || { echo "servis cloudandbear-gui nije uključen"; exit 1; }
[ -f chroot/usr/share/cloudandbear/debian.sources ] || { echo "nema debian.sources u slici"; exit 1; }
grep -q '^CODESET="Lat2"' chroot/etc/default/console-setup || { echo "console-setup nije naš (Lat2)"; exit 1; }
[ -f chroot/etc/default/grub.d/cloudandbear.cfg ] || { echo "nema grub.d/cloudandbear.cfg"; exit 1; }
# preseed mora da bude u initrd-u instalatera, inače se instalacija vraća na "debian" i bez sudo grupe
if ! find binary/install -name 'initrd.gz' -exec sh -c 'zcat "$1" | cpio -t 2> /dev/null | grep -qE "^(\./)?preseed\.cfg$"' _ {} \; -print | grep -q .; then
  echo "UPOZORENJE: preseed.cfg nije nađen u initrd-u instalatera (binary/install)"
fi
cp -v "$iso" "$out/cloudandbear-debian.iso"
cp -v build.log "$out/cloudandbear-debian.build.log"
(cd "$out" && sha256sum cloudandbear-debian.iso | tee cloudandbear-debian.iso.sha256)
echo "== spisak paketa u slici (provera da nema GUI-ja)"
grep -E '^Package: ' chroot/var/lib/dpkg/status | sed 's/^Package: //' | sort > "$out/cloudandbear-debian.packages.txt" || true
wc -l "$out/cloudandbear-debian.packages.txt" 2> /dev/null || true
