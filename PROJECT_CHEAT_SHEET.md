# 🤖 Pixel Little Helper — База знаний и шпаргалка разработчика (Developer Cheat Sheet)

> **Назначение файла**: Этот справочник создан как единая точка входа для быстрого погружения в архитектуру, исходный код, подсистемы, интеграции и типичные сценарии решения проблем и доработок в проекте **Pixel Little Helper**.

---

## ⚡ 1. Экспресс-справочник (Quick Reference)

### 📌 Репозиторий и ключевые пути
| Компонент | Путь | Назначение |
|---|---|---|
| **Клиент (WPF Desktop)** | `LitleHelperClient/` | Десктоп-ассистент, робот Пиксель, почта, чат, Anti-Theft, AV |
| **Сервер (ASP.NET Core)** | `LitleHelperServer/` | Minimal APIs, SignalR Hubs, EF Core SQLite/Postgres, Web-панель |
| **Веб-панель (Frontend)** | `LitleHelperServer/wwwroot/` | `index.html`, `app.js`, `styles.css` (Vanilla JS, без бандлеров) |
| **Инсталлятор WiX** | `LitleHelperClient/installer/` | `Product.wxs`, `Harvest.xsl`, скрипт `tools/build.ps1` |
| **Расширение браузера** | `LitleHelperClient/extension/` | `WebGuard` (Chrome/Edge Native Messaging) |
| **Интеграция GLPI** | `glpi-plugin/` | Плагин обратной интеграции тикетов и оценок с GLPI |
| **Тесты** | `LitleHelperServer/tests/`, `tests/` | Node.js тесты панели, опросов, форматирования сообщений |
| **Хранилище данных клиента** | `%APPDATA%\PixelHelper\` | `settings.json`, `MailStore/mail_index.json`, `Quarantine/` |

### 🛠️ Быстрые команды терминала
```powershell
# 1. Сборка клиента (Debug)
dotnet build LitleHelperClient\PixelHelper.csproj

# 2. Сборка сервера (Debug)
dotnet build LitleHelperServer\LitleHelperServer.csproj

# 3. Полная компиляция инсталлятора MSI (без отправки на автообновление!)
powershell -ExecutionPolicy Bypass -File LitleHelperClient\tools\build.ps1
# Результат MSI: LitleHelperClient\artifacts\release-X.X.XX\PixelHelper.msi

# 4. Запуск тестов веб-панели и настроек
node LitleHelperServer\tests\settings-panel.cjs

