namespace LitleHelperServer;

public static class AntivirusGpoGenerator
{
    public const string DefaultGpoName = "PixelHelper Antivirus Trust Policy";

    public static string GenerateScript(string gpoName = DefaultGpoName)
    {
        string template = @"<#
.SYNOPSIS
    Развертывание групповой политики (GPO) для настройки доверия Windows Defender к PixelHelper Antivirus.
.DESCRIPTION
    Скрипт проверяет наличие модуля GroupPolicy, проверяет существование GPO '__GPO_NAME__'.
    Если GPO отсутствует, создает ее в Active Directory.
    Настраивает исключения Microsoft Defender Antivirus (пути, процессы, расширения файлов):
    - Исключение путей: C:\Program Files\PixelHelper и C:\ProgramData\PixelHelper
    - Исключение процессов: PixelHelper.exe, PixelHelperHost.exe
    - Исключение расширений баз и карантина: .cvd, .cld, .yar, .quarantine
    Привязывает GPO к корню домена и выводит отчет о проделанной работе.
.NOTES
    Запускать от имени Администратора домена на контроллере домена Active Directory.
#>

[CmdletBinding()]
param(
    [string]$PolicyName = '__GPO_NAME__'
)

$ErrorActionPreference = 'Stop'

Write-Host ""================================================================="" -ForegroundColor Cyan
Write-Host ""  РАЗВЕРТЫВАНИЕ ПОЛИТИКИ ДОВЕРИЯ WINDOWS DEFENDER ДЛЯ PIXELHELPER "" -ForegroundColor Cyan
Write-Host ""================================================================="" -ForegroundColor Cyan

# 1. Проверка модуля GroupPolicy
Write-Host ""[1/4] Проверка окружения Active Directory..."" -ForegroundColor Yellow
if (-not (Get-Module -ListAvailable -Name GroupPolicy)) {
    Write-Error ""Модуль PowerShell 'GroupPolicy' не найден. Запустите скрипт на контроллере домена или сервере с установленным RSAT.""
    exit 1
}
Import-Module GroupPolicy -ErrorAction Stop

# Определение текущего домена
try {
    $domain = [System.DirectoryServices.ActiveDirectory.Domain]::GetCurrentDomain().Name
    Write-Host ""    Текущий домен: $domain"" -ForegroundColor Green
} catch {
    Write-Warning ""Не удалось автоматически определить домен через .NET. Используется домен по умолчанию из сессии.""
    $domain = $env:USERDNSDOMAIN
}

# 2. Поиск или создание GPO
Write-Host ""`n[2/4] Проверка наличия объекта групповой политики (GPO)..."" -ForegroundColor Yellow
$gpo = Get-GPO -Name $PolicyName -ErrorAction SilentlyContinue
$createdNew = $false

if ($null -eq $gpo) {
    Write-Host ""    GPO '$PolicyName' не найдена. Создание нового объекта GPO..."" -ForegroundColor Cyan
    $gpo = New-GPO -Name $PolicyName -Comment ""Политика доверия и исключений Microsoft Defender для антивирусного модуля PixelHelper""
    $createdNew = $true
    Write-Host ""    [+] Создана новая GPO: '$PolicyName' (ID: $($gpo.Id))"" -ForegroundColor Green
} else {
    Write-Host ""    [i] Найдена существующая GPO: '$PolicyName' (ID: $($gpo.Id))"" -ForegroundColor Green
}

# 3. Настройка параметров реестра для Microsoft Defender Antivirus
Write-Host ""`n[3/4] Конфигурация параметров исключений Microsoft Defender..."" -ForegroundColor Yellow

$exclusions = @(
    # Исключение путей каталогов установки и баз данных
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Paths'
        ValueName = 'C:\Program Files\PixelHelper'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion: C:\Program Files\PixelHelper'
    },
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Paths'
        ValueName = 'C:\ProgramData\PixelHelper'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion: C:\ProgramData\PixelHelper'
    },
    # Исключение исполняемых процессов
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Processes'
        ValueName = 'PixelHelper.exe'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion Process: PixelHelper.exe'
    },
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Processes'
        ValueName = 'PixelHelperHost.exe'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion Process: PixelHelperHost.exe'
    },
    # Исключение расширений баз и изолированных файлов карантина
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Extensions'
        ValueName = '.cvd'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion Extension: .cvd (ClamAV Main/Daily)'
    },
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Extensions'
        ValueName = '.cld'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion Extension: .cld (ClamAV Local)'
    },
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Extensions'
        ValueName = '.yar'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion Extension: .yar (YARA Rules)'
    },
    @{
        Key = 'HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\Extensions'
        ValueName = '.quarantine'
        Type = 'DWord'
        Value = 0
        Description = 'Defender Exclusion Extension: .quarantine (PixelHelper Quarantine)'
    }
)

foreach ($item in $exclusions) {
    try {
        Set-GPRegistryValue -Name $PolicyName `
            -Key $item.Key `
            -ValueName $item.ValueName `
            -Type $item.Type `
            -Value $item.Value `
            -ErrorAction Stop | Out-Null
        Write-Host ""    [+] Записан параметр: $($item.Description)"" -ForegroundColor Green
    } catch {
        Write-Warning ""    [!] Ошибка при записи $($item.Description): $_""
    }
}

# 4. Связывание GPO с корнем домена (GPO Link)
Write-Host ""`n[4/4] Проверка связывания GPO с доменом..."" -ForegroundColor Yellow
$links = Get-GPOReport -Name $PolicyName -ReportType Xml
$isLinked = $links -match '<LinksTo>'

if (-not $isLinked) {
    Write-Host ""    Привязка GPO '$PolicyName' к корню домена '$domain'..."" -ForegroundColor Cyan
    try {
        New-GPLink -Name $PolicyName -Target ""dc=$($domain.Replace('.', ',dc='))"" -LinkEnabled Yes -ErrorAction Stop | Out-Null
        Write-Host ""    [+] GPO успешно привязана к корню домена."" -ForegroundColor Green
    } catch {
        Write-Warning ""    Не удалось автоматически привязать GPO к корню домена: $_. Вы можете привязать ее вручную через gpmc.msc.""
    }
} else {
    Write-Host ""    [i] GPO уже имеет активные связи в структуре Active Directory."" -ForegroundColor Green
}

$statusText = if ($createdNew) { 'Создана и настроена' } else { 'Существовала, параметры обновлены' }
Write-Host ""`n================================================================="" -ForegroundColor Cyan
Write-Host ""  ИТОГОВЫЙ ОТЧЕТ РАЗВЕРТЫВАНИЯ                                  "" -ForegroundColor Cyan
Write-Host ""================================================================="" -ForegroundColor Cyan
Write-Host ""Имя GPO:          $PolicyName"" -ForegroundColor White
Write-Host ""Идентификатор:    $($gpo.Id)"" -ForegroundColor White
Write-Host ""Статус:           $statusText"" -ForegroundColor White
Write-Host ""Домен:            $domain"" -ForegroundColor White
Write-Host ""Исключения:       Пути C:\Program Files\PixelHelper, C:\ProgramData\PixelHelper;"" -ForegroundColor White
Write-Host ""                  Процессы PixelHelper.exe, PixelHelperHost.exe;"" -ForegroundColor White
Write-Host ""                  Расширения .cvd, .cld, .yar, .quarantine."" -ForegroundColor White
Write-Host ""`nПолитика готова. На клиентах вступит в силу при следующем цикле обновления или после 'gpupdate /force'."" -ForegroundColor Green
";
        return template.Replace("__GPO_NAME__", gpoName);
    }
}
