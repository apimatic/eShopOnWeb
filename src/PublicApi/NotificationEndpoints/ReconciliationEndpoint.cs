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
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lists the provider's own record of
/// messages for a date range (from this application's configured sending number only) and lines them up
/// against what eShop believes it sent. Operator action: restricted to the administrator role.
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
                IOptions<TwilioSettings> settings,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseIso(from, out var fromDt) || !TryParseIso(to, out var toDt))
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' must be valid ISO-8601 date-times." });
                }

                if (fromDt > toDt)
                {
                    return Results.BadRequest(new { message = "'from' must not be after 'to'." });
                }

                var report = await service.ReconcileAsync(fromDt, toDt, cancellationToken);

                return Results.Ok(new
                {
                    from = report.From,
                    to = report.To,
                    fromNumber = settings.Value.FromNumber,
                    providerCount = report.ProviderCount,
                    eShopCount = report.EShopCount,
                    matchedCount = report.Matched.Count,
                    providerOnlyCount = report.ProviderOnly.Count,
                    eShopOnlyCount = report.EShopOnly.Count,
                    matched = report.Matched,
                    providerOnly = report.ProviderOnly,
                    eShopOnly = report.EShopOnly
                });
            })
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) &&
               DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
