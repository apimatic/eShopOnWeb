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

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — the provider's record of messages
/// from this application's configured sending number over a date range, lined up against what eShop
/// believes it sent. from/to are ISO-8601 date-times. Administrator only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseIso(from, out var fromDto) || !TryParseIso(to, out var toDto))
                {
                    return Results.BadRequest(new { error = "Both 'from' and 'to' must be supplied as ISO-8601 date-times." });
                }
                if (fromDto > toDto)
                {
                    return Results.BadRequest(new { error = "'from' must not be later than 'to'." });
                }

                var report = await service.ReconcileAsync(fromDto, toDto, cancellationToken);
                return Results.Ok(ReconciliationResponse.Create(report));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
