using System;
using System.Globalization;
using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

internal static class BillingEndpointSupport
{
    /// <summary>The caller's identity always comes from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name)
            ? throw new BillingException(401, "The token carries no user name.")
            : name;
    }

    public static DateOnly ParseDate(string? text, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BillingException(400, $"{what} is required (format YYYY-MM-DD).");
        }

        var t = text.Trim();
        if (DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }

        if (t.Length > 10 && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return DateOnly.FromDateTime(dto.DateTime);
        }

        throw new BillingException(400, $"{what} '{text}' is not a valid date (format YYYY-MM-DD).");
    }
}
