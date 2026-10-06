# LitleHelperServer

ASP.NET Core 8 Web API + SignalR + EF Core. Адаптивная панель HTML/CSS/Vanilla JS раздаётся из `wwwroot`. Библиотека SignalR включена локально; внешние CDN не нужны.

## Docker Compose и PostgreSQL

```bash
# Из корня клонированного репозитория (Docker Compose 2.20.3+):
docker compose up -d --build
# Также работает из LitleHelperServer.
```

Файл .env не обязателен. Одноразовый контейнер init-secrets генерирует пароль PostgreSQL в generated-secrets; сервер создаёт JWT-ключ в server-data. Повторный запуск использует сохранённые значения. Для GLPI или собственных секретов можно заполнить LitleHelperServer/.env по примеру .env.example. POSTGRES_PASSWORD применяется при первой инициализации; смена существующего пароля требует ротации в PostgreSQL.

Панель: `http://localhost:5000`. Первый вход: **admin / admin123**. До обязательной смены пароля остальные API и хаб заблокированы. Пароли: 8–72 символа, максимум 72 байта UTF-8 (ограничение BCrypt). Для нового или сброшенного пароля смена также обязательна.

Сервер работает от пользователя контейнера `app`; PostgreSQL не публикует порт наружу. База сохраняется в named volume.

```powershell
docker compose logs -f server
docker compose down
# down не удаляет данные; down -v удаляет volumes.
```

Для корпоративной эксплуатации используйте HTTPS reverse proxy с поддержкой WebSocket и ограничьте доступ к сети. По HTTP токены могут быть перехвачены; адрес HTTP сохранён по заданию для локального запуска. Секреты GLPI/JWT не включаются в браузер или MSI.

## Локальный запуск, SQLite и сборка

```powershell
cd LitleHelperServer
dotnet run --project LitleHelperServer.csproj
```

По умолчанию: `data/helper.db`, порт 5000. При пустом Jwt:SigningKey локальный случайный ключ сохраняется в `data/jwt.key`; сохраняйте его между перезапусками и ограничьте доступ к каталогу. В Compose ключ также генерируется автоматически, если JWT_SIGNING_KEY не задан.

В текущем окружении SDK установлен локально. Из корня двух проектов:

```powershell
$env:DOTNET_CLI_HOME = "$PWD\LitleHelperClient\.tools\cli"
$env:NUGET_PACKAGES = "$PWD\LitleHelperClient\.tools\nuget"
$dotnet = "$PWD\LitleHelperClient\.tools\dotnet\dotnet.exe"
& $dotnet build LitleHelperServer/LitleHelperServer.csproj -c Release
Push-Location LitleHelperServer
& $dotnet run --project LitleHelperServer.csproj --no-launch-profile
Pop-Location
```

Готовая framework-dependent публикация: `LitleHelperServer/artifacts/publish`. Запускайте из серверной папки, где находятся appsettings.json и data:

```powershell
cd LitleHelperServer
dotnet artifacts/publish/LitleHelperServer.dll
```

Нужен ASP.NET Core Runtime 8. При первом запуске схема создаётся `EnsureCreated`; автоматических EF migrations для будущих изменений схемы нет. Выпуск 1.1 рассчитан на новую серверную БД. На старте сохранённые соединения помечаются Offline, клиенты переподключаются.

## Роли

| Возможность | SuperAdmin | Admin | Operator | User |
|---|---|---|---|---|
| Список и статусы ПК | Да | Да | Да | Нет |
| Подключение ПК / ключ | Да | Да | Нет | Нет |
| Произвольный CMD/PS | Да | Нет | Нет | Нет |
| Скрипты, питание, Kill, запрос инвентаря | Да | Да | Нет | Нет |
| Чтение инвентаря | Да | Нет | Нет | Нет |
| Редактор кнопок | Да | Да¹ | Нет | Нет |
| Пользователи панели | Да | Нет | Нет | Нет |
| Аудит | Весь | Свои базовые операции | Нет | Нет |
| Заявки GLPI | Все | Все | Все | Только свои, чтение |
| Удаление ПК / аудита | Да | Нет | Нет | Нет |

