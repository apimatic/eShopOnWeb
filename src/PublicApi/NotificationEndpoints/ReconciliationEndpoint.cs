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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report lining up the provider's own record of messages from this application's
/// configured sending number, over a date range, against what eShop believes it sent — so a
/// message the provider knows about and eShop doesn't, or the reverse, is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseIso(from, out var fromUtc) || !TryParseIso(to, out var toUtc))
                {
                    return Results.BadRequest(new { error = "'from' and 'to' must be ISO-8601 date-times." });
                }

                if (toUtc < fromUtc)
                {
                    return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
                }

                var report = await notificationService.ReconcileAsync(fromUtc, toUtc, cancellationToken);
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
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }
}
