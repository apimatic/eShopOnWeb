using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Domain-specific guard clauses used by the supplier-catalog sync feature.
/// </summary>
public static class GuardExtensions
{
    /// <summary>
    /// Throws when <paramref name="input"/> is not a well-formed absolute http(s) URL.
    /// Returns the trimmed URL otherwise.
    /// </summary>
    public static string InvalidHttpUrl(this IGuardClause guardClause, string input, string parameterName)
    {
        Guard.Against.NullOrWhiteSpace(input, parameterName);
        var trimmed = input.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{parameterName}' must be an absolute http or https URL.", parameterName);
        }

        return trimmed;
    }
}
