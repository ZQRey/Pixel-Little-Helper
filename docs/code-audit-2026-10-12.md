# Аудит интерактивного помощника — 12.10.2026

Проверены клиент 1.2.44, локальные серверные исходники и свежий снимок рабочего сервера, полученный по SSH. Изменения в прикладной код и рабочие сервисы не вносились; созданы этот отчёт и диагностические фикстуры.

Выделено 20 замечаний: P1 — 9, P2 — 10, P3 — 1. P1 требует первоочередного исправления; P2 влияет на работу/интеграции; P3 — упрощение и сопровождение. Условные риски и локальные, ещё не развёрнутые модули отмечены отдельно.

## Что подтверждено воспроизведением

- Анонимные POST принятия/сброса Белого кода: HTTP 200 на отдельном сервере с SQLite и отключёнными интеграциями.
- Подтверждение специалиста с клиентским набором аргументов: ошибка настоящего SignalR.
- Сброс тревоги внутри хаба достигает AllAgents/PanelStaff без проверки контекста пользователя.
- Конкурентная запись настроек: 52 IOException из 80 вызовов в отдельной временной папке.
- На рабочем сервере отсутствует модуль базы знаний, который вызывает клиент.

## Замечания

### F01. P1 — Сброс пароля произвольного пользователя AD без специального права

Область: Рабочий сервер и локальный код.

POST /api/ad/change-password требует только любую сессию Bearer либо Agent. Логин берётся из тела запроса; не проверяются права на сброс, принадлежность учётной записи инициатору и текущий пароль. AdPasswordService выполняет Replace unicodePwd от сервисной записи. Если этой записи предоставлено право Reset Password, обычный пользователь панели или агент сможет сбрасывать чужие пароли. На рабочем AD сброс не проверялся; права сервисной записи на Reset Password не исследовались.

Проверка: Подтверждено анализом кода

Код: [IntegrationApi.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/IntegrationApi.cs:83), [AdPasswordService.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/AdPasswordService.cs:87).

Рекомендация: Разделить самостоятельную смену с подтверждением пользователя/старого пароля и административный сброс с отдельным правом, аудитом и ограничением частоты.

### F02. P1 — Белый код можно принять и сбросить без входа

Область: Рабочий сервер и локальный код.

POST /api/guard/white-ack и /api/guard/white-reset не требуют авторизации. Доступный по сети клиент может снять тревогу охраны. Публичный просмотр экрана можно оставить, но публичное изменение состояния — отдельная проблема.

Проверка: Воспроизведено на изолированном HTTP-сервере: оба POST без токена вернули 200.

Код: [IntegrationApi.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/IntegrationApi.cs:325), [IntegrationApi.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/IntegrationApi.cs:327).

Рекомендация: Защитить изменение состояния правом охраны/дежурного, добавить аудит и защиту браузерных запросов.

### F03. P1 — Любой авторизованный клиент может объявить глобальный отбой

Область: Рабочий сервер и клиент.

HelperHub.CancelEmergencyAlert не проверяет Agent(), роль или право. Класс хаба допускает Bearer и Agent: обычный зарегистрированный агент либо пользователь панели может вызвать метод. Отбой отправляется AllAgents всех филиалов. При этом состояние GuardWhiteService и активное вещание AudioRONGTA не сбрасываются, поэтому разные системы показывают разные состояния.

Проверка: Изолированная фикстура подтвердила рассылку AllAgents/PanelStaff без обращения к контексту и правам.

Код: [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:386), [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:1474).

Рекомендация: Отделить локальное закрытие окна от административного отбоя. Серверный отбой привязать к событию/филиалу и правам; согласованно сбрасывать охрану и внешнюю систему.

### F04. P1 — Матрица запуска кодов доверяет заявленному агентом логину

Область: Рабочий сервер.

RegisterComputer принимает info.UserName и записывает его в CurrentUser. GetEmergencyCodes выбирает роль по этому значению. Аутентификация Agent подтверждает компьютер, но не Windows-пользователя. Владелец ключа агента может заявить имя администратора и получить его набор разрешённых кодов. Аналогичное доверие влияет на адресные группы и автора заявок.

Проверка: Подтверждено анализом кода

Код: [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:80), [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:197), [Security.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/Security.cs:47).

Рекомендация: Привязывать пользователя к проверенной доменной/мессенджерной сессии. Самостоятельно сообщённое имя использовать как инвентаризационные данные, а не основание для прав.

