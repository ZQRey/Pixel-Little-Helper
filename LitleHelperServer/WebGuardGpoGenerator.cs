namespace LitleHelperServer;

public static class WebGuardGpoGenerator
{
    public const string ExtensionId = "jhmfmcbcjmhpoogeljiailchclchcccp";
    public const string DefaultGpoName = "PixelHelper Web Guard Policy";

    public static string GenerateScript(string serverHost = "helper.gp1.loc", string gpoName = DefaultGpoName)
    {
        return $@"<#
.SYNOPSIS
    Развертывание групповой политики (GPO) для принудительной установки расширения PixelHelper Web Guard
    и защиты пользователей домена от фишинговых ссылок.
.DESCRIPTION
    Скрипт проверяет наличие модуля GroupPolicy, проверяет существование GPO '{gpoName}'.
    Если GPO отсутствует, создает ее в Active Directory.
    Настраивает реестр групповой политики для браузеров Microsoft Edge, Google Chrome и Яндекс Браузер:
    - ExtensionInstallForcelist (принудительная тихая установка расширения PixelHelper Web Guard)
    - NativeMessagingHosts (разрешение взаимодействия с локальным клиентом PixelHelper)
    Привязывает GPO к корню домена и выводит отчет о проделанной работе.
.NOTES
    Запускать от имени Администратора домена на контроллере домена Active Directory.
#>

[CmdletBinding()]
param(
    [string]$PolicyName = '{gpoName}',
    [string]$ServerAddress = '{serverHost}'
)

$ErrorActionPreference = 'Stop'

Write-Host ""================================================================="" -ForegroundColor Cyan
Write-Host ""  РАЗВЕРТЫВАНИЕ ПОЛИТИКИ БЕЗОПАСНОСТИ PIXELHELPER WEB GUARD     "" -ForegroundColor Cyan
Write-Host ""================================================================="" -ForegroundColor Cyan

# 1. Проверка модуля GroupPolicy
Write-Host ""[1/5] Проверка окружения Active Directory..."" -ForegroundColor Yellow
if (-not (Get-Module -ListAvailable -Name GroupPolicy)) {{
    Write-Error ""Модуль PowerShell 'GroupPolicy' не найден. Запустите скрипт на контроллере домена или сервере с установленным RSAT (инструменты управления групповыми политиками).""
    exit 1
}}
Import-Module GroupPolicy -ErrorAction Stop

# Определение текущего домена
try {{
    $domain = [System.DirectoryServices.ActiveDirectory.Domain]::GetCurrentDomain().Name
    Write-Host ""    Текущий домен: $domain"" -ForegroundColor Green
}} catch {{
    Write-Warning ""Не удалось автоматически определить домен через .NET. Используется домен по умолчанию из сессии.""
    $domain = $env:USERDNSDOMAIN
}}

# 2. Поиск или создание GPO
Write-Host ""`n[2/5] Проверка наличия объекта групповой политики (GPO)..."" -ForegroundColor Yellow
$gpo = Get-GPO -Name $PolicyName -ErrorAction SilentlyContinue
$createdNew = $false

if ($null -eq $gpo) {{
    Write-Host ""    GPO '$PolicyName' не найдена. Создание нового объекта GPO..."" -ForegroundColor Cyan
    $gpo = New-GPO -Name $PolicyName -Comment ""Политика автоматической защиты от фишинга и вредоносных ссылок PixelHelper Web Guard""
    $createdNew = $true
    Write-Host ""    [+] Создана новая GPO: '$PolicyName' (ID: $($gpo.Id))"" -ForegroundColor Green
}} else {{
    Write-Host ""    [i] Найдена существующая GPO: '$PolicyName' (ID: $($gpo.Id))"" -ForegroundColor Green
}}

# 3. Настройка параметров реестра для браузеров (HKLM в контексте компьютера)
Write-Host ""`n[3/5] Конфигурация параметров групповой политики для браузеров..."" -ForegroundColor Yellow

$extensionId = '{ExtensionId}'
# Источник обновления: локальный файл из каталога установки клиента (гарантированно работает в закрытой сети)
$localUpdateXml = 'file:///C:/Program Files/PixelHelper/extension/update.xml'
$forcelistValue = ""$extensionId;$localUpdateXml""

$policies = @(
    # Microsoft Edge
    @{{
        Key = 'HKLM\Software\Policies\Microsoft\Edge\ExtensionInstallForcelist'
        ValueName = '1'
        Type = 'String'
        Value = $forcelistValue
        Description = 'Microsoft Edge: ExtensionInstallForcelist'
    }},
    # Google Chrome
    @{{
        Key = 'HKLM\Software\Policies\Google\Chrome\ExtensionInstallForcelist'
        ValueName = '1'
        Type = 'String'
        Value = $forcelistValue
        Description = 'Google Chrome: ExtensionInstallForcelist'
    }},
    # Яндекс Браузер
    @{{
        Key = 'HKLM\Software\Policies\YandexBrowser\ExtensionInstallForcelist'
        ValueName = '1'
        Type = 'String'
        Value = $forcelistValue
        Description = 'Яндекс Браузер: ExtensionInstallForcelist'
    }},
    # Microsoft Edge NativeMessagingHosts
    @{{
        Key = 'HKLM\Software\Microsoft\Edge\NativeMessagingHosts\com.pixelhelper.webguard'
        ValueName = ''
        Type = 'String'
        Value = 'C:\Program Files\PixelHelper\extension\com.pixelhelper.webguard.json'
        Description = 'Microsoft Edge: NativeMessagingHost'
    }},
    # Google Chrome NativeMessagingHosts
    @{{
        Key = 'HKLM\Software\Google\Chrome\NativeMessagingHosts\com.pixelhelper.webguard'
        ValueName = ''
        Type = 'String'
        Value = 'C:\Program Files\PixelHelper\extension\com.pixelhelper.webguard.json'
        Description = 'Google Chrome: NativeMessagingHost'
    }}
)

$modifiedCount = 0
foreach ($p in $policies) {{
    try {{
        Set-GPRegistryValue -Name $PolicyName -Key $p.Key -ValueName $p.ValueName -Type $p.Type -Value $p.Value -ErrorAction Stop | Out-Null
        Write-Host ""    [+] Настроен параметр: $($p.Description)"" -ForegroundColor Gray
        $modifiedCount++
    }} catch {{
        Write-Warning ""    [-] Не удалось записать параметр $($p.Description): $($_.Exception.Message)""
    }}
}}

# 4. Привязка GPO к домену
Write-Host ""`n[4/5] Проверка связей групповой политики (GPO Link)..."" -ForegroundColor Yellow
$links = Get-GPOReport -Name $PolicyName -ReportType Xml
$isLinked = $links -match '<LinksTo>'

if (-not $isLinked) {{
    Write-Host ""    Привязка GPO '$PolicyName' к корню домена '$domain'..."" -ForegroundColor Cyan
    try {{
        New-GPLink -Name $PolicyName -Target ""dc=$($domain.Replace('.', ',dc='))"" -LinkEnabled Yes -ErrorAction Stop | Out-Null
        Write-Host ""    [+] GPO успешно привязана к корню домена."" -ForegroundColor Green
    }} catch {{
        Write-Warning ""    Не удалось автоматически привязать GPO к корню домена: $($_.Exception.Message). Вы можете привязать ее вручную через консоль gpmc.msc.""
    }}
}} else {{
    Write-Host ""    [i] GPO уже имеет активные связи в структуре Active Directory."" -ForegroundColor Green
}}

# 5. Итоговый отчет для администратора
Write-Host ""`n[5/5] ИТОГОВЫЙ ОТЧЕТ ВЫПОЛНЕНИЯ:"" -ForegroundColor Cyan
Write-Host ""-----------------------------------------------------------------"" -ForegroundColor Gray
if ($createdNew) {{
    Write-Host ""[СОЗДАНА НОВАЯ ГРУППОВАЯ ПОЛИТИКА]: '$PolicyName'"" -ForegroundColor Green
}} else {{
    Write-Host ""[ИЗМЕНЕНИЯ ВНЕСЕНЫ В СУЩЕСТВУЮЩУЮ GPO]: '$PolicyName'"" -ForegroundColor Yellow
}}
Write-Host ""Идентификатор GPO (GUID): $($gpo.Id)"" -ForegroundColor White
Write-Host ""Домен:                    $domain"" -ForegroundColor White
Write-Host ""Обновлено параметров:     $modifiedCount из $($policies.Count)"" -ForegroundColor White
Write-Host ""ID расширения:            $extensionId"" -ForegroundColor White
Write-Host ""-----------------------------------------------------------------"" -ForegroundColor Gray
Write-Host ""`nПолитика успешно сконфигурирована! На клиентских компьютерах она применится"" -ForegroundColor Green
Write-Host ""при следующем обновлении политик (gpupdate /force) либо при перезагрузке."" -ForegroundColor Green
Write-Host ""=================================================================`n"" -ForegroundColor Cyan
";
    }
}