¹ `run_command` создаёт/изменяет только SuperAdmin: иначе Admin смог бы обойти запрет терминала через кнопку. Admin управляет open_folder/open_url/ticket. Предустановленные скрипты задаются доверенным разделом `Scripts` серверной конфигурации; Admin передаёт только ID.

Права проверяются сервером в API и RPC, а не только скрытием кнопок. Изменение роли/пароля/активности отзывает JWT через SecurityVersion и отключает живое соединение панели. Последнего активного SuperAdmin нельзя удалить/отключить. Вход: 12 попыток в минуту на IP. Для reverse proxy отдельно настройте доверенные forwarded headers, иначе ограничение относится к IP proxy.

## Регистрация ПК

1. В панели «Компьютеры → Подключить ПК» введите точное имя, например `PC-001`.
2. Сохраните выданный `clientToken`: он показывается один раз. Сервер хранит SHA-256 хэш, ключ привязан к машине.
3. На целевом ПК создайте `%APPDATA%\PixelHelper\config.json`:

```json
{
  "x": null, "y": null,
  "serverUrl": "http://helper-server:5000",
  "hubUrl": null,
  "clientToken": "КЛЮЧ-ДЛЯ-ЭТОГО-ПК",
  "enableAdministrativeCommands": true,
  "allowRemoteCommands": false
}
```

4. Запустите новый помощник. `hubUrl` задаёт полный адрес явно; по умолчанию serverUrl + `/helperHub`. Без ключа включается офлайн-режим.
5. ПК появится Online и отправит имя, пользователя, домен, IP, Windows, железо и ПО.

Для смены ключа ПК должен быть Offline; после смены перезапустите клиент. Для массового развёртывания используйте отдельные ключи и GPP/систему управления, а не общий секрет в MSI. Ограничьте чтение config.json нужным пользователем и администраторами.

Агент работает в контексте пользователя; локальный пользователь с доступом к ключу входит в доверенную границу ПК. AD-логин поступает от доверенного агента. LDAP/SSO входа в панель нет. Для роли User создавайте логин, совпадающий с Environment.UserName, регистр нормализуется.

## Команды и привилегии

Основной RPC: `ExecuteCommand({ taskId, type, payload })`. TaskId добавлен для связи с конкретной отправкой/администратором/ПК. Вывод: SendExecutionOutput(taskId, chunk); итог: SendExecutionResult(taskId, output, exitCode). Сервер принимает только результаты существующих Pending-задач назначенной машины.

Типы: cmd, powershell, script, kill, reboot, shutdown, inventory. Script преобразуется в доверенную команду сервера. «Все» создаёт отдельные AuditLog для всех зарегистрированных ПК; Offline отмечается сразу, очереди команд на будущий запуск нет. Незавершённые задачи получают TimedOut через 5 минут. Клиент исполняет последовательно, максимум 3 минуты и 128 КиБ вывода.

**Выбран fallback из задания:** SYSTEM-служба PixelHelper.Worker не устанавливается. Команды работают с правами пользователя помощника; отказ доступа, stderr и exit code возвращаются в панель. Автоматического повышения прав/UAC-обхода нет. Reboot/Shutdown вызывают shutdown /r или /s /t 5 /f; интерфейс требует подтверждения, несохранённые данные могут быть потеряны.

## Кнопки

Поля Title/IconName/ActionType/Payload/OrderIndex/IsActive/TargetGroup. Группы: `All`, `domain:DOMAIN`, `pc:PCNAME`. Сохранение меняет БД; «Применить изменения» отправляет OnButtonsUpdated активным агентам. Они получают актуальные кнопки при регистрации/реконнекте и каждые 30 минут. Максимум 12 активных кнопок, сортировка OrderIndex/Id.

run_command Payload: полный путь EXE или JSON:

```json
{"executable":"C:\\Windows\\System32\\notepad.exe","arguments":[]}
```

Кнопки запуска требуют allowRemoteCommands=true на клиенте. RPC регулируются отдельным enableAdministrativeCommands. IconName сохраняется в редакторе и модели, но графический набор значков не поставляется; WPF-меню показывает текст.

