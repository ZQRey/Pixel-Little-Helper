"""Prepare (never start) dedicated OpenCode Compose services. Run on the Linux Docker host."""
import argparse
import json
import os
from pathlib import Path
import secrets
import shutil

IMAGE = "ghcr.io/anomalyco/opencode@sha256:543fe6b18470c9e4787e9be9813c4d340fba5738085b833ea76fb330ddff514a"


def prepare(output, users, provider, network):
    if not users or len(users) > 32 or len(set(users)) != len(users) or any(u < 1 for u in users):
        raise ValueError("Supply 1..32 distinct positive user IDs")
    if not provider or len(provider) > 100 or not all(c.isascii() and (c.isalnum() or c in "-_") for c in provider):
        raise ValueError("Invalid provider ID")
    output = Path(output).resolve()
    output.mkdir(mode=0o700, parents=False, exist_ok=False)
    config = output / "opencode.json"
    shutil.copyfile(Path(__file__).with_name("opencode.json"), config)
    os.chmod(config, 0o644)
    services, volumes, bindings = {}, {}, []
    for owner in users:
        name = f"opencode-user-{owner}"
        password = secrets.token_hex(32)
        env = output / (name + ".env")
        env.write_text("OPENCODE_SERVER_PASSWORD=" + password + "\n", encoding="utf-8")
        os.chmod(env, 0o600)
        volumes[name] = {}
        services[name + "-init"] = {
            "image": IMAGE, "user": "0:0", "entrypoint": ["/bin/sh", "-ec"],
            "command": ["mkdir -p /state/data /state/config /state/cache /state/home; chown -R 10001:10001 /state; chmod 700 /state /state/data /state/config /state/cache /state/home"],
            "network_mode": "none", "read_only": True, "cap_drop": ["ALL"],
            "cap_add": ["CHOWN", "FOWNER", "DAC_OVERRIDE"], "security_opt": ["no-new-privileges:true"],
            "volumes": [name + ":/state"], "restart": "no"}
        services[name] = {
            "image": IMAGE, "user": "10001:10001", "entrypoint": ["/bin/sh", "-ec"],
            "command": [': "$${OPENCODE_SERVER_PASSWORD:?Missing worker password}"; umask 077; exec opencode serve --hostname 0.0.0.0 --port 4096'],
            "working_dir": "/state/home", "env_file": [str(env)],
            "environment": {"HOME": "/state/home", "XDG_CONFIG_HOME": "/state/config", "XDG_DATA_HOME": "/state/data", "XDG_CACHE_HOME": "/state/cache",
                            "OPENCODE_CONFIG": "/etc/helper-opencode.json", "OPENCODE_DISABLE_DEFAULT_PLUGINS": "true", "OPENCODE_DISABLE_MODELS_FETCH": "true"},
            "volumes": [name + ":/state", str(config) + ":/etc/helper-opencode.json:ro"],
            "networks": {"backend": {"aliases": [name]}}, "read_only": True,
            "tmpfs": ["/tmp:rw,nosuid,nodev,size=128m,mode=1777"], "cap_drop": ["ALL"], "security_opt": ["no-new-privileges:true"],
            "mem_limit": "768m", "cpus": 1.0, "pids_limit": 96, "restart": "unless-stopped",
            "depends_on": {name + "-init": {"condition": "service_completed_successfully"}},
            "healthcheck": {"test": ["CMD-SHELL", 'auth=$$(printf "opencode:%s" "$${OPENCODE_SERVER_PASSWORD}" | base64 | tr -d "\\n"); wget -T 3 -t 1 -q -O /dev/null --header="Authorization: Basic $$auth" http://127.0.0.1:4096/global/health'],
                            "interval": "20s", "timeout": "5s", "retries": 5, "start_period": "30s"}}
        bindings.append({"ownerUserId": owner, "url": f"http://{name}:4096/", "password": password, "provider": provider})
    compose = {"name": "helper-personal-ai", "services": services, "volumes": volumes,
               "networks": {"backend": {"external": True, "name": network}}}
    for filename, value in (("compose.json", compose), ("bindings.secret.json", bindings)):
        file = output / filename
        file.write_text(json.dumps(value, indent=2), encoding="utf-8")
        os.chmod(file, 0o600)
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, help="New private directory; existing directories are refused")
    parser.add_argument("--users", required=True, type=int, nargs="+")
    parser.add_argument("--provider", default="openrouter")
    parser.add_argument("--network", default="pixel-little-helper_default")
    args = parser.parse_args()
    result = prepare(args.output, args.users, args.provider, args.network)
    print(f"Prepared {len(args.users)} workers in {result}. No containers started; passwords are in protected files.")
