#!/usr/bin/env bash
# ==============================================================================
# setup_bootloaders.sh
# Production-ready PXE Bootloader Provisioner (UEFI Secure Boot + Legacy BIOS)
# ==============================================================================
set -euo pipefail

# Configurable paths (can be overridden via environment)
TFTP_ROOT="${TFTP_ROOT:-/srv/tftp}"
WEB_ROOT="${WEB_ROOT:-/srv/www/pxe}"
DEBIAN_RELEASE="${DEBIAN_RELEASE:-bookworm}"

log_info()  { echo -e "\033[1;34m[INFO]\033[0m $*"; }
log_ok()    { echo -e "\033[1;32m[OK]\033[0m $*"; }
log_warn()  { echo -e "\033[1;33m[WARN]\033[0m $*"; }
log_err()   { echo -e "\033[1;31m[ERROR]\033[0m $*" >&2; }

if [[ "$(id -u)" -ne 0 ]]; then
    log_err "This script must be run as root (or with sudo)."
    exit 1
fi

log_info "Step 1: Creating directory tree..."
mkdir -p "${TFTP_ROOT}"/{pxelinux.cfg,grub,boot/grub}
mkdir -p "${WEB_ROOT}"/{grub,os/{debian,sysrescue,clonezilla,windows/{boot,sources,iso_extracted}},tools}

# Ensure permissions
chmod 755 "${TFTP_ROOT}" "${WEB_ROOT}"

# Temporary workspace for deb extraction
TMP_DIR="$(mktemp -d /tmp/pxe-bootloaders.XXXXXX)"
cleanup() {
    rm -rf "${TMP_DIR}"
}
trap cleanup EXIT

log_info "Step 2: Installing syslinux dependencies for Legacy BIOS..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq syslinux-common pxelinux binutils curl ca-certificates

# -----------------------------------------------------------------------------
# 1. UEFI Secure Boot Loaders (shimx64.efi & signed GRUB 2.12 with ProxyDHCP)
# -----------------------------------------------------------------------------
log_info "Step 3: Downloading official signed Debian packages for UEFI Secure Boot..."
cd "${TMP_DIR}"

# Download signed shim and signed GRUB from Debian Trixie (grub 2.12+).
# GRUB 2.12 contains the efinet ProxyDHCP patch required for DHCP Proxy / dnsmasq setups.
apt-get download -qq shim-signed grub-efi-amd64-signed grub-efi-amd64-bin

log_info "Extracting shim-signed and grub-efi-amd64-signed..."
mkdir -p shim_extracted grub_signed_extracted grub_bin_extracted
SHIM_DEB=$(ls shim-signed_*.deb | head -n 1)
dpkg-deb -x "${SHIM_DEB}" shim_extracted/

GRUB_SIGNED_DEB=$(ls grub-efi-amd64-signed_*.deb | head -n 1)
dpkg-deb -x "${GRUB_SIGNED_DEB}" grub_signed_extracted/

GRUB_BIN_DEB=$(ls grub-efi-amd64-bin_*.deb | head -n 1)
dpkg-deb -x "${GRUB_BIN_DEB}" grub_bin_extracted/

# Locate shimx64.efi.signed
SHIM_BIN=$(find shim_extracted/ -name "shimx64.efi.signed" | head -n 1)
if [[ -z "${SHIM_BIN}" ]]; then
    log_err "shimx64.efi.signed not found in ${SHIM_DEB}!"
    exit 1
fi
mkdir -p "${TFTP_ROOT}/debian-installer/amd64"
cp -v "${SHIM_BIN}" "${TFTP_ROOT}/shimx64.efi"
cp -v "${SHIM_BIN}" "${TFTP_ROOT}/debian-installer/amd64/bootnetx64.efi"

# Locate signed GRUB installer netboot binary
GRUB_NET_BIN=$(find grub_signed_extracted/ -name "grubnetx64-installer.efi.signed" | head -n 1)
if [[ -z "${GRUB_NET_BIN}" ]]; then
    GRUB_NET_BIN=$(find grub_signed_extracted/ -name "grubnetx64.efi.signed" -o -name "grubx64.efi.signed" | head -n 1)
