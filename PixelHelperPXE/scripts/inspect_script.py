with open('/srv/tftp/debian-installer/amd64/grubx64.efi', 'rb') as f:
    b = f.read()

idx = b.find(b'prefix/x86_64-efi/grub.cfg')
if idx != -1:
    start = max(0, idx - 200)
    end = min(len(b), idx + 300)
    print(b[start:end].decode('latin1', errors='ignore'))
