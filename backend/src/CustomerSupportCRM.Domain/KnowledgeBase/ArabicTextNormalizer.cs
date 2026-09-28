using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomerSupportCRM.Domain.KnowledgeBase;

/// <summary>Text normalisation for knowledge-base search and slugs.
///
/// Arabic is written with optional diacritics and with letters that readers treat as
/// interchangeable — أ إ آ for ا, ى for ي, ة for ه. Someone searching "استعادة كلمه المرور"
/// expects to find an article titled "استعادة كلمة المرور". Comparing the raw strings would
/// miss it, so both the stored search text and the query are folded to the same shape.
///
/// Pure and dependency-free so it can live in Domain and be applied identically when writing
/// a row and when reading one.</summary>
public static partial class ArabicTextNormalizer
{
    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacters();

    /// <summary>Arabic diacritics (tashkeel) and the tatweel stretching character. They carry
    /// pronunciation, not meaning, and are typed inconsistently.</summary>
    private const string Diacritics = "ًٌٍَُِّْٕٓٔـ";

    /// <summary>Folds a string to the form used for comparison: no diacritics, unified
    /// letter shapes, single spaces, lower case.</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var builder = new StringBuilder(input.Length);

        foreach (var ch in input)
        {
            if (Diacritics.Contains(ch)) continue;

            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                // Arabic-Indic digits fold to Latin so a search for ٢٠٢٦ finds 2026.
                >= '٠' and <= '٩' => (char)(ch - '٠' + '0'),
                >= '۰' and <= '۹' => (char)(ch - '۰' + '0'),
                _ => char.ToLowerInvariant(ch)
            });
        }

        return Whitespace().Replace(builder.ToString(), " ").Trim();
    }

    /// <summary>Markup removed, wording untouched. Use this for anything a person will read;
    /// Normalize folds letters and case for comparison and would make prose look misspelt.</summary>
    public static string StripHtml(string? htmlOrText)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText)) return string.Empty;

        // A space, not an empty string: "<p>one</p><p>two</p>" must not become "onetwo".
        var text = HtmlTag().Replace(htmlOrText, " ");

        text = text
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);

        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>The plain-text, normalised projection of an article body, stored alongside it
    /// so search never has to strip markup at query time.</summary>
    public static string BuildSearchText(string? htmlOrText) => Normalize(StripHtml(htmlOrText));

    /// <summary>A url-safe slug. Arabic is kept as Arabic rather than transliterated: the
    /// reader recognises it, and browsers percent-encode it transparently.</summary>
    public static string Slugify(string? input, string culture)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0) return string.Empty;

        if (!string.Equals(culture, "ar", StringComparison.OrdinalIgnoreCase))
        {
            // Strip accents from Latin text so "café" and "cafe" produce the same slug.
            normalized = new string(normalized
                .Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray())
                .Normalize(NormalizationForm.FormC);
        }

        var slug = NonSlugCharacters().Replace(normalized, "-").Trim('-');

        // Long slugs add nothing and complicate storage; the id disambiguates anyway.
        return slug.Length <= 120 ? slug : slug[..120].TrimEnd('-');
    }
}
