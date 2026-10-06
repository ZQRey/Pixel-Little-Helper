param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$packagePath = (Resolve-Path -LiteralPath $Path).Path
$database = $installer.OpenDatabase($packagePath, 0)
function Rows([string]$Query) {
    $view = $database.OpenView($Query)
    $view.Execute() | Out-Null
    while ($record = $view.Fetch()) {
        $values = @()
        for ($index = 1; $index -le (($Query -split ' FROM ')[0].Split(',').Count); $index++) { $values += $record.StringData($index) }
        ,$values
    }
    $view.Close() | Out-Null
}
function Check([bool]$Passed, [string]$Description) {
    if (!$Passed) { throw $Description }
    Write-Host "PASS $Description"
}
$secure = @(Rows 'SELECT `Value` FROM `Property` WHERE `Property` = ''SecureCustomProperties''')
Check ($secure[0][0].Split(';') -contains 'SERVERURL') 'SERVERURL reaches elevated execute sequence'
$registry = @(Rows 'SELECT `Root`, `Key`, `Name`, `Value` FROM `Registry` WHERE `Name` = ''ServerUrl''')
Check ($registry.Count -eq 1 -and $registry[0][0] -eq '2' -and $registry[0][3] -eq '[SERVERURL]') 'address persisted in HKLM by MSI component'
$ui = @(Rows 'SELECT `Action`, `Sequence` FROM `InstallUISequence`')
$address = $ui | Where-Object { $_[0] -eq 'ServerAddressDlg' }
$progress = $ui | Where-Object { $_[0] -eq 'InstallProgressDlg' }
$execute = $ui | Where-Object { $_[0] -eq 'ExecuteAction' }
Check ([int]$address[1] -lt [int]$progress[1] -and [int]$progress[1] -lt [int]$execute[1]) 'address dialog precedes progress and installation'
$session = $installer.OpenPackage($packagePath, 1)
$actions = @(Rows 'SELECT `Action`, `Condition`, `Sequence` FROM `InstallExecuteSequence`') | Where-Object { $_[0] -in @('UseExistingServerUrl', 'UseDefaultServerUrl') } | Sort-Object { [int]$_[2] }
foreach ($case in @(
    @{ Supplied = 'https://custom.example:5443'; Existing = 'http://old.example:5000'; Expected = 'https://custom.example:5443' },
    @{ Supplied = ''; Existing = 'http://old.example:5000'; Expected = 'http://old.example:5000' },
    @{ Supplied = ''; Existing = ''; Expected = 'http://helper-server' }
)) {
    $session.Property('SERVERURL') = $case.Supplied
    $session.Property('EXISTINGSERVERURL') = $case.Existing
    foreach ($action in $actions) {
        if ($session.EvaluateCondition($action[1]) -eq 1) { $session.DoAction($action[0]) | Out-Null }
    }
    Check ($session.Property('SERVERURL') -eq $case.Expected) "execute sequence chooses $($case.Expected) without UI"
}
Write-Host 'MSI checks passed; no installation performed.'
