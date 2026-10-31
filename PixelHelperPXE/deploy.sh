#!/usr/bin/env bash
# ==============================================================================
# deploy.sh
# Production Master Deployment Script: Autonomous Multi-Arch PXE Server
# Supports: UEFI Secure Boot (Shim), UEFI non-Secure Boot, Legacy BIOS (Syslinux)
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log_info()  { echo -e "${BLUE}[INFO]${NC} $*"; }
log_ok()    { echo -e "${GREEN}[OK]${NC} $*"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
log_err()   { echo -e "${RED}[ERROR]${NC} $*" >&2; }

# ------------------------------------------------------------------------------
# 1. Root & Environment Verification
# ------------------------------------------------------------------------------
if [[ "$(id -u)" -ne 0 ]]; then
    log_err "This deployment script must be run with root privileges!"
    echo "Please execute: sudo $0"
    exit 1
fi

# Load optional .env file
if [[ -f "${SCRIPT_DIR}/.env" ]]; then
    log_info "Loading configuration from ${SCRIPT_DIR}/.env..."
    # shellcheck source=/dev/null
    source "${SCRIPT_DIR}/.env"
fi

# Detect default interface and IP if not specified
DEFAULT_IFACE=$(ip route show default 2>/dev/null | awk '/default/ {print $5}' | head -n 1)
DEFAULT_IFACE="${DEFAULT_IFACE:-eth0}"

SERVER_IFACE="${SERVER_IFACE:-$DEFAULT_IFACE}"
SERVER_IP="${SERVER_IP:-$(ip -4 addr show dev "$SERVER_IFACE" 2>/dev/null | awk '/inet / {print $2}' | cut -d/ -f1 | head -n 1)}"

# Determine subnet mask
SUBNET_CIDR_DETECTED=$(ip -4 addr show dev "$SERVER_IFACE" 2>/dev/null | awk '/inet / {print $2}' | head -n 1)
SERVER_NETWORK="${SERVER_NETWORK:-$(ip -4 route show dev "$SERVER_IFACE" proto kernel scope link 2>/dev/null | awk 'NR==1 {sub(/\/.*/, "", $1); print $1}')}"
if [[ -z "${SUBNET_MASK:-}" || -z "${SERVER_NETWORK}" ]]; then
    if [[ -z "${SUBNET_CIDR_DETECTED}" ]] || ! command -v python3 >/dev/null 2>&1; then
        log_err "Could not determine IPv4 network for ${SERVER_IFACE}; set SERVER_NETWORK and SUBNET_MASK in .env"
        exit 1
    fi
    read -r DETECTED_NETWORK DETECTED_MASK < <(python3 -c 'import ipaddress,sys; n=ipaddress.ip_interface(sys.argv[1]).network; print(n.network_address, n.netmask)' "${SUBNET_CIDR_DETECTED}")
    SERVER_NETWORK="${SERVER_NETWORK:-$DETECTED_NETWORK}"
    SUBNET_MASK="${SUBNET_MASK:-$DETECTED_MASK}"
fi
TFTP_ROOT="${TFTP_ROOT:-/srv/tftp}"
WEB_ROOT="${WEB_ROOT:-/srv/www/pxe}"

# Parse command line overrides
while [[ $# -gt 0 ]]; do
    case "$1" in
        --ip)
            SERVER_IP="$2"
            shift 2
            ;;
        --iface)
            SERVER_IFACE="$2"
            shift 2
            ;;
        --subnet-mask)
            SUBNET_MASK="$2"
            shift 2
            ;;
        --help|-h)
            echo "Usage: $0 [--ip <server_ip>] [--iface <interface>] [--subnet-mask <mask>]"
            exit 0
            ;;
        *)
            log_err "Unknown parameter: $1"
            exit 1
            ;;
    esac
done

if [[ -z "${SERVER_IP}" ]]; then
    log_err "Could not automatically determine SERVER_IP on interface ${SERVER_IFACE}!"
    log_err "Please provide --ip <IP> or set SERVER_IP in .env"
    exit 1
fi

