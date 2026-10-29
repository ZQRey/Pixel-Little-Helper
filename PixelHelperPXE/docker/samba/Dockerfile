FROM alpine:3.20

RUN apk add --no-cache samba shadow

RUN mkdir -p /srv/www/pxe/os/windows/iso_extracted /var/log/samba

EXPOSE 445/tcp 139/tcp

ENTRYPOINT ["smbd", "--foreground", "--no-process-group", "--configfile=/etc/samba/smb.conf"]

