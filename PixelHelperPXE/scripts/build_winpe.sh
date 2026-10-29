#!/usr/bin/env bash
# ==============================================================================
# build_winpe.sh
# Extract and Inject startnet.cmd into Windows boot.wim using wimlib-imagex
# No Windows ADK required - runs 100% on Linux
# ==============================================================================
set -euo pipefail

log_info()  { echo -e "\033[1;34m[INFO]\033[0m $*"; }
log_ok()    { echo -e "\033[1;32m[OK]\033[0m $*"; }
log_warn()  { echo -e "\033[1;33m[WARN]\033[0m $*"; }
log_err()   { echo -e "\033[1;31m[ERROR]\033[0m $*" >&2; }

if [[ "$(id -u)" -ne 0 ]]; then
    log_err "This script must be run as root (or with sudo)."
    exit 1
fi

ISO_PATH="${1:-}"
SERVER_IP="${2:-172.16.16.6}"
WEB_ROOT="${WEB_ROOT:-/srv/www/pxe}"
WIN_DIR="${WEB_ROOT}/os/windows"
STARTNET_SRC="$(dirname "$0")/startnet.cmd"

if [[ -z "${ISO_PATH}" || ! -f "${ISO_PATH}" ]]; then
    log_err "Usage: $0 <path-to-windows.iso> [server-ip]"
    log_err "Example: $0 /tmp/Win11_24H2.iso 172.16.16.6"
    exit 1
fi

log_info "Ensuring wimtools and p7zip-full are installed..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq wimtools p7zip-full

mkdir -p "${WIN_DIR}"/{boot,sources,iso_extracted}

TMP_MOUNT="$(mktemp -d /tmp/winiso.XXXXXX)"
cleanup() {
    if mountpoint -q "${TMP_MOUNT}"; then
        umount -f "${TMP_MOUNT}" || true
    fi
    rm -rf "${TMP_MOUNT}"
}
trap cleanup EXIT

log_info "Mounting Windows ISO: ${ISO_PATH}..."
mount -o loop,ro "${ISO_PATH}" "${TMP_MOUNT}"

log_info "Extracting bootloader files for wimboot..."
# 1. BIOS boot manager
if [[ -f "${TMP_MOUNT}/bootmgr" ]]; then
    cp -v "${TMP_MOUNT}/bootmgr" "${WIN_DIR}/bootmgr"
fi

# 2. UEFI boot manager
if [[ -f "${TMP_MOUNT}/efi/boot/bootx64.efi" ]]; then
    cp -v "${TMP_MOUNT}/efi/boot/bootx64.efi" "${WIN_DIR}/bootmgr.efi"
elif [[ -f "${TMP_MOUNT}/EFI/BOOT/BOOTX64.EFI" ]]; then
    cp -v "${TMP_MOUNT}/EFI/BOOT/BOOTX64.EFI" "${WIN_DIR}/bootmgr.efi"
fi

# 3. BCD store and boot.sdi
for f in bcd boot.sdi; do
    SRC_FILE=$(find "${TMP_MOUNT}/boot" -maxdepth 1 -iname "${f}" | head -n 1)
    if [[ -n "${SRC_FILE}" ]]; then
        cp -v "${SRC_FILE}" "${WIN_DIR}/boot/${f}"
    fi
done

# 4. boot.wim
log_info "Copying boot.wim..."
BOOT_WIM_SRC=$(find "${TMP_MOUNT}/sources" -maxdepth 1 -iname "boot.wim" | head -n 1)
if [[ -z "${BOOT_WIM_SRC}" ]]; then
    log_err "sources/boot.wim not found in ISO!"
    exit 1
fi
cp -v "${BOOT_WIM_SRC}" "${WIN_DIR}/sources/boot.wim"
chmod 644 "${WIN_DIR}/sources/boot.wim"

log_info "Generating parameterized startnet.cmd with Server IP: ${SERVER_IP}..."
TMP_STARTNET="/tmp/startnet_gen.cmd"
sed "s/SERVER_IP_PLACEHOLDER/${SERVER_IP}/g" "${STARTNET_SRC}" > "${TMP_STARTNET}"

# Check available images in boot.wim
IMAGE_COUNT=$(wiminfo "${WIN_DIR}/sources/boot.wim" | grep "Image Count:" | awk '{print $3}')
log_info "Detected ${IMAGE_COUNT} image(s) in boot.wim."

# Inject custom startnet.cmd into all images (typically 1: WinPE, 2: Windows Setup)
for idx in $(seq 1 "${IMAGE_COUNT}"); do
    log_info "Injecting startnet.cmd into boot.wim [Index ${idx}]..."
    wimlib-imagex update "${WIN_DIR}/sources/boot.wim" "${idx}" \
        --command="add ${TMP_STARTNET} /Windows/System32/startnet.cmd"
done
rm -f "${TMP_STARTNET}"

log_info "Unpacking full Windows ISO into Samba installation directory..."
log_info "Target: ${WIN_DIR}/iso_extracted (this may take a few minutes)..."
rsync -a --info=progress2 "${TMP_MOUNT}/" "${WIN_DIR}/iso_extracted/"
chmod -R a+r "${WIN_DIR}/iso_extracted/"
find "${WIN_DIR}/iso_extracted/" -type d -exec chmod 755 {} +

log_ok "Windows WinPE boot files and Samba installation share prepared successfully!"
log_ok "Wimboot files:"
ls -lh "${WIN_DIR}/bootmgr"* "${WIN_DIR}/boot/"* "${WIN_DIR}/sources/boot.wim"

