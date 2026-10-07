#!/bin/sh
set -eu
case "${SSH_ORIGINAL_COMMAND:-}" in
    status|request|install) operation=$SSH_ORIGINAL_COMMAND ;;
    *) echo 'Unsupported certificate operation' >&2; exit 2 ;;
esac
cd /home/zqrey/Pixel-Little-Helper
exec docker compose exec -T proxy sh /opt/helper/cert-channel.sh "$operation"
