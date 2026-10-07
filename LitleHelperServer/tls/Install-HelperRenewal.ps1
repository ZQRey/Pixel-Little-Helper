param([Parameter(Mandatory)][string]$SshHostKey)
# Run elevated on dc01. Example host key: 'ssh-ed25519 AAAA...'.
$ErrorActionPreference = 'Stop'
if ($SshHostKey -notmatch '^ssh-ed25519 [A-Za-z0-9+/=]+$') { throw 'Supply the independently verified SSH host public key without comment.' }
$root = 'C:\ProgramData\HelperCertificateRenewal'
New-Item -ItemType Directory -Force $root | Out-Null
$system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$directoryAcl = [Security.AccessControl.DirectorySecurity]::new()
$directoryAcl.SetAccessRuleProtection($true, $false)
$directoryAcl.SetOwner($system)
foreach ($sid in @($system, $admins)) {
    $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
}
Set-Acl $root $directoryAcl
Copy-Item "$PSScriptRoot\Renew-HelperCertificate.ps1", "$PSScriptRoot\renewal-ca.pem" $root -Force
if (!(Test-Path "$root\renewal.key")) {
    & "$env:WINDIR\System32\OpenSSH\ssh-keygen.exe" -t ed25519 -N '""' -f "$root\renewal.key" -q
    if ($LASTEXITCODE -ne 0) { throw 'SSH key generation failed' }
}
$fileAcl = [Security.AccessControl.FileSecurity]::new()
$fileAcl.SetAccessRuleProtection($true, $false)
$fileAcl.SetOwner($system)
foreach ($sid in @($system, $admins)) { $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'Allow')) }
Set-Acl "$root\renewal.key" $fileAcl
Set-Content "$root\known_hosts" "helper.gp1.loc $SshHostKey" -Encoding ascii
$action = New-ScheduledTaskAction -Execute "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument ('-NoProfile -ExecutionPolicy Bypass -File "' + $root + '\Renew-HelperCertificate.ps1"')
$trigger = New-ScheduledTaskTrigger -Daily -At '03:30' -RandomDelay (New-TimeSpan -Minutes 10)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 15) -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
Register-ScheduledTask -TaskName Helper-Certificate-Renewal -Action $action -Trigger $trigger -Settings $settings -User SYSTEM -RunLevel Highest -Force | Out-Null
Write-Host 'Add this public key to the server with the restricted renewal command, then start Helper-Certificate-Renewal:'
Get-Content "$root\renewal.key.pub"
