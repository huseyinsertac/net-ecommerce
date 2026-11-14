using System.Text.RegularExpressions;

namespace Catalog.Infrastructure.Reading;

/// <summary>
/// Turns free search text into a safe <c>to_tsquery</c> string: words (letters and digits only) joined by <c>&amp;</c>,
/// the last one as a prefix (<c>:*</c>) so partial input like "noise canc" matches. Every tsquery operator and quote
/// is stripped as punctuation, so the output is always valid tsquery syntax. Text without any word yields null
/// (= no text filter). The result is still always sent as a parameter.
/// </summary>
public static partial class TsQueryBuilder
{
    private const int MaxTokens = 8;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separators();

    public static string? Build(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var tokens = Separators().Split(text.ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Take(MaxTokens)
            .ToList();

        if (tokens.Count == 0)
        {
            return null;
        }

        return string.Join(" & ", tokens.Select((t, i) => i == tokens.Count - 1 ? t + ":*" : t));
    }
}