fi
if [[ -z "${GRUB_NET_BIN}" ]]; then
    log_err "Signed GRUB binary not found!"
    exit 1
fi
cp -v "${GRUB_NET_BIN}" "${TFTP_ROOT}/grubx64.efi"
cp -v "${GRUB_NET_BIN}" "${TFTP_ROOT}/debian-installer/amd64/grubx64.efi"

# Populate GRUB 2.12 modules across all search prefixes
for mod_dir in "${TFTP_ROOT}/debian-installer/amd64/grub/x86_64-efi" "${TFTP_ROOT}/debian-installer/amd64/x86_64-efi" "${TFTP_ROOT}/grub/x86_64-efi" "${TFTP_ROOT}/x86_64-efi"; do
    mkdir -p "${mod_dir}"
    cp -a grub_bin_extracted/usr/lib/grub/x86_64-efi/. "${mod_dir}/"
    echo "source \$prefix/grub.cfg" > "${mod_dir}/grub.cfg"
done

# Locate mmx64.efi (MokManager) which shim calls if needed for MOK enrollment
MOK_BIN=$(find shim_extracted/ -name "mmx64.efi.signed" -o -name "mmx64.efi" | head -n 1)
if [[ -n "${MOK_BIN}" ]]; then
    cp -v "${MOK_BIN}" "${TFTP_ROOT}/mmx64.efi"
fi

# shimx64.efi looks for grubx64.efi in the same directory by default.

# -----------------------------------------------------------------------------
# 2. Legacy BIOS Loaders (Syslinux / PXELINUX)
# -----------------------------------------------------------------------------
log_info "Step 4: Copying Syslinux/PXELINUX binaries for Legacy BIOS..."
# Locate lpxelinux.0 (PXELINUX with HTTP/FTP/TFTP transport support)
LPXE_SRC=""
for p in /usr/lib/PXELINUX/lpxelinux.0 /usr/lib/syslinux/modules/bios/lpxelinux.0; do
    if [[ -f "$p" ]]; then
        LPXE_SRC="$p"
        break
    fi
done

if [[ -z "${LPXE_SRC}" ]]; then
    log_err "lpxelinux.0 not found! Please check pxelinux package installation."
    exit 1
fi
cp -v "${LPXE_SRC}" "${TFTP_ROOT}/lpxelinux.0"

# Copy essential 32-bit Syslinux modules
SYSLINUX_BIOS_DIR="/usr/lib/syslinux/modules/bios"
for mod in ldlinux.c32 menu.c32 vesamenu.c32 libutil.c32 libcom32.c32 chain.c32 reboot.c32 poweroff.c32 linux.c32; do
    if [[ -f "${SYSLINUX_BIOS_DIR}/${mod}" ]]; then
        cp -v "${SYSLINUX_BIOS_DIR}/${mod}" "${TFTP_ROOT}/"
    else
        log_warn "Optional Syslinux module ${mod} not found in ${SYSLINUX_BIOS_DIR}."
    fi
done

# -----------------------------------------------------------------------------
# 3. Wimboot for Windows PE network booting (HTTP)
# -----------------------------------------------------------------------------
log_info "Step 5: Downloading latest wimboot for WinPE network booting..."
WIMBOOT_URL="https://github.com/ipxe/wimboot/releases/latest/download/wimboot"
if curl -fsSL -o "${WEB_ROOT}/tools/wimboot" "${WIMBOOT_URL}"; then
    chmod 755 "${WEB_ROOT}/tools/wimboot"
    # Also link or copy to TFTP just in case
    cp "${WEB_ROOT}/tools/wimboot" "${TFTP_ROOT}/wimboot"
    log_ok "wimboot installed successfully."
else
    log_warn "Could not download wimboot from GitHub. Windows WinPE wimboot will require manual file placement in ${WEB_ROOT}/tools/wimboot."
fi

# Ensure permissions on TFTP root
chmod -R a+r "${TFTP_ROOT}"
find "${TFTP_ROOT}" -type d -exec chmod 755 {} +

log_ok "Bootloaders setup complete!"
ls -la "${TFTP_ROOT}"