### F05. P1 — Подтверждение вызова специалиста нарушает контракт SignalR

Область: Рабочий клиент и сервер.

Клиент отправляет два аргумента AcknowledgeSpecialistCall(callId, accepted), сервер ожидает три, включая reason без значения по умолчанию. Метод отклоняется ещё до выполнения. Дополнительно SPECIALIST_CALL получает случайный CallId вместо ID записи AudioRONGTA: шлюз alert запускает диспетчер вещания, но не создаёт specialist_call. Даже после исправления аргументов ack может получить 404. accepted=false также отправляется во внешний endpoint ack, фактически означающий принятие.

Проверка: Воспроизведено через настоящий SignalR на изолированном сервере: клиентский вызов с двумя аргументами получил HubException.

Код: [HubConnectionService.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/HubConnectionService.cs:272), [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:361), [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:313).

Рекомендация: Согласовать DTO/аргументы, создавать внешний вызов через API специалистов и сохранять полученный ID. Разделить принятие и отказ; показывать результат подтверждения пользователю.

### F06. P1 — Ошибка внешней доставки скрывается за успешным результатом

Область: Рабочий сервер.

В TriggerEmergencyAlert и REST alert ошибки AudioRONGTA поглощаются пустым catch. Аудит уже записан как Completed, клиент/панель получает успех, хотя внешний сервис мог вернуть 403, 500 или тайм-аут. Аналогично скрываются ошибки ack и cancel. EmergencySyncWorker помечает lastSeenAlarmId/lastSeenCallId до доставки: после ошибки тот же активный вызов не повторяется.

Проверка: Подтверждено анализом кода

Код: [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:344), [IntegrationApi.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/IntegrationApi.cs:315), [EmergencySyncWorker.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/EmergencySyncWorker.cs:62).

Рекомендация: Хранить результаты доставки по каналам, добавить очередь/повторы и идемпотентность. Помечать входящее событие обработанным после успешной доставки. Успех локального экрана отличать от подтверждённой внешней доставки.

### F07. P1 — Окно тревоги может открыться снова после отбоя

Область: Рабочий клиент.

Для Красного/Чёрного/Оранжевого/Розового кода создаётся локальный preTimer на 3,5 секунды. Он не сохраняется в поле и не отменяется ни локальным, ни входящим отбоем. Отбой в этот интервал закрывает текущий экран, после чего таймер всё равно открывает EmergencyAlertWindow и запускает речь. Последовательные тревоги также оставляют несколько конкурирующих таймеров.

Проверка: Подтверждено анализом кода

Код: [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:1411), [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:1463), [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:1482).

Рекомендация: Использовать один управляемый таймер/CTS и ID поколения события. Отменять отложенный показ при отбое, новой тревоге и закрытии приложения.

### F08. P2 — В обратной синхронизации путаются ID отделений

Область: Рабочий сервер.

AudioRONGTA сопоставляет логические ID Helper с собственными ID и возвращает departmentMap. Helper эту карту не сохраняет. Worker затем ищет входящий department_id среди em.Departments по исходному Id. После сопоставления по имени или создания нового отделения номера могут отличаться: выбирается «Дежурная служба» и нужные получатели не находятся.

Проверка: Подтверждено анализом кода

Код: [EmergencyDirectorySync.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/EmergencyDirectorySync.cs:61), [EmergencySyncWorker.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/EmergencySyncWorker.cs:91).

Рекомендация: Сохранить двустороннюю карту ID либо использовать стабильный внешний ключ отделения. Проверить сценарий, где исходный ID 10 сопоставлен внешнему ID 3.

### F09. P2 — Смена пароля из клиента не проходит аутентификацию агента

Область: Рабочий клиент и сервер.

ApiClient.ChangeAdPasswordAsync отправляет X-Client-Key, но не X-Machine-Name. AgentAuthenticationHandler требует оба заголовка. Bearer клиент также не отправляет. Поэтому штатный вызов из CompanionChatWindow отклоняется, независимо от корректности введённого пароля. Исправлять заголовок отдельно от F01 опасно: сначала нужна корректная авторизация самой операции.

Проверка: Подтверждено анализом кода

Код: [ApiClient.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/ApiClient.cs:74), [Security.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/Security.cs:47).

