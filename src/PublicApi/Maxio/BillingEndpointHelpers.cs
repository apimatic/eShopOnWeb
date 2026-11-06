using System;
using System.Globalization;
using System.Net;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class BillingEndpointHelpers
{
    /// <summary>The caller's identity, taken from the JWT.</summary>
    public static string UserName(ClaimsPrincipal? user)
    {
        var name = user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(HttpStatusCode.Unauthorized, "The token does not identify a user.");
        return name;
    }

    /// <summary>Parses a calendar date written exactly as YYYY-MM-DD.</summary>
    public static DateOnly ParseDate(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new BillingException(HttpStatusCode.BadRequest, $"'{field}' is required and must be a date written as YYYY-MM-DD.");
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BillingException(HttpStatusCode.BadRequest, $"'{field}' must be a date written as YYYY-MM-DD.");
        return date;
    }
}
