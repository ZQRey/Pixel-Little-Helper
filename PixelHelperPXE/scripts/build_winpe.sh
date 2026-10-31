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
SERVER_IP="${2:-172.16.16.61}"
AUTOUNATTEND_PATH="${3:-}"
WEB_ROOT="${WEB_ROOT:-/srv/www/pxe}"
WIN_DIR="${WEB_ROOT}/os/windows"
STARTNET_SRC="$(dirname "$0")/startnet.cmd"

if [[ -z "${ISO_PATH}" || ! -f "${ISO_PATH}" ]]; then
    log_err "Usage: $0 <path-to-windows.iso> [server-ip] [autounattend-path]"
    log_err "Example: $0 /srv/www/pxe/images/Win10.iso 172.16.16.61"
    exit 1
fi

# Ensure wimtools and p7zip-full are available
if ! command -v wimlib-imagex >/dev/null 2>&1 || ! command -v 7z >/dev/null 2>&1; then
    log_info "Installing missing dependencies (wimtools, p7zip-full)..."
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq wimtools p7zip-full
fi

# Validate ISO archive integrity and header
log_info "Проверка целостности ISO-образа: ${ISO_PATH}..."
if ! 7z t -bso0 -bsp0 "${ISO_PATH}" bootmgr >/dev/null 2>&1; then
    ACTUAL_SIZE=$(stat -c%s "${ISO_PATH}" 2>/dev/null || echo 0)
    ACTUAL_MB=$(( ACTUAL_SIZE / 1024 / 1024 ))
    log_err "=========================================================================="
    log_err "ОШИБКА: ISO-образ повреждён или загружен не полностью!"
    log_err "Текущий размер файла на сервере: ${ACTUAL_MB} МБ."
    log_err "Оригинальный дистрибутив Windows обычно занимает 4500-5500 МБ."
    log_err "Пожалуйста, удалите этот файл во вкладке 'Образы ОС' и загрузите его заново,"
    log_err "дождавшись 100% завершения загрузки."
    log_err "=========================================================================="
    exit 1
fi

mkdir -p "${WIN_DIR}"/{boot,sources,iso_extracted}

# Check disk space on target partition (need at least 6 GB free)
AVAIL_KB=$(df -k "${WIN_DIR}" | awk 'NR==2 {print $4}')
if [[ "${AVAIL_KB}" -lt 6291456 ]]; then
    AVAIL_MB=$(( AVAIL_KB / 1024 ))
    log_err "ОШИБКА: Недостаточно свободного места на диске (${AVAIL_MB} МБ). Требуется минимум 6144 МБ (6 ГБ)."
    exit 1
fi

