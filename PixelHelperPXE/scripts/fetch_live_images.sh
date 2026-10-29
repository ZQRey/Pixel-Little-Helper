#!/usr/bin/env bash
# ==============================================================================
# fetch_live_images.sh
# Download official Debian Netboot, SystemRescue and Clonezilla images
# ==============================================================================
set -euo pipefail

WEB_ROOT="${WEB_ROOT:-/srv/www/pxe}"

log_info()  { echo -e "\033[1;34m[INFO]\033[0m $*"; }
log_ok()    { echo -e "\033[1;32m[OK]\033[0m $*"; }
log_warn()  { echo -e "\033[1;33m[WARN]\033[0m $*"; }
log_err()   { echo -e "\033[1;31m[ERROR]\033[0m $*" >&2; }

if [[ "$(id -u)" -ne 0 ]]; then
    log_err "This script must be run as root (or with sudo)."
    exit 1
fi

# 1. Debian Netboot (Bookworm 12 / Trixie)
log_info "1. Fetching Debian 12 (Bookworm) official Netboot kernel and initrd..."
DEBIAN_DIR="${WEB_ROOT}/os/debian"
mkdir -p "${DEBIAN_DIR}"
DEBIAN_NETBOOT_URL="https://deb.debian.org/debian/dists/bookworm/main/installer-amd64/current/images/netboot/debian-installer/amd64"

if curl -fsSL -o "${DEBIAN_DIR}/linux" "${DEBIAN_NETBOOT_URL}/linux"; then
    log_ok "Debian kernel downloaded."
else
    log_warn "Failed to download Debian netboot kernel."
fi

if curl -fsSL -o "${DEBIAN_DIR}/initrd.gz" "${DEBIAN_NETBOOT_URL}/initrd.gz"; then
    log_ok "Debian initrd downloaded."
else
    log_warn "Failed to download Debian netboot initrd."
fi

# 2. SystemRescue
log_info "2. SystemRescue directory is ready at ${WEB_ROOT}/os/sysrescue"
mkdir -p "${WEB_ROOT}/os/sysrescue"
cat << 'EOF' > "${WEB_ROOT}/os/sysrescue/README.txt"
To add SystemRescue Live:
1. Download latest SystemRescue ISO from https://www.system-rescue.org/Download/
2. Mount ISO: mount -o loop systemrescue-x.y.z-amd64.iso /mnt
3. Copy kernel and initramfs:
   cp /mnt/sysrescue/boot/x86_64/vmlinuz /srv/www/pxe/os/sysrescue/
   cp /mnt/sysrescue/boot/x86_64/initram.img /srv/www/pxe/os/sysrescue/
4. Copy sysrescue.sfs / squashfs:
   cp /mnt/sysrescue/x86_64/sysrescue.sfs /srv/www/pxe/os/sysrescue/
EOF

# 3. Clonezilla Live
log_info "3. Clonezilla directory is ready at ${WEB_ROOT}/os/clonezilla"
mkdir -p "${WEB_ROOT}/os/clonezilla"
cat << 'EOF' > "${WEB_ROOT}/os/clonezilla/README.txt"
To add Clonezilla Live:
1. Download latest Clonezilla Live zip/iso from https://clonezilla.org/downloads.php
2. Extract files:
   - live/vmlinuz -> /srv/www/pxe/os/clonezilla/vmlinuz
   - live/initrd.img -> /srv/www/pxe/os/clonezilla/initrd.img
   - live/filesystem.squashfs -> /srv/www/pxe/os/clonezilla/filesystem.squashfs
EOF

chmod -R a+r "${WEB_ROOT}/os"
find "${WEB_ROOT}/os" -type d -exec chmod 755 {} +

log_ok "Base OS media directories initialized."