# 5. Проверка состояния почтового индекса пользователя
powershell -NoProfile -Command "& { `$p = [System.Environment]::GetFolderPath('ApplicationData') + '\PixelHelper\MailStore\mail_index.json'; if (Test-Path `$p) { `$raw = Get-Content `$p -Raw | ConvertFrom-Json; Write-Host ('Всего: ' + `$raw.Count); Write-Host ('Непрочитанных исходящих: ' + (`$raw | Where-Object { `$_.Folder -eq 1 -and -not `$_.IsRead }).Count) } }"
```

### 🌐 Сетевые реквизиты и серверная инфраструктура
* **Сервер Помощника**: `ssh zqrey@helper` (Linux/Docker, ASP.NET Core)
* **Сервер GLPI Helpdesk**: `ssh zqrey@glpi` (GLPI 10.x, PHP, MySQL)
* **Внешний порт для удаленных клиентов / ноутбуков вне LAN**: `83` (через Nginx Reverse Proxy в Proxmox / MikroTik)
* **WireGuard VPN**: сеть филиалов и удаленных устройств через MikroTik; клиент использует криптографию Curve25519 (`WireGuardCrypto.cs`).
* **LiveKit WebRTC Server**: порт `7880` (голосовые и видеозвонки в корпоративном мессенджере).

---

## 🏛️ 2. Архитектура LitleHelperServer (Бэкенд и Веб-панель)

### 2.1 Стек и Точка входа (`Program.cs`)
* **ASP.NET Core 8 Minimal APIs** + **SignalR**
* **База данных**: EF Core (`HelperDb` в `Models.cs`). Поддерживает как SQLite (`helper.db`), так и PostgreSQL через переменную окружения `DATABASE_URL`.
* **Аутентификация и роли**:
  * Роли: `SuperAdmin`, `Admin`, `Operator`, `Psychologist`, `User`.
  * Логика проверки прав: `Access.cs` (`Effective(PanelUser user)`).
  * Поддержка гибридного входа: **Local** (PBKDF2/SHA256) + **Active Directory / LDAP** (`MessengerWindowsAuthentication.cs` / `PanelApi.cs`).

### 2.2 SignalR Hubs
1. **`/helperHub` (`HelperHub.cs`)**:
   * Подключение десктопных клиентов `LitleHelperClient`.
   * Регистрация ПК: метод `Register(ComputerInfo info)`.
   * Периодический Heartbeat: метод `Status(Computer c)`.
   * Отправка динамических кнопок действий (`ActionButton`).
   * Трансляция экстренных сигналов тревоги (Код Синий / Красный).
2. **`/chatHub` (`Messenger.cs`)**:
   * Корпоративный чат (P2P и группы).
   * Статусы присутствия (онлайн, печатает).
   * Опросы (`ChatPoll`), реакции на сообщения (`ChatReaction`).

### 2.3 Веб-панель (`wwwroot/app.js` и `styles.css`)
* **Архитектурный принцип**: Чистый нативный JavaScript (ES6+), отсутствие сборщиков типа Webpack/Vite. Любая правка в `app.js` или `styles.css` сразу применяется на сервере.
* **Экран авторизации**:
  * Единое поле ввода логина и пароля: бэкенд в `/api/auth/login` сам сначала проверяет локальную базу, затем пытается авторизовать через Active Directory LDAP.
  * **SVG-голова робота Пикселя**:
    * Слежение за курсором мыши (зрачки `#robot-pupil-*` и рот `#robot-mouth` рассчитывают дельту от центра синего блока).
    * Фокус на поле пароля: робот закрывает веки (`#robot-eyelid-*`), чтобы «не подсматривать».
    * 5 неверных паролей подряд: триггер анимации ярости (уменьшение, разгон, удар о монитор, появление трещин стекла `.crack-overlay` на 60 сек, вращение головы от головокружения, постепенное заживление трещин и ребут).
* **Разделение устройств**:
  * Обычные ПК (`IsPortable = false`) vs Переносные устройства / ноутбуки (`IsPortable = true`).
  * **Карточка переносного устройства**:
    * Отображение уровня заряда батареи, статус питания.
    * Отображение текущей и истории Wi-Fi сетей (SSID, BSSID).
    * Геолокация (координаты, обратный геокодинг адреса).
    * **Комплексная удаленная блокировка (Pixel Anti-Theft)**: выбор любого из 3 уровней по отдельности или всех 3 сразу («🔥 Все 3 уровня сразу», «Только экран», «Только сеанс», «Только BitLocker»).

---

## 💻 3. Архитектура LitleHelperClient (Десктоп-ассистент)

### 3.1 Жизненный цикл (`App.xaml.cs`)
1. **Single Instance**: захват глобального Mutex `PixelHelper_SingleInstance`.
2. **Флаги запуска**:
   * `--service`: тихий запуск в фоне (подсистема Anti-Theft / системный сервис).
   * Обычный запуск: инициализация плавающего окна робота (`PetWindow`), трей-иконки (`TrayIcon.cs`), фоновых воркеров.
3. **Проверка регистрации без AD (`NonAdEnrollmentWindow.cs`)**:
   * Если ПК не в домене Active Directory и в `settings.json` отсутствует подтвержденная привязка, показывается модальное окно поверх всех окон, требующее указать ФИО сотрудника и инвентарный номер (имя ПК) перед началом работы.