log_info "Очистка предыдущих файлов распакованного образа в ${WIN_DIR}/iso_extracted..."
rm -rf "${WIN_DIR}/iso_extracted"/*

TMP_MOUNT="$(mktemp -d /tmp/winiso.XXXXXX)"
cleanup() {
    if mountpoint -q "${TMP_MOUNT}" 2>/dev/null; then
        umount -f "${TMP_MOUNT}" 2>/dev/null || true
    fi
    rm -rf "${TMP_MOUNT}"
}
trap cleanup EXIT

USE_LOOP=false
if mount -o loop,ro "${ISO_PATH}" "${TMP_MOUNT}" 2>/dev/null; then
    USE_LOOP=true
    log_info "Mounted Windows ISO via loop device."
else
    log_info "Loop mount not available in container; extracting directly with 7z..."
fi

if [[ "${USE_LOOP}" == "true" ]]; then
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
        SRC_FILE=$(find "${TMP_MOUNT}/boot" -maxdepth 1 -iname "${f}" 2>/dev/null | head -n 1)
        if [[ -n "${SRC_FILE}" ]]; then
            cp -v "${SRC_FILE}" "${WIN_DIR}/boot/${f}"
        fi
    done

    # 4. boot.wim
    log_info "Copying boot.wim..."
    BOOT_WIM_SRC=$(find "${TMP_MOUNT}/sources" -maxdepth 1 -iname "boot.wim" 2>/dev/null | head -n 1)
    if [[ -z "${BOOT_WIM_SRC}" ]]; then
        log_err "sources/boot.wim not found in ISO!"
        exit 1
    fi
    cp -v "${BOOT_WIM_SRC}" "${WIN_DIR}/sources/boot.wim"

    log_info "Unpacking full Windows ISO into Samba installation directory..."
    log_info "Target: ${WIN_DIR}/iso_extracted (this may take a few minutes)..."
    rsync -a --info=progress2 "${TMP_MOUNT}/" "${WIN_DIR}/iso_extracted/"
else
    log_info "Extracting full Windows ISO to ${WIN_DIR}/iso_extracted/ using 7z..."
    7z x -y "${ISO_PATH}" -o"${WIN_DIR}/iso_extracted/"

    # 1. BIOS boot manager
    BOOTMGR_SRC=$(find "${WIN_DIR}/iso_extracted" -maxdepth 2 -iname "bootmgr" 2>/dev/null | head -n 1)
    if [[ -n "${BOOTMGR_SRC}" ]]; then
        cp -v "${BOOTMGR_SRC}" "${WIN_DIR}/bootmgr"
    fi

    # 2. UEFI boot manager
    BOOT_EFI=$(find "${WIN_DIR}/iso_extracted" -maxdepth 4 -ipath "*/efi/boot/bootx64.efi" 2>/dev/null | head -n 1)
    if [[ -z "${BOOT_EFI}" ]]; then
        BOOT_EFI=$(find "${WIN_DIR}/iso_extracted" -maxdepth 4 -iname "bootx64.efi" 2>/dev/null | head -n 1)
    fi
    if [[ -n "${BOOT_EFI}" ]]; then
        cp -v "${BOOT_EFI}" "${WIN_DIR}/bootmgr.efi"
    fi

    # 3. BCD store and boot.sdi
    for f in bcd boot.sdi; do
        SRC_FILE=$(find "${WIN_DIR}/iso_extracted" -maxdepth 3 -iname "${f}" 2>/dev/null | head -n 1)
        if [[ -n "${SRC_FILE}" ]]; then
            cp -v "${SRC_FILE}" "${WIN_DIR}/boot/${f}"
        fi
    done

    # 4. boot.wim
    log_info "Locating boot.wim..."
    BOOT_WIM_SRC=$(find "${WIN_DIR}/iso_extracted" -maxdepth 3 -iname "boot.wim" 2>/dev/null | head -n 1)
    if [[ -z "${BOOT_WIM_SRC}" ]]; then
        log_err "sources/boot.wim not found in extracted ISO!"
        exit 1
    fi
    cp -v "${BOOT_WIM_SRC}" "${WIN_DIR}/sources/boot.wim"
fi

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

# If autounattend.xml is provided, place it in iso_extracted and in boot.wim root
if [[ -n "${AUTOUNATTEND_PATH}" && -f "${AUTOUNATTEND_PATH}" ]]; then
    log_info "Deploying autounattend.xml to installation share..."
    cp -v "${AUTOUNATTEND_PATH}" "${WIN_DIR}/iso_extracted/autounattend.xml"
    chmod 644 "${WIN_DIR}/iso_extracted/autounattend.xml"

    for idx in $(seq 1 "${IMAGE_COUNT}"); do
        log_info "Injecting autounattend.xml into boot.wim [Index ${idx}]..."
        wimlib-imagex update "${WIN_DIR}/sources/boot.wim" "${idx}" \
            --command="add ${AUTOUNATTEND_PATH} /autounattend.xml" || true
    done
fi

chmod -R a+r "${WIN_DIR}/iso_extracted/"
find "${WIN_DIR}/iso_extracted/" -type d -exec chmod 755 {} +

log_ok "Windows сетевая установка и WinPE подготовлены успешно!"
log_ok "Wimboot файлы:"
ls -lh "${WIN_DIR}/bootmgr"* "${WIN_DIR}/boot/"* "${WIN_DIR}/sources/boot.wim"
