import asyncio
import os
import re
import shutil
import subprocess
import time
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional

import psutil
from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.templating import Jinja2Templates
from pydantic import BaseModel

app = FastAPI(title="PXE Server Management Panel", version="2.0.0")

BASE_DIR = Path(__file__).resolve().parent
TEMPLATES_DIR = BASE_DIR / "templates"
STATIC_DIR = BASE_DIR / "static"

templates = Jinja2Templates(directory=str(TEMPLATES_DIR))

# Storage locations
TFTP_ROOT = Path(os.getenv("TFTP_ROOT", "/srv/tftp"))
WEB_ROOT = Path(os.getenv("WEB_ROOT", "/srv/www/pxe"))
SCRIPTS_DIR = Path(os.getenv("SCRIPTS_DIR", "/home/zqrey/pxe-server/scripts"))
CONFIGS_DIR = Path(os.getenv("CONFIGS_DIR", "/home/zqrey/pxe-server/configs"))

# Services
SERVICES = ["dnsmasq", "nginx", "smbd"]
CONTAINER_MAP = {
    "dnsmasq": "pxe-dnsmasq",
    "nginx": "pxe-nginx",
    "smbd": "pxe-samba"
}

DOCKER_MODE = os.path.exists("/var/run/docker.sock")


# -----------------------------------------------------------------------------
# System & Docker Helpers
# -----------------------------------------------------------------------------
async def run_cmd(cmd: str) -> tuple[int, str, str]:
    proc = await asyncio.create_subprocess_shell(
        cmd,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE
    )
    stdout, stderr = await proc.communicate()
    return proc.returncode or 0, stdout.decode().strip(), stderr.decode().strip()


def get_service_status(service_name: str) -> dict:
    if DOCKER_MODE:
        container_name = CONTAINER_MAP.get(service_name, f"pxe-{service_name}")
        try:
            res = subprocess.run(
                ["docker", "inspect", container_name, "--format", "{{.State.Status}}|{{.State.Pid}}|{{.State.StartedAt}}"],
                capture_output=True,
                text=True
            )
            if res.returncode == 0:
                parts = res.stdout.strip().split("|")
                status = parts[0] if len(parts) > 0 else "unknown"
                pid = parts[1] if len(parts) > 1 else "0"
                since = parts[2] if len(parts) > 2 else ""
                return {
                    "name": service_name,
                    "active": status == "running",
                    "substate": f"docker ({status})",
                    "pid": pid,
                    "since": since
                }
        except Exception:
            pass

    # Fallback to systemctl
    try:
        res = subprocess.run(
            ["systemctl", "is-active", service_name],
            capture_output=True,
            text=True
        )
        active = res.stdout.strip() == "active"
        show = subprocess.run(
            ["systemctl", "show", service_name, "--property=MainPID,ActiveEnterTimestamp,SubState"],
            capture_output=True,
            text=True
        )
        props = {}
        for line in show.stdout.strip().splitlines():
            if "=" in line:
                k, v = line.split("=", 1)
                props[k] = v
        return {
            "name": service_name,
            "active": active,
            "substate": props.get("SubState", "unknown"),
            "pid": props.get("MainPID", "0"),
            "since": props.get("ActiveEnterTimestamp", "")
        }
    except Exception as e:
        return {"name": service_name, "active": False, "substate": "error", "error": str(e)}


def get_system_stats() -> dict:
    cpu_percent = psutil.cpu_percent(interval=None)
    mem = psutil.virtual_memory()
    disk_srv = shutil.disk_usage(str(WEB_ROOT)) if WEB_ROOT.exists() else shutil.disk_usage("/")
    
    return {
        "cpu_percent": cpu_percent,
        "memory_used_mb": round((mem.total - mem.available) / (1024 * 1024)),
        "memory_total_mb": round(mem.total / (1024 * 1024)),
        "memory_percent": mem.percent,
        "disk_free_gb": round(disk_srv.free / (1024 * 1024 * 1024), 2),
        "disk_total_gb": round(disk_srv.total / (1024 * 1024 * 1024), 2),
        "disk_used_percent": round((disk_srv.used / disk_srv.total) * 100, 1),
        "docker_mode": DOCKER_MODE
    }