## GLPI

Настройте Glpi в appsettings.json либо `Glpi__BaseUrl`, `Glpi__AppToken`, `Glpi__UserToken`, `Glpi__ServiceUserId` (Compose использует GLPI_* из .env).

* BaseUrl: `http://glpi.gp1.loc/apirest.php`.
* AppToken: токен API-клиента GLPI.
* UserToken: remote access token сервисной учётной записи с правами на пользователей/заявки.
* ServiceUserId: положительный ID сервисного заявителя для fallback, если AD-пользователь не найден.

В GLPI включите REST API, вход по user_token и диапазон IP сервера. Поддерживается классический API GLPI 10 apirest.php: [официальная документация](https://github.com/glpi-project/glpi/blob/10.0/bugfixes/apirest.md).

Последовательность: initSession → точный search/User → POST Ticket → killSession в finally. Поиск включает forcedisplay[0]=2 для получения ID; _users_id_requester передаётся при создании. Пользователь вводит только проблему; ПК и логин добавляются автоматически, реальный GLPI ID возвращается в бабл. При ошибке успешное подтверждение не выводится; офлайн-очереди заявок нет.

Панель показывает **заявки, созданные через этот сервер**, не импортирует всю историю GLPI. В карточке запрашиваются текущие данные GLPI; Staff меняет статусы 1–6. User не может читать чужие записи по ID. Таблица хранит последний установленный через панель статус; изменения извне GLPI видны при открытии карточки.

## Единый клиентский MSI для GPO

```powershell
cd LitleHelperClient
dotnet build PixelHelper.csproj -c Release
dotnet run --project tests/PixelHelper.Tests.csproj -c Release
.\tools\build.ps1 -WixBin 'C:\Tools\wix314'
```

MSI: `LitleHelperClient/artifacts/release-1.1.1/PixelHelper.msi`; EXE с зависимостями: release-1.1.1/publish. WiX 3.14.1, heat/candle/light. Из корня с локальными инструментами:

```powershell
.\LitleHelperClient\tools\build.ps1 -Dotnet "$PWD\LitleHelperClient\.tools\dotnet\dotnet.exe" -WixBin "$PWD\LitleHelperClient\.tools\wix"
msiexec /i PixelHelper.msi /qn /norestart SERVERURL="http://helper-server:5000" /l*v install.log
msiexec /x PixelHelper.msi /qn /norestart /l*v uninstall.log
```

Это один MSI **клиентского агента** с .NET Desktop Runtime, WPF, SignalR и System.Management. Сервер/PostgreSQL ставятся централизованно через Compose, не на каждом ПК. Установка per-machine, общий ярлык, прежний UpgradeCode, новая версия 1.1.0. Upgrade сохраняет config; полное удаление запускает прежнюю очистку профилей. Фактическое обновление/GPO/roaming profiles требуют пилота.

## Проверки

```powershell
.\LitleHelperServer\tests\run.ps1 -Dotnet "$PWD\LitleHelperClient\.tools\dotnet\dotnet.exe"
```

Настоящий локальный Kestrel/SQLite, отдельная БД artifacts/test-*, имитаторы агента и GLPI. Реальные команды на чужих ПК/HelpDesk-заявки не отправляются. Проверяются роли, обязательный пароль, ключи, инвентарь, доставка/аудит, обновление меню и собственные тикеты. Клиентские тесты выполняют только echo/Write-Output, чтение WMI/реестра и WPF hit-testing.

Источники: [SignalR authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-8.0), [SignalR .NET client](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-8.0), [Npgsql](https://www.npgsql.org/efcore/). Результаты — `../VERIFICATION.md`.

Для подключения Windows-клиента получите ключ ПК в панели и укажите http://172.16.16.61:5000 в настройках или в MSI SERVERURL. SSH-ключ доступа к GitHub/серверу, JWT-ключ панели и ключ регистрации ПК имеют разные назначения: SSH-ключ не нужно передавать клиенту или контейнеру.
