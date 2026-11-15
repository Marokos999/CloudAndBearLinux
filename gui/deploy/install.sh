#!/bin/bash
# install.sh — postavlja CloudAndBear GUI na uređaj (laptop). Pokreće se sa sudo.
#   sudo bash ~/deploy/install.sh            # program je u ~/cab-gui (scp -r publish ...:cab-gui)
#   sudo bash ~/deploy/install.sh /putanja   # program je negde drugde
# Postavlja: program u /opt/cloudandbear-gui, servis, nalog cloudandbear (sysusers), PAM, polkit pravila; pa pokreće servis.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
app=${1:-${SUDO_USER:+$(getent passwd "$SUDO_USER" | cut -d: -f6)/cab-gui}}
app=${app:-$here/../cab-gui}

[ "$(id -u)" = 0 ] || { echo "pokreni sa sudo"; exit 1; }
[ -f "$app/CloudAndBear.Desktop" ] || { echo "nema $app/CloudAndBear.Desktop (prvo scp -r publish ...:cab-gui)"; exit 1; }
for f in cloudandbear-gui.service cloudandbear.sysusers cloudandbear-gui.pam cloudandbear-gui.rules; do
  [ -f "$here/$f" ] || { echo "nema $here/$f"; exit 1; }
done

echo "== zaustavljam stari GUI"
systemctl stop cloudandbear-gui 2> /dev/null || true

echo "== program u /opt/cloudandbear-gui"
rm -rf /opt/cloudandbear-gui
cp -r "$app" /opt/cloudandbear-gui
# vlasnik root, ostali smeju samo da čitaju i izvršavaju (scp sa Windowsa pravi foldere samo za vlasnika, a nalog cloudandbear ne bi mogao da ih otvori)
chown -R root:root /opt/cloudandbear-gui
chmod -R a+rX,go-w /opt/cloudandbear-gui
chmod 755 /opt/cloudandbear-gui/CloudAndBear.Desktop

echo "== servis, nalog, PAM, polkit"
install -D -m 644 "$here/cloudandbear-gui.service" /etc/systemd/system/cloudandbear-gui.service
install -D -m 644 "$here/cloudandbear.sysusers" /usr/lib/sysusers.d/cloudandbear.conf
install -D -m 644 "$here/cloudandbear-gui.pam" /etc/pam.d/cloudandbear-gui
install -D -m 644 "$here/cloudandbear-gui.rules" /etc/polkit-1/rules.d/49-cloudandbear-gui.rules
systemd-sysusers /usr/lib/sysusers.d/cloudandbear.conf
# stanje je do sada pripadalo root-u (raspored tastature): sada ga ima nalog cloudandbear
mkdir -p /var/lib/cloudandbear
chown -R cloudandbear:cloudandbear /var/lib/cloudandbear
id cloudandbear

systemctl daemon-reload
systemctl enable cloudandbear-gui
echo "== pokrećem GUI (kao nalog cloudandbear)"
systemctl start cloudandbear-gui
sleep 3
systemctl --no-pager --lines=0 status cloudandbear-gui | sed -n 1,4p
ps -o user=,pid=,args= -C CloudAndBear.Desktop
