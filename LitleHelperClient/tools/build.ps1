param([switch]$SkipMsi, [string]$Dotnet = 'dotnet', [string]$WixBin = $env:WIX, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'PixelHelper.csproj') -Raw)).Project.PropertyGroup.Version
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot "artifacts\release-$version" }
$publish = Join-Path $output 'publish'
New-Item -ItemType Directory -Force $output | Out-Null
& $Dotnet publish (Join-Path $projectRoot 'PixelHelper.csproj') -c Release -r win-x64 --self-contained true -o $publish '-p:PublishSingleFile=false' '-p:PublishReadyToRun=false'
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
if ($SkipMsi) { return }
if (-not $WixBin) { throw 'Set -WixBin to the WiX 3.14 bin directory (heat, candle, light).' }
if (Test-Path (Join-Path $WixBin 'bin\heat.exe')) { $WixBin = Join-Path $WixBin 'bin' }
$harvest = Join-Path $output 'PublishedFiles.wxs'
& (Join-Path $WixBin 'heat.exe') dir $publish -nologo -cg PublishedFiles -dr INSTALLFOLDER -ag -srd -sreg -scom -var var.PublishDir -t (Join-Path $projectRoot 'installer\Harvest.xsl') -out $harvest
if ($LASTEXITCODE -ne 0) { throw 'heat failed' }
& (Join-Path $WixBin 'candle.exe') -nologo -arch x64 "-dPublishDir=$publish" -out "$output\" (Join-Path $projectRoot 'installer\Product.wxs') $harvest
if ($LASTEXITCODE -ne 0) { throw 'candle failed' }
& (Join-Path $WixBin 'light.exe') -nologo -ext WixUIExtension -cultures:ru-ru -out (Join-Path $output 'PixelHelper.msi') (Join-Path $output 'Product.wixobj') (Join-Path $output 'PublishedFiles.wixobj')
if ($LASTEXITCODE -ne 0) { throw 'light failed' }
Write-Host "MSI: $output\PixelHelper.msi"
if(Test-Path (Join-Path $projectRoot 'artifacts\update-signing-key.pem')) { & (Join-Path $PSScriptRoot 'sign-update.ps1') -Msi (Join-Path $output 'PixelHelper.msi') -Dotnet $Dotnet }
