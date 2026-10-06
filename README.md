# LitleHelper 1.1

* `LitleHelperClient` — WPF-помощник: SignalR, инвентаризация, команды, GLPI и фиксированный офлайн-набор.
* `LitleHelperServer` — ASP.NET Core 8, EF Core, PostgreSQL/SQLite и веб-панель с четырьмя ролями.

Начните с [инструкции сервера](LitleHelperServer/README.md): запуск, GLPI, подключение ПК и MSI. [Инструкция клиента](LitleHelperClient/README.md) описывает настройки и ограничения.

Готовый MSI: `LitleHelperClient/artifacts/release-1.1.0/PixelHelper.msi`. Новая публикация размещена отдельно от старого запущенного помощника. Старый каталог `LitleHelperClient/artifacts/publish` не является новым релизом.

Результаты проверок — [VERIFICATION.md](VERIFICATION.md). `data/`, секреты `.env`, `bin/obj`, `.tools/` и `artifacts/` исключены из Git.
