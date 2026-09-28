using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Ai;

// ---- DTOs ----

public sealed record TicketSummaryDto(
    string Summary,
    AiCallOutcome Outcome,
    string? Rationale,
    string Provider);

public sealed record SuggestedReplyDto(
    string Body,
    AiCallOutcome Outcome,
    string? Rationale,
    IReadOnlyList<SuggestedArticleDto> BasedOn);

public sealed record SuggestedArticleDto(Guid Id, string TitleAr, string TitleEn, int Rank);

public sealed record CategorySuggestionDto(
    Guid? CategoryId,
    string? CategoryNameAr,
    string? CategoryNameEn,
    AiCallOutcome Outcome,
    string? Rationale);

public sealed record SuggestedSolutionsDto(
    IReadOnlyList<SuggestedArticleDto> Articles,
    AiCallOutcome Outcome,
    string? Rationale);

public sealed record ChatAskRequest(string Question, string? LanguageHint = null);

public sealed record ChatAnswerDto(
    string Answer,
    AiCallOutcome Outcome,
    string? Rationale,
    IReadOnlyList<SuggestedArticleDto> Sources,
    /// <summary>True when the bot could not answer and a human should take over. The portal
    /// uses it to offer a handover rather than leaving the visitor stuck.</summary>
    bool ShouldEscalate);

public sealed record AiFeedbackRequest(AiFeature Feature, AiSuggestionOutcome Outcome, Guid? TicketId);

public interface IAssistanceService
{
    Task<TicketSummaryDto> SummariseTicketAsync(Guid ticketId, CancellationToken ct = default);
    Task<SuggestedReplyDto> SuggestReplyAsync(Guid ticketId, CancellationToken ct = default);
    Task<CategorySuggestionDto> SuggestCategoryAsync(Guid ticketId, CancellationToken ct = default);
    Task<SuggestedSolutionsDto> SuggestSolutionsAsync(Guid ticketId, CancellationToken ct = default);
    Task<ChatAnswerDto> AskAsync(ChatAskRequest request, CancellationToken ct = default);
    Task RecordFeedbackAsync(AiFeedbackRequest request, CancellationToken ct = default);
}