def parse_pxe_clients() -> list[dict]:
    clients_map: Dict[str, dict] = {}

    # Get dnsmasq logs either from docker or journalctl
    lines = []
    if DOCKER_MODE:
        try:
            res = subprocess.run(["docker", "logs", "--tail", "400", "pxe-dnsmasq"], capture_output=True, text=True)
            lines = res.stdout.splitlines() + res.stderr.splitlines()
        except Exception:
            pass

    if not lines:
        try:
            res = subprocess.run(["journalctl", "-u", "dnsmasq", "-n", "400", "--no-pager"], capture_output=True, text=True)
            lines = res.stdout.splitlines()
        except Exception:
            pass

    for line in lines:
        ts_match = re.match(r"^([A-Z][a-z]{2}\s+\d+\s+\d{2}:\d{2}:\d{2})", line)
        log_time = ts_match.group(1) if ts_match else ""

        # TFTP file transfer log: e.g. "sent /srv/tftp/shimx64.efi to 172.16.16.50"
        tftp_match = re.search(r"dnsmasq-tftp.*:\s+sent\s+(/\S+)\s+to\s+([0-9.]+)", line)
        if tftp_match:
            file_path = tftp_match.group(1)
            ip = tftp_match.group(2)
            file_name = os.path.basename(file_path)
            
            arch = "Unknown"
            if "shim" in file_name or "grub" in file_name:
                arch = "UEFI x64 (Secure Boot)"
            elif "lpxelinux" in file_name or ".c32" in file_name:
                arch = "Legacy BIOS (x86)"
            elif "wimboot" in file_name:
                arch = "WinPE Network Boot"

            if ip not in clients_map:
                clients_map[ip] = {
                    "ip": ip,
                    "hostname": "PXE-Client",
                    "mac": "-",
                    "arch": arch,
                    "file": file_name,
                    "protocol": "TFTP",
                    "status": "Booting",
                    "last_seen": log_time
                }
            else:
                clients_map[ip]["file"] = file_name
                clients_map[ip]["protocol"] = "TFTP"
                clients_map[ip]["arch"] = arch
                clients_map[ip]["last_seen"] = log_time

        # DHCP Proxy Option 93 & vendor class inspection
        xid_match = re.search(r"dnsmasq-dhcp.*:\s+(\d+)\s+(.+)", line)
        if xid_match:
            xid = xid_match.group(1)
            content = xid_match.group(2)

            if "vendor class:" in content:
                vclass = content.split("vendor class:", 1)[1].strip()
                if "PXEClient" in vclass:
                    arch = "UEFI x64" if ("00007" in vclass or "00009" in vclass) else "Legacy BIOS"
                    key = f"PXE-{xid[-6:]}"
                    if key not in clients_map:
                        clients_map[key] = {
                            "ip": "Assigning...",
                            "hostname": f"Client-{xid[-4:]}",
                            "mac": "-",
                            "arch": arch,
                            "file": "shimx64.efi" if "UEFI" in arch else "lpxelinux.0",
                            "protocol": "DHCP Proxy",
                            "status": "Requesting Bootloader",
                            "last_seen": log_time
                        }
            elif "client provides name:" in content:
                cname = content.split("client provides name:", 1)[1].strip()
                for client in clients_map.values():
                    if client["hostname"].startswith("Client-") and xid.endswith(client["hostname"].split("-")[1]):
                        client["hostname"] = cname

    # Correlate Nginx access log
    for log_path in ["/var/log/nginx/pxe_access.log", "/var/log/nginx/access.log"]:
        p = Path(log_path)
        if p.exists():
            try:
                with open(p, "r", encoding="utf-8", errors="ignore") as f:
                    for line in f.readlines()[-100:]:
                        m = re.match(r"^([0-9.]+)\s+-\s+-\s+\[([^\]]+)\]\s+\"GET\s+(\S+)\s+HTTP/[0-9.]+\"\s+(\d+)", line)
                        if m:
                            ip = m.group(1)
                            req_time = m.group(2).split()[0]
                            url_path = m.group(3)
                            status_code = m.group(4)
                            file_name = os.path.basename(url_path)

                            if url_path.endswith((".cfg", ".txt", ".html")):
                                continue

                            if ip in clients_map:
                                clients_map[ip]["file"] = file_name
                                clients_map[ip]["protocol"] = "HTTP"
                                clients_map[ip]["last_seen"] = req_time
                                clients_map[ip]["status"] = "Downloading OS Media" if status_code == "200" else f"HTTP {status_code}"
                            else:
                                clients_map[ip] = {
                                    "ip": ip,
                                    "hostname": "HTTP-Boot-Client",
                                    "mac": "-",
                                    "arch": "UEFI / BIOS",
                                    "file": file_name,
                                    "protocol": "HTTP",
                                    "status": "Downloading OS Media",
                                    "last_seen": req_time
                                }
                break
            except Exception:
                pass

    return list(clients_map.values())


# -----------------------------------------------------------------------------
# Web Routes
# -----------------------------------------------------------------------------
@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    return templates.TemplateResponse(request=request, name="index.html", context={})


@app.get("/api/status")
async def api_status():
    service_states = [get_service_status(s) for s in SERVICES]
    stats = get_system_stats()
    clients = parse_pxe_clients()
    return {
        "services": service_states,
        "stats": stats,
        "clients": clients,
        "server_time": datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    }


@app.post("/api/service/{service_name}/{action}")
async def api_service_action(service_name: str, action: str):
    if service_name not in SERVICES:
        raise HTTPException(status_code=400, detail="Invalid service name")
    if action not in ["start", "stop", "restart", "reload"]:
        raise HTTPException(status_code=400, detail="Invalid action")
    
    if DOCKER_MODE:
        container_name = CONTAINER_MAP.get(service_name, f"pxe-{service_name}")
        act = "restart" if action == "reload" else action
        code, out, err = await run_cmd(f"docker {act} {container_name}")
    else:
        code, out, err = await run_cmd(f"sudo systemctl {action} {service_name}")

    if code != 0:
        raise HTTPException(status_code=500, detail=f"Failed to {action} {service_name}: {err or out}")
    
    return {"status": "ok", "service": service_name, "action": action, "info": get_service_status(service_name)}


