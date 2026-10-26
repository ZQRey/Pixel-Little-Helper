"""Host-side lifecycle controller for a fixed, pre-generated personal OpenCode pool.
No arbitrary Docker commands, compose paths, images, credentials or owners are accepted over HTTP.
"""
import argparse
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import logging
import os
from pathlib import Path
import re
import subprocess
import threading
import time

IMAGE = "ghcr.io/anomalyco/opencode@sha256:543fe6b18470c9e4787e9be9813c4d340fba5738085b833ea76fb330ddff514a"


def audit(owner, operation, status, reason=""):
    logging.getLogger("helper.workers").info(json.dumps({"time": int(time.time()), "ownerUserId": owner, "operation": operation, "status": status, "reason": reason}))


class Failure(Exception):
    def __init__(self, status, message):
        self.status, self.message = status, message


class ComposeDriver:
    def __init__(self, directory):
        self.directory = Path(directory).resolve(strict=True)
        self.compose = self.directory / "compose.json"
        for file in (self.directory, self.compose, self.directory / "bindings.secret.json"):
            if file.stat().st_uid != os.getuid() or file.stat().st_mode & 0o022:
                raise ValueError("Controller files must be owned by its account and not writable by other accounts")
        config = json.loads(self.compose.read_text())
        bindings = json.loads((self.directory / "bindings.secret.json").read_text())
        self.urls = {row["ownerUserId"]: row["url"] for row in bindings}
        if not self.urls or len(self.urls) != len(bindings) or len(self.urls) > 32 or any(type(owner) is not int or owner < 1 for owner in self.urls):
            raise ValueError("Invalid owner pool")
        expected = {f"opencode-user-{owner}{suffix}" for owner in self.urls for suffix in ("", "-init")}
        if set(config["services"]) != expected:
            raise ValueError("Compose services do not match the owner pool")
        for owner, url in self.urls.items():
            service = config["services"][f"opencode-user-{owner}"]
            if url != f"http://opencode-user-{owner}:4096/" or service.get("image") != IMAGE or service.get("user") != "10001:10001" or not service.get("read_only") or service.get("ports") or service.get("build"):
                raise ValueError("Use the hardened generated Compose definition")
            if service.get("cap_drop") != ["ALL"] or service.get("cap_add") or service.get("privileged") or service.get("network_mode") == "host":
                raise ValueError("Worker isolation is invalid")
            if service.get("volumes") != [f"opencode-user-{owner}:/state", str(self.directory / "opencode.json") + ":/etc/helper-opencode.json:ro"]:
                raise ValueError("Unexpected worker mounts")
            if service.get("env_file") != [str(self.directory / (f"opencode-user-{owner}.env"))]:
                raise ValueError("Unexpected worker environment path")

    def command(self, *args, timeout=75):
        try:
            result = subprocess.run(["docker", "compose", "-f", str(self.compose), *args], capture_output=True, text=True, timeout=timeout)
        except (OSError, subprocess.TimeoutExpired):
            raise Failure(503, "Docker operation unavailable") from None
        if result.returncode:
            raise Failure(503, "Docker operation failed")
        return result.stdout

    def state(self, owner):
        # Compose resolves the fixed service; HTTP input can never become a container ID or shell argument.
        raw = self.command("ps", "--all", "--format", "json", f"opencode-user-{owner}", timeout=15).strip()
        if not raw:
            return False, False
        try:
            parsed = json.loads(raw)
            rows = parsed if isinstance(parsed, list) else [parsed]
            if len(rows) != 1:
                raise ValueError()
            state = rows[0].get("State")
            return state in ("running", "paused", "restarting"), state == "running" and rows[0].get("Health") == "healthy"
        except (ValueError, TypeError, KeyError):
            raise Failure(503, "Unexpected Compose state") from None

    def start(self, owner):
        self.command("up", "-d", "--no-recreate", "--no-build", "--pull", "never", "--wait", "--wait-timeout", "45", f"opencode-user-{owner}")

    def stop(self, owner):
        self.command("stop", "--timeout", "10", f"opencode-user-{owner}", timeout=25)


