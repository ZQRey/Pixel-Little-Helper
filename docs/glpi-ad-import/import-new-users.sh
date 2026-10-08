#!/bin/sh
set -eu
exec /usr/bin/flock -n /home/zqrey/.glpi-ad-import.lock /usr/bin/docker exec -u www-data glpi-app php /var/www/html/glpi/bin/console ldap:synchronize_users --ldap-server-id=1 --only-create-new '--ldap-filter=(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))' --no-interaction
