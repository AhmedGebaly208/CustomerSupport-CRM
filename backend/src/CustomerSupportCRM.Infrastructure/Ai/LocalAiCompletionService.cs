using System.Diagnostics;
using System.Text;
using CustomerSupportCRM.Application.Ai;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.KnowledgeBase;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Infrastructure.Ai;

/// <summary>Assistance that runs entirely on this machine (PDF area 7).
///
/// It is not a language model and does not pretend to be one. It is extractive: summaries
/// are sentences lifted from the ticket, suggested solutions are knowledge-base articles
/// scored against the ticket's words, and categorisation is a term overlap with the category
/// names. Every answer carries the evidence it used, so an agent can check it rather than
/// trust it.
///
/// That honesty is the point. A stub returning invented prose would look like the feature
/// working while being worse than nothing on a real ticket; this produces output an agent can
/// actually use today, and swapping in a hosted model later is one sibling adapter with
/// nothing above it changing.</summary>
public sealed class LocalAiCompletionService(
    ILogger<LocalAiCompletionService> logger) : IAiCompletionService
{
    public string Name => "local-extractive";

    /// <summary>Sentences kept in a summary. Three is enough to say what happened and what
    /// was tried without becoming as long as the thing it summarises.</summary>
    private const int SummarySentences = 3;

    /// <summary>Words too common to carry meaning, in both desk languages. Without these a
    /// score is dominated by "the" and "من".</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the","a","an","and","or","but","if","then","is","are","was","were","be","been","to","of",
        "in","on","at","for","with","from","by","as","it","this","that","i","we","you","he","she",
        "they","my","our","your","not","no","can","cannot","have","has","had","do","does","did",
        "please","hello","hi","thanks","thank","regards","dear",
        "من","الى","إلى","في","على","عن","مع","هذا","هذه","ذلك","التي","الذي","ان","أن","إن",
        "كان","كانت","هو","هي","انا","أنا","نحن","انت","أنت","لا","ما","لم","لن","قد","هل",
        "او","أو","ثم","بعد","قبل","عند","كل","بعض","شكرا","مرحبا","السلام","عليكم","لكن"
    };

    public Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = Stopwatch.GetTimestamp();

        var result = request.Feature switch
        {
            AiFeature.Summary => Summarise(request),
            AiFeature.SuggestedReply => SuggestReply(request),
            AiFeature.Categorisation => Categorise(request),
            AiFeature.SuggestedSolutions => SuggestSolutions(request),
            AiFeature.Chatbot => Answer(request),
            _ => Nothing("Unsupported feature.")
        };

        var latency = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        logger.LogDebug("Local assistance produced {Outcome} for {Feature} in {Latency}ms.",
            result.Outcome, request.Feature, latency);

        return Task.FromResult(result with
        {
            Model = Name,
            LatencyMs = latency,
            // Tokens are a billing concept and this provider bills nothing. Reporting words
            // as tokens would put a fictional number into the cost log.
            PromptTokens = 0,
            CompletionTokens = 0
        });
    }

    // ---- features ----

    /// <summary>Picks the sentences that carry the most of the conversation's distinctive
    /// vocabulary, and keeps them in their original order so the summary still reads as a
    /// sequence of events.</summary>
    private AiCompletionResult Summarise(AiCompletionRequest request)
    {
        var text = Join(request.Data);
        var sentences = SplitSentences(text);

        if (sentences.Count == 0) return Nothing("There is nothing to summarise yet.");

        if (sentences.Count <= SummarySentences)
        {
            // Already shorter than a summary would be; returning it unchanged is the honest
            // answer rather than padding it out.
            return new AiCompletionResult(
                string.Join(' ', sentences), AiCallOutcome.Success,
                Rationale: "The conversation is already short enough to read in full.");
        }

        var frequencies = TermFrequencies(text);

        var ranked = sentences
            .Select((sentence, index) => (sentence, index, score: ScoreSentence(sentence, frequencies)))
            .OrderByDescending(x => x.score)
            .Take(SummarySentences)
            .OrderBy(x => x.index)
            .ToList();

        var summary = string.Join(' ', ranked.Select(x => x.sentence));

        return new AiCompletionResult(
            summary, AiCallOutcome.Success,
            Rationale: $"Selected {ranked.Count} of {sentences.Count} sentences by term weight.");
    }

    /// <summary>Drafts a reply from the matching knowledge-base articles the caller supplied,
    /// in the reader's language. With no article to lean on it declines rather than inventing
    /// an answer.</summary>
    private AiCompletionResult SuggestReply(AiCompletionRequest request)
    {
        var articles = request.Data.Where(d => d.Label.StartsWith("article", StringComparison.Ordinal)).ToList();

        if (articles.Count == 0)
        {
            return Nothing("No knowledge-base article matched this ticket closely enough to draft from.");
        }

        var arabic = IsArabic(request.LanguageHint);
        var builder = new StringBuilder();

        builder.AppendLine(arabic
            ? "شكرًا لتواصلك معنا. وفقًا لما هو موثّق لدينا:"
            : "Thank you for getting in touch. According to our documentation:");

        builder.AppendLine();

        foreach (var article in articles.Take(2))
        {
            var excerpt = FirstSentences(article.Body, 3);
            if (excerpt.Length == 0) continue;

            builder.AppendLine(excerpt);
            builder.AppendLine();
        }

        builder.Append(arabic
            ? "إن لم يحلّ ذلك المشكلة، أخبرنا وسنتابع معك."
            : "If that does not resolve it, let us know and we will follow up.");

        return new AiCompletionResult(
            builder.ToString().Trim(), AiCallOutcome.Success,
            Rationale: $"Drafted from {Math.Min(articles.Count, 2)} matching article(s).");
    }

    /// <summary>Scores each candidate category by how much of its vocabulary the ticket
    /// uses. Returns the best one only when it is clearly ahead — a near tie means the
    /// evidence does not support a suggestion.</summary>
    private AiCompletionResult Categorise(AiCompletionRequest request)
    {
        var ticket = request.Data.FirstOrDefault(d => d.Label == "ticket")?.Body ?? string.Empty;
        var candidates = request.Data.Where(d => d.Label.StartsWith("category:", StringComparison.Ordinal)).ToList();

        if (candidates.Count == 0 || ticket.Length == 0)
            return Nothing("There are no categories to choose between.");

        var ticketTerms = Terms(ticket).ToHashSet(StringComparer.Ordinal);

        var scored = candidates
            .Select(c => (
                id: c.Label["category:".Length..],
                score: Terms(c.Body).Count(ticketTerms.Contains)))
            .OrderByDescending(x => x.score)
            .ToList();

        var best = scored[0];
        if (best.score == 0) return Nothing("No category shares any distinctive term with this ticket.");

        var runnerUp = scored.Count > 1 ? scored[1].score : 0;

        if (best.score == runnerUp)
        {
            // Two categories fit equally. Picking one would be a coin toss presented as a
            // recommendation, which is worse than saying nothing.
            return Nothing("Two or more categories fit equally well.");
        }

        return new AiCompletionResult(
            best.id, AiCallOutcome.Success,
            Rationale: $"Matched {best.score} distinctive term(s); the next best matched {runnerUp}.");
    }

    /// <summary>Ranks the supplied articles against the ticket text and returns their ids,
    /// best first.</summary>
    private AiCompletionResult SuggestSolutions(AiCompletionRequest request)
    {
        var ticket = request.Data.FirstOrDefault(d => d.Label == "ticket")?.Body ?? string.Empty;
        var articles = request.Data.Where(d => d.Label.StartsWith("article:", StringComparison.Ordinal)).ToList();

        if (articles.Count == 0) return Nothing("No article matched this ticket.");

        var ticketTerms = Terms(ticket).ToHashSet(StringComparer.Ordinal);

        var ranked = articles
            .Select(a => (
                id: a.Label["article:".Length..],
                score: Terms(a.Body).Count(ticketTerms.Contains)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(5)
            .ToList();

        if (ranked.Count == 0) return Nothing("No article shares any distinctive term with this ticket.");

        return new AiCompletionResult(
            string.Join(',', ranked.Select(x => x.id)), AiCallOutcome.Success,
            Rationale: $"Top match shares {ranked[0].score} distinctive term(s) with the ticket.");
    }

    /// <summary>Answers a visitor's question from the knowledge base, quoting the article it
    /// came from. It never answers from nothing: with no match it says so, which is the
    /// behaviour that keeps a self-service bot from misleading a customer.</summary>
    private AiCompletionResult Answer(AiCompletionRequest request)
    {
        var question = request.Data.FirstOrDefault(d => d.Label == "question")?.Body ?? string.Empty;
        var articles = request.Data.Where(d => d.Label.StartsWith("article:", StringComparison.Ordinal)).ToList();

        var arabic = IsArabic(request.LanguageHint);

        if (question.Trim().Length == 0)
            return Nothing(arabic ? "لم يصل سؤال." : "No question was asked.");

        var questionTerms = Terms(question).ToHashSet(StringComparer.Ordinal);

        var best = articles
            .Select(a => (article: a, score: Terms(a.Body).Count(questionTerms.Contains)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best.article is null)
        {
            return new AiCompletionResult(
                arabic
                    ? "لم أجد إجابة موثّقة لهذا السؤال. سأحوّلك إلى أحد موظفي الدعم."
                    : "I could not find a documented answer for that. Let me pass you to a support agent.",
                AiCallOutcome.NoAnswer,
                Rationale: "No article shared a distinctive term with the question.");
        }

        return new AiCompletionResult(
            FirstSentences(best.article.Body, 4),
            AiCallOutcome.Success,
            Rationale: $"Answered from article {best.article.Label["article:".Length..]} " +
                       $"({best.score} matching term(s)).");
    }

    // ---- text mechanics ----

    private static AiCompletionResult Nothing(string why) =>
        new(string.Empty, AiCallOutcome.NoAnswer, Rationale: why);

    private static bool IsArabic(string? hint) =>
        !string.Equals(hint, "en", StringComparison.OrdinalIgnoreCase);

    private static string Join(IReadOnlyList<AiPromptSection> sections) =>
        string.Join("\n", sections.Select(s => s.Body));

    /// <summary>Splits on sentence enders in both scripts. Arabic uses ؟ and ، and often no
    /// full stop at all, so a period-only split would return one enormous sentence.</summary>
    private static List<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var sentences = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in text)
        {
            if (ch is '\r') continue;

            if (ch is '\n')
            {
                Flush();
                continue;
            }

            current.Append(ch);

            if (ch is '.' or '!' or '?' or '؟' or '۔') Flush();
        }

        Flush();
        return sentences;

        void Flush()
        {
            var sentence = current.ToString().Trim();
            current.Clear();

            // Fragments shorter than this are list bullets and sign-offs, not statements.
            if (sentence.Length >= 15) sentences.Add(sentence);
        }
    }

    /// <summary>Sentences as an author wrote them. Markup is removed but nothing is folded:
    /// this text is read by a customer, and normalised text looks misspelt.</summary>
    private static string FirstSentences(string text, int count)
    {
        var sentences = SplitSentences(ArabicTextNormalizer.StripHtml(text));

        return sentences.Count == 0
            ? text.Trim()
            : string.Join(' ', sentences.Take(count));
    }

    /// <summary>Meaning-bearing words, folded so Arabic spelling variants agree. Reuses the
    /// knowledge base's normaliser so a term matches here exactly as it would in search.</summary>
    private static IEnumerable<string> Terms(string text)
    {
        var normalized = ArabicTextNormalizer.Normalize(text);

        return normalized
            .Split([' ', '\t', '\n', ',', '،', '.', ':', ';', '!', '?', '؟', '(', ')', '"', '\'', '-', '/'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w));
    }

    private static Dictionary<string, int> TermFrequencies(string text)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var term in Terms(text))
            frequencies[term] = frequencies.GetValueOrDefault(term) + 1;

        return frequencies;
    }

    /// <summary>Sum of term weights, divided by length. Without the division the longest
    /// sentence always wins, which is not the same as the most informative one.</summary>
    private static double ScoreSentence(string sentence, IReadOnlyDictionary<string, int> frequencies)
    {
        var terms = Terms(sentence).ToList();
        if (terms.Count == 0) return 0;

        var total = terms.Sum(term => frequencies.GetValueOrDefault(term));

        return total / Math.Sqrt(terms.Count);
    }
}
