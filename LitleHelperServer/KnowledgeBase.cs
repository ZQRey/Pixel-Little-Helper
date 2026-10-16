using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LitleHelperServer;

public static class KnowledgeBase
{
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        bool pg = db.Database.IsNpgsql();
        string timestamp = pg ? "timestamp with time zone" : "TEXT";

        string sql = "CREATE TABLE IF NOT EXISTS \"KnowledgeArticles\" (" +
                     "\"Id\" " + (pg ? "SERIAL PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT") + ", " +
                     "\"Title\" TEXT NOT NULL, " +
                     "\"Category\" TEXT NOT NULL DEFAULT 'General', " +
                     "\"ContentMarkdown\" TEXT NOT NULL DEFAULT '', " +
                     "\"Keywords\" TEXT NOT NULL DEFAULT '', " +
                     "\"BranchId\" INTEGER NULL, " +
                     "\"TargetRole\" TEXT NOT NULL DEFAULT 'All', " +
                     "\"ViewCount\" INTEGER NOT NULL DEFAULT 0, " +
                     "\"IsPinned\" " + (pg ? "BOOLEAN" : "INTEGER") + " NOT NULL DEFAULT " + (pg?"FALSE":"0") + ", " +
                     "\"CreatedAt\" " + timestamp + " NOT NULL, " +
                     "\"UpdatedAt\" " + timestamp + " NOT NULL)";

        await db.Database.ExecuteSqlRawAsync(sql);

