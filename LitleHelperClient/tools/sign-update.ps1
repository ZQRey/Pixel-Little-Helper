param([Parameter(Mandatory)][string]$Msi, [string]$PrivateKey, [string]$Output, [string]$Dotnet)
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

if (-not $Dotnet) {
    $cand = Join-Path $root '.tools\dotnet\dotnet.exe'
    if (Test-Path $cand) { $Dotnet = $cand } else { $Dotnet = 'dotnet' }
}
$publicKey = Join-Path $root 'UpdatePublicKey.pem'
$signToolDll = Join-Path $PSScriptRoot 'SignTool\bin\Release\net8.0\SignTool.dll'
if (-not (Test-Path $signToolDll)) {
    & $Dotnet build (Join-Path $PSScriptRoot 'SignTool\SignTool.csproj') -c Release -nologo | Out-Null
}

& $Dotnet $signToolDll $version $hash $size ([IO.Path]::GetFullPath($PrivateKey)) ([IO.Path]::GetFullPath($publicKey)) ([IO.Path]::GetFullPath($Output))
if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }
