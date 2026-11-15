#!/bin/bash
# build-deb.sh — pravi paket cloudandbear-desktop (.deb) od objavljenog GUI-ja (gui/publish) i fajlova iz gui/deploy.
#   bash gui/package/build-deb.sh [verzija]     # izlaz: gui/package/out/cloudandbear-desktop_VERZIJA_amd64.deb
# Prvo: dotnet publish gui/CloudAndBear.Desktop -c Release -r linux-x64 --self-contained -o gui/publish
# Treba samo dpkg-deb (Debian ili Ubuntu). Verzija je podrazumevano 0.1.0+DATUM (UTC, do minuta).
set -euo pipefail
umask 022   # foldere pravi mkdir, a dpkg prava foldera iz paketa primenjuje i na /usr i /etc: moraju biti 755, ne 775

here=$(cd "$(dirname "$0")" && pwd)
gui=$(cd "$here/.." && pwd)
version=${1:-0.1.0+$(date -u +%Y%m%d%H%M)}
publish=${PUBLISH:-$gui/publish}
out=${OUT:-$here/out}

[ -f "$publish/CloudAndBear.Desktop" ] || { echo "nema $publish/CloudAndBear.Desktop: prvo dotnet publish (vidi zaglavlje)"; exit 1; }
for f in cloudandbear-gui.service cloudandbear.sysusers cloudandbear-gui.pam cloudandbear-gui.rules; do
  [ -f "$gui/deploy/$f" ] || { echo "nema $gui/deploy/$f"; exit 1; }
done
command -v dpkg-deb > /dev/null || { echo "treba dpkg-deb (paket dpkg-dev)"; exit 1; }

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT

# put IZVOR ODREDISTE [MOD]: kopira tekstualni fajl bez CR (Windows) sa zadatim pravima
put() {
  mkdir -p "$(dirname "$stage/$2")"
  sed 's/\r$//' "$1" > "$stage/$2"
  chmod "${3:-644}" "$stage/$2"
}

# program: vlasnik root, ostali smeju samo da čitaju i izvršavaju (Windows ne čuva prava, pa se postavljaju ovde)
mkdir -p "$stage/opt"
cp -a "$publish" "$stage/opt/cloudandbear-gui"
find "$stage/opt/cloudandbear-gui" -type d -exec chmod 755 {} +
find "$stage/opt/cloudandbear-gui" -type f -exec chmod 644 {} +
chmod 755 "$stage/opt/cloudandbear-gui/CloudAndBear.Desktop"
[ -f "$stage/opt/cloudandbear-gui/createdump" ] && chmod 755 "$stage/opt/cloudandbear-gui/createdump"

# servis, nalog (sysusers), PAM (conffile) i polkit pravila: isti fajlovi koje koristi gui/deploy/install.sh
put "$gui/deploy/cloudandbear-gui.service" usr/lib/systemd/system/cloudandbear-gui.service
put "$gui/deploy/cloudandbear.sysusers" usr/lib/sysusers.d/cloudandbear.conf
put "$gui/deploy/cloudandbear-gui.pam" etc/pam.d/cloudandbear-gui
put "$gui/deploy/cloudandbear-gui.rules" usr/share/polkit-1/rules.d/49-cloudandbear-gui.rules

# dokumentacija
put "$here/copyright" usr/share/doc/cloudandbear-desktop/copyright
changelog=$(mktemp)
cat > "$changelog" << EOF
cloudandbear-desktop ($version) stable; urgency=medium

  * Paket napravljen od gui/publish (build-deb.sh).

 -- Marokos999 <Marokos999@users.noreply.github.com>  $(date -u -R)
EOF
mkdir -p "$stage/usr/share/doc/cloudandbear-desktop"
gzip -9n -c "$changelog" > "$stage/usr/share/doc/cloudandbear-desktop/changelog.gz"
chmod 644 "$stage/usr/share/doc/cloudandbear-desktop/changelog.gz"
rm -f "$changelog"

# kontrolni podaci
mkdir -p "$stage/DEBIAN"
for s in postinst prerm postrm; do
  put "$here/$s" "DEBIAN/$s" 755
done
echo /etc/pam.d/cloudandbear-gui > "$stage/DEBIAN/conffiles"
size=$(du -sk --exclude=DEBIAN "$stage" | cut -f1)
cat > "$stage/DEBIAN/control" << EOF
Package: cloudandbear-desktop
Version: $version
Architecture: amd64
Maintainer: Marokos999 <Marokos999@users.noreply.github.com>
Installed-Size: $size
Depends: libc6 (>= 2.34), libgcc-s1, libstdc++6, zlib1g, libgl1, libfontconfig1, libdrm2, libegl1, libgbm1, libinput10, libevdev2, libgl1-mesa-dri, libegl-mesa0, systemd, libpam-systemd, polkitd, kbd
Recommends: network-manager, docker.io | docker-ce, kubernetes-client | kubectl, alsa-utils
Section: admin
Priority: optional
Homepage: https://github.com/Marokos999/CloudAndBearLinux
Description: CloudAndBear GUI (Avalonia, direktno na ekran)
 Jedini grafički interfejs CloudAndBear Linuxa: kontrolna tabla, mreža (Wi-Fi),
 kontejneri (Docker), Kubernetes (samo čitanje) i sistem. Crta direktno na ekran
 (DRM/KMS), bez X11 i Wayland servera.
 .
 Radi kao nalog cloudandbear na tty1 umesto konzolne prijave; Ctrl+Alt+Q vraća
 konzolu. Program je samostalan (.NET runtime je u paketu).
EOF
(cd "$stage" && find . -type f ! -path './DEBIAN/*' -printf '%P\0' | xargs -0 md5sum > DEBIAN/md5sums)
find "$stage" -type d -exec chmod 755 {} +   # za svaki slučaj, i za mktemp folder (0700)

mkdir -p "$out"
deb="$out/cloudandbear-desktop_${version}_amd64.deb"
dpkg-deb --root-owner-group -Zxz -b "$stage" "$deb" > /dev/null
echo "paket: $deb ($(du -h "$deb" | cut -f1))"
dpkg-deb -I "$deb" | sed -n '1,/^ Description/p' | head -20
