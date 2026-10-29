# Autonomous Production-Ready PXE Server (Debian 12/13)

Production-ready, vendor-neutral PXE network boot solution designed for heterogeneous enterprise networks. Operates in **DHCP Proxy mode**, working seamlessly alongside existing DHCP servers (MikroTik, Windows Server Active Directory DHCP Failover) without requiring changes to their configuration.

---

## 🌟 Key Features

1. **UEFI x64 with Secure Boot Enabled**:
   - Official Microsoft 3rd Party CA signed `shimx64.efi` from Debian's `shim-signed` package.
   - Chainloads Debian signed `grubx64.efi` (`grubnetx64.efi.signed`) with embedded network stack (`efinet`, `tftp`, `http`).
2. **UEFI x64 with Secure Boot Disabled**:
   - Fully compatible with standard UEFI firmware (native 64-bit).
3. **Legacy BIOS (CSM / x86)**:
   - Syslinux `lpxelinux.0` with HTTP transport capability, loading multi-gigabyte OS kernels directly over high-speed HTTP instead of slow TFTP.
4. **Non-intrusive DHCP Proxy Architecture**:
   - Dnsmasq snoops DHCPDISCOVER on UDP port 67 and provides PXE boot options on UDP port 4011 without interfering with primary DHCP IP allocations or scopes.
5. **High-Speed HTTP Engine (Nginx)**:
   - Optimized for large ISOs, squashfs roots, and WIM images (`sendfile`, `tcp_nopush`, 2MB chunks, byte ranges).
6. **Zero-Windows-Dependency WinPE Automation**:
   - Native Linux script (`build_winpe.sh`) using `wimlib-imagex` to unpack official Windows 10/11 ISOs, extract boot files, and inject automated network mounting scripts (`startnet.cmd`) without requiring Windows ADK.
7. **Samba SMB Share**:
   - Guest-accessible network share (`\\<SERVER_IP>\install`) hosting unpacked Windows ISO media for unattended setup.

---

## 🏗️ Architecture & Boot Flow

```
+-----------------------------------------------------------------------------------+
|                                 CLIENT BOOTS                                      |
+-----------------------------------------------------------------------------------+
                                          |
                        DHCPDISCOVER (Broadcast UDP 67)
                                          |
              +---------------------------+---------------------------+
              |                                                       |
              v                                                       v
   +--------------------+                                  +--------------------+
   | Primary DHCP       |                                  | PXE Proxy (Dnsmasq)|
   | (MikroTik / AD)    |                                  | (172.16.16.6)      |
   +--------------------+                                  +--------------------+
              |                                                       |
       Offers IP Lease                                    Detects Option 93 (Arch)
       (IP, Mask, DNS)                                    Offers PXE Boot Info
              |                                                       |
              +---------------------------+---------------------------+
                                          |
                              Client accepts IP & PXE
                                          |
                    +---------------------+---------------------+
                    |                                           |
         [Option 93 = 7, 9: UEFI x64]                [Option 93 = 0: Legacy BIOS]
                    |                                           |
             TFTP GET shimx64.efi                        TFTP GET lpxelinux.0
                    |                                           |
        Verifies via MS 3rd Party CA                 Loads Syslinux config & menu.c32
                    |                                           |
             Loads grubx64.efi                           Loads Menu over TFTP
                    |                                           |
             Loads grub.cfg                                     |
                    |                                           |
                    +---------------------+---------------------+
                                          |
                               Switch to HTTP Engine
                           (Nginx: http://172.16.16.6)
                                          |
         +--------------------------------+--------------------------------+
         |                                |                                |
         v                                v                                v
+------------------+             +------------------+             +------------------+
| Debian Installer |             |  Clonezilla /    |             | Windows 10 / 11  |
| linux + initrd   |             |  SystemRescue    |             | WinPE (wimboot)  |
| + preseed.cfg    |             |  vmlinuz + .sfs  |             | + SMB \\srv\inst |
+------------------+             +------------------+             +------------------+
```

---

## 📂 Project Structure

```
.
├── deploy.sh                     # Master one-click idempotent installation script
├── setup_bootloaders.sh          # Official bootloader provisioner (Shim, GRUB, Syslinux)
├── .env.example                  # Environment configuration template
├── configs/
│   ├── dnsmasq-pxe.conf          # Dnsmasq DHCP Proxy + TFTP config
│   ├── nginx-pxe.conf            # High-performance Nginx web server config
│   ├── samba-pxe.conf            # Anonymous Samba share config for Windows setup
│   ├── grub.cfg                  # Unified UEFI GRUB boot menu
│   ├── pxelinux.cfg.default      # Legacy BIOS Syslinux boot menu
│   └── preseed.cfg               # Unattended Debian installation preseed
└── scripts/
    ├── build_winpe.sh            # Extracts Windows boot files & injects startnet.cmd via wimlib
    ├── fetch_live_images.sh      # Downloads official Debian netboot, Clonezilla, SystemRescue
    └── startnet.cmd              # Windows PE network initialization & SMB mount script
```

