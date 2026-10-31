import asyncio
import json
import os
import re
import shutil
import subprocess
import time
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional

import psutil
from fastapi import FastAPI, File, Form, Header, HTTPException, Request, UploadFile, Query
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.templating import Jinja2Templates
from pydantic import BaseModel

app = FastAPI(title="PXE Server Management Panel", version="2.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

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
    used_mb, total_mb = 0, 0
    try:
        meminfo = {}
        with open("/proc/meminfo", "r") as f:
            for line in f:
                parts = line.split(":")
                if len(parts) == 2:
                    k = parts[0].strip()
                    v = parts[1].strip().split()[0]
                    meminfo[k] = int(v)
        total_kb = meminfo.get("MemTotal", 0)
        avail_kb = meminfo.get("MemAvailable", meminfo.get("MemFree", 0))
        used_kb = max(0, total_kb - avail_kb)
        if total_kb > 0:
            used_mb = round(used_kb / 1024)
            total_mb = round(total_kb / 1024)
    except Exception:
        pass

    if total_mb == 0:
        mem = psutil.virtual_memory()
        used_mb = round((mem.total - mem.available) / (1024 * 1024))
        total_mb = round(mem.total / (1024 * 1024))

    mem_percent = round((used_mb / total_mb) * 100, 1) if total_mb > 0 else 0
    disk_srv = shutil.disk_usage(str(WEB_ROOT)) if WEB_ROOT.exists() else shutil.disk_usage("/")
    
    return {
        "cpu_percent": cpu_percent,
        "memory_used_mb": used_mb,
        "memory_total_mb": total_mb,
        "memory_percent": mem_percent,
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


VALID_IMAGE_EXTENSIONS = {".iso", ".img", ".wim", ".vhd", ".vhdx", ".qcow2", ".squashfs"}
EXCLUDED_IMAGE_DIRS = {"iso_extracted", "extracted", "efi", "boot", "sources", "support"}


@app.get("/api/images")
async def api_list_images():
    images = []
    scan_dirs = [WEB_ROOT / "images", WEB_ROOT / "os"]
    
    seen = set()
    for s_dir in scan_dirs:
        if s_dir.exists():
            for root, dirs, files in os.walk(s_dir):
                # Don't recurse into extracted/internal system directories
                dirs[:] = [d for d in dirs if d.lower() not in EXCLUDED_IMAGE_DIRS and not d.startswith(".")]
                for file in files:
                    p = Path(root) / file
                    if p in seen or p.name.startswith("."):
                        continue
                    if p.suffix.lower() not in VALID_IMAGE_EXTENSIONS:
                        continue
                    seen.add(p)
                    rel_path = p.relative_to(WEB_ROOT)
                    try:
                        stat = p.stat()
                        images.append({
                            "name": file,
                            "path": str(rel_path).replace("\\", "/"),
                            "size_mb": round(stat.st_size / (1024 * 1024), 2),
                            "modified": datetime.fromtimestamp(stat.st_mtime).strftime("%Y-%m-%d %H:%M:%S")
                        })
                    except Exception:
                        pass
    return {"images": images}


@app.delete("/api/images")
async def api_delete_image(path: str = Query(...)):
    safe_path = Path(path).as_posix().lstrip("/")
    full_path = (WEB_ROOT / safe_path).resolve()
    
    # Security check: must reside inside /srv/www/pxe
    if not str(full_path).startswith(str(WEB_ROOT.resolve())) or not full_path.exists():
        raise HTTPException(status_code=404, detail="File not found")
    
    if full_path.is_file():
        full_path.unlink()
        regenerate_boot_menus()
        return {"status": "ok", "deleted": safe_path}
    
    raise HTTPException(status_code=400, detail="Cannot delete directories")


@app.post("/api/upload")
async def api_upload_image(file: UploadFile = File(...), target_dir: Optional[str] = Form("images")):
    safe_name = os.path.basename(file.filename or "uploaded.iso")
    # Always save to unified images folder
    dest_folder = WEB_ROOT / "images"
    dest_folder.mkdir(parents=True, exist_ok=True)
    dest_path = dest_folder / safe_name
    
    with open(dest_path, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)
    
    os.chmod(dest_path, 0o644)
    if safe_name.lower().endswith(".iso"):
        asyncio.create_task(_auto_prepare_iso(dest_path))
    return {"status": "ok", "filename": safe_name, "path": str(dest_path.relative_to(WEB_ROOT)).replace("\\", "/"), "size_mb": round(dest_path.stat().st_size / (1024 * 1024), 2)}


# ---------------------------------------------------------------------------
# Resumable (chunked) image upload
# ---------------------------------------------------------------------------
UPLOADS_DIR = WEB_ROOT / "images" / ".uploads"
UPLOAD_ID_RE = re.compile(r"^[A-Za-z0-9_-]{8,80}$")


class UploadInitRequest(BaseModel):
    upload_id: str
    filename: str
    total_size: int


def _validate_upload_id(upload_id: str) -> str:
    if not upload_id or not UPLOAD_ID_RE.match(upload_id):
        raise HTTPException(status_code=400, detail="Некорректный идентификатор загрузки.")
    return upload_id


def _upload_paths(upload_id: str):
    UPLOADS_DIR.mkdir(parents=True, exist_ok=True)
    return UPLOADS_DIR / f"{upload_id}.part", UPLOADS_DIR / f"{upload_id}.json"


def _load_upload_meta(upload_id: str):
    _, meta_path = _upload_paths(upload_id)
    if not meta_path.exists():
        return None
    try:
        with open(meta_path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return None


def _part_size(part: Path) -> int:
    return part.stat().st_size if part.exists() else 0


def _save_upload_meta(upload_id: str, meta: dict) -> None:
    _, meta_path = _upload_paths(upload_id)
    tmp = meta_path.with_suffix(".tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(meta, f)
    os.replace(tmp, meta_path)


@app.post("/api/upload/init")
async def api_upload_init(payload: UploadInitRequest):
    upload_id = _validate_upload_id(payload.upload_id)
    filename = os.path.basename((payload.filename or "").strip()) or "uploaded.iso"
    suffix = Path(filename).suffix.lower()
    if suffix not in VALID_IMAGE_EXTENSIONS:
        raise HTTPException(status_code=400, detail=f"Неподдерживаемый формат файла: {suffix or 'без расширения'}.")
    if payload.total_size <= 0:
        raise HTTPException(status_code=400, detail="Некорректный размер файла.")

    part, meta_path = _upload_paths(upload_id)
    meta = _load_upload_meta(upload_id)
    if meta and (meta.get("filename") != filename or int(meta.get("total_size", 0)) != int(payload.total_size)):
        # Same session id but different file: restart the upload.
        try:
            part.unlink()
        except FileNotFoundError:
            pass
        try:
            meta_path.unlink()
        except FileNotFoundError:
            pass
        meta = None

    if not meta:
        meta = {
            "upload_id": upload_id,
            "filename": filename,
            "total_size": int(payload.total_size),
        }
        _save_upload_meta(upload_id, meta)

    offset = _part_size(part)
    if offset > int(payload.total_size):
        try:
            part.unlink()
        except FileNotFoundError:
            pass
        offset = 0

    return {
        "upload_id": upload_id,
        "filename": filename,
        "total_size": int(payload.total_size),
        "offset": offset,
    }


@app.put("/api/upload/chunk/{upload_id}")
async def api_upload_chunk(
    upload_id: str,
    request: Request,
    offset: int = Header(..., alias="X-Upload-Offset"),
):
    upload_id = _validate_upload_id(upload_id)
    meta = _load_upload_meta(upload_id)
    if not meta:
        raise HTTPException(status_code=404, detail="Сессия загрузки не найдена.")
    total_size = int(meta.get("total_size", 0))
    if offset < 0 or offset > total_size:
        raise HTTPException(status_code=400, detail="Некорректное смещение загрузки.")

    part, _ = _upload_paths(upload_id)
    current = _part_size(part)
    if offset > current:
        raise HTTPException(
            status_code=409,
            detail={"message": "Offset mismatch", "received": current},
        )

    mode = "r+b" if part.exists() else "wb"
    with open(part, mode) as buffer:
        if offset < current:
            # Client re-sends an overlapping range after an interrupted attempt.
            buffer.truncate(offset)
        buffer.seek(offset)
        try:
            async for chunk in request.stream():
                if chunk:
                    buffer.write(chunk)
        finally:
            buffer.flush()
            os.fsync(buffer.fileno())

    received = _part_size(part)
    if received > total_size:
        raise HTTPException(status_code=400, detail="Получено больше данных, чем заявлено.")
    return {"upload_id": upload_id, "received": received, "total_size": total_size}


@app.get("/api/upload/status/{upload_id}")
async def api_upload_status(upload_id: str):
    upload_id = _validate_upload_id(upload_id)
    meta = _load_upload_meta(upload_id)
    if not meta:
        raise HTTPException(status_code=404, detail="Сессия загрузки не найдена.")
    part, _ = _upload_paths(upload_id)
    return {
        "upload_id": upload_id,
        "filename": meta.get("filename"),
        "received": _part_size(part),
        "total_size": int(meta.get("total_size", 0)),
    }


@app.post("/api/upload/complete/{upload_id}")
async def api_upload_complete(upload_id: str):
    upload_id = _validate_upload_id(upload_id)
    meta = _load_upload_meta(upload_id)
    if not meta:
        raise HTTPException(status_code=404, detail="Сессия загрузки не найдена.")

    part, meta_path = _upload_paths(upload_id)
    received = _part_size(part)
    total_size = int(meta.get("total_size", 0))
    if received != total_size:
        raise HTTPException(
            status_code=400,
            detail=f"Загрузка не завершена: получено {received} из {total_size} байт.",
        )

    dest_folder = WEB_ROOT / "images"
    dest_folder.mkdir(parents=True, exist_ok=True)
    safe_name = os.path.basename(meta.get("filename") or "uploaded.iso")
    dest_path = dest_folder / safe_name
    os.replace(part, dest_path)
    os.chmod(dest_path, 0o644)
    try:
        meta_path.unlink()
    except FileNotFoundError:
        pass

    if safe_name.lower().endswith(".iso"):
        asyncio.create_task(_auto_prepare_iso(dest_path))

    return {
        "status": "ok",
        "filename": safe_name,
        "path": str(dest_path.relative_to(WEB_ROOT)).replace("\\", "/"),
        "size_mb": round(dest_path.stat().st_size / (1024 * 1024), 2),
    }


async def _auto_prepare_iso(iso_path: Path):
    try:
        proc = await asyncio.create_subprocess_exec(
            "7z", "l", str(iso_path), "sources/boot.wim",
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE
        )
        stdout, _ = await proc.communicate()
        if b"boot.wim" in stdout:
            slug = _slugify(iso_path.stem)
            target_dir = WEB_ROOT / "os" / "windows" / slug
            if not (target_dir / "sources" / "boot.wim").exists():
                script = SCRIPTS_DIR / "build_winpe.sh"
                await run_cmd(f"bash '{script}' '{iso_path}' '{PXE_SERVER_IP}' '' '{slug}'")
                regenerate_boot_menus()
    except Exception as ex:
        print(f"[auto_prepare_iso] {iso_path}: {ex}")


def escape_xml(s: str) -> str:
    if not s:
        return ""
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;").replace("'", "&apos;")


class CustomApp(BaseModel):
    name: Optional[str] = "App"
    path: str
    args: Optional[str] = ""


class WinPeProcessRequest(BaseModel):
    iso_path: str
    server_ip: Optional[str] = "172.16.16.61"
    name_mode: Optional[str] = "prompt"
    name_pattern: Optional[str] = "PC-%RANDOM%"
    ad_join: Optional[bool] = False
    ad_domain: Optional[str] = ""
    ad_ou: Optional[str] = ""
    ad_user: Optional[str] = ""
    ad_password: Optional[str] = ""
    product_key: Optional[str] = ""
    custom_apps: Optional[List[CustomApp]] = []


def generate_autounattend_xml(req: WinPeProcessRequest) -> str:
    # Build ComputerName string
    comp_name = "*"
    if req.name_mode == "pattern" and req.name_pattern:
        cleaned = re.sub(r"[^A-Za-z0-9\-]", "", req.name_pattern.replace("%SERIAL%", "").replace("%RANDOM%", ""))
        if cleaned:
            comp_name = f"{cleaned[:10]}*"
        else:
            comp_name = "*"

    # Product key for automated edition selection (defaults to generic Windows 10/11 Pro key)
    product_key_val = (req.product_key or "").strip()
    if not product_key_val:
        product_key_val = "VK7JG-NPHTM-C97JM-9MPGT-3V66T"

    domain_xml = ""
    if req.ad_join and req.ad_domain:
        ou_line = f"<MachineObjectOU>{escape_xml(req.ad_ou)}</MachineObjectOU>" if req.ad_ou else ""
        domain_xml = f"""
        <component name="Microsoft-Windows-UnattendedSettings" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <Identification>
                <Credentials>
                    <Domain>{escape_xml(req.ad_domain)}</Domain>
                    <Password>{escape_xml(req.ad_password or '')}</Password>
                    <Username>{escape_xml(req.ad_user or '')}</Username>
                </Credentials>
                <JoinDomain>{escape_xml(req.ad_domain)}</JoinDomain>
                {ou_line}
            </Identification>
        </component>
        """

    commands_xml = []
    order = 1
    srv = req.server_ip or "172.16.16.61"

    # Always install PixelHelper agent
    commands_xml.append(f"""
                <SynchronousCommand wcm:action="add">
                    <Order>{order}</Order>
                    <CommandLine>msiexec /i "\\\\{srv}\\install\\PixelHelper.msi" /qn /norestart AGY_SERVER_URL="http://{srv}:5000"</CommandLine>
                    <Description>Install PixelHelper Agent</Description>
                </SynchronousCommand>
    """)
    order += 1

    if req.custom_apps:
        for app in req.custom_apps:
            cmd = f'"{app.path}" {app.args}'.strip()
            commands_xml.append(f"""
                <SynchronousCommand wcm:action="add">
                    <Order>{order}</Order>
                    <CommandLine>{escape_xml(cmd)}</CommandLine>
                    <Description>{escape_xml(app.name or 'Custom App')}</Description>
                </SynchronousCommand>
            """)
            order += 1

    commands_block = "\n".join(commands_xml)

    return f"""<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
    <settings pass="windowsPE">
        <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <SetupUILanguage>
                <UILanguage>ru-RU</UILanguage>
            </SetupUILanguage>
            <InputLocale>0419:00000419</InputLocale>
            <SystemLocale>ru-RU</SystemLocale>
            <UILanguage>ru-RU</UILanguage>
            <UserLocale>ru-RU</UserLocale>
        </component>
        <component name="Microsoft-Windows-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <UserData>
                <AcceptEula>true</AcceptEula>
                <ProductKey>
                    <Key>{product_key_val}</Key>
                    <WillShowUI>OnError</WillShowUI>
                </ProductKey>
            </UserData>
        </component>
    </settings>
    <settings pass="specialize">
        <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <ComputerName>{comp_name}</ComputerName>
            <TimeZone>Russian Standard Time</TimeZone>
        </component>
        {domain_xml}
    </settings>
    <settings pass="oobeSystem">
        <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <InputLocale>0419:00000419</InputLocale>
            <SystemLocale>ru-RU</SystemLocale>
            <UILanguage>ru-RU</UILanguage>
            <UserLocale>ru-RU</UserLocale>
        </component>
        <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <OOBE>
                <HideEULAPage>true</HideEULAPage>
                <HideLocalAccountScreen>true</HideLocalAccountScreen>
                <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                <ProtectYourPC>3</ProtectYourPC>
            </OOBE>
            <FirstLogonCommands>
{commands_block}
            </FirstLogonCommands>
        </component>
    </settings>
</unattend>
"""


# -----------------------------------------------------------------------------
# Dynamic boot menus: only show entries for images that are actually present
# -----------------------------------------------------------------------------
PXE_SERVER_IP = os.getenv("PXE_SERVER_IP", "172.16.16.61")
HTTP_PORT = os.getenv("PXE_HTTP_PORT", "8088")
WINDOWS_SOURCE_FILE = WEB_ROOT / "os" / "windows" / ".source"


def _menu_safe(text: str) -> str:
    return re.sub(r'[^A-Za-z0-9 \.\-\(\)_]', "", text).strip() or "Windows"


def _slugify(text: str) -> str:
    return re.sub(r'[^A-Za-z0-9_-]', '_', text).strip('_')


def _available_boot_entries() -> List[dict]:
    os_dir = WEB_ROOT / "os"
    entries = []

    # 1. Scan for all multi-image Windows distributions in /srv/www/pxe/os/windows/*/
    win_base = os_dir / "windows"
    if win_base.exists():
        for sub in sorted(win_base.iterdir()):
            if sub.is_dir() and sub.name not in ("boot", "sources", "iso_extracted"):
                wim = sub / "sources" / "boot.wim"
                bcd = sub / "boot" / "bcd"
                if wim.exists() and bcd.exists():
                    source_file = sub / ".source"
                    raw_title = source_file.read_text(encoding="utf-8").strip() if source_file.exists() else sub.name
                    title = Path(raw_title).stem
                    entries.append({
                        "kind": "windows",
                        "slug": sub.name,
                        "title": f"Install {_menu_safe(title)}",
                        "rel_path": f"os/windows/{sub.name}"
                    })

        # Backward compatibility for legacy single-image /os/windows/
        single_wim = win_base / "sources" / "boot.wim"
        if single_wim.exists() and (win_base / "boot" / "bcd").exists() and not any(e["kind"] == "windows" for e in entries):
            source_file = win_base / ".source"
            raw_title = source_file.read_text(encoding="utf-8").strip() if source_file.exists() else "Windows"
            title = Path(raw_title).stem
            entries.append({
                "kind": "windows",
                "slug": "windows",
                "title": f"Install {_menu_safe(title)}",
                "rel_path": "os/windows"
            })

    # 2. Linux distributions
    if (os_dir / "debian" / "linux").exists() and (os_dir / "debian" / "initrd.gz").exists():
        entries.append({"kind": "debian", "title": "Debian Netinstall", "rel_path": "os/debian"})
    if (os_dir / "sysrescue" / "vmlinuz").exists() and (os_dir / "sysrescue" / "initram.img").exists():
        entries.append({"kind": "sysrescue", "title": "SystemRescue", "rel_path": "os/sysrescue"})
    if (os_dir / "clonezilla" / "vmlinuz").exists() and (os_dir / "clonezilla" / "filesystem.squashfs").exists():
        entries.append({"kind": "clonezilla", "title": "Clonezilla Live", "rel_path": "os/clonezilla"})
    return entries


def _build_pxelinux_menu(entries: List[dict]) -> str:
    h = f"http://{PXE_SERVER_IP}:{HTTP_PORT}"
    lines = [
        "# Автоматически сгенерировано панелью PXE. Не редактируйте вручную.",
        "UI vesamenu.c32",
        "PROMPT 0",
        "TIMEOUT 150",
        "ONTIMEOUT local",
        "",
        "MENU RESOLUTION 1024 768",
        "MENU BACKGROUND splash_bios.png",
        "MENU NOTITLE 1",
        "",
        "# Position menu inside branded container card",
        "MENU VSHIFT 10",
        "MENU ROWS 8",
        "MENU MARGIN 16",
        "MENU WIDTH 96",
        "MENU TABMSGROW 20",
        "MENU CMDLINEROW 20",
        "MENU TIMEOUTROW 22",
        "",
        "# Color palette matching Pixel Little Helper dark navy & accent blue",
        "MENU COLOR border 30;44 #00000000 #00000000 none",
        "MENU COLOR title 1;36;44 #ffffff #00000000 none",
        "MENU COLOR sel 7;37;40 #ffffff #246bfd all",
        "MENU COLOR unsel 37;44 #9fb5ce #00000000 none",
        "MENU COLOR help 37;40 #82caef #00000000 none",
        "MENU COLOR timeout_msg 37;40 #82caef #00000000 none",
        "MENU COLOR timeout 1;37;40 #97fff1 #00000000 none",
        "MENU COLOR tabmsg 31;40 #627c9e #00000000 none",
        "",
    ]
    for i, e in enumerate(entries, 1):
        lines.append(f"LABEL img{i}")
        lines.append(f"    MENU LABEL ^{i}. {e['title']}")
        k = e["kind"]
        rp = e.get("rel_path", "os/windows")
        if k == "windows":
            lines.append("    KERNEL linux.c32")
            lines.append(f"    APPEND {h}/tools/wimboot initrdfile={h}/{rp}/bootmgr,{h}/{rp}/boot/bcd,{h}/{rp}/boot/boot.sdi,{h}/{rp}/sources/boot.wim")
        elif k == "debian":
            lines.append(f"    KERNEL {h}/{rp}/linux")
            lines.append(f"    APPEND initrd={h}/{rp}/initrd.gz vga=788 --- quiet")
        elif k == "sysrescue":
            lines.append(f"    KERNEL {h}/{rp}/vmlinuz")
            lines.append(f"    APPEND initrd={h}/{rp}/initram.img archisobasedir=sysrescue ip=dhcp sysrescue.http={h}/{rp}/ copytoram")
        elif k == "clonezilla":
            lines.append(f"    KERNEL {h}/{rp}/vmlinuz")
            lines.append(f"    APPEND initrd={h}/{rp}/initrd.img boot=live config components union=overlay noswap nomodeset net.ifnames=0 nosplash noprompt fetch={h}/{rp}/filesystem.squashfs")
        lines.append("")
    if not entries:
        lines += ["LABEL none", "    MENU LABEL (No images available)", "    MENU DISABLE", ""]
    lines += [
        "MENU SEPARATOR", "",
        "LABEL local", "    MENU LABEL Boot from local hard drive", "    MENU DEFAULT", "    COM32 chain.c32", "    APPEND hd0", "",
        "LABEL reboot", "    MENU LABEL Reboot machine", "    COM32 reboot.c32", "",
        "LABEL poweroff", "    MENU LABEL Power off machine", "    COM32 poweroff.c32", "",
    ]
    return "\n".join(lines)


def _build_grub_menu(entries: List[dict]) -> str:
    s = f"(http,{PXE_SERVER_IP}:{HTTP_PORT})"
    h = f"http://{PXE_SERVER_IP}:{HTTP_PORT}"
    out = [
        "# Автоматически сгенерировано панелью PXE (Pixel Little Helper). Не редактируйте вручную.",
        "set timeout=15",
        "set default=0",
        "",
        "# Core network & protocol modules",
        "insmod net",
        "insmod efinet",
        "insmod http",
        "insmod tftp",
        "insmod chain",
        "",
        "# Video & Graphical Terminal modules",
        "insmod efi_gop",
        "insmod efi_uga",
        "insmod all_video",
        "insmod font",
        "insmod gfxterm",
        "insmod gfxmenu",
        "insmod png",
        "",
        "# Graphics Mode & Terminal Output",
        "set gfxmode=auto,1920x1080,1024x768",
        "set gfxpayload=keep",
        "terminal_output gfxterm",
        "",
        "# Load Fonts (with and without prefix for maximum compatibility)",
        "loadfont $prefix/themes/pixel/fonts/dejavu_14.pf2",
        "loadfont $prefix/themes/pixel/fonts/dejavu_bold_14.pf2",
        "loadfont $prefix/themes/pixel/fonts/dejavu_11.pf2",
        "loadfont /debian-installer/amd64/grub/themes/pixel/fonts/dejavu_14.pf2",
        "loadfont /grub/themes/pixel/fonts/dejavu_14.pf2",
        "loadfont /themes/pixel/fonts/dejavu_14.pf2",
        "loadfont $prefix/font.pf2",
        "",
        "# Set Branded Pixel Little Helper Theme",
        "set theme=$prefix/themes/pixel/theme.txt",
        "",
        "# Fallback text colors (used if graphical mode is unavailable)",
        "set color_normal=light-gray/black",
        "set color_highlight=white/blue",
        "",
    ]
    for e in entries:
        k = e["kind"]
        cls = "os"
        if k == "windows":
            cls = "windows"
        elif k == "debian":
            cls = "debian"
        elif k == "sysrescue":
            cls = "sysrescue"
        elif k == "clonezilla":
            cls = "clonezilla"
        rp = e.get("rel_path", "os/windows")
        out.append(f'menuentry "{e["title"]}" --class {cls} --class os {{')
        if k == "windows":
            out.append(f"    linux {s}/tools/wimboot")
            out.append(f"    initrd newc:bootmgr.efi:{s}/{rp}/bootmgr.efi newc:bcd:{s}/{rp}/boot/bcd newc:boot.sdi:{s}/{rp}/boot/boot.sdi newc:boot.wim:{s}/{rp}/sources/boot.wim")
        elif k == "debian":
            out.append(f"    linux {s}/{rp}/linux vga=788 --- quiet")
            out.append(f"    initrd {s}/{rp}/initrd.gz")
        elif k == "sysrescue":
            out.append(f"    linux {s}/{rp}/vmlinuz archisobasedir=sysrescue ip=dhcp sysrescue.http={h}/{rp}/ copytoram")
            out.append(f"    initrd {s}/{rp}/initram.img")
        elif k == "clonezilla":
            out.append(f"    linux {s}/{rp}/vmlinuz boot=live config components union=overlay noswap nomodeset net.ifnames=0 nosplash noprompt fetch={h}/{rp}/filesystem.squashfs")
            out.append(f"    initrd {s}/{rp}/initrd.img")
        out += ["}", ""]
    if not entries:
        out += ['menuentry "(Нет доступных образов)" { true }', ""]
    out += [
        'menuentry "Загрузка с локального диска (Local Hard Drive)" --class harddrive --class disk {',
        "    exit",
        "}", "",
        'menuentry "Перезагрузка (Reboot)" --class reboot { reboot }',
        'menuentry "Выключение (Power Off)" --class halt --class power { halt }', "",
    ]
    return "\n".join(out)


def regenerate_boot_menus() -> List[dict]:
    entries = _available_boot_entries()
    pxe = _build_pxelinux_menu(entries)
    grub = _build_grub_menu(entries)
    targets = [
        (TFTP_ROOT / "pxelinux.cfg" / "default", pxe),
        (TFTP_ROOT / "grub.cfg", grub),
        (TFTP_ROOT / "grub" / "grub.cfg", grub),
        (TFTP_ROOT / "grub" / "x86_64-efi" / "grub.cfg", grub),
        (TFTP_ROOT / "boot" / "grub" / "grub.cfg", grub),
        (TFTP_ROOT / "debian-installer" / "amd64" / "grub.cfg", grub),
        (TFTP_ROOT / "debian-installer" / "amd64" / "x86_64-efi" / "grub.cfg", grub),
        (TFTP_ROOT / "debian-installer" / "amd64" / "grub" / "grub.cfg", grub),
        (TFTP_ROOT / "debian-installer" / "amd64" / "grub" / "x86_64-efi" / "grub.cfg", grub),
        (WEB_ROOT / "grub" / "grub.cfg", grub),
    ]
    for path, content in targets:
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            if path.is_symlink():
                path.unlink()
            path.write_text(content, encoding="utf-8")
        except Exception as ex:
            print(f"[menu] cannot write {path}: {ex}")
    return entries


@app.on_event("startup")
async def _startup_regenerate_menus():
    regenerate_boot_menus()


@app.post("/api/menus/regenerate")
async def api_regenerate_menus():
    return {"status": "ok", "entries": regenerate_boot_menus()}


@app.post("/api/process-windows")
async def api_process_windows(req: WinPeProcessRequest):
    iso_file = Path(req.iso_path)
    if not iso_file.is_absolute():
        iso_file = WEB_ROOT / req.iso_path
        if not iso_file.exists():
            iso_file = WEB_ROOT / "images" / req.iso_path
        if not iso_file.exists():
            iso_file = WEB_ROOT / "os" / req.iso_path
    
    if not iso_file.exists():
        raise HTTPException(status_code=404, detail=f"ISO file not found: {iso_file}")
    
    # Generate autounattend.xml
    autounattend_content = generate_autounattend_xml(req)
    unattend_tmp = Path("/tmp/autounattend.xml")
    unattend_tmp.write_text(autounattend_content, encoding="utf-8")

    target_slug = _slugify(iso_file.stem)
    script = SCRIPTS_DIR / "build_winpe.sh"
    cmd = f"bash '{script}' '{iso_file}' '{req.server_ip}' '{unattend_tmp}' '{target_slug}'"
    code, out, err = await run_cmd(cmd)
    if code == 0:
        try:
            WINDOWS_SOURCE_FILE.parent.mkdir(parents=True, exist_ok=True)
            WINDOWS_SOURCE_FILE.write_text(iso_file.name, encoding="utf-8")
        except Exception:
            pass
    regenerate_boot_menus()

    return {
        "status": "ok" if code == 0 else "error",
        "return_code": code,
        "stdout": out,
        "stderr": err,
        "slug": target_slug
    }


@app.get("/api/configs/{config_type}")
async def api_get_config(config_type: str):
    paths = {
        "grub": TFTP_ROOT / "grub" / "grub.cfg",
        "syslinux": TFTP_ROOT / "pxelinux.cfg" / "default",
        "dnsmasq": CONFIGS_DIR / "dnsmasq-pxe.conf",
        "preseed": WEB_ROOT / "os" / "debian" / "preseed.cfg"
    }
    
    fallbacks = {
        "grub": CONFIGS_DIR / "grub.cfg",
        "syslinux": CONFIGS_DIR / "pxelinux.cfg.default",
        "dnsmasq": CONFIGS_DIR / "dnsmasq-pxe.conf",
        "preseed": CONFIGS_DIR / "preseed.cfg"
    }
    
    target = paths.get(config_type)
    if not target:
        raise HTTPException(status_code=400, detail="Invalid config type")
    
    if not target.exists():
        fb = fallbacks.get(config_type)
        if fb and fb.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(fb, target)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(f"# Default configuration for {config_type}\n", encoding="utf-8")
    
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
    
    target.parent.mkdir(parents=True, exist_ok=True)
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
