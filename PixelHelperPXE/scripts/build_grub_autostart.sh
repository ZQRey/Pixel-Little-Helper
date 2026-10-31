#!/usr/bin/env bash
set -euo pipefail

# Build a standalone GRUB EFI image that initializes UEFI networking itself,
# then loads the PXE menu from the configured server. Shim must trust the
# signing certificate before this image can be used with Secure Boot enabled.

SERVER_IP="${SERVER_IP:-172.16.16.61}"
OUTPUT_DIR="${OUTPUT_DIR:-$(pwd)/grub-autostart-output}"
MOK_KEY="${MOK_KEY:?Set MOK_KEY to the private signing key path}"
MOK_CERT="${MOK_CERT:?Set MOK_CERT to the matching PEM certificate path}"
GRUB_MODULE_DIR="${GRUB_MODULE_DIR:-/usr/lib/grub/x86_64-efi}"

for command in grub-mkstandalone sbsign; do
    command -v "${command}" >/dev/null || {
        echo "Required tool not found: ${command}" >&2
        exit 1
    }
done

[[ -r "${MOK_KEY}" ]] || { echo "Cannot read MOK_KEY: ${MOK_KEY}" >&2; exit 1; }
[[ -r "${MOK_CERT}" ]] || { echo "Cannot read MOK_CERT: ${MOK_CERT}" >&2; exit 1; }
[[ -d "${GRUB_MODULE_DIR}" ]] || { echo "GRUB module directory not found: ${GRUB_MODULE_DIR}" >&2; exit 1; }

WORK_DIR="$(mktemp -d)"
cleanup() { rm -rf "${WORK_DIR}"; }
trap cleanup EXIT
mkdir -p "${OUTPUT_DIR}"

cat > "${WORK_DIR}/early.cfg" <<EOF
insmod efinet
insmod net
net_dhcp
set pxe_server=${SERVER_IP}
configfile (tftp,\${pxe_server})/grub/grub.cfg
echo Failed to load PXE menu from ${SERVER_IP}.
sleep 15
EOF

grub-mkstandalone \
    --format=x86_64-efi \
    --directory="${GRUB_MODULE_DIR}" \
    --modules="efinet net tftp http normal configfile linux chain efi_gop font test reboot halt sleep" \
    --output="${WORK_DIR}/grubx64.efi" \
    "boot/grub/grub.cfg=${WORK_DIR}/early.cfg"

sbsign \
    --key "${MOK_KEY}" \
    --cert "${MOK_CERT}" \
    --output "${OUTPUT_DIR}/grubx64.efi" \
    "${WORK_DIR}/grubx64.efi"

echo "Signed custom GRUB written to: ${OUTPUT_DIR}/grubx64.efi"
echo "Enroll the certificate in MOK on each Secure Boot client before deploying this image."