        // Seed initial articles if empty
        if (!await db.KnowledgeArticles.AnyAsync())
        {
            db.KnowledgeArticles.AddRange(
                new KnowledgeArticle
                {
                    Title = "Запуск и решение проблем с NCALayer",
                    Category = "NCALayer",
                    Keywords = "ncalayer, эцп, подпись, 13579, websocket, браузер",
                    ContentMarkdown = "### Инструкция по работе с NCALayer\n\n" +
                                      "**NCALayer** необходим для подписания документов ЭЦП в системах DMED, ЕИСЗ и на порталах Республики Казахстан.\n\n" +
                                      "#### Быстрая диагностика:\n" +
                                      "1. Убедитесь, что в правом нижнем углу экрана (возле часов) видна иконка NCALayer.\n" +
                                      "2. Если иконки нет, запустите ярлык **NCALayer** с Рабочего стола.\n" +
                                      "3. Проверьте статус через меню Артёмки: **🩺 Экспресс-диагностика ПК**.\n\n" +
                                      "#### Если сайт пишет «NCALayer не запущен»:\n" +
                                      "- Перезапустите NCALayer (правой кнопкой на иконку -> Выход, затем снова запустите с Рабочего стола).\n" +
                                      "- Обновите страницу браузера комбинацией `Ctrl + F5`.\n" +
                                      "- Если проблема сохраняется, создайте заявку в ИТ через кнопку ниже.",
                    IsPinned = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new KnowledgeArticle
                {
                    Title = "Вход и работа с медицинской системой DMED",
                    Category = "DMED",
                    Keywords = "dmed, дмед, krg.dmed.kz, вход, пароль, прием, пациент",
                    ContentMarkdown = "### Работа с порталом DMED (krg.dmed.kz)\n\n" +
                                      "Портал **DMED** является основной медицинской информационной системой.\n\n" +
                                      "#### Как войти в систему:\n" +
                                      "1. Нажмите кнопку быстрого доступа **«Открыть DMED»** в меню Артёмки.\n" +
                                      "2. Введите ваш логин (ИИН) и пароль сотрудника.\n" +
                                      "3. При подписании приёма выберите сертификат ЭЦП (файл `RSA` / `GOST` или токен).\n\n" +
                                      "#### Частые вопросы:\n" +
                                      "- **Забыли пароль?** Обратитесь в техподдержку с указанием вашего ИИН и отделения.\n" +
                                      "- **Долгая загрузка страниц:** Воспользуйтесь очисткой кэша браузера в меню Артёмки: **🧹 Очистить временные файлы**.",
                    IsPinned = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new KnowledgeArticle
                {
                    Title = "Что делать при замятии бумаги или остановке печати",
                    Category = "Принтеры и картриджи",
                    Keywords = "принтер, печать, очередь, замятие, бумага, картридж, spooler",
                    ContentMarkdown = "### Инструкция при сбоях печати\n\n" +
                                      "Если принтер перестал печатать или документ «завис» в очереди:\n\n" +
                                      "#### Шаги решения:\n" +
                                      "1. **Проверьте лоток:** Есть ли бумага в лотке и плотно ли закрыты крышки принтера.\n" +
                                      "2. **Очистите очередь печати:** В меню Артёмки нажмите **🖨️ Очистить очередь печати** (система перезапустит службу печати и удалит зависшие задания).\n" +
                                      "3. **Перезагрузите принтер:** Выключите питание кнопкой на принтере на 5 секунд и включите снова.\n" +
                                      "4. Попробуйте отправить тестовую страницу.\n\n" +
                                      "Если печать не возобновилась, сформируйте заявку инженерам через кнопку внизу.",
                    IsPinned = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new KnowledgeArticle
                {
                    Title = "Установка и продление сертификатов ЭЦП НУЦ РК",
                    Category = "ЭЦП",
                    Keywords = "эцп, нуц рк, pki.gov.kz, сертификат, p12, срок действия, ключ",
                    ContentMarkdown = "### Сертификаты ЭЦП Национального удостоверяющего центра РК\n\n" +
                                      "Срок действия сертификатов ЭЦП составляет 1 год.\n\n" +
                                      "#### Упреждающий мониторинг:\n" +
                                      "Артёмка автоматически предупредит вас за **14, 7 и 3 дня** до окончания срока действия ключа.\n\n" +
                                      "#### Продление ключа:\n" +
                                      "1. До истечения срока перейдите на портал **pki.gov.kz** в Личный кабинет.\n" +
                                      "2. Войдите по действующему ключу и подайте заявку на перевыпуск.\n" +
                                      "3. Сохраните новые ключи (`AUTH_*.p12` и `GOST_*.p12`).\n" +
                                      "4. В меню Артёмки выберите **🔑 Установить ключ ЭЦП в систему**, укажите файл и пароль.",
                    IsPinned = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new KnowledgeArticle
                {
                    Title = "Работа с порталом ЕИСЗ (eisz.kz)",
                    Category = "ЕИСЗ",
                    Keywords = "еисз, eisz, портал, отчеты, минздрав, стационар",
                    ContentMarkdown = "### Работа с Единой информационной системой здравоохранения (ЕИСЗ)\n\n" +
                                      "Портал **ЕИСЗ (eisz.kz)** используется для передачи отчетов и взаимодействия со стационаром.\n\n" +
                                      "#### Рекомендации по работе:\n" +
                                      "- Рекомендуемый браузер: Google Chrome или Microsoft Edge актуальной версии.\n" +
                                      "- При входе требуется установленный модуль NCALayer.\n" +
                                      "- Если сайт не открывается, проверьте доступность узла в окне **🩺 Экспресс-диагностика ПК**.",
                    IsPinned = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );
            await db.SaveChangesAsync();
        }
    }

    public static IQueryable<KnowledgeArticle> Visible(HelperDb db,int? branchId,string role) =>
        db.KnowledgeArticles.AsNoTracking().Where(a =>
            (a.BranchId==null || (branchId!=null && a.BranchId==branchId)) &&
            (a.TargetRole=="All" || a.TargetRole==role));
    public static async Task<(int? Branch,string Role)> Audience(HelperDb db,ClaimsPrincipal user)
    {
        if(user.Identity?.IsAuthenticated!=true) return (null,"Anonymous");
        if(user.IsInRole("Agent")) {
            var machine=await db.Computers.AsNoTracking().SingleOrDefaultAsync(c=>c.MachineName==user.Identity.Name);
            return (machine?.BranchId,machine==null?Roles.User:(await AgentUserSessions.UserAsync(machine,db))?.Role??Roles.User);
        }
        var account=await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Username==user.Identity.Name && u.IsActive);
        return (account?.BranchId,account?.Role??"Anonymous");
    }

    public static void MapKnowledgeBaseApi(this WebApplication app)
    {
        app.MapGet("/api/knowledge-base", async (HelperDb db, ClaimsPrincipal user, string? category, string? search, int? branchId) =>
        {
            var audience=await Audience(db,user);
            var q = Visible(db,audience.Branch,audience.Role);
            if (!string.IsNullOrWhiteSpace(category) && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
                q = q.Where(a => a.Category == category);
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim().ToLower();
                q = q.Where(a => a.Title.ToLower().Contains(term) || a.Keywords.ToLower().Contains(term) || a.ContentMarkdown.ToLower().Contains(term));
            }
            if (branchId.HasValue)
                q = q.Where(a => a.BranchId == null || a.BranchId == branchId.Value);

            var list = await q.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.UpdatedAt).ToListAsync();
            return Results.Ok(list);
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute {AuthenticationSchemes="Bearer,Agent"});

        app.MapGet("/api/knowledge-base/categories", async (HelperDb db, ClaimsPrincipal user) =>
        {
            var audience=await Audience(db,user);
            var categories = await Visible(db,audience.Branch,audience.Role)
                .GroupBy(a => a.Category)
                .Select(g => new { category = g.Key, count = g.Count() })
                .OrderBy(c => c.category)
                .ToListAsync();
            return Results.Ok(categories);
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute {AuthenticationSchemes="Bearer,Agent"});

        app.MapGet("/api/knowledge-base/{id:int}", async (int id, HelperDb db, ClaimsPrincipal user) =>
        {
            var audience=await Audience(db,user);
            var article = await Visible(db,audience.Branch,audience.Role).SingleOrDefaultAsync(a=>a.Id==id);
            if (article == null) return Results.NotFound();
            await db.KnowledgeArticles.Where(a=>a.Id==id).ExecuteUpdateAsync(set=>set.SetProperty(a=>a.ViewCount,a=>a.ViewCount+1));
            article.ViewCount++;
            return Results.Ok(article);
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute {AuthenticationSchemes="Bearer,Agent"});

        app.MapPost("/api/knowledge-base", async (KnowledgeArticleRequest request, HelperDb db) =>
        {
            if(request.TargetRole!=null && request.TargetRole!="All" && !Roles.All.Contains(request.TargetRole))
                return Results.BadRequest(new {error="Неизвестная роль статьи"});
            if(request.BranchId.HasValue && !await db.Branches.AnyAsync(b=>b.Id==request.BranchId && b.IsActive))
                return Results.BadRequest(new {error="Неизвестный филиал статьи"});
            if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest(new { error = "Заголовок обязателен" });
            var article = new KnowledgeArticle
            {
                Title = request.Title.Trim(),
                Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim(),
                ContentMarkdown = request.ContentMarkdown ?? "",
                Keywords = request.Keywords ?? "",
                BranchId = request.BranchId,
                TargetRole = request.TargetRole ?? "All",
                IsPinned = request.IsPinned,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.KnowledgeArticles.Add(article);
            await db.SaveChangesAsync();
            return Results.Created($"/api/knowledge-base/{article.Id}", article);
        }).RequireAuthorization("buttons.manage");

        app.MapPut("/api/knowledge-base/{id:int}", async (int id, KnowledgeArticleRequest request, HelperDb db) =>
        {
            var article = await db.KnowledgeArticles.FindAsync(id);
            if (article == null) return Results.NotFound();
            if(request.TargetRole!=null && request.TargetRole!="All" && !Roles.All.Contains(request.TargetRole))
                return Results.BadRequest(new {error="Неизвестная роль статьи"});
            if(request.BranchId.HasValue && !await db.Branches.AnyAsync(b=>b.Id==request.BranchId && b.IsActive))
                return Results.BadRequest(new {error="Неизвестный филиал статьи"});
            if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest(new { error = "Заголовок обязателен" });
            article.Title = request.Title.Trim();
            article.Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim();
            article.ContentMarkdown = request.ContentMarkdown ?? "";
            article.Keywords = request.Keywords ?? "";
            article.BranchId = request.BranchId;
            article.TargetRole = request.TargetRole ?? "All";
            article.IsPinned = request.IsPinned;
            article.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(article);
        }).RequireAuthorization("buttons.manage");

        app.MapDelete("/api/knowledge-base/{id:int}", async (int id, HelperDb db) =>
        {
            var article = await db.KnowledgeArticles.FindAsync(id);
            if (article == null) return Results.NotFound();
            db.KnowledgeArticles.Remove(article);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("buttons.manage");
    }
}
