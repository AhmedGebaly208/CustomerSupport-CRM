using System.Text.RegularExpressions;

namespace CustomerSupportCRM.Application.Workspace;

/// <summary>Finds "@handle" references in a comment body (PDF area 4, "@ mentions").
///
/// Handles are matched against the local part of an agent's email, which is the one
/// identifier that is already unique, already known to colleagues, and does not change when
/// someone's display name is corrected.</summary>
public static partial class MentionParser
{
    /// <summary>An @ that starts a word, followed by the characters an email local part can
    /// contain. The leading boundary stops it firing inside an email address that happens to
    /// appear in the text.</summary>
    [GeneratedRegex(@"(?<![\w.])@([A-Za-z0-9._-]{1,64})", RegexOptions.CultureInvariant)]
    private static partial Regex HandlePattern();

    /// <summary>Distinct handles in the order they appear, lowercased for comparison.
    /// A trailing dot is trimmed so "@sara." at the end of a sentence still matches.</summary>
    public static IReadOnlyList<string> Extract(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handles = new List<string>();

        foreach (Match match in HandlePattern().Matches(body))
        {
            var handle = match.Groups[1].Value.TrimEnd('.');
            if (handle.Length == 0) continue;

            if (seen.Add(handle)) handles.Add(handle.ToLowerInvariant());
        }

        return handles;
    }
}
