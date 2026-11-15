#!/bin/bash
# build-repo.sh — pravi apt repozitorijum (običan folder, spreman za bilo koji web server) od paketa u gui/package/out.
#   bash gui/package/build-repo.sh [izlazni-folder]          # podrazumevano gui/package/repo
# Potpisivanje: APT_SIGNING_KEY je ID (ili otisak) tajnog GPG ključa iz GNUPGHOME-a (ili iz ~/.gnupg).
# Bez njega repozitorijum je nepotpisan i klijent ga sme da koristi samo sa [trusted=yes] (samo za probu).
# Izlaz: pool/main/*.deb, dists/trixie/{Release,InRelease,Release.gpg,main/binary-amd64/Packages*} i cloudandbear.gpg (javni ključ).
# Treba: apt-ftparchive (paket apt-utils), gzip, xz, a za potpis gpg.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
repo=${1:-$here/repo}
in=${OUT:-$here/out}
dist=trixie
comp=main
arch=amd64

command -v apt-ftparchive > /dev/null || { echo "treba apt-ftparchive (paket apt-utils)"; exit 1; }
ls "$in"/*.deb > /dev/null 2>&1 || { echo "nema paketa u $in: prvo build-deb.sh"; exit 1; }

rm -rf "$repo"
mkdir -p "$repo/pool/$comp" "$repo/dists/$dist/$comp/binary-$arch"
cp "$in"/*.deb "$repo/pool/$comp/"

cd "$repo"
apt-ftparchive packages "pool/$comp" > "dists/$dist/$comp/binary-$arch/Packages"
gzip -9nk "dists/$dist/$comp/binary-$arch/Packages"
xz -9k "dists/$dist/$comp/binary-$arch/Packages"

apt-ftparchive \
  -o APT::FTPArchive::Release::Origin=CloudAndBear \
  -o APT::FTPArchive::Release::Label=CloudAndBear \
  -o APT::FTPArchive::Release::Suite=stable \
  -o APT::FTPArchive::Release::Codename=$dist \
  -o APT::FTPArchive::Release::Architectures=$arch \
  -o APT::FTPArchive::Release::Components=$comp \
  -o APT::FTPArchive::Release::Description="CloudAndBear Linux paketi" \
  release "dists/$dist" > "dists/$dist/Release"

if [ -n "${APT_SIGNING_KEY:-}" ]; then
  gpg --batch --yes --default-key "$APT_SIGNING_KEY" --digest-algo SHA512 --clearsign -o "dists/$dist/InRelease" "dists/$dist/Release"
  gpg --batch --yes --default-key "$APT_SIGNING_KEY" --digest-algo SHA512 --armor --detach-sign -o "dists/$dist/Release.gpg" "dists/$dist/Release"
  gpg --batch --yes --export "$APT_SIGNING_KEY" > cloudandbear.gpg   # javni ključ (binarni, za Signed-By=)
  echo "repozitorijum je POTPISAN ključem $APT_SIGNING_KEY"
else
  echo "UPOZORENJE: repozitorijum NIJE potpisan (nema APT_SIGNING_KEY); klijenti ga mogu koristiti samo sa [trusted=yes]"
fi

echo "== sadržaj"
find . -type f | sort | sed 's/^\.\///'
