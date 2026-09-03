using System;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

internal static class EndpointResults
{
    internal static bool TryGetBuyerId(ClaimsPrincipal principal, out string buyerId)
    {
        buyerId = principal.Identity?.Name ?? string.Empty;
        return !string.IsNullOrWhiteSpace(buyerId);
    }

    internal static IResult BadRequest(string detail) =>
        Results.Problem(title: "Invalid request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    internal static IResult Conflict(string detail) =>
        Results.Problem(title: "Request conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);

    internal static IResult ProviderUnavailable() =>
        Results.Problem(
            title: "Messaging provider unavailable",
            detail: "The messaging provider could not complete the request. Try again later.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    internal static bool TryParseIso8601(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("T", StringComparison.Ordinal))
        {
            return false;
        }

        var timezoneStart = value.IndexOf('T') + 1;
        var hasTimezone = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                          value.IndexOf('+', timezoneStart) >= 0 ||
                          value.IndexOf('-', timezoneStart) >= 0;
        return hasTimezone && DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
            out result);
    }
}
