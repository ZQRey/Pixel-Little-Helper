#!/bin/sh
set -eu
exec 9>/etc/helper-tls/renew.lock
flock -w 30 9
cert=/etc/helper-tls/fullchain.pem
key=/etc/helper-tls/private.key
case "${1:-}" in
status) cat "$cert" ;;
request) openssl req -new -key "$key" -subj /CN=helper.gp1.loc -addext subjectAltName=DNS:helper.gp1.loc,IP:172.16.16.61 ;;
install)
    work=$(mktemp -d /etc/helper-tls/.renew.XXXXXX)
    trap 'rm -f "$work/incoming.pem" "$work/previous.pem"; rmdir "$work"' EXIT
    timeout 30 head -c 65537 > "$work/incoming.pem"
    test "$(wc -c < "$work/incoming.pem")" -le 65536
    openssl x509 -in "$work/incoming.pem" -noout -checkhost helper.gp1.loc >&2
    openssl x509 -in "$work/incoming.pem" -noout -checkend 5184000 >&2
    openssl verify -purpose sslserver -CAfile /opt/helper/renewal-ca.pem "$work/incoming.pem" >&2
    certkey=$(openssl x509 -in "$work/incoming.pem" -pubkey -noout | openssl pkey -pubin -outform DER | sha256sum)
    privatekey=$(openssl pkey -in "$key" -pubout -outform DER | sha256sum)
    test "$certkey" = "$privatekey"
    oldexpiry=$(date -d "$(openssl x509 -in "$cert" -noout -enddate | cut -d= -f2-)" +%s)
    newexpiry=$(date -d "$(openssl x509 -in "$work/incoming.pem" -noout -enddate | cut -d= -f2-)" +%s)
    test "$newexpiry" -gt "$oldexpiry"
    cp -p "$cert" "$work/previous.pem"
    chmod 644 "$work/incoming.pem"
    mv "$work/incoming.pem" "$cert"
    if ! nginx -t || ! nginx -s reload; then
        cp -p "$work/previous.pem" "$cert"
        nginx -s reload
        exit 1
    fi
    echo 'Certificate renewed and nginx reloaded'
    ;;
*) echo 'Unsupported certificate operation' >&2; exit 2 ;;
esac
