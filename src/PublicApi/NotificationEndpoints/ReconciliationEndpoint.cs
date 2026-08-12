using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages for a date range and lines
/// them up against what eShop believes it sent, so a message the provider knows about and eShop
/// doesn't — or the reverse — is visible. Counts only messages sent from the application's own
/// configured sending number. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                IOrderNotificationService notifications) =>
            {
                if (!TryParseIso(from, out var fromUtc))
                {
                    return Results.BadRequest(new { message = "A valid ISO-8601 'from' date-time is required." });
                }
                if (!TryParseIso(to, out var toUtc))
                {
                    return Results.BadRequest(new { message = "A valid ISO-8601 'to' date-time is required." });
                }
                if (fromUtc > toUtc)
                {
                    return Results.BadRequest(new { message = "'from' must not be after 'to'." });
                }

                var report = await notifications.ReconcileAsync(fromUtc, toUtc);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return false;
        }
        utc = parsed.ToUniversalTime();
        return true;
    }
}