Рекомендация: После исправления F01 использовать единый механизм клиентской аутентификации и обрабатывать HTTP-статус/ошибку API явно.

### F10. P2 — Выпущенный клиент и рабочий сервер расходятся по базе знаний

Область: Рабочая версия 1.2.44.

В клиенте есть меню базы знаний и вызов GetKnowledgeArticles. В полученном с рабочего сервера снимке нет GetKnowledgeArticles/GetKnowledgeArticle и KnowledgeBase.cs. Локально модуль есть. KnowledgeWindow скрывает ошибку и показывает встроенные статьи, поэтому пользователю кажется, что серверная база работает, хотя обновления статей не поступают.

Проверка: Подтверждено чтением текущих исходников рабочего сервера по SSH; встроенный fallback проверен по клиентскому коду.

Код: [KnowledgeWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/KnowledgeWindow.cs:235), [KnowledgeWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/KnowledgeWindow.cs:242), [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/scratch/audit-2026-10-12/live-server/HelperHub.cs:206).

Рекомендация: Ввести проверку возможностей/версии сервера и согласованный состав релиза. Развернуть проверенный модуль либо явно обозначать автономную базу и скрывать неподдерживаемые функции.

### F11. P2 — Сохранение настроек не защищено от параллельной записи

Область: Рабочий клиент.

AtomicWrite всегда использует один path.tmp и не синхронизирует записи. Save вызывается как из интерфейса, так и из обработчика SignalR медицинских статусов. Параллельные записи конфликтуют на File.WriteAllText/File.Move; часть сохранений теряется либо возвращает IOException.

Проверка: В отдельной временной папке 52 из 80 конкурентных вызовов AtomicWrite завершились IOException.

Код: [Settings.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/Settings.cs:92), [HubConnectionService.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/HubConnectionService.cs:136).

Рекомендация: Сериализовать запись для каждого файла, использовать уникальный временный файл и согласованный снимок настроек. Перед UI-изменениями переводить события на Dispatcher.

### F12. P1 — Неопределённый филиал превращает массовый вызов в общую рассылку

Область: Рабочий сервер.

EmergencyRecipients.Deliver выбирает AllAgents, если branchId отсутствует, даже при BroadcastAllBranches=false. Вызов из ПК без привязки/совпадения префикса поэтому уходит всем филиалам. REST-маршрут часть этого сценария уже отклоняет, но SignalR-путь и входящие события Worker ведут себя иначе.

Проверка: Подтверждено анализом кода

Код: [EmergencyRecipients.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/EmergencyRecipients.cs:77), [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:317).

Рекомендация: При неизвестном филиале явно отказывать либо требовать осознанное разрешение общей рассылки. Унифицировать правила для REST, SignalR и Worker.

### F13. P2 — Редактирование кабинета/филиала в панели не обновляет все связи

Область: Рабочий сервер и клиент.

PUT computers сохраняет новые Room/BranchId, но не вызывает EmergencyDirectorySync.Request и не переносит активное соединение между BranchAgents. ПК может до переподключения получать массовые тревоги старого филиала; новое место не отправится в AudioRONGTA до другого события. Очистка Room отправляется, но HandleRoomUpdated игнорирует пустую строку, оставляя старый кабинет в клиенте. Панель и UpdateClientRoom допускают 100 символов, а тревоги/заявки требуют максимум 50.

Проверка: Подтверждено анализом кода

Код: [PanelApi.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/PanelApi.cs:106), [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:411), [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:146).

Рекомендация: Свести изменение местоположения в один сервис: единая валидация, перенос групп, очередь синхронизации и передача в клиент в том числе пустого значения.

### F14. P1 — Клиентские тесты могут удалить реальные данные рабочей станции

Область: Тестовый набор клиента.

Program.cs напрямую вызывает PrinterWatchdog.ClearPrintQueue и DiskCleaner.CleanAsync. Они очищают настоящие задания печати, пытаются остановить Spooler и удаляют файлы из реальных TEMP/кэшей. Settings.TestFolder меняет папку настроек, но не эти операции. Такие тесты нельзя считать изолированными и безопасно запускать на рабочем ПК.

Проверка: Подтверждено чтением кода. При текущем аудите полный клиентский тестовый набор не запускался.

Код: [Program.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/tests/Program.cs:996), [Program.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/tests/Program.cs:1004), [PrinterWatchdog.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PrinterWatchdog.cs:160), [DiskCleaner.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/DiskCleaner.cs:18).

