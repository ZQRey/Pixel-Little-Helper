param([Parameter(Mandatory)][string]$ServerUrl)
$ErrorActionPreference = 'Stop'
$uri = $null
if ($ServerUrl -match '[\s\\,]' -or ![Uri]::TryCreate($ServerUrl,[UriKind]::Absolute,[ref]$uri) -or
    $uri.Scheme -notin @('http','https') -or $uri.HostNameType -eq [UriHostNameType]::Unknown -or
    $uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw 'Укажите адрес вида http://helper.gp1.loc, без обратных слешей и запятой.' }
$machine=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
try {
    $key=$machine.CreateSubKey('Software\PixelHelper')
    try {$key.SetValue('ServerUrl',$uri.AbsoluteUri.TrimEnd('/'),[Microsoft.Win32.RegistryValueKind]::String)} finally {$key.Dispose()}
} finally {$machine.Dispose()}
if(Get-Service PixelHelperUpdater -ErrorAction SilentlyContinue){Restart-Service PixelHelperUpdater}
Write-Output 'Адрес сохранён для всех пользователей и службы обновлений. Закройте помощника и запустите снова в пользовательском сеансе. Если в config.json задан hubUrl, удалите этот параметр или замените на null.'
