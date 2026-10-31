#!/usr/bin/env bash
set -e
ISO="/srv/www/pxe/images/Windows 11 24H2 Official.iso"
SLUG="Windows_11_24H2_Official"
docker exec pxe-panel bash /opt/pxe-panel/scripts/build_winpe.sh "${ISO}" 172.16.16.61 "" "${SLUG}"

