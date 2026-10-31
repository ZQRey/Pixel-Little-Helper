#!/usr/bin/env bash
set -e
ISO="/srv/www/pxe/images/Windows 10 22H2 Setup.iso"
SLUG="Windows_10_22H2_Setup"
docker exec pxe-panel bash /opt/pxe-panel/scripts/build_winpe.sh "${ISO}" 172.16.16.61 "" "${SLUG}"