### 3.2 Плавающий робот-ассистент (`PetWindow.xaml` / `PetWindow.cs`)
* Спрайтовый интерактивный персонаж на рабочем столе.
* Поддерживает скины (`ISkinManager.cs`, стеклянный морфизм, стелс-неон и др.).
* Динамическое колесо кнопок (`ButtonLayoutEngine.cs`): кнопки приходят из сервера по ролям/отделам пользователя.
* Реакции на действия: клики, перетаскивание, эмоции (`Dance`, `Twirl`, `Offended`), звуковое сопровождение (`SoundManager.cs`).

### 3.3 Подсистема Pixel Anti-Theft (`AntiTheftService.cs`)
* **Сбор телеметрии**:
  * Опрос Wi-Fi сетей в окружении через Win32 `wlanapi.dll` (`SystemInspector.GetVisibleWifiNetworks()`).
  * Заряд батареи через Win32 `GetSystemPowerStatus`.
  * Отправка на сервер: `POST /api/telemetry/anti-theft`.
* **3 Уровня реагирования на команду розыска**:
  1. `response.LockScreen` -> вызов `AntiTheftLockWindow.EnsureShown(response.LockMessage)`. Полноэкранный TopMost экран без рамки, блокирует клавиатуру и мышь, перехватывает Alt+Tab/Win, сообщает контакты IT для возврата ноутбука.
  2. `response.LockSession` -> вызов Win32 `NativeMethods.LockWorkStation()` (мгновенная блокировка сеанса пользователя).
  3. `response.TriggerBitLockerRecovery` -> выполнение системной команды `manage-bde -forcerecovery C:` (перевод системного диска в режим ввода 48-значного ключа восстановления при следующей перезагрузке).

### 3.4 Почтовый клиент (`Mail*` стек)
* **Архитектура классов**:
  * `MailProtocolClient.cs` — реализация сокетов IMAP4rev1, POP3, SMTP с поддержкой TLS/SSL и STARTTLS.
  * `MailStorageEngine.cs` — движок базы писем: шифрование EML и вложений на диске через DPAPI/AES-256 (`MailCryptoService.cs`), ведение индекса `mail_index.json`.
  * `MailSecurityScanner.cs` — антивирусная проверка вложений (ClamAV / YARA / эвристика) и антифишинг ссылок.
  * `MailWindow.xaml` / `MailComposeWindow.xaml` — интерфейс чтения, поиска, фильтрации и создания писем.
* **Правила синхронизации прочтения (Seen Status)**:
  * Письма в папке `Sent` **всегда** имеют `IsRead = true`.
  * При синхронизации IMAP выполняется `UID SEARCH SEEN` и парсинг флагов `(FLAGS BODY.PEEK[])`.
  * Если письмо прочитано на сервере или в стороннем клиенте, статус обновляется в `mail_index.json`.
  * Если пользователь читает письмо в PixelHelper, в фоне отправляется `UID STORE {uid} +FLAGS (\Seen)`.
  * Локальный статус прочтения существующих писем не перезаписывается в `false` при фоновом обновлении.

### 3.5 Корпоративный мессенджер (`Messenger*` стек)
* `MessengerWindow.cs` — двухпанельный интерфейс диалогов и групповых чатов.
* Форматирование текста: Markdown, код, спойлеры, цитаты (`MessageFormatting.cs`).
* Аудио- и видеоконференции: интеграция с LiveKit WebRTC (`VideoCallWindow.cs`, `LiveKitService.cs`).
* Опросы, реакции-эмодзи, отправка зашифрованных вложений через сервер.

### 3.6 Безопасность и WebGuard
* `WebGuard.cs` + Chrome/Edge Native Messaging Host (`NativeMessagingHost.cs`).
* Перехват открываемых URL, сопоставление со списком фишинговых и запрещенных доменов.
* Автоматическая генерация файлов групповых политик (GPO) для развертывания расширения в домене (`WebGuardGpoGenerator.cs`).
* Встроенный карантин для вредоносных файлов (`QuarantineManager.cs`).

---

## 🔄 4. Интеграции и взаимодействие с внешними системами

