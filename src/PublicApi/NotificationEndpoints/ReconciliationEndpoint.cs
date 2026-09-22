using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: the provider's own record of this application's messages (from its configured
/// sending number) over a date range, lined up against what eShop believes it sent. Covers the whole
/// range (paging through provider results), reporting when it had to truncate.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                }

                if (toDate < fromDate)
                {
                    return Results.BadRequest(new { message = "to must not be earlier than from." });
                }

                var report = await service.ReconcileAsync(fromDate, toDate, ct);
                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Complete = report.Complete,
                    ProviderCount = report.ProviderCount,
                    EShopCount = report.EShopCount,
                    Entries = report.Entries.Select(ReconciliationEntryView.From).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result);
    }
}