---

## 🚀 Quick Deployment Guide

### 1. Prerequisites
- Target OS: Debian 12 (Bookworm) or Debian 13 (Trixie).
- Root privileges (`sudo`).
- Network interface with a static IP (e.g. `172.16.16.6/23`).

### 2. Deploy
Run the master installer on the server:

```bash
sudo ./deploy.sh
```

Or pass custom parameters:
```bash
sudo ./deploy.sh --ip 172.16.16.6 --iface eth0 --subnet-mask 255.255.254.0
```

The script will:
1. Install all dependencies (`dnsmasq`, `nginx`, `samba`, `wimtools`, `syslinux-common`, `pxelinux`, `curl`, `dpkg-dev`).
2. Download official signed Debian deb packages (`shim-signed`, `grub-efi-amd64-signed`) and extract signed `shimx64.efi` and `grubx64.efi`.
3. Provision Syslinux `lpxelinux.0` and modules for Legacy BIOS.
4. Download `wimboot` from the official iPXE repository.
5. Deploy parameterized configurations for Dnsmasq, Nginx, Samba, GRUB, and Syslinux.
6. Automatically download official Debian 12 Netboot kernel and initrd.
7. Start and enable system services and verify listening ports.

---

## 🪟 Windows 10 / 11 Netboot Provisioning

To configure unattended Windows installation directly from an official Microsoft ISO:

```bash
sudo ./scripts/build_winpe.sh /path/to/Windows_11_24H2.iso 172.16.16.6
```

### What this does:
1. Mounts the ISO on Linux.
2. Extracts UEFI `bootmgr.efi`, BIOS `bootmgr`, `bcd`, `boot.sdi`, and `boot.wim`.
3. Injects custom `startnet.cmd` into `boot.wim` using `wimlib-imagex update` (no Windows ADK required!).
4. Unpacks the full ISO contents into `/srv/www/pxe/os/windows/iso_extracted`.
5. WinPE boots over HTTP via `wimboot`, initializes the network, mounts `\\172.16.16.6\install`, and automatically executes `Z:\setup.exe`.

---

## 🔍 Verification & Port Diagnostics

Once deployed, verify the active services:

| Protocol | Port | Service | Purpose |
| :--- | :--- | :--- | :--- |
| **UDP** | `69` | `dnsmasq` | TFTP bootloader delivery |
| **UDP** | `67` | `dnsmasq` | DHCP Proxy broadcast listener |
| **UDP** | `4011` | `dnsmasq` | ProxyDHCP PXE architecture response |
| **TCP** | `80` | `nginx` | High-speed HTTP kernel/initrd/WinPE delivery |
| **TCP** | `445` | `smbd` | Samba Windows install share |
| **TCP** | `8080` | `panel` | FastAPI Web Management UI |

---

## 🐳 Docker Compose Management

The entire stack is containerized and managed via Docker Compose:

```bash
# Start all services
docker compose up -d

# View service health and status
docker compose ps

# View container logs
docker compose logs -f [dnsmasq|nginx|samba|panel]

# Restart stack
docker compose restart

# Stop all services
docker compose down
```

### 🌐 Web Control Center

Navigate to `http://172.16.16.6:8080/` to access the PXE Server Control Center:
- **Service Control**: Start, stop, and restart individual containers with one click.
- **Client Tracking**: Live table of machines requesting DHCP/PXE leases and downloading OS media.
- **Image Manager**: Upload OS images (ISO, kernel, initrd) and automate Windows ISO conversion.
- **Menu Editor**: Live in-browser editing of `grub.cfg`, `pxelinux.cfg/default`, and `dnsmasq-pxe.conf`.
- **System Metrics**: Real-time CPU, RAM, and Disk storage graphs.

### Test Commands:
```bash
# Check listening ports
ss -tulpn | grep -E ':(67|69|80|445|4011|8080)'

# Test TFTP locally
tftp 172.16.16.6 -c get shimx64.efi

# Test HTTP locally
curl -I http://172.16.16.6/grub/grub.cfg

# Test Samba locally
smbclient -L //172.16.16.6 -N

# Test Web Panel locally
curl -s http://172.16.16.6:8080/api/status
```

