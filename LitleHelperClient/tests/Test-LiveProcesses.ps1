param([Parameter(Mandatory)][string]$AccessFile)
$ErrorActionPreference = 'Stop'
$access = Get-Content -LiteralPath $AccessFile -Raw | ConvertFrom-Json
$login = Invoke-RestMethod "$($access.serverUrl)/api/auth/login" -Method Post -ContentType application/json -Body (@{ username = $access.username; password = $access.password } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.token)" }
function TaskResult([string]$Type, [string]$Payload = '') {
    $tasks = Invoke-RestMethod "$($access.serverUrl)/api/commands" -Headers $headers -Method Post -ContentType application/json -Body (@{ machines = @($env:COMPUTERNAME); type = $Type; payload = $Payload } | ConvertTo-Json)
    $task = $tasks[0]
    $until = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $result = Invoke-RestMethod "$($access.serverUrl)/api/tasks/$($task.taskId)" -Headers $headers
        if ($result.status -ne 'Pending') { return $result }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $until)
    throw "Task $Type timed out"
}
$start = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
foreach ($argument in @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 90')) { $start.ArgumentList.Add($argument) }
$sleeper = [Diagnostics.Process]::Start($start)
try {
    $listed = TaskResult 'processes'
    if ($listed.status -ne 'Completed') { throw $listed.result }
    $rows = $listed.result | ConvertFrom-Json
    $target = $rows | Where-Object pid -eq $sleeper.Id
    if (!$target.canStop -or !$target.startTimeUtcTicks) { throw 'Owned test process not selectable' }
    Write-Host "PASS real computer returned $($rows.Count) processes including owned test PID"
    $stale = TaskResult 'kill_pid' (@{ pid = $target.pid; name = $target.name; startTimeUtcTicks = '1' } | ConvertTo-Json -Compress)
    if ($stale.status -ne 'Failed' -or $sleeper.HasExited) { throw 'Stale identity protection failed' }
    Write-Host 'PASS stale process selection rejected remotely without termination'
    $stopped = TaskResult 'kill_pid' (@{ pid = $target.pid; name = $target.name; startTimeUtcTicks = $target.startTimeUtcTicks } | ConvertTo-Json -Compress)
    if ($stopped.status -ne 'Completed' -or !$sleeper.WaitForExit(5000)) { throw 'Selected test process was not stopped' }
    Write-Host 'PASS selected test process stopped through real server and WPF client'
    $refreshed = TaskResult 'processes'
    if ($refreshed.status -ne 'Completed' -or (($refreshed.result | ConvertFrom-Json) | Where-Object pid -eq $target.pid)) { throw 'Stopped process remains in refreshed list' }
    Write-Host 'PASS refreshed process list no longer contains stopped PID'
} finally {
    if (!$sleeper.HasExited) { $sleeper.Kill(); $sleeper.WaitForExit() }
    $sleeper.Dispose()
}
