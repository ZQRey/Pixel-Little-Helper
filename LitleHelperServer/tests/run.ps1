param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$serverRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $serverRoot ('artifacts\test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $testRoot | Out-Null
& $Dotnet build (Join-Path $serverRoot 'LitleHelperServer.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }
& $Dotnet build (Join-Path $PSScriptRoot 'IntegrationTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests build failed' }
& $Dotnet (Join-Path $PSScriptRoot 'bin\Release\net8.0\IntegrationTests.dll') --telegram-management
if ($LASTEXITCODE -ne 0) { throw 'Telegram management checks failed' }
& $Dotnet (Join-Path $PSScriptRoot 'bin\Release\net8.0\IntegrationTests.dll') --messenger
if ($LASTEXITCODE -ne 0) { throw 'Messenger checks failed' }
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $probe.Start(); $serverPort = $probe.LocalEndpoint.Port; $probe.Stop()
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $probe.Start(); $mockPort = $probe.LocalEndpoint.Port; $probe.Stop()
$serverUrl = "http://127.0.0.1:$serverPort"
$mockUrl = "http://127.0.0.1:$mockPort"
$start = [Diagnostics.ProcessStartInfo]::new($Dotnet)
$start.ArgumentList.Add((Join-Path $serverRoot 'bin\Release\net8.0\LitleHelperServer.dll'))
$start.ArgumentList.Add('--urls'); $start.ArgumentList.Add($serverUrl)
$start.WorkingDirectory = $serverRoot; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.Environment['Database__Provider'] = 'Sqlite'
$start.Environment['ConnectionStrings__Database'] = "Data Source=$testRoot\helper.db"
$start.Environment['Jwt__SigningKey'] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$start.Environment['Glpi__SettingsFile'] = Join-Path $testRoot 'glpi-settings.json'
$start.Environment['Integrations__SettingsFile'] = Join-Path $testRoot 'integrations.json'
$start.Environment['Messenger__SettingsFile'] = Join-Path $testRoot 'messenger.json'
$start.Environment['ClientUpdates__Directory'] = Join-Path $testRoot 'client-updates'
$start.Environment['Telegram__ApiBaseUrl'] = $mockUrl
$start.Environment['Glpi__BaseUrl'] = "$mockUrl/apirest.php"
$start.Environment['Glpi__AppToken'] = 'mock-app'; $start.Environment['Glpi__UserToken'] = 'mock-user'; $start.Environment['Glpi__ServiceUserId'] = '99'
$server = [Diagnostics.Process]::Start($start)
try {
    $ready = $false
    for ($attempt=0; $attempt -lt 50; $attempt++) {
        if ($server.HasExited) { throw 'Server terminated before health check' }
        try { $response = Invoke-RestMethod "$serverUrl/health" -TimeoutSec 1; $ready = $response.status -eq 'ok'; if ($ready) { break } } catch { }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Server did not start' }
    & $Dotnet (Join-Path $PSScriptRoot 'bin\Release\net8.0\IntegrationTests.dll') $serverUrl $mockUrl (Join-Path $testRoot 'glpi-settings.json') (Join-Path $testRoot 'integrations.json') (Join-Path $serverRoot '..\LitleHelperClient\artifacts\release-1.2.0')
    if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed' }
} finally {
    if (-not $server.HasExited) { $server.Kill(); $server.WaitForExit() }
    $server.Dispose()
}
