param([switch]$ProbeEnrollment)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ca = 'dc01.gp1.loc\gp1-DC01-CA'
$ssh = "$env:WINDIR\System32\OpenSSH\ssh.exe"
$sshArgs = @('-i', "$root\renewal.key", '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', "UserKnownHostsFile=$root\known_hosts", '-o', 'ConnectTimeout=15', 'zqrey@helper.gp1.loc')
function Remote([string]$Operation, [string]$InputText = '') {
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        if ($InputText) { $result = $InputText | & $ssh @sshArgs $Operation 2> "$root\ssh-error.log" }
        else { $result = & $ssh @sshArgs $Operation 2> "$root\ssh-error.log" }
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($exitCode -ne 0) { throw "Helper certificate operation failed: $Operation; $(Get-Content "$root\ssh-error.log" -Raw)" }
    return ($result -join "`n")
}
function Read-PemCertificate([string]$Pem) {
    $match = [regex]::Match($Pem, '-----BEGIN CERTIFICATE-----\s*(.*?)\s*-----END CERTIFICATE-----', 'Singleline')
    if (!$match.Success) { throw 'No certificate in response' }
    return [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String($match.Groups[1].Value))
}
try {
    if ($ProbeEnrollment) { $csr = Get-Content "$root\probe.csr" -Raw }
    else {
        $current = Read-PemCertificate (Remote 'status')
        if ($current.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow.AddDays(60)) {
            "$(Get-Date -Format o) Certificate valid until $($current.NotAfter.ToString('o')); no renewal needed" | Add-Content "$root\renewal.log"
            exit 0
        }
        $csr = Remote 'request'
    }
    $request = New-Object -ComObject CertificateAuthority.Request
    if (!$ProbeEnrollment -and (Test-Path "$root\pending.txt")) {
        $disposition = $request.RetrievePending([int](Get-Content "$root\pending.txt"), $ca)
    } else {
        $csrBase64 = $csr -replace '-----BEGIN CERTIFICATE REQUEST-----|-----END CERTIFICATE REQUEST-----|\s', ''
        $disposition = $request.Submit(257, $csrBase64, 'CertificateTemplate:WebServer', $ca)
    }
    if ($disposition -eq 5) {
        $request.GetRequestId() | Set-Content "$root\pending.txt"
        throw "CA request $($request.GetRequestId()) is pending approval"
    }
    if ($disposition -ne 3) { throw "AD CS denied request: $($request.GetDispositionMessage())" }
    $pem = "-----BEGIN CERTIFICATE-----`n" + $request.GetCertificate(1).Trim() + "`n-----END CERTIFICATE-----`n" + (Get-Content "$root\renewal-ca.pem" -Raw)
    if ($ProbeEnrollment) {
        $pem | Set-Content "$root\probe-issued.pem" -Encoding ascii
        "Enrollment probe issued request $($request.GetRequestId())" | Add-Content "$root\renewal.log"
        exit 0
    }
    $issued = Read-PemCertificate $pem
    if ($issued.NotAfter.ToUniversalTime() -le $current.NotAfter.ToUniversalTime()) { throw 'CA returned a certificate without an extended lifetime' }
    $result = Remote 'install' $pem
    $verified = Read-PemCertificate (Remote 'status')
    if ($verified.Thumbprint -ne $issued.Thumbprint) { throw 'Installed certificate does not match CA response' }
    Remove-Item "$root\pending.txt" -ErrorAction SilentlyContinue
    "$(Get-Date -Format o) $result; valid until $($verified.NotAfter.ToString('o'))" | Add-Content "$root\renewal.log"
    exit 0
} catch {
    "$(Get-Date -Format o) ERROR: $($_.Exception.Message)" | Add-Content "$root\renewal.log"
    Write-Error $_ -ErrorAction Continue
    exit 1
}