### 4.1 GLPI Helpdesk
* Сервер общается с REST API GLPI (`glpi-plugin` и `IntegrationApi.cs`).
* Двусторонняя синхронизация: создание тикетов пользователем из виджета помощника -> регистрация в GLPI -> назначение специалиста -> закрытие -> опрос удовлетворенности пользователя (звездочки и отзыв).
* Оценки пользователей интегрированы в карточки специалистов и общую статистику.

### 4.2 Telegram Bot (`TelegramTicketManagement.cs`)
* Инструмент для выездных инженеров и системных администраторов.
* Команды:
  * `/my` — список активных заявок, назначенных на инженера.
  * `/take <id>` — взять тикет в работу.
  * `/close <id>` — закрыть тикет с комментарием решения.
* *Важно*: перед выводом информации о заявке бот всегда валидирует ее актуальность в GLPI/БД.

### 4.3 MikroTik и WireGuard
* Для ноутбуков вне корпоративной сети настраивается туннель WireGuard.
* `WireGuardCrypto.cs` генерирует пару ключей Curve25519 (Private/Public) в клиенте, позволяя передавать публичный ключ на MikroTik и заводить ноутбук в домен через VPN.

---

## ⚠️ 5. Подводные камни и частые ошибки (Trap & Bug Prevention)

| Область | Опасность / Подводный камень | Как делать правильно |
|---|---|---|
| **Автообновления** | Случайная отправка сборки в прод для сотен машин | **Никогда** не выкладывать файлы обновления на сервер без явной команды пользователя. Собирать только локально через `build.ps1`. |
| **Почтовый индекс** | Сброс статуса прочитанных писем | При вызове `IngestRawMimeAsync` всегда проверять `existing.IsRead`, не затирать `true` на `false`. Папку `Sent` всегда помечать `IsRead = true`. |
| **Сигнатура `IngestRawMimeAsync`** | Сдвиг параметров `folder`, `isSeen`, `token` | Вызывать с именованными аргументами `token: token`, чтобы компилятор не путал `CancellationToken` и `bool? isSeen`. |
| **WiX сборка MSI** | Ошибки версий и блокировка файлов | При изменении версии в `PixelHelper.csproj` следить, чтобы в `installer/Product.wxs` не возникало конфликта `UpgradeCode`. Не держать запущенным `PixelHelper.exe` во время сборки. |
| **C# lock (Monitor)** | Дедлоки при вложенных вызовах методов хранилища | В C# `lock (gate)` является реентерабельным для одного потока. Однако при асинхронных вызовах (`await`) внутри `lock` использовать нельзя — для асинхронности применять `SemaphoreSlim`. |
| **Шифрование DPAPI** | Попытка расшифровать данные другого пользователя Windows | DPAPI привязан к текущему пользователю ОС (`CurrentUser`). Если служба работает под `SYSTEM`, она не сможет прочитать DPAPI пользователя. Для общих данных используется `LocalMachine` или общий ключ. |
| **DOM & CSS Веб-панели** | Рассинхронизация селекторов в `app.js` и `styles.css` | Вся анимация робота (глаза, зрачки, веки, рот, трещины) завязана на точные ID SVG элементов: `#robot-head`, `#robot-eye-left`, `#robot-pupil-left`, `#robot-mouth`, `#crack-canvas`. |

---

## 📋 6. Чек-лист проверки перед сдачей задач

Перед тем как сообщать пользователю об успешном завершении доработки:
1. [ ] **Клиент компилируется без ошибок**: `dotnet build LitleHelperClient\PixelHelper.csproj` (0 ошибок).
2. [ ] **Сервер компилируется без ошибок**: `dotnet build LitleHelperServer\LitleHelperServer.csproj` (0 ошибок).
3. [ ] **UI тесты веб-панели проходят**: `node LitleHelperServer\tests\settings-panel.cjs`.
4. [ ] **Сохранность пользовательских данных**: изменения не ломают существующие JSON файлы конфигов и базы почты.
5. [ ] **Локальная сборка (если требовался билд)**: инсталлятор лежит в `LitleHelperClient\artifacts\release-X.X.XX\PixelHelper.msi`.
6. [ ] **Финальный отчет**: краткий, структурированный, на русском языке, со ссылками на измененные файлы.

