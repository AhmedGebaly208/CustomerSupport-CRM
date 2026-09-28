using System.Text.RegularExpressions;

namespace CustomerSupportCRM.Application.Channels;

/// <summary>The "[TKT-000123]" token carried in an outbound subject so a customer's reply
/// threads back onto the right ticket.
///
/// A visible token in the subject rather than a hidden header, because it is the only
/// threading signal that survives a customer forwarding the mail, replying from a different
/// address, or a provider that rewrites headers.</summary>
public static partial class TicketReference
{
    [GeneratedRegex(@"\[\s*(TKT-\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>Prefixes a subject with the ticket's reference, leaving it alone if the
    /// token is already there — otherwise a long thread accumulates one per reply.</summary>
    public static string Stamp(string ticketNumber, string? subject)
    {
        var text = subject?.Trim() ?? string.Empty;

        if (Extract(text) is { } existing
            && string.Equals(existing, ticketNumber, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return text.Length == 0 ? $"[{ticketNumber}]" : $"[{ticketNumber}] {text}";
    }

    /// <summary>The ticket number in a subject, or null. Case-insensitive because mail
    /// clients and customers both rewrite case freely.</summary>
    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = TokenPattern().Match(text);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }
}
