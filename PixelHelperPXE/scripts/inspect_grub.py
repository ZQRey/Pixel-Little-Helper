import re
with open('/srv/tftp/debian-installer/amd64/grubx64.efi', 'rb') as f:
    b = f.read()

print("SET:", [m.decode(errors='ignore') for m in re.findall(rb"set [^\x00\r\n]+", b)][:10])
print("PREFIX:", [m.decode(errors='ignore') for m in re.findall(rb"prefix[^\x00\r\n]+", b)][:10])
print("CFG:", [m.decode(errors='ignore') for m in re.findall(rb"[a-zA-Z0-9_/.-]*grub\.cfg", b)][:10])
