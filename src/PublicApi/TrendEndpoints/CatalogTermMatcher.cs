using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Finds which catalog brands/types a page title mentions: case-insensitive, whole words only
/// (so the brand "Other" is not found in "Mother"), with underscores in titles read as spaces.
/// </summary>
public sealed class CatalogTermMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Regex? _pattern;
    private readonly Dictionary<string, string> _canonical;

    public CatalogTermMatcher(IEnumerable<string?> terms)
    {
        var distinct = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Longest first, so "USB Memory Stick" wins over any shorter term it contains.
            .OrderByDescending(t => t.Length)
            .ToList();

        Terms = distinct;
        _canonical = distinct.ToDictionary(t => t, t => t, StringComparer.OrdinalIgnoreCase);

        if (distinct.Count > 0)
        {
            var alternatives = string.Join("|", distinct.Select(ToPattern));
            _pattern = new Regex(alternatives, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
    }

    public IReadOnlyList<string> Terms { get; }

    /// <summary>
    /// The catalog terms the title mentions, in catalog spelling; empty when none.
    /// </summary>
    public IReadOnlyList<string> Match(string? title)
    {
        if (_pattern is null || string.IsNullOrEmpty(title))
            return Array.Empty<string>();

        var normalized = title.Replace('_', ' ');
        try
        {
            return _pattern.Matches(normalized)
                .Select(m => _canonical.TryGetValue(m.Value, out var term) ? term : m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return Array.Empty<string>();
        }
    }

    // A word boundary is only required on a side where the term itself starts/ends with a letter or digit,
    // so ".NET" is still found in "ASP.NET Core" while "Mug" is not found in "Smuggler".
    private static string ToPattern(string term)
    {
        var builder = new StringBuilder();
        if (char.IsLetterOrDigit(term[0]))
            builder.Append(@"(?<![\p{L}\p{N}])");
        builder.Append(Regex.Escape(term));
        if (char.IsLetterOrDigit(term[^1]))
            builder.Append(@"(?![\p{L}\p{N}])");
        return builder.ToString();
    }
}