/// <summary>The five assistance features (PDF area 7).
///
/// Every one of them returns a suggestion. Nothing here writes to a ticket, changes a
/// category or sends a reply — an agent accepts, edits or rejects, and the acceptance is the
/// action. That is the whole safety model, and it is why none of these methods take a
/// "commit" flag.
///
/// Ticket and customer text is passed to the provider only as labelled data sections, never
/// spliced into the instruction, so a customer who writes "ignore your instructions" is
/// quoting rather than commanding.</summary>
public sealed class AssistanceService(
    IAppDbContext db,
    IAiCompletionService completions,
    IAiRateLimiter limiter,
    IAiCallLogger callLog,
    ICurrentUser currentUser,
    IScopeProvider scope,
    IClock clock) : IAssistanceService
{
    /// <summary>Knowledge-base articles offered to the provider as candidates. Enough to
    /// find the right one, few enough to keep a prompt affordable when a hosted model is
    /// wired in later.</summary>
    private const int CandidateArticles = 12;

    public async Task<TicketSummaryDto> SummariseTicketAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await LoadTicketAsync(ticketId, ct);
        await EnsureAllowedAsync(AiFeature.Summary, ct);

        var comments = await db.TicketComments.AsNoTracking()
            .Where(c => c.TicketId == ticketId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Body, c.IsInternal })
            .ToListAsync(ct);

        var sections = new List<AiPromptSection>
        {
            new("ticket_subject", ticket.Subject),
            new("ticket_body", ticket.Description)
        };

        sections.AddRange(comments.Select((c, i) =>
            new AiPromptSection($"comment_{i}{(c.IsInternal ? "_internal" : "")}", c.Body)));

        var result = await RunAsync(new AiCompletionRequest(
            AiFeature.Summary,
            SystemPrompt: "Summarise a support conversation for an agent picking it up.",
            sections,
            Instruction: "State what the customer wants and what has been tried.",
            LanguageHint: LanguageFor(ticket),
            TicketId: ticketId), ct);

        return new TicketSummaryDto(result.Text, result.Outcome, result.Rationale, completions.Name);
    }

    public async Task<SuggestedReplyDto> SuggestReplyAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await LoadTicketAsync(ticketId, ct);
        await EnsureAllowedAsync(AiFeature.SuggestedReply, ct);

        var articles = await CandidateArticlesAsync(ticket, ct);

        var sections = new List<AiPromptSection>
        {
            new("ticket", $"{ticket.Subject}\n{ticket.Description}")
        };

        var arabic = LanguageFor(ticket) != "en";

        sections.AddRange(articles.Select(a =>
            new AiPromptSection($"article:{a.Id}", arabic ? a.BodyAr : a.BodyEn)));

        var result = await RunAsync(new AiCompletionRequest(
            AiFeature.SuggestedReply,
            SystemPrompt: "Draft a reply to a customer using only the documentation provided.",
            sections,
            Instruction: "Answer the customer's question. Do not promise anything the articles do not state.",
            LanguageHint: LanguageFor(ticket),
            TicketId: ticketId), ct);

        return new SuggestedReplyDto(
            result.Text, result.Outcome, result.Rationale,
            articles.Select((a, i) => new SuggestedArticleDto(a.Id, a.TitleAr, a.TitleEn, i + 1))
                .Take(2).ToList());
    }

    public async Task<CategorySuggestionDto> SuggestCategoryAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await LoadTicketAsync(ticketId, ct);
        await EnsureAllowedAsync(AiFeature.Categorisation, ct);

        var categories = await db.TicketCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.NameAr, c.NameEn })
            .ToListAsync(ct);

        if (categories.Count == 0)
            return new CategorySuggestionDto(null, null, null, AiCallOutcome.NoAnswer, "No active categories.");

        var sections = new List<AiPromptSection>
        {
            new("ticket", $"{ticket.Subject}\n{ticket.Description}")
        };

        sections.AddRange(categories.Select(c =>
            new AiPromptSection($"category:{c.Id}", $"{c.NameAr} {c.NameEn}")));

        var result = await RunAsync(new AiCompletionRequest(
            AiFeature.Categorisation,
            SystemPrompt: "Choose the category that best fits a support ticket.",
            sections,
            Instruction: "Reply with the category id only.",
            LanguageHint: LanguageFor(ticket),
            TicketId: ticketId), ct);

        if (result.Outcome != AiCallOutcome.Success || !Guid.TryParse(result.Text.Trim(), out var chosen))
            return new CategorySuggestionDto(null, null, null, result.Outcome, result.Rationale);

        var match = categories.FirstOrDefault(c => c.Id == chosen);

        return match is null
            // The provider named something that is not on the list it was given. Treated as
            // no answer rather than passed through, so a bad id never reaches the UI.
            ? new CategorySuggestionDto(null, null, null, AiCallOutcome.NoAnswer,
                "The suggested category was not one of the candidates.")
            : new CategorySuggestionDto(match.Id, match.NameAr, match.NameEn, result.Outcome, result.Rationale);
    }

    public async Task<SuggestedSolutionsDto> SuggestSolutionsAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await LoadTicketAsync(ticketId, ct);
        await EnsureAllowedAsync(AiFeature.SuggestedSolutions, ct);

        var articles = await CandidateArticlesAsync(ticket, ct);

        if (articles.Count == 0)
            return new SuggestedSolutionsDto([], AiCallOutcome.NoAnswer, "No published articles to match against.");

        var arabic = LanguageFor(ticket) != "en";

        var sections = new List<AiPromptSection>
        {
            new("ticket", $"{ticket.Subject}\n{ticket.Description}")
        };

        sections.AddRange(articles.Select(a => new AiPromptSection(
            $"article:{a.Id}",
            arabic ? $"{a.TitleAr} {a.BodyAr}" : $"{a.TitleEn} {a.BodyEn}")));

        var result = await RunAsync(new AiCompletionRequest(
            AiFeature.SuggestedSolutions,
            SystemPrompt: "Rank knowledge-base articles by how well they answer a ticket.",
            sections,
            Instruction: "Reply with matching article ids, best first, comma separated.",
            LanguageHint: LanguageFor(ticket),
            TicketId: ticketId), ct);

        var ranked = ParseIds(result.Text)
            .Select((id, index) => (id, index))
            .Join(articles, x => x.id, a => a.Id,
                (x, a) => new SuggestedArticleDto(a.Id, a.TitleAr, a.TitleEn, x.index + 1))
            .OrderBy(x => x.Rank)
            .ToList();

        return new SuggestedSolutionsDto(ranked, result.Outcome, result.Rationale);
    }

    public async Task<ChatAnswerDto> AskAsync(ChatAskRequest request, CancellationToken ct = default)
    {
        await EnsureAllowedAsync(AiFeature.Chatbot, ct);

        var arabic = !string.Equals(request.LanguageHint, "en", StringComparison.OrdinalIgnoreCase);

        // Only published, public articles: the bot answers customers, and an internal
        // runbook must never be quoted back to one.
        var articles = await db.Articles.AsNoTracking()
            .Where(a => a.Status == ArticleStatus.Published && a.IsPublic)
            .OrderByDescending(a => a.HelpfulCount - a.NotHelpfulCount)
            .Take(CandidateArticles * 2)
            .Select(a => new { a.Id, a.TitleAr, a.TitleEn, a.BodyAr, a.BodyEn })
            .ToListAsync(ct);

        var sections = new List<AiPromptSection> { new("question", request.Question) };

        // The real bodies, not the search projections: the answer is quoted back to a
        // customer, and folded text reads as misspelt.
        sections.AddRange(articles.Select(a => new AiPromptSection(
            $"article:{a.Id}", arabic ? a.BodyAr : a.BodyEn)));

        var result = await RunAsync(new AiCompletionRequest(
            AiFeature.Chatbot,
            SystemPrompt: "Answer a customer's question using only the documentation provided.",
            sections,
            Instruction: "If the documentation does not answer it, say so and offer a human.",
            LanguageHint: request.LanguageHint), ct);

        var sourceId = ExtractSourceId(result.Rationale);

        var sources = sourceId is { } id
            ? articles.Where(a => a.Id == id)
                .Select(a => new SuggestedArticleDto(a.Id, a.TitleAr, a.TitleEn, 1)).ToList()
            : [];

        return new ChatAnswerDto(
            result.Text, result.Outcome, result.Rationale, sources,
            ShouldEscalate: result.Outcome != AiCallOutcome.Success);
    }

    public async Task RecordFeedbackAsync(AiFeedbackRequest request, CancellationToken ct = default)
    {
        db.AiSuggestionFeedback.Add(new AiSuggestionFeedback
        {
            Feature = request.Feature,
            Outcome = request.Outcome,
            TicketId = request.TicketId,
            UserId = currentUser.UserId,
            OccurredAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    private async Task<Ticket> LoadTicketAsync(Guid ticketId, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        scope.EnsureCanAccess(ticket);
        return ticket;
    }

    /// <summary>Refuses before calling the provider when the caller is over their cap, and
    /// records the refusal so the cost log shows demand as well as spend.</summary>
    private async Task EnsureAllowedAsync(AiFeature feature, CancellationToken ct)
    {
        var scopeKey = currentUser.UserId is { } userId ? $"user:{userId}" : "anonymous";

        if (await limiter.TryAcquireAsync(feature, scopeKey, ct)) return;

        await callLog.LogAsync(
            feature,
            new AiCompletionResult(string.Empty, AiCallOutcome.RateLimited),
            null, ct);

        throw new ConflictException(
            "You have used this assistance too often in a short period. Try again shortly.",
            ErrorCodes.AiRateLimited);
    }

    private async Task<AiCompletionResult> RunAsync(AiCompletionRequest request, CancellationToken ct)
    {
        AiCompletionResult result;

        try
        {
            result = await completions.CompleteAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Assistance failing must never fail the work it was assisting with, so the
            // failure is recorded and returned as an outcome rather than thrown.
            result = new AiCompletionResult(string.Empty, AiCallOutcome.Failed, Rationale: ex.Message);
        }

        await callLog.LogAsync(request.Feature, result, request.TicketId, ct);
        return result;
    }

    private async Task<List<ArticleCandidate>> CandidateArticlesAsync(Ticket ticket, CancellationToken ct)
    {
        return await db.Articles.AsNoTracking()
            .Where(a => a.Status == ArticleStatus.Published)
            .OrderByDescending(a => a.HelpfulCount - a.NotHelpfulCount)
            .ThenByDescending(a => a.ViewCount)
            .Take(CandidateArticles)
            .Select(a => new ArticleCandidate(
                a.Id, a.TitleAr, a.TitleEn, a.BodyAr, a.BodyEn))
            .ToListAsync(ct);
    }

    private static string LanguageFor(Ticket ticket) =>
        string.Equals(ticket.Customer?.PreferredLanguage, "en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "ar";

    private static IEnumerable<Guid> ParseIds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id)) yield return id;
        }
    }

    /// <summary>Pulls the article id the provider says it answered from, so the UI can cite
    /// it. Absent or unparseable means the answer is shown without a source rather than with
    /// a wrong one.</summary>
    private static Guid? ExtractSourceId(string? rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale)) return null;

        foreach (var token in rationale.Split([' ', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(token, out var id)) return id;
        }

        return null;
    }

    private sealed record ArticleCandidate(
        Guid Id, string TitleAr, string TitleEn, string BodyAr, string BodyEn);
}
