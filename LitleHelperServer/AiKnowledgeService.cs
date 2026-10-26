using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LitleHelperServer;

public sealed record AiKnowledgeSource(int Id, string Title, string Url);
public sealed record AiKnowledgeExcerpt(AiKnowledgeSource Source, string Text);

public sealed class AiKnowledgeService
{
    private static IQueryable<KnowledgeArticle> Visible(HelperDb db, PanelUser user)
    {
        var branches = user.BranchIds;
        return db.KnowledgeArticles.AsNoTracking().Where(a =>
            (a.TargetRole == "All" || a.TargetRole == user.Role) && (a.BranchId == null || branches.Contains(a.BranchId.Value)));
    }
    public async Task<AiKnowledgeExcerpt> ReadAsync(HelperDb db, PanelUser user, int id, CancellationToken token)
    {
        var article = await Visible(db, user).Where(a => a.Id == id)
            .Select(a => new { a.Id, a.Title, Text = a.ContentMarkdown.Substring(0, Math.Min(a.ContentMarkdown.Length, 2000)) }).SingleOrDefaultAsync(token)
            ?? throw new UnauthorizedAccessException("Knowledge article unavailable");
        return new(new(article.Id, article.Title, "/api/ai/knowledge/" + article.Id), AiApi.Redact(article.Text));
    }
    public async Task ValidateHistoryAsync(HelperDb db, PanelUser user, IEnumerable<AiMessage> history, CancellationToken token)
    {
        var ids = history.SelectMany(m => System.Text.Json.JsonSerializer.Deserialize<AiKnowledgeSource[]>(m.SourcesJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? []).Select(s => s.Id).Distinct().ToArray();
        if (ids.Length > 0 && await Visible(db, user).CountAsync(a => ids.Contains(a.Id), token) != ids.Length)
            throw new ArgumentException("Knowledge access changed; start a new conversation");
    }
    public async Task<IReadOnlyList<AiKnowledgeExcerpt>> SearchAsync(HelperDb db, PanelUser user, string question, CancellationToken token)
    {
        var terms = Regex.Matches(question.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}", RegexOptions.None, TimeSpan.FromMilliseconds(100))
            .Select(m => m.Value).Distinct().Take(6).ToArray();
        if (terms.Length == 0) return [];
        // Filter before ranking or reading content. No privileged search, no cross-user cache.
        var visible = Visible(db, user);
        var candidates = new Dictionary<int, (string Title, string Keywords, string Content)>();
        foreach (string term in terms)
        {
            var matches = await visible.Where(a => a.Title.ToLower().Contains(term) || a.Keywords.ToLower().Contains(term) || a.ContentMarkdown.ToLower().Contains(term))
                .OrderByDescending(a => a.IsPinned).ThenBy(a => a.Id).Take(6)
                .Select(a => new { a.Id, Title = a.Title.Substring(0, Math.Min(a.Title.Length, 256)),
                    Keywords = a.Keywords.Substring(0, Math.Min(a.Keywords.Length, 512)),
                    Content = a.ContentMarkdown.Substring(0, Math.Min(a.ContentMarkdown.Length, 2000)) }).ToListAsync(token);
            foreach (var article in matches) candidates[article.Id] = (article.Title, article.Keywords, article.Content);
        }
        return candidates.OrderByDescending(a => terms.Sum(t => (a.Value.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ? 4 : 0) +
                (a.Value.Keywords.Contains(t, StringComparison.OrdinalIgnoreCase) ? 2 : 0) + (a.Value.Content.Contains(t, StringComparison.OrdinalIgnoreCase) ? 1 : 0)))
            .ThenBy(a => a.Key).Take(3)
            .Select(a => new AiKnowledgeExcerpt(new(a.Key, a.Value.Title, "/api/ai/knowledge/" + a.Key), AiApi.Redact(a.Value.Content))).ToArray();
    }
}
