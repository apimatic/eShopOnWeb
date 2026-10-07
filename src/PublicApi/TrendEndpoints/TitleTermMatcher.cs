using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Finds which catalog terms a page title mentions, case-insensitively.
/// Underscores count as spaces (titles may arrive in their URL form), and a term must stand as a word of its own,
/// optionally pluralised with "s": "Mug" matches "Coffee mugs" but not "Mughal Empire"; ".NET" matches "ASP.NET Core".
/// </summary>
public class TitleTermMatcher
{
    private readonly IReadOnlyList<(string Term, string Normalized)> _terms;

    public TitleTermMatcher(IEnumerable<string> terms)
    {
        _terms = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => (t, Normalize(t.Trim())))
            .ToList();
    }

    public IReadOnlyList<string> Match(string? title)
    {
        if (string.IsNullOrEmpty(title) || _terms.Count == 0)
            return Array.Empty<string>();

        var normalizedTitle = Normalize(title);
        List<string>? matched = null;
        foreach (var (term, normalized) in _terms)
        {
            if (Mentions(normalizedTitle, normalized))
                (matched ??= new List<string>()).Add(term);
        }
        return matched ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    private static string Normalize(string value) => value.Replace('_', ' ');

    private static bool Mentions(string title, string term)
    {
        for (var start = title.IndexOf(term, StringComparison.OrdinalIgnoreCase);
             start >= 0;
             start = title.IndexOf(term, start + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = start + term.Length;
            var startsWord = !char.IsLetterOrDigit(term[0]) || start == 0 || !char.IsLetterOrDigit(title[start - 1]);
            if (!startsWord)
                continue;

            if (!char.IsLetterOrDigit(term[^1]) || end == title.Length || !char.IsLetterOrDigit(title[end]))
                return true;

            // Allow a plural "s" ("T-Shirts", "Mugs").
            if (char.ToLowerInvariant(title[end]) == 's' && (end + 1 == title.Length || !char.IsLetterOrDigit(title[end + 1])))
                return true;
        }
        return false;
    }
}
