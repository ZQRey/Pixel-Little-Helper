param()
$ErrorActionPreference = 'Stop'
$extDir = $PSScriptRoot
$srcDir = Join-Path $extDir 'src'
$keyFile = Join-Path $extDir 'extension-key.pem'
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $edge)) {
    $edge = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
}

# Pack the extension using Edge CLI
& $edge --pack-extension=$srcDir --pack-extension-key=$keyFile --no-message-box
Start-Sleep -Seconds 2

# msedge creates src.crx next to src directory
$createdCrx = Join-Path $extDir 'src.crx'
$targetCrx = Join-Path $extDir 'pixelhelper-webguard.crx'
if (Test-Path $createdCrx) {
    Move-Item $createdCrx $targetCrx -Force
    Write-Host "CRX generated: $targetCrx"
} else {
    Write-Host "Warning: src.crx not found at expected location, checking $extDir"
    Get-ChildItem $extDir
}

# Generate update.xml
$updateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
  <app appid='jhmfmcbcjmhpoogeljiailchclchcccp'>
    <updatecheck codebase='pixelhelper-webguard.crx' version='1.0.0' />
  </app>
</gupdate>
"@
Set-Content -Path (Join-Path $extDir 'update.xml') -Value $updateXml -Encoding UTF8

# Generate com.pixelhelper.webguard.json
$nativeJson = @"
{
  "name": "com.pixelhelper.webguard",
  "description": "PixelHelper Web Guard Native Host",
  "path": "PixelHelper.exe",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://jhmfmcbcjmhpoogeljiailchclchcccp/"
  ]
}
"@
Set-Content -Path (Join-Path $extDir 'com.pixelhelper.webguard.json') -Value $nativeJson -Encoding UTF8

Write-Host "Extension packaging complete!"
