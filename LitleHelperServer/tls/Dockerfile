FROM nginx:stable-alpine
RUN apk add --no-cache openssl util-linux coreutils
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY init-tls.sh cert-channel.sh /opt/helper/
COPY renewal-ca.pem /opt/helper/renewal-ca.pem