class Pool:
    def __init__(self, driver, max_running=4, idle_seconds=600, lease_seconds=120, clock=time.monotonic):
        self.driver, self.max_running, self.idle, self.ttl, self.clock = driver, max_running, idle_seconds, lease_seconds, clock
        self.capacity = threading.Lock()
        now = clock()
        # A restarted controller must allow existing clients to renew their lost in-memory leases.
        self.grace_until = now + lease_seconds + 5
        self.workers = {}
        for owner, url in driver.urls.items():
            running, healthy = driver.state(owner)
            self.workers[owner] = dict(url=url, lock=threading.Lock(), running=running, phase="Running" if healthy else "Unhealthy" if running else "Stopped",
                                       leases={}, last_used=now, retry_after=0, recovery=running)

    def worker(self, owner, url):
        worker = self.workers.get(owner)
        if worker is None:
            raise Failure(404, "Worker is outside the host pool")
        if url != worker["url"]:
            raise Failure(409, "Worker binding does not match the host pool")
        return worker

    def expire(self, worker):
        now = self.clock()
        worker["leases"] = {key: until for key, until in worker["leases"].items() if until > now}

    def view(self, worker):
        return {"phase": worker["phase"], "activeLeases": len(worker["leases"]), "leaseSeconds": self.ttl, "idleSeconds": self.idle}

    def acquire(self, owner, url, lease):
        worker = self.worker(owner, url)
        with worker["lock"]:
            self.expire(worker)
            if lease not in worker["leases"] and len(worker["leases"]) >= 8:
                raise Failure(429, "Worker lease limit reached")
            running, healthy = self.driver.state(owner)
            worker["running"] = running
            if not healthy:
                if self.clock() < worker["retry_after"]:
                    raise Failure(503, "Worker startup is cooling down")
                with self.capacity:
                    occupied = sum(w["running"] or w["phase"] in ("Starting", "Stopping") for w in self.workers.values())
                    if not running and occupied >= self.max_running:
                        raise Failure(429, "Running worker capacity reached")
                    worker["phase"] = "Starting"
                try:
                    audit(owner, "start", "Starting")
                    self.driver.start(owner)
                    worker["running"], healthy = self.driver.state(owner)
                    if not worker["running"] or not healthy:
                        raise Failure(503, "Worker did not become healthy")
                except Exception:
                    try:
                        worker["running"], _ = self.driver.state(owner)
                    except Exception:
                        worker["running"] = True  # Unknown state consumes capacity until a successful inspection.
                    worker["phase"], worker["retry_after"] = "Faulted", self.clock() + 30
                    audit(owner, "start", "Failed")
                    raise
                audit(owner, "start", "Running")
            worker["phase"], worker["last_used"] = "Running", self.clock()
            worker["leases"][lease] = self.clock() + self.ttl
            return self.view(worker)

    def release(self, owner, url, lease):
        worker = self.worker(owner, url)
        with worker["lock"]:
            self.expire(worker)
            if worker["leases"].pop(lease, None) is not None:
                worker["last_used"] = self.clock()
            return self.view(worker)

    def status(self, owner, url):
        worker = self.worker(owner, url)
        with worker["lock"]:
            self.expire(worker)
            worker["running"], healthy = self.driver.state(owner)
            worker["phase"] = "Running" if healthy else "Unhealthy" if worker["running"] else "Stopped"
            return self.view(worker)

    def stop_locked(self, owner, worker, reason="Manual"):
        self.expire(worker)
        if worker["leases"]:
            raise Failure(409, "Worker has active leases")
        if worker["recovery"] and self.clock() < self.grace_until:
            raise Failure(409, "Controller is waiting for existing clients to renew leases")
        worker["phase"] = "Stopping"
        audit(owner, "stop", "Stopping", reason)
        try:
            self.driver.stop(owner)
            worker["running"], _ = self.driver.state(owner)
            if worker["running"]:
                raise Failure(503, "Worker is still running")
            worker["phase"] = "Stopped"
            audit(owner, "stop", "Stopped", reason)
        except Exception:
            worker["phase"] = "Faulted"
            audit(owner, "stop", "Failed", reason)
            raise
        return self.view(worker)

    def stop(self, owner, url):
        worker = self.worker(owner, url)
        with worker["lock"]:
            return self.stop_locked(owner, worker)

    def sweep(self):
        if self.clock() < self.grace_until:
            return
        for owner, worker in self.workers.items():
            if not worker["lock"].acquire(blocking=False):
                continue
            try:
                self.expire(worker)
                if not worker["leases"] and worker["running"] and self.clock() - worker["last_used"] >= self.idle:
                    self.stop_locked(owner, worker, "Idle")
            except Exception:
                pass  # A failed stop is retried at the next sweep. No provider payloads are logged.
            finally:
                worker["lock"].release()


