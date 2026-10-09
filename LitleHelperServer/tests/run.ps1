param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
if (Test-Path $Dotnet) { $Dotnet = (Resolve-Path $Dotnet).Path }
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
& $Dotnet (Join-Path $PSScriptRoot 'bin\Release\net8.0\IntegrationTests.dll') --emergency
if ($LASTEXITCODE -ne 0) { throw 'Emergency checks failed' }
& node (Join-Path $PSScriptRoot 'ticket-panel.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Ticket panel checks failed' }
& node (Join-Path $PSScriptRoot 'process-panel.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Process panel checks failed' }
& node (Join-Path $PSScriptRoot 'settings-panel.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Settings panel checks failed' }
& node (Join-Path $PSScriptRoot 'messenger-i18n.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Messenger i18n checks failed' }
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $probe.Start(); $serverPort = $probe.LocalEndpoint.Port; $probe.Stop()
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $probe.Start(); $mockPort = $probe.LocalEndpoint.Port; $probe.Stop()
$serverUrl = "http://127.0.0.1:$serverPort"
$mockUrl = "http://127.0.0.1:$mockPort"
$dllPath = Join-Path $serverRoot 'bin\Release\net8.0\LitleHelperServer.dll'
$start = [Diagnostics.ProcessStartInfo]::new($Dotnet)
$start.Arguments = "`"$dllPath`" --urls $serverUrl"
$start.WorkingDirectory = $serverRoot; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
$rngBytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rngBytes)
$jwtKey = [Convert]::ToBase64String($rngBytes)
$envMap = @{
    'Database__Provider' = 'Sqlite'
    'ConnectionStrings__Database' = "Data Source=$testRoot\helper.db"
    'Jwt__SigningKey' = $jwtKey
    'Glpi__SettingsFile' = (Join-Path $testRoot 'glpi-settings.json')
    'Integrations__SettingsFile' = (Join-Path $testRoot 'integrations.json')
    'Messenger__SettingsFile' = (Join-Path $testRoot 'messenger.json')
    'ClientUpdates__Directory' = (Join-Path $testRoot 'client-updates')
    'Telegram__ApiBaseUrl' = $mockUrl
    'Glpi__BaseUrl' = "$mockUrl/apirest.php"
    'Glpi__AppToken' = 'mock-app'
    'Glpi__UserToken' = 'mock-user'
    'Glpi__ServiceUserId' = '99'
}
foreach ($k in $envMap.Keys) {
    if ($start.PSObject.Properties['Environment'] -and $null -ne $start.Environment) {
        $start.Environment[$k] = $envMap[$k]
    } else {
        $start.EnvironmentVariables[$k] = $envMap[$k]
    }
}
$start.RedirectStandardError = $true
$start.RedirectStandardOutput = $true
$server = [Diagnostics.Process]::Start($start)
try {
    $ready = $false
    for ($attempt=0; $attempt -lt 50; $attempt++) {
        if ($server.HasExited) {
            $stdout = $server.StandardOutput.ReadToEnd()
            $stderr = $server.StandardError.ReadToEnd()
            throw "Server terminated before health check. ExitCode: $($server.ExitCode)`nSTDOUT: $stdout`nSTDERR: $stderr"
        }
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
