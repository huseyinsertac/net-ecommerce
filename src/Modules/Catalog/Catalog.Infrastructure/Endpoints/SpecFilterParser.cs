using System.Globalization;
using Catalog.Application.Products;

namespace Catalog.Infrastructure.Endpoints;

/// <summary>
/// Parses the <c>spec</c> query parameter: <c>key:min..max</c>, <c>key:min..</c>, <c>key:..max</c> (ranges, invariant-culture
/// decimals) or <c>key:v1,v2</c> (a value list; <c>key:true</c> is a list of one). Whether the key exists and the shape fits
/// its type is checked later by the search handler against the category's spec schema.
/// </summary>
public static class SpecFilterParser
{
    private const string RangeSeparator = "..";
    private const NumberStyles NumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static bool TryParse(string raw, out SpecFilter filter, out string error)
    {
        filter = new SpecFilter(string.Empty, null, null, []);
        error = string.Empty;

        var colon = raw?.IndexOf(':') ?? -1;
        if (colon < 0)
        {
            error = "Expected the form key:value.";
            return false;
        }

        var key = raw![..colon].Trim();
        var body = raw[(colon + 1)..];
        if (key.Length == 0)
        {
            error = "The spec key is empty.";
            return false;
        }

        if (body.Contains("...", StringComparison.Ordinal))
        {
            error = "A range uses exactly two dots (min..max).";
            return false;
        }

        var dots = body.IndexOf(RangeSeparator, StringComparison.Ordinal);
        if (dots >= 0)
        {
            return TryParseRange(key, body[..dots].Trim(), body[(dots + RangeSeparator.Length)..].Trim(), out filter, out error);
        }

        var values = body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 0)
        {
            error = "At least one value is required.";
            return false;
        }

        filter = new SpecFilter(key, null, null, values);
        return true;
    }

    private static bool TryParseRange(string key, string min, string max, out SpecFilter filter, out string error)
    {
        filter = new SpecFilter(string.Empty, null, null, []);
        error = string.Empty;

        if (min.Length == 0 && max.Length == 0)
        {
            error = "A range needs a minimum or a maximum.";
            return false;
        }

        if (!TryBound(min, out var minValue) || !TryBound(max, out var maxValue))
        {
            error = "Range bounds must be numbers.";
            return false;
        }

        filter = new SpecFilter(key, minValue, maxValue, []);
        return true;
    }

    private static bool TryBound(string text, out decimal? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!decimal.TryParse(text, NumberStyle, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
