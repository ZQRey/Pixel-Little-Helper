param([Parameter(Mandatory)][string]$Msi, [string]$PrivateKey, [string]$Output)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $PrivateKey){$PrivateKey=Join-Path $root 'artifacts\update-signing-key.pem'}
if(-not $Output){$Output=Join-Path (Split-Path ([IO.Path]::GetFullPath($Msi))) 'PixelHelper.update.json'}
if(-not(Test-Path -LiteralPath $PrivateKey)){throw 'Signing key missing. Restore the private key matching UpdatePublicKey.pem; do not generate a new key for existing clients.'}
$installer=New-Object -ComObject WindowsInstaller.Installer
$database=$installer.OpenDatabase([IO.Path]::GetFullPath($Msi),0)
function ReadMsiProperty([string]$prop){$v=$database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$prop'");$null=$v.Execute();$r=$v.Fetch();return $r.StringData(1)}
$version=ReadMsiProperty 'ProductVersion'
if((ReadMsiProperty 'UpgradeCode') -ne '{0C2B80D1-52E0-4931-8F48-FBB031A89A55}' -or (ReadMsiProperty 'ProductName') -ne 'PixelHelper'){throw 'Not a PixelHelper MSI'}
$size=(Get-Item -LiteralPath $Msi).Length
$hash=(Get-FileHash -LiteralPath $Msi -Algorithm SHA256).Hash.ToLowerInvariant()
$data=[Text.Encoding]::UTF8.GetBytes("PixelHelper-MSI-v1`n$version`n$hash`n$size")
$rsa=[Security.Cryptography.RSA]::Create()
try{
    $rsa.ImportFromPem([IO.File]::ReadAllText([IO.Path]::GetFullPath($PrivateKey)))
    $trusted=[Security.Cryptography.RSA]::Create()
    try{$trusted.ImportFromPem([IO.File]::ReadAllText((Join-Path $root 'UpdatePublicKey.pem')));if($rsa.ExportSubjectPublicKeyInfoPem() -ne $trusted.ExportSubjectPublicKeyInfoPem()){throw 'Signing key does not match client public key'}}finally{$trusted.Dispose()}
    $signature=[Convert]::ToBase64String($rsa.SignData($data,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))
    $manifest=@{version=$version;sha256=$hash;size=$size;signature=$signature}|ConvertTo-Json
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),$manifest)
}finally{$rsa.Dispose()}
Write-Host "Signed update manifest: $Output"
