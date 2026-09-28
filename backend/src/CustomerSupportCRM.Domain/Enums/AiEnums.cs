namespace CustomerSupportCRM.Domain.Enums;

/// <summary>The five assistance features from PDF area 7. Every one of them produces a
/// suggestion an agent accepts, edits or rejects — none of them acts on a ticket.</summary>
public enum AiFeature
{
    Summary = 0,
    SuggestedReply = 1,
    Categorisation = 2,
    SuggestedSolutions = 3,
    Chatbot = 4
}

public enum AiCallOutcome
{
    Success = 0,
    Failed = 1,
    /// <summary>Refused by the rate limiter rather than attempted.</summary>
    RateLimited = 2,
    /// <summary>The feature is switched off.</summary>
    Disabled = 3,
    /// <summary>Answered from a previous identical call.</summary>
    Cached = 4,
    /// <summary>The provider had nothing useful to say — no matching article, too little
    /// text to summarise. A real answer, not a failure.</summary>
    NoAnswer = 5
}

/// <summary>What an agent did with a suggestion. The point of recording it is to tell
/// whether the assistance is worth keeping.</summary>
public enum AiSuggestionOutcome
{
    Offered = 0,
    Accepted = 1,
    Edited = 2,
    Rejected = 3
}
