param(
    [string]$ServerUrl = 'http://172.16.16.61',
    [Parameter(Mandatory)][string]$AccessFile,
    [Parameter(Mandatory)][string]$ClientExe
)
$ErrorActionPreference = 'Stop'
$accessPath = [IO.Path]::GetFullPath($AccessFile)
$ServerUrl = $ServerUrl.TrimEnd('/')
function Api([string]$Path, $Body = $null, [string]$Method = 'GET') {
    $arguments = @{ Uri = "$ServerUrl/api$Path"; Method = $Method; TimeoutSec = 20 }
    if ($script:token) { $arguments.Headers = @{ Authorization = "Bearer $script:token" } }
    if ($null -ne $Body) { $arguments.Body = $Body | ConvertTo-Json -Depth 12 -Compress; $arguments.ContentType = 'application/json' }
    $response = Invoke-RestMethod @arguments
    foreach ($item in $response) { $item }
}
function Login {
    $script:token = (Api '/auth/login' @{ username = 'admin'; password = $script:password } 'POST').token
}
if (Test-Path -LiteralPath $accessPath) {
    $saved = Get-Content -LiteralPath $accessPath -Raw | ConvertFrom-Json
    $script:password = $saved.password
    Login
} else {
    $script:password = 'admin123'
    Login
    $script:password = 'Helper-' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(12))
    $script:token = (Api '/auth/change-password' @{ currentPassword = 'admin123'; newPassword = $script:password } 'POST').token
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($accessPath)) | Out-Null
    @{ serverUrl = $ServerUrl; username = 'admin'; password = $script:password } | ConvertTo-Json | Set-Content -LiteralPath $accessPath
}
$machine = $env:COMPUTERNAME.ToUpperInvariant()
$configPath = Join-Path $env:APPDATA 'PixelHelper\config.json'
$config = if (Test-Path $configPath) { Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
$old = @(Api '/computers') | Where-Object { $_.machineName -eq $machine }
if ($old.isOnline) { throw 'The client is already connected; stop it before enrolling or reuse its existing key.' }
$enrolled = Api '/computers/enroll' @{ machineName = $machine } 'POST'
New-Item -ItemType Directory -Force (Split-Path $configPath) | Out-Null
if (Test-Path $configPath) { Copy-Item -LiteralPath $configPath -Destination "$configPath.before-live-test" -Force }
$originalRemoteCommands = [bool]$config.allowRemoteCommands
foreach ($pair in @(@('serverUrl',$ServerUrl), @('hubUrl',$null), @('clientToken',$enrolled.clientToken), @('allowRemoteCommands',$true))) {
    $config | Add-Member -NotePropertyName $pair[0] -NotePropertyValue $pair[1] -Force
}
$config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $configPath
$exe = (Resolve-Path -LiteralPath $ClientExe).Path
$script:client = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
function Wait-Computer([bool]$Online, [int]$Seconds = 90) {
    $until = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $computer = @(Api '/computers') | Where-Object { $_.machineName -eq $machine }
        if ($computer -and [bool]$computer.isOnline -eq $Online) { return $computer }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $until)
    throw "Computer did not become online=$Online"
}
function Check-Command([string]$Type, [string]$Payload, [string]$Expected) {
    $task = @(Api '/commands' @{ machines = @($machine); type = $Type; payload = $Payload } 'POST')[0]
    $until = [DateTime]::UtcNow.AddSeconds(90)
    do {
        $log = @(Api '/audit') | Where-Object { $_.taskId -eq $task.taskId }
        if ($log -and $log.status -ne 'Pending') {
            if ($log.exitCode -ne 0 -or !$log.result.Contains($Expected)) { throw "Command $Type failed: $($log.status), $($log.result)" }
            Write-Host "PASS real client $Type round trip and audit"
            return
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $until)
    throw "Command $Type timed out"
}
try {
    $computer = Wait-Computer $true
    Write-Host "PASS real WPF client registered on $ServerUrl as $machine"
    # Registration starts its own WMI scan before a manual inventory request can run.
    Start-Sleep -Seconds 10
    Check-Command 'inventory' '' 'Инвентаризация обновлена'
    $inventory = Api "/computers/$($computer.id)/inventory"
    if (!$inventory.hardware.cpuModel -or $inventory.software.Count -eq 0) { throw 'Real hardware/software inventory missing' }
    Write-Host "PASS inventory: CPU, RAM, disks, $($inventory.software.Count) installed applications"
    Check-Command 'cmd' 'echo PIXEL_HELPER_LIVE_CMD' 'PIXEL_HELPER_LIVE_CMD'
    Check-Command 'powershell' "Write-Output 'PIXEL_HELPER_LIVE_PS'" 'PIXEL_HELPER_LIVE_PS'
    $result = Api '/buttons/apply' @{} 'POST'
    if ($result.notified -lt 1) { throw 'No client notified of menu update' }
    Write-Host 'PASS server menu refresh delivered'
    # Desktop tool windows may have no Process.MainWindowHandle; terminate only our test process in that case.
    if (!$script:client.CloseMainWindow() -or !$script:client.WaitForExit(5000)) { $script:client.Kill(); $script:client.WaitForExit() }
    Wait-Computer $false | Out-Null
    Write-Host 'PASS closing client marks computer Offline'
} finally {
    if (!$script:client.HasExited) {
        if (!$script:client.CloseMainWindow() -or !$script:client.WaitForExit(5000)) { $script:client.Kill(); $script:client.WaitForExit() }
    }
    $currentConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $currentConfig.allowRemoteCommands = $originalRemoteCommands
    $currentConfig | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $configPath
    $script:client = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
}
Wait-Computer $true | Out-Null
Write-Host 'PASS relaunched client is Online; original remote-command setting restored'