def handler(pool, secret):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def setup(self):
            super().setup()
            self.connection.settimeout(5)

        def respond(self, status, value):
            data = json.dumps(value).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_POST(self):
            try:
                if not hmac.compare_digest(self.headers.get("Authorization", ""), "Bearer " + secret):
                    raise Failure(401, "Controller authentication required")
                match = re.fullmatch(r"/workers/([1-9][0-9]{0,9})/(status|stop|leases/([a-f0-9]{32})|release/([a-f0-9]{32}))", self.path)
                if not match:
                    raise Failure(404, "Unknown controller operation")
                length = int(self.headers.get("Content-Length", "0"))
                if not 1 <= length <= 1024 or self.headers.get("Transfer-Encoding"):
                    raise Failure(400, "Invalid controller request size")
                value = json.loads(self.rfile.read(length))
                if not isinstance(value, dict) or set(value) != {"url"} or not isinstance(value["url"], str):
                    raise Failure(400, "Only the registered worker URL is accepted")
                owner, operation = int(match[1]), match[2]
                if operation == "status":
                    result = pool.status(owner, value["url"])
                elif operation == "stop":
                    result = pool.stop(owner, value["url"])
                elif match[3]:
                    result = pool.acquire(owner, value["url"], match[3])
                else:
                    result = pool.release(owner, value["url"], match[4])
                self.respond(200, result)
            except Failure as error:
                self.respond(error.status, {"error": error.message})
            except (ValueError, TypeError, TimeoutError):
                self.respond(400, {"error": "Invalid controller request"})
            except Exception:
                self.respond(503, {"error": "Worker controller unavailable"})
    return Handler


class ControllerServer(ThreadingHTTPServer):
    """Bound concurrent connections, including unauthenticated requests and slow clients."""
    def __init__(self, *args, **kwargs):
        self.slots = threading.BoundedSemaphore(16)
        super().__init__(*args, **kwargs)

    def process_request(self, request, client_address):
        if not self.slots.acquire(blocking=False):
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, client_address)
        except Exception:
            self.slots.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.slots.release()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True)
    parser.add_argument("--token-file", required=True)
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=4097)
    parser.add_argument("--idle-seconds", type=int, default=600)
    parser.add_argument("--max-running", type=int, default=4)
    args = parser.parse_args()
    token_file = Path(args.token_file)
    if token_file.stat().st_uid != os.getuid() or token_file.stat().st_mode & 0o077:
        raise ValueError("Token file must be owned by controller account with mode 600")
    secret = token_file.read_text().strip()
    if len(secret) < 32 or not secret.isascii() or any(c.isspace() for c in secret):
        raise ValueError("Use a random ASCII controller token of at least 32 characters")
    if not 60 <= args.idle_seconds <= 86400 or not 1 <= args.max_running <= 32:
        raise ValueError("Invalid controller limits")
    pool = Pool(ComposeDriver(args.directory), args.max_running, args.idle_seconds)
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    server = ControllerServer((args.bind, args.port), handler(pool, secret))
    stopped = threading.Event()
    def cleanup():
        while not stopped.wait(15):
            pool.sweep()
    threading.Thread(target=cleanup, daemon=True).start()
    try:
        server.serve_forever()
    finally:
        stopped.set()
        server.server_close()