Рекомендация: Заменить системные операции адаптерами/фикстурами. В основном наборе применять только временные каталоги и имитацию очереди печати; натурные проверки запускать отдельно и явно.

### F15. P2 — Поиск заявителя GLPI использует неверную семантику equals

Область: Рабочий сервер; подтверждено для GLPI 10.

CreateTicketAsync передаёт строковый логин в search/User, поле 1, searchtype=equals. В GLPI 10 это поле имеет datatype=itemlink, а equals сравнивает ID записи. Для обычного логина поиск может не найти пользователя и перейти к ServiceUserId, записывая заявку на служебного заявителя. Для техникa в этом же файле уже применяется anchored contains и проверка единственного результата. Тестовый mock возвращает alice независимо от searchtype/value, поэтому не выявляет ошибку.

Проверка: Семантика подтверждена официальным кодом GLPI 10: [User.php](https://github.com/glpi-project/glpi/blob/10.0/bugfixes/src/User.php#L3525), [Search.php](https://github.com/glpi-project/glpi/blob/10.0/bugfixes/src/Search.php#L4886). Версия и результаты запроса рабочей GLPI в этом аудите не проверялись.

Код: [GlpiService.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/GlpiService.cs:90), [GlpiService.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/GlpiService.cs:163), [Program.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/tests/Program.cs:98).

Рекомендация: Использовать проверенный точный поиск логина и проверку количества/активности результатов. На рабочем GLPI сверить listSearchOptions/User и проверить requester без создания реальных заявок.

### F16. P2 — Отбой и окончание внешней тревоги не останавливают речь согласованно

Область: Рабочий клиент и сервер.

Входящий HandleEmergencyAlertCanceled закрывает окно, но не вызывает TtsNotifier.CancelEmergency. Worker при исчезновении active_broadcast только очищает lastSeenAlarmId и не рассылает отбой. Сама CancelEmergency меняет поколение очереди, но не прерывает уже запущенный синхронный SAPI Speak или PlaySync; речь может продолжиться после закрытия экрана.

Проверка: Подтверждено анализом кода

Код: [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:1482), [TtsNotifier.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/TtsNotifier.cs:51), [EmergencySyncWorker.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/EmergencySyncWorker.cs:14).

Рекомендация: Единый отменяемый цикл тревоги: сопоставленный ID, отбой по внешнему завершению, отмена таймеров, очереди и текущего звука/речи.

### F17. P2 — Диагностика принтеров может задерживать интерфейс и переживать закрытие окна

Область: Рабочий клиент.

Очистка очереди вызывается синхронно из WPF-обработчиков; внутри WMI, PrintQueue и несколько WaitForExit(5000). Это задерживает интерфейс. PrinterWatchdog/CertificateWatchdog создаются локальными переменными в Loaded, стартуют через Task.Run без отмены, Stop не вызывается в Closed. Старт после остановки/закрытия и оставшиеся DispatcherTimer требуют явного управления.

Проверка: Подтверждено анализом кода

Код: [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:237), [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:211), [PrinterWatchdog.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PrinterWatchdog.cs:24), [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:314).

Рекомендация: Хранить службы в полях, внедрить IDisposable/CTS и ждать безопасного завершения. Выполнять WMI/очистку вне UI с ограниченными тайм-аутами и подтверждением области очистки.

### F18. P2 — Ограничения статей по филиалу/роли не обеспечены сервером

Область: Только локальный модуль KnowledgeBase, пока не развёрнут.

Модель хранит BranchId и TargetRole, но методы хаба возвращают статьи без фильтра по текущему пользователю. GET API допускает анонимное чтение, BranchId необязателен и передаётся клиентом, TargetRole не учитывается. Если администратор добавит ограниченную инструкцию, эти метаданные не обеспечат её конфиденциальность.

Проверка: Подтверждено анализом кода

Код: [HelperHub.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/HelperHub.cs:214), [KnowledgeBase.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/KnowledgeBase.cs:126).

Рекомендация: До развёртывания определить модель доступа: публичные статьи отдельно, закрытые — фильтровать по проверенной роли/филиалу во всех list/get/search API.

### F19. P2 — Интеграция картриджей работает без аутентификации при пустом ключе

Область: Рабочий сервер; условный риск конфигурации.

Маршрут ready помечен AllowAnonymous, проверка выполняется только при непустом ApiKey. CartridgeOptions по умолчанию Enabled=true и ключ пустой. При свежей установке либо очистке ключа доступный по сети клиент сможет отправлять поддельные уведомления сотрудникам. Фактический ключ рабочего сервиса в рамках этого аудита не проверялся.

Проверка: Подтверждено анализом кода

Код: [IntegrationApi.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/IntegrationApi.cs:103), [IntegrationSettings.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/IntegrationSettings.cs:15).

Рекомендация: Запретить включение без ключа и использовать fail-closed проверку. Ввести лимит размера/числа уведомлений, dispose JsonDocument и аудит источника.

### F20. P3 — Устаревшие ветки и дублирование усложняют поддержку

Область: Локальный и рабочий код.

GetActionsAsync/ReadCache/SendTicketAsync обслуживают старые /api/v1/assistant/...; вызовов в приложении не найдено, они используются тестами. В SSE создаётся TaskCompletionSource, результата которого никто не ждёт. PetWindow содержит повторяющиеся блоки создания заявок и почти 2000 строк разнородной логики; аварийные сценарии повторены в Hub/REST/Worker. Есть встроенный общий fallback-ключ для heartbeat/cancel, несмотря на отдельную настройку ключа.

Проверка: Подтверждено анализом кода

Код: [ApiClient.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/ApiClient.cs:50), [IntegrationApi.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/IntegrationApi.cs:344), [EmergencySyncWorker.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperServer/EmergencySyncWorker.cs:38), [PetWindow.cs](C:/Users/zqrey3/Documents/Codex/LitleHelper/LitleHelperClient/PetWindow.cs:1653).

Рекомендация: Удалять только подтверждённо неиспользуемые ветки после обновления тестов. Вынести TicketPresenter, EmergencyCoordinator и единый EmergencyService; убрать общий fallback-секрет и явно сигнализировать отсутствие настройки.

## Что сделано правильно

Проверка подписи манифеста и SHA256 MSI, ограничение размера, запрет понижения версии и ACL каталога обновлений реализованы. Для обычных заявок Telegram используется сохраняемая очередь. Есть проверки прав API и версии токена, отдельный ключ агента, защита доменных паролей и тесты адресной доставки.

По проверке NuGet (включая транзитивные зависимости, источник api.nuget.org) известных уязвимых пакетов для клиента и сервера не обнаружено на момент проверки. Это не отменяет перечисленных ошибок прикладной логики.

Предупреждения EF1002 сами по себе не признаны SQL-инъекциями: просмотренные миграционные строки используют фиксированные имена/типы. Массовые пустые catch требуют разборки по назначению: в журналировании рабочего стола они могут быть оправданы, при доставке тревог — скрывают потерю операции.

## Приоритет исправлений

1. Закрыть произвольный сброс AD-паролей, публичное изменение состояния охраны и глобальный отбой без прав.
2. Исправить контракт специалиста, создание/сопоставление ID и результат доставки внешним системам.
3. Отменять отложенный показ и речь; исключить широковещательный fallback неизвестного филиала.
4. Изолировать системные действия тестов и устранить гонку сохранения настроек.
5. Согласовать состав релиза клиента/сервера, местоположение/группы и поиск заявителя GLPI.
6. После регрессий упростить дублирующийся код.

## Ограничения аудита

Это анализ и целевые воспроизведения, а не исчерпывающий пентест. Реальные пароли AD не менялись, тревоги/Telegram-сообщения не запускались, очередь печати и TEMP не очищались. Полный клиентский тестовый набор не запускался из-за F14. Рабочую GLPI не опрашивал для проверки requester. Не исследованы права Reset Password сервисной записи AD, физическое воспроизведение на усилителях и поведение на всех версиях Windows.

Диагностические материалы: [воспроизведение записи/сброса](C:/Users/zqrey3/Documents/Codex/LitleHelper/scratch/audit-2026-10-12/probe.log:2), [HTTP и SignalR](C:/Users/zqrey3/Documents/Codex/LitleHelper/scratch/audit-2026-10-12/http-probe.log:1), [зависимости клиента](C:/Users/zqrey3/Documents/Codex/LitleHelper/scratch/audit-2026-10-12/client-packages.log:2), [зависимости сервера](C:/Users/zqrey3/Documents/Codex/LitleHelper/scratch/audit-2026-10-12/server-packages.log:2).