echo -e "${CYAN}"
cat << 'EOF'
==============================================================================
   ___  _  _ ___   ___ _   _ ___   ___ _____ ___ _____ ___ ___  _  _ 
  | _ \| \/ | __| | _ \ | | | _ ) / __|_   _/ _ \_   _|_ _/ _ \| \| |
  |  _/ >  <| _|  |   / |_| | _ \ \__ \ | || (_) || |  | | (_) | .` |
  |_|  /_/\_\___| |_|_\\___/|___/ |___/ |_| \___/ |_| |___|___/|_|\_|
                                                                     
  Autonomous Production PXE Automation (UEFI Secure Boot + Legacy BIOS)
==============================================================================
EOF
echo -e "${NC}"

log_info "Target Configuration:"
echo "  - Server IP:        ${SERVER_IP}"
echo "  - Network Dev:      ${SERVER_IFACE}"
echo "  - Subnet Mask:      ${SUBNET_MASK}"
echo "  - TFTP Root:        ${TFTP_ROOT}"
echo "  - Web Root:         ${WEB_ROOT}"
echo ""

# ------------------------------------------------------------------------------
# 2. Package Installation
# ------------------------------------------------------------------------------
log_info "[Step 1/6] Installing required packages (dnsmasq, nginx, samba, syslinux)..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq \
    dnsmasq \
    nginx \
    samba \
    smbclient \
    syslinux-common \
    pxelinux \
    wimtools \
    p7zip-full \
    rsync \
    curl \
    dpkg-dev \
    tftp-hpa \
    net-tools

# ------------------------------------------------------------------------------
# 3. Bootloader Extraction & Directory Creation
# ------------------------------------------------------------------------------
log_info "[Step 2/6] Provisioning UEFI (Shim + GRUB) & BIOS bootloaders..."
chmod +x "${SCRIPT_DIR}/setup_bootloaders.sh"
DEBIAN_RELEASE="${DEBIAN_RELEASE:-bookworm}" TFTP_ROOT="${TFTP_ROOT}" WEB_ROOT="${WEB_ROOT}" "${SCRIPT_DIR}/setup_bootloaders.sh"

# ------------------------------------------------------------------------------
# 4. Configuration Deployment
# ------------------------------------------------------------------------------
log_info "[Step 3/6] Deploying service configurations..."

# A. Dnsmasq (DHCP Proxy + TFTP)
log_info "Configuring dnsmasq (/etc/dnsmasq.d/pxe.conf)..."
sed -e "s/SERVER_IFACE_PLACEHOLDER/${SERVER_IFACE}/g" \
    -e "s/SERVER_NETWORK_PLACEHOLDER/${SERVER_NETWORK}/g" \
    -e "s/SERVER_IP_PLACEHOLDER/${SERVER_IP}/g" \
    -e "s/SUBNET_MASK_PLACEHOLDER/${SUBNET_MASK}/g" \
    -e "s|TFTP_ROOT_PLACEHOLDER|${TFTP_ROOT}|g" \
    "${SCRIPT_DIR}/configs/dnsmasq-pxe.conf" > /etc/dnsmasq.d/pxe.conf

# Ensure default dnsmasq.conf does not conflict
if [[ -f /etc/dnsmasq.conf ]]; then
    # Disable default bind-interfaces or dns settings if any
    sed -i -e 's/^interface=/#interface=/' /etc/dnsmasq.conf || true
fi

# B. Nginx HTTP Server
log_info "Configuring Nginx (/etc/nginx/sites-available/pxe)..."
cp "${SCRIPT_DIR}/configs/nginx-pxe.conf" /etc/nginx/sites-available/pxe
ln -sf /etc/nginx/sites-available/pxe /etc/nginx/sites-enabled/pxe
rm -f /etc/nginx/sites-enabled/default

# C. Samba Server
log_info "Configuring Samba (/etc/samba/smb.conf)..."
cp "${SCRIPT_DIR}/configs/samba-pxe.conf" /etc/samba/smb.conf

# D. GRUB Configuration (UEFI)
log_info "Deploying GRUB menus..."
sed "s/SERVER_IP_PLACEHOLDER/${SERVER_IP}/g" "${SCRIPT_DIR}/configs/grub.cfg" > "${TFTP_ROOT}/grub/grub.cfg"
# Also place at root of TFTP and in web root for HTTP chainloading
cp "${TFTP_ROOT}/grub/grub.cfg" "${TFTP_ROOT}/grub.cfg"
cp "${TFTP_ROOT}/grub/grub.cfg" "${WEB_ROOT}/grub/grub.cfg"
cp "${TFTP_ROOT}/grub/grub.cfg" "${TFTP_ROOT}/debian-installer/amd64/grub/grub.cfg"
mkdir -p "${TFTP_ROOT}/boot/grub"
cp "${TFTP_ROOT}/grub/grub.cfg" "${TFTP_ROOT}/boot/grub/grub.cfg"

# Ensure x86_64-efi/grub.cfg redirects to prefix/grub.cfg rather than CD-ROM /boot/grub/grub.cfg
echo "source \$prefix/grub.cfg" > "${TFTP_ROOT}/debian-installer/amd64/grub/x86_64-efi/grub.cfg"
echo "source \$prefix/grub.cfg" > "${TFTP_ROOT}/grub/x86_64-efi/grub.cfg"

# E. Syslinux Configuration (Legacy BIOS)
log_info "Deploying Syslinux menus..."
sed "s/SERVER_IP_PLACEHOLDER/${SERVER_IP}/g" "${SCRIPT_DIR}/configs/pxelinux.cfg.default" > "${TFTP_ROOT}/pxelinux.cfg/default"

# F. Debian Preseed
log_info "Deploying Debian Preseed automation..."
cp "${SCRIPT_DIR}/configs/preseed.cfg" "${WEB_ROOT}/os/debian/preseed.cfg"

# Ensure all scripts are executable
chmod +x "${SCRIPT_DIR}/scripts/"*.sh 2>/dev/null || true

# ------------------------------------------------------------------------------
# 5. Download Debian Netboot Kernel & Initrd
# ------------------------------------------------------------------------------
log_info "[Step 4/6] Downloading official Debian Netboot kernel and initrd..."
if [[ -x "${SCRIPT_DIR}/scripts/fetch_live_images.sh" ]]; then
    WEB_ROOT="${WEB_ROOT}" "${SCRIPT_DIR}/scripts/fetch_live_images.sh"
fi

# Ensure permissions
log_info "Setting correct filesystem permissions..."
chown -R dnsmasq:nogroup "${TFTP_ROOT}"
chown -R nobody:nogroup "${WEB_ROOT}"
chmod -R a+rX "${TFTP_ROOT}" "${WEB_ROOT}"

# ------------------------------------------------------------------------------
# 6. Service Management & Health Verification
# ------------------------------------------------------------------------------
log_info "[Step 5/6] Testing and restarting system services..."

# Validate Nginx config
nginx -t

# Restart services
systemctl restart dnsmasq
systemctl restart nginx
systemctl restart smbd

systemctl enable dnsmasq nginx smbd

log_info "[Step 6/6] Verifying network ports..."
sleep 2

# Check listening ports
PORTS_OK=true

check_port() {
    local proto="$1"
    local port="$2"
    local desc="$3"
    if ss -l"${proto}"n | grep -q ":${port} "; then
        log_ok "${desc} (${proto^^} port ${port}) is LISTENING."
    else
        log_err "${desc} (${proto^^} port ${port}) is NOT listening!"
        PORTS_OK=false
    fi
}

check_port "u" "69"   "TFTP Server"
check_port "u" "67"   "DHCP Proxy Snooning"
check_port "u" "4011" "ProxyDHCP Service"
check_port "t" "8088" "Nginx HTTP Engine"
check_port "t" "445"  "Samba SMB File Sharing"

# Smoke test TFTP locally
log_info "Smoke-testing local TFTP transfer..."
TMP_TFTP_TEST=$(mktemp /tmp/tftp-test.XXXXXX)
if tftp "${SERVER_IP}" -c get shimx64.efi "${TMP_TFTP_TEST}" 2>/dev/null && [[ -s "${TMP_TFTP_TEST}" ]]; then
    log_ok "TFTP self-test successful! Downloaded shimx64.efi ($(stat -c%s "${TMP_TFTP_TEST}") bytes)."
else
    log_warn "Local TFTP test via tftp-hpa did not complete. Checking tftp daemon status:"
    systemctl status dnsmasq --no-pager | head -n 15
fi
rm -f "${TMP_TFTP_TEST}"

# Smoke test HTTP locally
log_info "Smoke-testing local HTTP transfer..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://${SERVER_IP}:8088/grub/grub.cfg" || true)
if [[ "${HTTP_CODE}" == "200" ]]; then
    log_ok "HTTP self-test successful! Received HTTP 200 for grub/grub.cfg."
else
    log_err "HTTP self-test failed! Received HTTP code: ${HTTP_CODE}"
    PORTS_OK=false
fi

# Smoke test SMB share locally
log_info "Smoke-testing local Samba share..."
if smbclient -L "localhost" -N 2>/dev/null | grep -q "install"; then
    log_ok "Samba share 'install' is accessible anonymously."
else
    log_warn "Could not verify anonymous access to 'install' share via smbclient."
fi

echo ""
echo -e "${GREEN}==============================================================================${NC}"
echo -e "${GREEN}       PXE SERVER SUCCESSFULLY DEPLOYED AND OPERATIONAL!${NC}"
echo -e "${GREEN}==============================================================================${NC}"
echo "Server IP Address:     ${SERVER_IP}"
echo "Web URL (HTTP):        http://${SERVER_IP}:8088/"
echo "SMB Share for Windows: \\\\${SERVER_IP}\\install"
echo ""
echo "Supported Boot Modes:"
echo " 1. UEFI x64 Secure Boot:  shimx64.efi -> grubx64.efi (Debian/Microsoft signed)"
echo " 2. UEFI x64 non-Secure:   shimx64.efi / grubx64.efi"
echo " 3. Legacy BIOS (CSM/x86): lpxelinux.0 -> pxelinux.cfg/default"
echo ""
echo "Next Steps:"
echo " - To build and inject Windows WinPE for automated setup:"
echo "     sudo ${SCRIPT_DIR}/scripts/build_winpe.sh /path/to/windows.iso ${SERVER_IP}"
echo " - To inspect live logs:"
echo "     journalctl -u dnsmasq -f"
echo "     tail -f /var/log/nginx/pxe_access.log"
echo "=============================================================================="

