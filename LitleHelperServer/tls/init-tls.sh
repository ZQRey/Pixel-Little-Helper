#!/bin/sh
set -eu
umask 077
mkdir -p /etc/helper-tls
if [ ! -s /etc/helper-tls/private.key ]; then
    openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out /etc/helper-tls/private.key
fi
if [ ! -s /etc/helper-tls/fullchain.pem ]; then
    # Bootstrap only. The AD CS task immediately replaces this one-day certificate.
    openssl req -new -x509 -days 1 -key /etc/helper-tls/private.key -subj /CN=helper.gp1.loc \
        -addext subjectAltName=DNS:helper.gp1.loc,IP:172.16.16.61 -out /etc/helper-tls/fullchain.pem
fi