@app.get("/api/images")
async def api_list_images():
    os_dir = WEB_ROOT / "os"
    images = []
    
    if os_dir.exists():
        for root, dirs, files in os.walk(os_dir):
            for file in files:
                p = Path(root) / file
                rel_path = p.relative_to(WEB_ROOT)
                stat = p.stat()
                images.append({
                    "name": file,
                    "path": str(rel_path),
                    "size_mb": round(stat.st_size / (1024 * 1024), 2),
                    "modified": datetime.fromtimestamp(stat.st_mtime).strftime("%Y-%m-%d %H:%M:%S")
                })
    return {"images": images}


@app.post("/api/upload")
async def api_upload_image(file: UploadFile = File(...), target_dir: str = Form("isos")):
    safe_name = os.path.basename(file.filename or "uploaded.iso")
    dest_folder = WEB_ROOT / "os" / target_dir
    dest_folder.mkdir(parents=True, exist_ok=True)
    dest_path = dest_folder / safe_name
    
    with open(dest_path, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)
    
    os.chmod(dest_path, 0o644)
    return {"status": "ok", "filename": safe_name, "path": str(dest_path), "size_mb": round(dest_path.stat().st_size / (1024 * 1024), 2)}


class WinPeProcessRequest(BaseModel):
    iso_path: str
    server_ip: Optional[str] = "172.16.16.6"


@app.post("/api/process-windows")
async def api_process_windows(req: WinPeProcessRequest):
    iso_file = Path(req.iso_path)
    if not iso_file.is_absolute():
        iso_file = WEB_ROOT / "os" / req.iso_path
    
    if not iso_file.exists():
        raise HTTPException(status_code=404, detail=f"ISO file not found: {iso_file}")
    
    script = SCRIPTS_DIR / "build_winpe.sh"
    cmd = f"bash '{script}' '{iso_file}' '{req.server_ip}'"
    code, out, err = await run_cmd(cmd)
    
    return {
        "status": "ok" if code == 0 else "error",
        "return_code": code,
        "stdout": out,
        "stderr": err
    }


@app.get("/api/configs/{config_type}")
async def api_get_config(config_type: str):
    paths = {
        "grub": TFTP_ROOT / "grub" / "grub.cfg",
        "syslinux": TFTP_ROOT / "pxelinux.cfg" / "default",
        "dnsmasq": CONFIGS_DIR / "dnsmasq-pxe.conf",
        "preseed": WEB_ROOT / "os" / "debian" / "preseed.cfg"
    }
    
    target = paths.get(config_type)
    if not target or not target.exists():
        raise HTTPException(status_code=404, detail="Config not found")
    
    with open(target, "r", encoding="utf-8") as f:
        content = f.read()
    
    return {"type": config_type, "path": str(target), "content": content}


class ConfigSaveRequest(BaseModel):
    content: str


@app.post("/api/configs/{config_type}")
async def api_save_config(config_type: str, req: ConfigSaveRequest):
    paths = {
        "grub": TFTP_ROOT / "grub" / "grub.cfg",
        "syslinux": TFTP_ROOT / "pxelinux.cfg" / "default",
        "dnsmasq": CONFIGS_DIR / "dnsmasq-pxe.conf",
        "preseed": WEB_ROOT / "os" / "debian" / "preseed.cfg"
    }
    
    target = paths.get(config_type)
    if not target:
        raise HTTPException(status_code=400, detail="Invalid config type")
    
    with open(target, "w", encoding="utf-8") as f:
        f.write(req.content)
    
    # Sync GRUB across locations
    if config_type == "grub":
        shutil.copy(target, TFTP_ROOT / "grub.cfg")
        if (WEB_ROOT / "grub").exists():
            shutil.copy(target, WEB_ROOT / "grub" / "grub.cfg")
    elif config_type == "dnsmasq" and DOCKER_MODE:
        await run_cmd("docker restart pxe-dnsmasq")
    
    return {"status": "ok", "message": f"Config {config_type} updated successfully"}


@app.get("/api/logs/{service_name}")
async def api_get_logs(service_name: str, lines: int = 150):
    if DOCKER_MODE:
        if service_name == "nginx-access":
            cmd = f"tail -n {lines} /var/log/nginx/pxe_access.log 2>/dev/null || docker logs --tail {lines} pxe-nginx"
        else:
            container_name = CONTAINER_MAP.get(service_name, f"pxe-{service_name}")
            cmd = f"docker logs --tail {lines} {container_name}"
        code, out, err = await run_cmd(cmd)
        return {"service": service_name, "logs": out or err}
    
    if service_name == "nginx-access":
        cmd = f"tail -n {lines} /var/log/nginx/pxe_access.log 2>/dev/null || echo 'No Nginx access log'"
    else:
        cmd = f"journalctl -u {service_name} -n {lines} --no-pager"
    
    code, out, err = await run_cmd(cmd)
    return {"service": service_name, "logs": out or err}
