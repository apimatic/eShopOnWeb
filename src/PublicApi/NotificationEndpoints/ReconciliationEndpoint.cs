using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — line the provider's own record of messages
/// (for the configured sending number, over the whole date range) up against what eShop believes it sent.
/// <c>from</c>/<c>to</c> are ISO-8601 date-times. Administrator only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromUtc) || !TryParseIso(to, out var toUtc))
                {
                    return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });
                }
                if (toUtc < fromUtc)
                {
                    return Results.BadRequest(new { message = "'to' must be at or after 'from'." });
                }

                ReconciliationReport report = await notifications.ReconcileAsync(fromUtc, toUtc, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            result = parsed.ToUniversalTime();
            return true;
        }
        result = default;
        return false;
    }
}
