FROM alpine:3.20

RUN apk add --no-cache dnsmasq tzdata

# Create TFTP root directory
RUN mkdir -p /srv/tftp /etc/dnsmasq.d

EXPOSE 67/udp 69/udp 4011/udp

ENTRYPOINT ["dnsmasq", "-k", "--conf-dir=/etc/dnsmasq.d,.dpkg-dist,.dpkg-old,.dpkg-new"]

