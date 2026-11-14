using System.Text.RegularExpressions;

namespace Catalog.Infrastructure.Reading;

/// <summary>
/// The pieces of a search-as-you-type tsquery.
/// </summary>
/// <param name="Words">The complete words before the last one, joined by <c>&amp;</c> (parsed with the 'english' config); null if none.</param>
/// <param name="Prefix">The last token as a prefix, e.g. <c>canc:*</c>.</param>
/// <param name="ShortPrefix">
/// The last token has at most <see cref="TsQueryBuilder.ShortPrefixLength"/> characters. It is most likely an unfinished
/// word ("a" → "audio"), so it is kept as a prefix even when it is an English stop word. A longer stop word ("the") is
/// most likely complete and is dropped.
/// </param>
public sealed record TsQueryParts(string? Words, string Prefix, bool ShortPrefix);

/// <summary>
/// Turns free search text into safe <c>to_tsquery</c> inputs: tokens of letters and digits only (every tsquery operator
/// and quote is stripped as punctuation, so each piece is valid tsquery syntax), at most 8 of them, the last as a prefix.
/// Text without any word yields null (= no text filter). The pieces are always sent as parameters.
/// </summary>
public static partial class TsQueryBuilder
{
    public const int ShortPrefixLength = 2;
    private const int MaxTokens = 8;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separators();

    public static TsQueryParts? Build(string? text)
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

        var last = tokens[^1];
        var words = tokens.Count > 1 ? string.Join(" & ", tokens.Take(tokens.Count - 1)) : null;
        return new TsQueryParts(words, last + ":*", last.Length <= ShortPrefixLength);
    }
}
